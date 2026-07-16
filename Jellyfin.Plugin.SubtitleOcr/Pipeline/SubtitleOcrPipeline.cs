using System.Collections.Frozen;
using System.Net.Http;
using Jellyfin.Plugin.SubtitleOcr.Configuration;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;
using SubtitleOcr.Core.Extraction;
using SubtitleOcr.Core.NOcr;
using SubtitleOcr.Core.Ocr;
using SubtitleOcr.Core.Output;
using SubtitleOcr.Core.Pgs;
using SubtitleOcr.Core.Subtitles;
using SubtitleOcr.Core.VobSub;

namespace Jellyfin.Plugin.SubtitleOcr.Pipeline;

/// <summary>
/// Processes one media file: enumerates image-based subtitle streams (VobSub, PGS) via ffprobe,
/// decodes them to images, runs nOCR, writes .srt files. Stateless per call except the loaded databases.
/// </summary>
public class SubtitleOcrPipeline
{
    private const string EmbeddedLatinKey = "<embedded-latin>";

    // A subtitle whose vertical center is above this fraction of the frame is treated as positioned
    // (a sign or top caption) rather than normal bottom dialogue, so SRT would misplace it.
    private const double AssPositionThreshold = 0.6;

    // A track needs this many cues of one color before that color counts as deliberate rather than as a
    // stray sample off an odd cue.
    private const int MinimumCuesPerColor = 3;

    // Cues read to decide a track's polarity. A disc draws every cue the same way, so this only has to outvote
    // the odd fade or logo, and reading the whole track would cost a second pass over every pixel of it.
    private const int PolaritySampleSize = 25;

    private readonly ILogger<SubtitleOcrPipeline> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    // One task runs at a time, but its streams run concurrently, so every read and populate takes the
    // dictionary's own lock. Loading happens outside the lock: a race costs a duplicate load, not a tear.
    // Language keys are LanguageCodes.Normalize output, which is always lowercase, so Ordinal is enough.
    private readonly Dictionary<string, NOcrDb> _dbCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ISpellCorrector> _spellCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OcrFixReplaceList> _ocrFixCache = new(StringComparer.Ordinal);
    private int _running;

    public SubtitleOcrPipeline(ILogger<SubtitleOcrPipeline> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Claims the single-run slot without blocking, so the tasks cannot use the shared caches at
    /// once. Returns a token to dispose when the run ends, or null if another task holds it (caller skips).</summary>
    public IDisposable? TryBeginRun() =>
        Interlocked.CompareExchange(ref _running, 1, 0) == 0 ? new RunToken(this) : null;

    private sealed class RunToken : IDisposable
    {
        private readonly SubtitleOcrPipeline _pipeline;

        public RunToken(SubtitleOcrPipeline pipeline) => _pipeline = pipeline;

        public void Dispose() => Interlocked.Exchange(ref _pipeline._running, 0);
    }

    /// <summary>
    /// Processes one media file end to end: enumerates image-based subtitle streams, decodes, OCRs, corrects,
    /// and writes SRT files. Returns the written files and the image streams found. See
    /// <see cref="SubtitleOcrRequest"/> for the inputs.
    /// </summary>
    public async Task<SubtitleOcrResult> ProcessAsync(SubtitleOcrRequest request, CancellationToken cancellationToken)
    {
        var mediaPath = request.MediaPath;
        var config = request.Config;
        var nOcrFolder = request.NOcrFolder;
        var dictionaryFolder = request.DictionaryFolder;
        var textSubtitleLanguages = request.TextSubtitleLanguages;
        var protectedWords = request.ProtectedWords;
        var progress = request.Progress;

        var reader = new FfprobeSubtitleReader(request.FfprobePath);
        var header = await reader.ReadHeaderAsync(mediaPath, cancellationToken).ConfigureAwait(false);
        var streams = header.Streams;
        if (streams.Count == 0)
        {
            return new SubtitleOcrResult(Array.Empty<WrittenSubtitle>(), streams);
        }

        // Normalize the existing text-subtitle languages once so per-stream lookups align with the
        // normalized image-stream language (eng/en/... collapse to one code). Empty (the shared
        // allocation-free singleton) when the option is off, so Contains is always false.
        IReadOnlySet<string> coveredLanguages = config.SkipLanguagesWithTextSubtitle && textSubtitleLanguages.Count > 0
            ? textSubtitleLanguages.Select(LanguageCodes.Normalize).ToHashSet(StringComparer.Ordinal)
            : FrozenSet<string>.Empty;

        // Allowlist: OCR only the selected languages (normalized). Empty means extract all.
        IReadOnlySet<string> allowedLanguages = config.Languages is { Length: > 0 }
            ? config.Languages.Select(LanguageCodes.Normalize).ToHashSet(StringComparer.Ordinal)
            : FrozenSet<string>.Empty;

        // Protect proper nouns from spell-correction: the item's metadata words plus the file's own tags
        // (container/stream/chapter titles), which the library metadata may not carry.
        var effectiveProtected = protectedWords;
        if (config.SpellCheck && header.MetadataWords.Count > 0)
        {
            header.MetadataWords.UnionWith(protectedWords);
            effectiveProtected = header.MetadataWords;
        }

        var options = new NOcrEngineOptions
        {
            SpaceMinGap = config.SpaceMinGap,
            SpaceGapFactor = config.SpaceGapFactor,
            MaxWrongPixels = config.MaxWrongPixels,
            DeepSeek = config.DeepSeek,
            UnknownCharacter = config.Placeholder,
        };

        var titleTag = SanitizeTag(config.SubtitleTitleTag);

        var writtenSubtitles = new List<WrittenSubtitle>();
        var positioned = false;

        // A language with several tracks keeps the source stream number in each file name; a lone track gets
        // the clean {base}.{lang}.srt. Keyed by the normalized code, which is what names the file: "dut" and
        // "nld" tracks would otherwise both claim {base}.nld.srt.
        var languageCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in streams)
        {
            var lang = LanguageCodes.Normalize(string.IsNullOrEmpty(s.Language) ? LanguageCodes.Undetermined : s.Language);
            languageCounts[lang] = languageCounts.GetValueOrDefault(lang) + 1;
        }

        // One pass over the container for every stream at once, rather than a full demux per stream.
        using var extraction = await SubtitleStreamExtractor.ExtractAsync(
            request.FfprobePath,
            request.FfmpegPath,
            mediaPath,
            request.TempFolder,
            streams.ConvertAll(s => s.StreamIndex),
            cancellationToken).ConfigureAwait(false);

        // Streams run concurrently: recognition of one overlaps the slicing of another. The image loop
        // inside splits the remaining budget, so a single-stream file still uses every core.
        var streamDegree = Math.Min(streams.Count, ParallelismOf(config));
        var imageDegree = Math.Max(1, ParallelismOf(config) / streamDegree);
        var streamsDone = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, streams.Count),
            new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = streamDegree },
            async (si, streamToken) =>
        {
            var stream = streams[si];
            var language = string.IsNullOrEmpty(stream.Language) ? LanguageCodes.Undetermined : stream.Language;
            var normalizedLanguage = LanguageCodes.Normalize(language);

            if (allowedLanguages.Count > 0 && !allowedLanguages.Contains(normalizedLanguage))
            {
                _logger.LogDebug("Language {Language} not selected, skipping image stream {Index}", normalizedLanguage, stream.StreamIndex);
                return;
            }

            // Skip only on a confident language match: an untagged image track (undetermined) could be a
            // different language than any existing text subtitle, so convert it rather than risk a gap.
            if (!string.IsNullOrEmpty(normalizedLanguage) &&
                coveredLanguages.Contains(normalizedLanguage))
            {
                _logger.LogInformation(
                    "Text subtitle already present for {Language}, skipping image stream {Index}",
                    normalizedLanguage, stream.StreamIndex);
                return;
            }

            // Identifies the track in Jellyfin: its title, else "Commentary" from the disposition.
            var descriptor = SanitizeTag(stream.Title);
            if (descriptor.Length == 0 && stream.Commentary)
            {
                descriptor = "Commentary";
            }

            var includeStreamIndex = languageCounts[normalizedLanguage] > 1;

            // A tagged track's file name is known now (unless Auto defers the extension), so skip early
            // (before OCR) if it already exists. Undetermined language and Auto format both depend on the OCR
            // text below, so their existence check waits until after recognition.
            var isUndetermined = normalizedLanguage == LanguageCodes.Undetermined;
            if (!isUndetermined && config.OutputFormat != SubtitleOutputFormat.Auto && !config.OverwriteExisting)
            {
                var extension = config.OutputFormat == SubtitleOutputFormat.Ass ? "ass" : "srt";
                var earlyPath = BuildOutputPath(
                    mediaPath, normalizedLanguage, titleTag, descriptor, stream.Forced, stream.HearingImpaired,
                    stream.StreamIndex, includeStreamIndex, extension);

                // SDH is only known after recognition, so an existing file may carry a .sdh this cannot
                // predict. Either name means the work is done.
                var earlySdhPath = BuildOutputPath(
                    mediaPath, normalizedLanguage, titleTag, descriptor, stream.Forced, hearingImpaired: true,
                    stream.StreamIndex, includeStreamIndex, extension);

                if (File.Exists(earlyPath) || File.Exists(earlySdhPath))
                {
                    _logger.LogDebug("Subtitle file exists, skipping: {Path}", earlyPath);
                    return;
                }
            }

            var engine = new NOcrEngine(GetDatabase(config, normalizedLanguage, nOcrFolder), options);

            var packets = extraction.Read(stream.StreamIndex);
            if (packets.Count == 0)
            {
                return;
            }

            var images = stream.Format == SubtitleFormat.Pgs
                ? PgsTrackDecoder.Decode(packets)
                : VobSubTrackDecoder.Decode(packets, SpuPalette.FromExtradataText(stream.ExtradataText));

            // A subtitle sitting above the bottom region (a sign or top caption) loses its placement in SRT.
            if (images.Exists(i => i.VerticalCenter < AssPositionThreshold))
            {
                Volatile.Write(ref positioned, true);
            }

            var invertLuma = DetectInvertLuma(images, stream.StreamIndex);

            // The matcher only reads the database, so images recognize in parallel. Results stay indexed by
            // image, which keeps the cue order without sorting threads against each other.
            var recognized = new SubtitleEvent?[images.Count];
            var dropped = 0;
            var done = 0;

            Parallel.For(
                0,
                images.Count,
                new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = imageDegree },
                ii =>
                {
                    var image = images[ii];
                    if (!config.SkipForcedOnly || !image.Forced)
                    {
                        // Disposed here and not held: the mask is pooled, and the splitter copies every glyph
                        // it keeps out of it, so nothing downstream points back into these pixels.
                        using var binarized = image.Bitmap.Binarize(invertLuma: invertLuma);
                        var result = engine.Recognize(binarized.Mask);
                        if (result.GlyphCount > 0)
                        {
                            if (result.UnknownCount > result.GlyphCount * config.MaxUnknownRatio)
                            {
                                Interlocked.Increment(ref dropped);
                            }
                            else
                            {
                                recognized[ii] = new SubtitleEvent
                                {
                                    Start = image.Start,
                                    End = image.End,
                                    Text = OcrPostProcessor.Fix(result.Text, normalizedLanguage, config.Placeholder, config.NormalizeEllipsis),
                                    VerticalCenter = image.VerticalCenter,
                                    Color = binarized.ForegroundColor,
                                };
                            }
                        }
                    }

                    progress?.Report((si + (double)Interlocked.Increment(ref done) / images.Count) / streams.Count);
                });

            var events = new List<SubtitleEvent>(images.Count);
            foreach (var e in recognized)
            {
                if (e is not null)
                {
                    events.Add(e);
                }
            }

            if (events.Count == 0)
            {
                _logger.LogWarning(
                    "Stream {Index} of {Path} produced no usable events ({Dropped} dropped); the database likely needs training for this font",
                    stream.StreamIndex, mediaPath, dropped);
                return;
            }

            // Label an undetermined track with the language detected from its recognized text.
            var effectiveNormalized = normalizedLanguage;
            if (isUndetermined)
            {
                var detected = LanguageDetector.Detect(string.Join(' ', events.Select(e => e.Text)));
                if (detected is not null)
                {
                    effectiveNormalized = LanguageCodes.Normalize(detected);
                    _logger.LogInformation("Detected {Language} for untagged image stream {Index}", effectiveNormalized, stream.StreamIndex);

                    if (coveredLanguages.Contains(effectiveNormalized))
                    {
                        _logger.LogInformation(
                            "Text subtitle already present for detected {Language}, discarding image stream {Index}",
                            effectiveNormalized, stream.StreamIndex);
                        return;
                    }
                }
            }

            // Auto picks ASS for what SRT would lose: a positioned cue, or color used to tell speakers apart.
            var useAss = config.OutputFormat switch
            {
                SubtitleOutputFormat.Ass => true,
                SubtitleOutputFormat.Auto =>
                    events.Exists(e => e.VerticalCenter < AssPositionThreshold) || UsesColor(events),
                _ => false,
            };

            // A remuxed disc flags nothing, so fall back to what the text says.
            var hearingImpaired = stream.HearingImpaired;
            if (!hearingImpaired
                && config.DetectHearingImpaired
                && SdhDetector.IsHearingImpaired(events.ConvertAll(e => e.Text), SdhDetector.DefaultRatio, SdhDetector.DefaultMinimumCues))
            {
                hearingImpaired = true;
                _logger.LogInformation("Image stream {Index} reads as hearing-impaired (SDH)", stream.StreamIndex);
            }

            var srtFilePath = BuildOutputPath(
                mediaPath, effectiveNormalized, titleTag, descriptor, stream.Forced, hearingImpaired,
                stream.StreamIndex, includeStreamIndex, useAss ? "ass" : "srt");
            if (File.Exists(srtFilePath) && !config.OverwriteExisting)
            {
                _logger.LogDebug("SRT file exists, skipping: {Path}", srtFilePath);
                return;
            }

            var totalEvents = events.Count + dropped;
            if (dropped > totalEvents * config.MaxDroppedRatio)
            {
                _logger.LogWarning(
                    "Stream {Index} of {Path}: {Dropped} of {Total} events dropped (over {Ratio:P0}); discarding SRT",
                    stream.StreamIndex, mediaPath, dropped, totalEvents, config.MaxDroppedRatio);
                if (File.Exists(srtFilePath))
                {
                    File.Delete(srtFilePath);
                }

                return;
            }

            // Apply the OCR fix list then spell-correct, both with the effective (possibly detected) language.
            var ocrFix = await GetOcrFixListAsync(effectiveNormalized, dictionaryFolder, config, cancellationToken).ConfigureAwait(false);
            var spell = await GetSpellCorrectorAsync(effectiveNormalized, dictionaryFolder, config, cancellationToken).ConfigureAwait(false);
            foreach (var e in events)
            {
                e.Text = spell.Correct(ocrFix.Apply(e.Text), effectiveProtected);
            }

            // After correction, never before: capitalizing a cue's first letter turns a misread lowercase l
            // into L, and spell-check reads Title-case as a proper noun and protects it, so a leading "lndistinct"
            // would freeze as "Lndistinct" instead of resolving to "indistinct". Latin-only and needs the cues in
            // display order, both true here.
            if (LanguageCodes.IsLatinScript(effectiveNormalized))
            {
                SentenceCase.Apply(events, config.Placeholder);
            }

            // The two ratios above are per cue and per dropped cue; a track the database cannot read at all is
            // damaged in every cue and drops none, so only the finished text shows it.
            var textLength = 0;
            var placeholders = 0;
            foreach (var e in events)
            {
                textLength += e.Text.Length;
                foreach (var c in e.Text)
                {
                    if (c == config.Placeholder)
                    {
                        placeholders++;
                    }
                }
            }

            if (textLength > 0 && placeholders > textLength * config.MaxPlaceholderRatio)
            {
                _logger.LogWarning(
                    "Stream {Index} of {Path}: {Ratio:P1} of the text is unreadable (over {Max:P0}); discarding. No usable database for {Language}",
                    stream.StreamIndex, mediaPath, (double)placeholders / textLength, config.MaxPlaceholderRatio, effectiveNormalized);
                if (File.Exists(srtFilePath))
                {
                    File.Delete(srtFilePath);
                }

                return;
            }

            events.Sort((a, b) => a.Start.CompareTo(b.Start));
            SrtWriter.NormalizeTimings(events);
            var content = useAss ? AssWriter.Serialize(events) : SrtWriter.Serialize(events);
            await File.WriteAllTextAsync(srtFilePath, content, cancellationToken).ConfigureAwait(false);
            lock (writtenSubtitles)
            {
                writtenSubtitles.Add(new WrittenSubtitle(srtFilePath, effectiveNormalized, events.Count));
            }

            _logger.LogInformation(
                "Wrote {Count} events ({Dropped} dropped) to {Path}",
                events.Count, dropped, srtFilePath);
            progress?.Report((double)Interlocked.Increment(ref streamsDone) / streams.Count);
        }).ConfigureAwait(false);

        progress?.Report(1.0);
        return new SubtitleOcrResult(writtenSubtitles, streams) { PositionedSubtitles = positioned };
    }

    /// <summary>Loads and caches the Hunspell corrector for a language from a drop-in {language}.dic, downloading
    /// it first when enabled. Returns the no-op corrector when spell-check is off or no dictionary is available.</summary>
    private async Task<ISpellCorrector> GetSpellCorrectorAsync(string normalizedLanguage, string dictionaryFolder, PluginConfiguration config, CancellationToken cancellationToken)
    {
        if (!config.SpellCheck || string.IsNullOrEmpty(normalizedLanguage) || normalizedLanguage == LanguageCodes.Undetermined)
        {
            return NullSpellCorrector.Instance;
        }

        lock (_spellCache)
        {
            if (_spellCache.TryGetValue(normalizedLanguage, out var cached))
            {
                return cached;
            }
        }

        var dicPath = Path.Combine(dictionaryFolder, $"{normalizedLanguage}.dic");
        if (NeedsDownload(dicPath, config) && config.DownloadDictionaries && !string.IsNullOrWhiteSpace(config.DictionaryDownloadUrl))
        {
            await TryDownloadDictionaryAsync(normalizedLanguage, dicPath, config.DictionaryDownloadUrl, cancellationToken).ConfigureAwait(false);
        }

        var corrector = NullSpellCorrector.Instance;
        if (File.Exists(dicPath))
        {
            corrector = SpellCorrector.LoadDictionary(dicPath, config.Placeholder);
            if (corrector is NullSpellCorrector)
            {
                _logger.LogWarning("Failed to load spell dictionary {Path}", dicPath);
            }
            else
            {
                _logger.LogInformation("Loaded spell dictionary for {Language}: {Path}", normalizedLanguage, dicPath);
            }
        }
        else
        {
            // Spell-check is on but there is nothing to check against, so the text ships uncorrected.
            _logger.LogWarning(
                "Spell-check is enabled but no {Language} dictionary was found at {Path}; text is left uncorrected. Drop in the .dic/.aff there or enable dictionary download.",
                normalizedLanguage, dicPath);
        }

        lock (_spellCache)
        {
            if (_spellCache.TryGetValue(normalizedLanguage, out var raced))
            {
                return raced;
            }

            _spellCache[normalizedLanguage] = corrector;
        }
        return corrector;
    }

    /// <summary>Loads and caches the OCR fix replace list for a language, downloading it first when enabled.
    /// Returns <see cref="OcrFixReplaceList.Empty"/> when disabled or none is available.</summary>
    private async Task<OcrFixReplaceList> GetOcrFixListAsync(string normalizedLanguage, string dictionaryFolder, PluginConfiguration config, CancellationToken cancellationToken)
    {
        if (!config.UseOcrFixList || string.IsNullOrEmpty(normalizedLanguage) || normalizedLanguage == LanguageCodes.Undetermined)
        {
            return OcrFixReplaceList.Empty;
        }

        lock (_ocrFixCache)
        {
            if (_ocrFixCache.TryGetValue(normalizedLanguage, out var cached))
            {
                return cached;
            }
        }

        var path = Path.Combine(dictionaryFolder, $"{normalizedLanguage}_OCRFixReplaceList.xml");
        if (NeedsDownload(path, config) && !string.IsNullOrWhiteSpace(config.OcrFixListDownloadUrl))
        {
            var url = config.OcrFixListDownloadUrl.Replace("{code}", normalizedLanguage, StringComparison.Ordinal);
            await TryDownloadFileAsync(path, url, cancellationToken).ConfigureAwait(false);
        }

        var list = File.Exists(path) ? OcrFixReplaceList.LoadFile(path) : OcrFixReplaceList.Empty;
        if (!list.IsEmpty)
        {
            _logger.LogInformation("Loaded OCR fix list for {Language}: {Path}", normalizedLanguage, path);
        }

        lock (_ocrFixCache)
        {
            if (_ocrFixCache.TryGetValue(normalizedLanguage, out var raced))
            {
                return raced;
            }

            _ocrFixCache[normalizedLanguage] = list;
        }
        return list;
    }

    /// <summary>True when a cached asset is absent or older than the configured refresh interval.</summary>
    /// <summary>
    /// Which way round this track draws its text, for the binarizer, read off the track's own cues by majority:
    /// one odd cue (a fade, a logo) must not invert a whole track, and a track that says nothing keeps the
    /// usual polarity. Sampling stops once the outcome is settled, since the remaining cues cannot overturn it.
    ///
    /// Per track rather than per file or per user: polarity belongs to the track, and a disc can carry twenty.
    /// </summary>
    private bool DetectInvertLuma(List<SubtitleImage> images, int streamIndex)
    {
        var sampleSize = Math.Min(images.Count, PolaritySampleSize);
        var majority = (sampleSize / 2) + 1;
        var dark = 0;
        var light = 0;

        for (var i = 0; i < sampleSize; i++)
        {
            switch (images[i].Bitmap.LooksDarkOnLight())
            {
                case true:
                    dark++;
                    break;
                case false:
                    light++;
                    break;
                default:
                    break;
            }

            if (dark >= majority || light >= majority)
            {
                break;
            }
        }

        if (dark <= light)
        {
            return false;
        }

        _logger.LogInformation(
            "Image stream {Index} reads as dark text on a light background ({Dark} of {Sampled} cues); inverting binarization",
            streamIndex, dark, dark + light);
        return true;
    }

    /// <summary>
    /// Whether the track uses color to mean something, which only more than one color can. A track that is
    /// entirely yellow says nothing SRT cannot: it is just this disc's idea of a subtitle. A track that is
    /// mostly white with yellow lines is distinguishing them, and dropping to SRT would lose that.
    /// </summary>
    private static bool UsesColor(List<SubtitleEvent> events)
    {
        var counts = new Dictionary<int, int>();
        var deliberate = 0;
        foreach (var e in events)
        {
            if (e.Color is not { } c)
            {
                continue;
            }

            // Shades of one color are one color; the sampled mean drifts across antialiased cues.
            var key = ((c.R >> 3) << 10) | ((c.G >> 3) << 5) | (c.B >> 3);
            counts.TryGetValue(key, out var count);
            counts[key] = ++count;

            // A color turns deliberate on the cue that crosses the threshold, and the second one to do it
            // settles the track: nothing later can make it less colorful.
            if (count == MinimumCuesPerColor && ++deliberate > 1)
            {
                return true;
            }
        }

        return false;
    }

    private static bool NeedsDownload(string path, PluginConfiguration config)
    {
        if (!File.Exists(path))
        {
            return true;
        }

        return config.AssetRefreshDays > 0
            && (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalDays >= config.AssetRefreshDays;
    }

    private async Task TryDownloadFileAsync(string path, string url, CancellationToken cancellationToken)
    {
        try
        {
            var http = _httpClientFactory.CreateClient(NamedClient.Default);
            var bytes = await http.GetByteArrayAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Downloaded {Path} from {Url}", path, url);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not download {Url}", url);
        }
    }

    /// <summary>Downloads {language}.dic/.aff from the configured template ({code} = ISO 639-1, "no" mapped to
    /// Bokmal "nb" for the wooorm source). Best-effort: a failure leaves the language without a dictionary.</summary>
    private async Task TryDownloadDictionaryAsync(string normalizedLanguage, string dicPath, string urlTemplate, CancellationToken cancellationToken)
    {
        var code = LanguageCodes.ToTwoLetter(normalizedLanguage) ?? normalizedLanguage;
        if (code == "no")
        {
            code = "nb";
        }

        var baseUrl = urlTemplate.Replace("{code}", code, StringComparison.Ordinal);
        try
        {
            var http = _httpClientFactory.CreateClient(NamedClient.Default);
            var dic = await http.GetByteArrayAsync(new Uri(baseUrl + ".dic"), cancellationToken).ConfigureAwait(false);
            var aff = await http.GetByteArrayAsync(new Uri(baseUrl + ".aff"), cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(dicPath, dic, cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(Path.ChangeExtension(dicPath, ".aff"), aff, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Downloaded {Language} dictionary from {Url}", normalizedLanguage, baseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not download {Language} dictionary from {Url}", normalizedLanguage, baseUrl);
        }
    }

    /// <summary>
    /// Resolves and caches the database for a language. Precedence: a matching per-language config
    /// entry, then a drop-in <c>{language}.nocr</c> in <paramref name="nOcrFolder"/>, then
    /// <see cref="PluginConfiguration.NOcrDatabasePath"/>, then the bundled Latin database.
    /// </summary>
    private NOcrDb GetDatabase(PluginConfiguration config, string normalizedLanguage, string nOcrFolder)
    {
        var path = ResolveDatabasePath(config, normalizedLanguage, nOcrFolder);
        var key = path ?? EmbeddedLatinKey;
        lock (_dbCache)
        {
            if (_dbCache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var db = path is null ? NOcrDb.LoadEmbeddedLatin() : NOcrDb.LoadFile(path);
        lock (_dbCache)
        {
            if (_dbCache.TryGetValue(key, out var raced))
            {
                return raced;
            }

            _dbCache[key] = db;
        }
        _logger.LogInformation(
            "Loaded nOCR database for {Language} ({Source}): {Count} glyphs",
            normalizedLanguage, path ?? "bundled Latin", db.TotalCharacterCount);
        return db;
    }

    /// <summary>Returns the database path for a language, or null to use the bundled Latin database.</summary>
    private static string? ResolveDatabasePath(PluginConfiguration config, string normalizedLanguage, string nOcrFolder)
    {
        foreach (var entry in config.LanguageDatabases)
        {
            if (!string.IsNullOrWhiteSpace(entry.Path) &&
                LanguageCodes.Normalize(entry.Language) == normalizedLanguage)
            {
                return Resolve(entry.Path, nOcrFolder);
            }
        }

        // Drop-in convention: {language}.nocr in the plugin's nOCR folder.
        var dropIn = Path.Combine(nOcrFolder, $"{normalizedLanguage}.nocr");
        if (File.Exists(dropIn))
        {
            return dropIn;
        }

        return string.IsNullOrWhiteSpace(config.NOcrDatabasePath) ? null : Resolve(config.NOcrDatabasePath, nOcrFolder);
    }

    /// <summary>A rooted path is used as-is; a bare name is resolved against the drop-in folder.</summary>
    private static string Resolve(string path, string nOcrFolder) =>
        Path.IsPathRooted(path) ? path : Path.Combine(nOcrFolder, path);

    /// <summary>Images to recognize at once. The default leaves half the cores to the server, which is
    /// transcoding for someone while this background task runs.</summary>
    private static int ParallelismOf(PluginConfiguration config) =>
        config.MaxParallelism > 0 ? config.MaxParallelism : Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>
    /// Builds {base}.{lang}[.{descriptor}][.forced][.sdh][.{tag}][.{index}].srt. The tag (empty to omit) marks
    /// the file as plugin-created, so overwrite and discard only ever touch our own output; the descriptor
    /// (source title or "Commentary") and forced/sdh flags let Jellyfin label the track; the index keeps
    /// several tracks of one language from overwriting each other.
    /// </summary>
    private static string BuildOutputPath(
        string mediaPath, string language, string tag, string? descriptor, bool forced, bool hearingImpaired,
        int streamIndex, bool includeStreamIndex, string extension)
    {
        var directory = Path.GetDirectoryName(mediaPath) ?? string.Empty;
        var name = $"{Path.GetFileNameWithoutExtension(mediaPath)}.{language}";

        if (!string.IsNullOrEmpty(descriptor))
        {
            name += $".{descriptor}";
        }

        // Flags Jellyfin recognizes on external subtitle file names.
        if (forced)
        {
            name += ".forced";
        }

        if (hearingImpaired)
        {
            name += ".sdh";
        }

        if (tag.Length > 0)
        {
            name += $".{tag}";
        }

        if (includeStreamIndex)
        {
            name += $".{streamIndex}";
        }

        return Path.Combine(directory, $"{name}.{extension}");
    }

    /// <summary>Strips dots and path-invalid characters so the tag stays a single, safe file-name segment.</summary>
    private static string SanitizeTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        return new string(tag.Where(c => c != '.' && !invalid.Contains(c)).ToArray()).Trim();
    }
}

/// <summary>No-op progress used when a caller does not track progress.</summary>
public sealed class NullProgress : IProgress<double>
{
    public static readonly IProgress<double> Instance = new NullProgress();

    private NullProgress()
    {
    }

    public void Report(double value)
    {
    }
}

/// <summary>Inputs for processing one media file. Optional collections/progress default to empty/no-op.</summary>
public sealed record SubtitleOcrRequest
{
    public required string MediaPath { get; init; }

    public required string FfprobePath { get; init; }

    /// <summary>ffmpeg, for copying subtitle payloads out raw (IMediaEncoder.EncoderPath).</summary>
    public required string FfmpegPath { get; init; }

    /// <summary>Where extracted payloads go (Plugin.TempFolder).</summary>
    public required string TempFolder { get; init; }

    public required PluginConfiguration Config { get; init; }

    public required string NOcrFolder { get; init; }

    public required string DictionaryFolder { get; init; }

    /// <summary>Languages the item already has a text subtitle for (image tracks in these are skipped).</summary>
    public IReadOnlySet<string> TextSubtitleLanguages { get; init; } = FrozenSet<string>.Empty;

    /// <summary>Words spell-correction must never change (item and file metadata proper nouns).</summary>
    public IReadOnlySet<string> ProtectedWords { get; init; } = FrozenSet<string>.Empty;

    public IProgress<double> Progress { get; init; } = NullProgress.Instance;
}

/// <summary>Outcome of processing one media file: the SRT files written and the image-based subtitle
/// streams ffprobe found (present regardless of whether each was written, skipped, or empty).</summary>
public sealed record SubtitleOcrResult(IReadOnlyList<WrittenSubtitle> WrittenSubtitles, IReadOnlyList<ImageSubtitleStream> ImageStreams)
{
    public int SrtFilesWritten => WrittenSubtitles.Count;

    /// <summary>True when a subtitle was positioned away from the bottom (a sign or top caption), which SRT
    /// cannot place and ASS could.</summary>
    public bool PositionedSubtitles { get; init; }
}

/// <summary>One SRT the pipeline wrote: its path, language, and event count.</summary>
public sealed record WrittenSubtitle(string Path, string Language, int Events);

/// <summary>Result of reprocessing an existing SRT: the new content, its effective language, and whether the
/// language changed (an undetermined track was identified) so the file should be renamed.</summary>
