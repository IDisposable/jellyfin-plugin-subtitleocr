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
    private static readonly HashSet<string> ImageSubtitleCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "dvd_subtitle", "dvdsub", "vobsub", // VobSub
        "hdmv_pgs_subtitle", "pgssub",      // PGS (Blu-ray)
    };

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

    public string Category => "Library";

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

        var candidates = new List<Candidate>();
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Path) || !File.Exists(item.Path))
            {
                continue;
            }

            var streams = item.GetMediaStreams();
            var hasImageSubtitle = streams.Any(s =>
                s.Type == MediaStreamType.Subtitle &&
                !s.IsExternal &&
                s.Codec is not null &&
                ImageSubtitleCodecs.Contains(s.Codec));
            if (!hasImageSubtitle)
            {
                continue;
            }

            // Languages already covered by a text-based subtitle, embedded or external sidecar
            // (IsTextSubtitleStream is set for both). The pipeline uses this to skip image tracks whose
            // language is already present when SkipLanguagesWithTextSubtitle is enabled.
            var textLanguages = streams
                .Where(s => s.Type == MediaStreamType.Subtitle && s.IsTextSubtitleStream && !string.IsNullOrEmpty(s.Language))
                .Select(s => s.Language!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            candidates.Add(new Candidate(item, textLanguages));
        }

        _logger.LogInformation("Found {Count} items with embedded image-based subtitle streams", candidates.Count);

        var totalWritten = 0;
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
                var writtenForItem = await _pipeline
                    .ProcessAsync(item.Path, _mediaEncoder.ProbePath, config, Plugin.Instance?.NOcrDatabaseFolder, candidates[i].TextLanguages, itemProgress, cancellationToken)
                    .ConfigureAwait(false);
                totalWritten += writtenForItem;

                // Once all of an item's streams are decoded, refresh it so Jellyfin attaches the
                // new SRT files immediately instead of waiting for the next library scan.
                if (writtenForItem > 0)
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
        }

        _logger.LogInformation("Subtitle OCR complete: {Written} SRT files written across {Items} items", totalWritten, candidates.Count);
    }

    /// <summary>A library item to OCR, with the languages it already has a text-based subtitle for.</summary>
    private sealed record Candidate(BaseItem Item, IReadOnlySet<string> TextLanguages);

    /// <summary>Forwards progress synchronously (a background task has no sync context for Progress&lt;T&gt;).</summary>
    private sealed class SyncProgress : IProgress<double>
    {
        private readonly Action<double> _report;

        public SyncProgress(Action<double> report) => _report = report;

        public void Report(double value) => _report(value);
    }
}
