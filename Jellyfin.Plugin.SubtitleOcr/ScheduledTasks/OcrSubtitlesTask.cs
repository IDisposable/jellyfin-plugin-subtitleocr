using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SubtitleOcr.Pipeline;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleOcr.ScheduledTasks;

public class OcrSubtitlesTask : IScheduledTask
{
    private static readonly IReadOnlySet<string> EmptyLanguages = new HashSet<string>();

    private readonly ILibraryManager _libraryManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly SubtitleOcrPipeline _pipeline;
    private readonly ILogger<OcrSubtitlesTask> _logger;

    public OcrSubtitlesTask(
        ILibraryManager libraryManager,
        IMediaEncoder mediaEncoder,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        SubtitleOcrPipeline pipeline,
        ILogger<OcrSubtitlesTask> logger)
    {
        _libraryManager = libraryManager;
        _mediaEncoder = mediaEncoder;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _pipeline = pipeline;
        _logger = logger;
    }

    public string Name => "OCR image-based subtitles";

    public string Description => "Converts embedded image-based subtitle tracks (VobSub, PGS) to SRT files via nOCR.";

    public string Category => "Subtitle OCR";

    public string Key => "SubtitleOcr";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.WeeklyTrigger,
            DayOfWeek = DayOfWeek.Sunday,
            TimeOfDayTicks = TimeSpan.FromHours(3).Ticks,
        };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode },
            IsVirtualItem = false,
            Recursive = true,
        });

        _logger.LogInformation("Scanning {Count} Movie/Episode items for image-based subtitles (ffprobe)", items.Count);

        var scanStatePath = Plugin.Instance?.ScanStatePath;
        var store = scanStatePath is null ? null : new ScanStateStore(scanStatePath, _logger);
        var state = store?.Load() ?? new Dictionary<Guid, ScanRecord>();

        var logPath = Plugin.Instance?.ExtractionLogPath;
        var extractionLog = logPath is null ? null : new ExtractionLog(logPath, _logger);
        var extractions = extractionLog?.Load() ?? new List<ExtractionRecord>();

        var force = config.ForceRescan;
        if (force)
        {
            config.ForceRescan = false;
            Plugin.Instance?.UpdateConfiguration(config);
            _logger.LogInformation("Force rescan: probing every file and clearing the flag");
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        var windowTicks = config.RescanIntervalDays > 0 ? TimeSpan.FromDays(config.RescanIntervalDays).Ticks : long.MaxValue;

        // ffprobe in the pipeline is the sole detector; Jellyfin's stored streams omit image-based tracks.
        // Text-subtitle languages still come from Jellyfin (only when skipping is enabled), as those
        // include external sidecars ffprobe cannot see. Skip a file that is unchanged since its last probe
        // and still within the rescan window; a changed mtime or an elapsed window re-probes it.
        var noPath = 0;
        var skippedCached = 0;
        var candidates = new List<Candidate>(items.Count);
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Path))
            {
                noPath++;
                continue;
            }

            var fileTicks = item.DateModified.Ticks;
            if (!force
                && state.TryGetValue(item.Id, out var record)
                && record.FileModifiedTicks == fileTicks
                && nowTicks - record.LastProbedTicks < windowTicks)
            {
                skippedCached++;
                continue;
            }

            IReadOnlySet<string> textLanguages = config.SkipLanguagesWithTextSubtitle
                ? item.GetMediaStreams()
                    .Where(s => s.Type == MediaStreamType.Subtitle && s.IsTextSubtitleStream && !string.IsNullOrEmpty(s.Language))
                    .Select(s => s.Language!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : EmptyLanguages;

            candidates.Add(new Candidate(item, textLanguages, fileTicks));
        }

        _logger.LogInformation(
            "Probing {Count} items ({NoPath} no path, {Skipped} unchanged and cached)",
            candidates.Count, noPath, skippedCached);

        var totalWritten = 0;
        var itemsWithImageStreams = 0;
        var census = new Dictionary<string, StreamTally>(StringComparer.OrdinalIgnoreCase);
        var sinceSave = 0;

        try
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = candidates[i].Item;

                // Map this item's 0..1 progress into its slice of the overall bar so it advances
                // smoothly through a large file's streams rather than jumping only between items.
                var itemIndex = i;
                var itemProgress = new SyncProgress(fraction =>
                    progress.Report((itemIndex + Math.Clamp(fraction, 0, 1)) * 100.0 / candidates.Count));

                try
                {
                    var result = await _pipeline
                        .ProcessAsync(item.Path, _mediaEncoder.ProbePath, config, Plugin.Instance?.NOcrDatabaseFolder, candidates[i].TextLanguages, itemProgress, cancellationToken)
                        .ConfigureAwait(false);
                    totalWritten += result.SrtFilesWritten;

                    if (result.ImageStreams.Count > 0)
                    {
                        itemsWithImageStreams++;
                        var isEpisode = item.GetBaseItemKind() == BaseItemKind.Episode;
                        foreach (var stream in result.ImageStreams)
                        {
                            var language = string.IsNullOrEmpty(stream.Language) ? "und" : stream.Language;
                            var key = $"{stream.Format} [{language}]";
                            if (!census.TryGetValue(key, out var tally))
                            {
                                tally = new StreamTally();
                                census[key] = tally;
                            }

                            if (isEpisode)
                            {
                                tally.Episodes++;
                            }
                            else
                            {
                                tally.Movies++;
                            }
                        }
                    }

                    // Record success only; a throw leaves no record so the item is retried next run.
                    state[item.Id] = new ScanRecord
                    {
                        FileModifiedTicks = candidates[i].FileModifiedTicks,
                        LastProbedTicks = nowTicks,
                        HadImageStreams = result.ImageStreams.Count > 0,
                    };

                    foreach (var written in result.WrittenSubtitles)
                    {
                        extractions.RemoveAll(r => string.Equals(r.SrtPath, written.Path, StringComparison.OrdinalIgnoreCase));
                        extractions.Add(new ExtractionRecord
                        {
                            ItemId = item.Id,
                            ItemName = item.Name ?? string.Empty,
                            SrtPath = written.Path,
                            Language = written.Language,
                            Events = written.Events,
                            WhenTicks = DateTime.UtcNow.Ticks,
                        });
                    }

                    // Refresh so Jellyfin attaches the new SRT files immediately instead of waiting for a scan.
                    if (result.SrtFilesWritten > 0)
                    {
                        _providerManager.QueueRefresh(
                            item.Id,
                            new MetadataRefreshOptions(new DirectoryService(_fileSystem)),
                            RefreshPriority.Normal);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OCR failed for {Path}", item.Path);
                }

                // Bound each item's slice even when it wrote nothing or threw mid-way.
                progress.Report((i + 1) * 100.0 / candidates.Count);

                if (++sinceSave >= 100)
                {
                    store?.Save(state);
                    extractionLog?.Save(extractions);
                    sinceSave = 0;
                }
            }
        }
        finally
        {
            store?.Save(state);
            extractionLog?.Save(extractions);
        }

        _logger.LogInformation(
            "Subtitle OCR complete: {Written} SRT files written; {WithImages} of {Probed} items had image-based subtitle streams",
            totalWritten, itemsWithImageStreams, candidates.Count);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Image-based subtitle census (format [language] = movies/episodes): {Census}",
                census.Count == 0
                    ? "(none)"
                    : string.Join(", ", census
                        .OrderByDescending(kv => kv.Value.Movies + kv.Value.Episodes)
                        .Select(kv => $"{kv.Key}={kv.Value.Movies}/{kv.Value.Episodes}")));
        }
    }

    /// <summary>A library item to OCR, its existing text-subtitle languages, and its file's modified ticks.</summary>
    private sealed record Candidate(BaseItem Item, IReadOnlySet<string> TextLanguages, long FileModifiedTicks);

    private sealed class StreamTally
    {
        public int Movies { get; set; }

        public int Episodes { get; set; }
    }

    /// <summary>Forwards progress synchronously (a background task has no sync context for Progress&lt;T&gt;).</summary>
    private sealed class SyncProgress : IProgress<double>
    {
        private readonly Action<double> _report;

        public SyncProgress(Action<double> report) => _report = report;

        public void Report(double value) => _report(value);
    }
}
