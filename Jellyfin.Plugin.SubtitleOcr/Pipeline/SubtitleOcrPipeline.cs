using System.Collections.Frozen;
using Jellyfin.Plugin.SubtitleOcr.Configuration;
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

    private readonly ILogger<SubtitleOcrPipeline> _logger;
    private readonly Dictionary<string, NOcrDb> _dbCache = new(StringComparer.Ordinal);

    public SubtitleOcrPipeline(ILogger<SubtitleOcrPipeline> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns the number of SRT files written. <paramref name="nOcrFolder"/> is the drop-in folder for
    /// per-language databases; <paramref name="progress"/> receives a 0..1 fraction across this item's
    /// streams and their subtitle images. When <see cref="PluginConfiguration.SkipLanguagesWithTextSubtitle"/>
    /// is set, image streams whose language appears in <paramref name="textSubtitleLanguages"/> (the raw
    /// language codes of the item's existing text-based subtitles) are skipped.
    /// </summary>
    public async Task<SubtitleOcrResult> ProcessAsync(string mediaPath, string ffprobePath, PluginConfiguration config, string? nOcrFolder, IReadOnlySet<string>? textSubtitleLanguages, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var reader = new FfprobeSubtitleReader(ffprobePath);
        var streams = await reader.GetImageSubtitleStreamsAsync(mediaPath, cancellationToken).ConfigureAwait(false);
        if (streams.Count == 0)
        {
            return new SubtitleOcrResult(Array.Empty<WrittenSubtitle>(), streams);
        }

        // Normalize the existing text-subtitle languages once so per-stream lookups align with the
        // normalized image-stream language (eng/en/... collapse to one code). Empty (the shared
        // allocation-free singleton) when the option is off, so Contains is always false.
        IReadOnlySet<string> coveredLanguages = config.SkipLanguagesWithTextSubtitle && textSubtitleLanguages is { Count: > 0 }
            ? textSubtitleLanguages.Select(LanguageCodes.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : FrozenSet<string>.Empty;

        // Optional allowlist: OCR only the selected languages (normalized). Null when empty (extract all).
        IReadOnlySet<string>? allowedLanguages = config.Languages is { Length: > 0 }
            ? config.Languages.Select(LanguageCodes.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        var options = new NOcrEngineOptions
        {
            SpaceMinGap = config.SpaceMinGap,
            SpaceGapFactor = config.SpaceGapFactor,
            MaxWrongPixels = config.MaxWrongPixels,
            DeepSeek = config.DeepSeek,
            UnknownCharacter = config.UnknownCharacter,
        };

        var titleTag = SanitizeTag(config.SubtitleTitleTag);

        var writtenSubtitles = new List<WrittenSubtitle>();

        // Count streams per language so a language with several tracks keeps the source stream number
        // in each file name (nothing overwrites); a lone track gets the clean {base}.{lang}.srt.
        var languageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in streams)
        {
            var lang = string.IsNullOrEmpty(s.Language) ? LanguageCodes.Undetermined : s.Language;
            languageCounts[lang] = languageCounts.GetValueOrDefault(lang) + 1;
        }

        for (var si = 0; si < streams.Count; si++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((double)si / streams.Count);

            var stream = streams[si];
            var language = string.IsNullOrEmpty(stream.Language) ? LanguageCodes.Undetermined : stream.Language;
            var normalizedLanguage = LanguageCodes.Normalize(language);

            if (allowedLanguages is not null && !allowedLanguages.Contains(normalizedLanguage))
            {
                _logger.LogDebug("Language {Language} not selected, skipping image stream {Index}", normalizedLanguage, stream.StreamIndex);
                continue;
            }

            // Skip only on a confident language match: an untagged image track (undetermined) could be a
            // different language than any existing text subtitle, so convert it rather than risk a gap.
            if (!string.IsNullOrEmpty(normalizedLanguage) &&
                coveredLanguages.Contains(normalizedLanguage))
            {
                _logger.LogInformation(
                    "Text subtitle already present for {Language}, skipping image stream {Index}",
                    normalizedLanguage, stream.StreamIndex);
                continue;
            }

            var srtFilePath = BuildSrtFilePath(mediaPath, language, titleTag, stream.StreamIndex, languageCounts[language] > 1);
            if (File.Exists(srtFilePath) && !config.OverwriteExisting)
            {
                _logger.LogDebug("SRT file exists, skipping: {Path}", srtFilePath);
                continue;
            }

            var engine = new NOcrEngine(GetDatabase(config, normalizedLanguage, nOcrFolder), options);
            var latinScript = LanguageCodes.IsLatinScript(normalizedLanguage);

            var packets = await reader.GetPacketsAsync(mediaPath, stream.StreamIndex, cancellationToken).ConfigureAwait(false);
            if (packets.Count == 0)
            {
                continue;
            }

            var images = stream.Format == SubtitleFormat.Pgs
                ? PgsTrackDecoder.Decode(packets)
                : VobSubTrackDecoder.Decode(packets, SpuPalette.FromExtradataText(stream.ExtradataText));

            var events = new List<SubtitleEvent>(images.Count);
            var dropped = 0;

            for (var ii = 0; ii < images.Count; ii++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report((si + (double)ii / images.Count) / streams.Count);

                var image = images[ii];
                if (config.SkipForcedOnly && image.Forced)
                {
                    continue;
                }

                var binary = image.Bitmap.Binarize(invertLuma: config.InvertLuma);
                var result = engine.Recognize(binary);
                if (result.GlyphCount == 0)
                {
                    continue;
                }

                if (result.UnknownCount > result.GlyphCount * config.MaxUnknownRatio)
                {
                    dropped++;
                    continue;
                }

                events.Add(new SubtitleEvent
                {
                    Start = image.Start,
                    End = image.End,
                    Text = OcrPostProcessor.Fix(result.Text, latinScript),
                });
            }

            if (events.Count == 0)
            {
                _logger.LogWarning(
                    "Stream {Index} of {Path} produced no usable events ({Dropped} dropped); check InvertLuma or train the database",
                    stream.StreamIndex, mediaPath, dropped);
                continue;
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

                continue;
            }

            events.Sort((a, b) => a.Start.CompareTo(b.Start));
            SrtWriter.NormalizeTimings(events);
            await File.WriteAllTextAsync(srtFilePath, SrtWriter.Serialize(events), cancellationToken).ConfigureAwait(false);
            writtenSubtitles.Add(new WrittenSubtitle(srtFilePath, normalizedLanguage, events.Count));

            _logger.LogInformation(
                "Wrote {Count} events ({Dropped} dropped) to {Path}",
                events.Count, dropped, srtFilePath);
        }

        progress?.Report(1.0);
        return new SubtitleOcrResult(writtenSubtitles, streams);
    }

    /// <summary>
    /// Resolves and caches the database for a language. Precedence: a matching per-language config
    /// entry, then a drop-in <c>{language}.nocr</c> in <paramref name="nOcrFolder"/>, then
    /// <see cref="PluginConfiguration.NOcrDatabasePath"/>, then the bundled Latin database.
    /// </summary>
    private NOcrDb GetDatabase(PluginConfiguration config, string normalizedLanguage, string? nOcrFolder)
    {
        var path = ResolveDatabasePath(config, normalizedLanguage, nOcrFolder);
        var key = path ?? EmbeddedLatinKey;
        if (_dbCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var db = path is null ? NOcrDb.LoadEmbeddedLatin() : NOcrDb.LoadFile(path);
        _dbCache[key] = db;
        _logger.LogInformation(
            "Loaded nOCR database for {Language} ({Source}): {Count} glyphs",
            normalizedLanguage, path ?? "bundled Latin", db.TotalCharacterCount);
        return db;
    }

    /// <summary>Returns the database path for a language, or null to use the bundled Latin database.</summary>
    private static string? ResolveDatabasePath(PluginConfiguration config, string normalizedLanguage, string? nOcrFolder)
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
        if (nOcrFolder is not null)
        {
            var dropIn = Path.Combine(nOcrFolder, $"{normalizedLanguage}.nocr");
            if (File.Exists(dropIn))
            {
                return dropIn;
            }
        }

        return string.IsNullOrWhiteSpace(config.NOcrDatabasePath) ? null : Resolve(config.NOcrDatabasePath, nOcrFolder);
    }

    /// <summary>A rooted path is used as-is; a bare name is resolved against the drop-in folder.</summary>
    private static string Resolve(string path, string? nOcrFolder) =>
        Path.IsPathRooted(path) || nOcrFolder is null ? path : Path.Combine(nOcrFolder, path);

    /// <summary>
    /// A single track gets {base}.{lang}.{tag}.srt; several tracks sharing a language each keep their source
    /// stream index ({base}.{lang}.{tag}.{index}.srt). The tag (empty to omit) marks the file as plugin-created
    /// so overwrite and discard only ever touch our own output. Jellyfin reads it as the subtitle title.
    /// </summary>
    private static string BuildSrtFilePath(string mediaPath, string language, string tag, int streamIndex, bool includeStreamIndex)
    {
        var directory = Path.GetDirectoryName(mediaPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(mediaPath);
        var tagPart = tag.Length == 0 ? string.Empty : $".{tag}";
        return includeStreamIndex
            ? Path.Combine(directory, $"{baseName}.{language}{tagPart}.{streamIndex}.srt")
            : Path.Combine(directory, $"{baseName}.{language}{tagPart}.srt");
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

/// <summary>Outcome of processing one media file: the SRT files written and the image-based subtitle
/// streams ffprobe found (present regardless of whether each was written, skipped, or empty).</summary>
public sealed record SubtitleOcrResult(IReadOnlyList<WrittenSubtitle> WrittenSubtitles, IReadOnlyList<ImageSubtitleStream> ImageStreams)
{
    public int SrtFilesWritten => WrittenSubtitles.Count;
}

/// <summary>One SRT the pipeline wrote: its path, language, and event count.</summary>
public sealed record WrittenSubtitle(string Path, string Language, int Events);
