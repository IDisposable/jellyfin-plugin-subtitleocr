using Jellyfin.Plugin.SubtitleOcr.Configuration;
using Jellyfin.Plugin.SubtitleOcr.Pipeline;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleOcr.ScheduledTasks;

/// <summary>
/// Re-OCRs every logged item from its source, so the current ruleset, output format, and language detection all
/// apply and positions the written text cannot carry are recovered. Manual (no default trigger).
/// </summary>
public class ReprocessSubtitlesTask : IScheduledTask
{
    private static readonly IReadOnlySet<string> EmptyStringSet = new HashSet<string>();

    private readonly ILibraryManager _libraryManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly SubtitleOcrPipeline _pipeline;
    private readonly ILogger<ReprocessSubtitlesTask> _logger;

    public ReprocessSubtitlesTask(
        ILibraryManager libraryManager,
        IMediaEncoder mediaEncoder,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        SubtitleOcrPipeline pipeline,
        ILogger<ReprocessSubtitlesTask> logger)
    {
        _libraryManager = libraryManager;
        _mediaEncoder = mediaEncoder;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _pipeline = pipeline;
        _logger = logger;
    }

    public string Name => "Reprocess extracted subtitles";

    public string Description => "Re-OCRs previously extracted subtitles from their source files, applying the current corrections, output format, and language detection.";

    public string Category => "Subtitle OCR";

    public string Key => "SubtitleOcrReprocess";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            _logger.LogError("Subtitle OCR plugin is not initialized; skipping run.");
            return;
        }

        using var run = _pipeline.TryBeginRun();
        if (run is null)
        {
            _logger.LogInformation("Another Subtitle OCR task is already running; skipping.");
            return;
        }

        var config = plugin.Configuration;
        var store = new ExtractionLog(plugin.ExtractionLogPath, _logger);
        var records = store.Load();

        var itemGroups = records.GroupBy(r => r.ItemId).ToList();
        _logger.LogInformation("Re-OCRing {Files} logged files across {Items} items", records.Count, itemGroups.Count);

        var updated = new List<ExtractionRecord>();
        var written = 0;
        var skipped = 0;
        var refreshItems = new HashSet<Guid>();

        for (var g = 0; g < itemGroups.Count; g++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = itemGroups[g];
            var itemRecords = group.ToList();
            var item = _libraryManager.GetItemById(group.Key);

            try
            {
                if (item?.Path is null || !File.Exists(item.Path))
                {
                    // Keep the records so the log still points at the files.
                    _logger.LogWarning("Source file missing for item {Item}; leaving its subtitles as they are", group.Key);
                    updated.AddRange(itemRecords);
                    skipped++;
                }
                else
                {
                    written += await ReOcrItemAsync(plugin, config, item, itemRecords, updated, refreshItems, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reprocess item {Item}", group.Key);
                updated.AddRange(itemRecords); // keep the originals so nothing is lost from the log
            }

            progress.Report((g + 1) * 100.0 / itemGroups.Count);
        }

        store.Save(updated);

        foreach (var id in refreshItems)
        {
            _providerManager.QueueRefresh(
                id,
                new MetadataRefreshOptions(new DirectoryService(_fileSystem)),
                RefreshPriority.Normal);
        }

        _logger.LogInformation(
            "Reprocess complete: {Written} files written across {Items} items ({Skipped} skipped, source missing)",
            written, itemGroups.Count - skipped, skipped);
    }

    /// <summary>Deletes the item's existing OCR output, re-runs OCR, and replaces its log records with the
    /// fresh output. Returns the files written.</summary>
    private async Task<int> ReOcrItemAsync(
        Plugin plugin, PluginConfiguration config, BaseItem item, List<ExtractionRecord> itemRecords,
        List<ExtractionRecord> updated, HashSet<Guid> refreshItems, CancellationToken cancellationToken)
    {
        foreach (var record in itemRecords)
        {
            if (!string.IsNullOrEmpty(record.SrtPath) && File.Exists(record.SrtPath))
            {
                File.Delete(record.SrtPath);
            }
        }

        var protectedWords = config.SpellCheck ? MetadataWords.From(item, _libraryManager) : EmptyStringSet;
        var result = await _pipeline.ProcessAsync(
            new SubtitleOcrRequest
            {
                MediaPath = item.Path!,
                FfprobePath = _mediaEncoder.ProbePath,
                        FfmpegPath = _mediaEncoder.EncoderPath,
                        TempFolder = plugin.TempFolder,
                Config = config,
                NOcrFolder = plugin.NOcrDatabaseFolder,
                DictionaryFolder = plugin.DictionaryFolder,
                ProtectedWords = protectedWords,
            },
            cancellationToken).ConfigureAwait(false);

        foreach (var subtitle in result.WrittenSubtitles)
        {
            updated.Add(new ExtractionRecord
            {
                ItemId = item.Id,
                ItemName = item.Name ?? string.Empty,
                SrtPath = subtitle.Path,
                Language = subtitle.Language,
                Events = subtitle.Events,
                WhenTicks = DateTime.UtcNow.Ticks,
            });
        }

        refreshItems.Add(item.Id);
        return result.SrtFilesWritten;
    }
}
