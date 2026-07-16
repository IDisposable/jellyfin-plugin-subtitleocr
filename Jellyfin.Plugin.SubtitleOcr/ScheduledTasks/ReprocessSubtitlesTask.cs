using Jellyfin.Plugin.SubtitleOcr.Pipeline;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleOcr.ScheduledTasks;

/// <summary>
/// Re-OCRs every logged item from its source, so the current ruleset, output format, and language detection all
/// apply and positions the written text cannot carry are recovered. Manual (no default trigger).
/// </summary>
public class ReprocessSubtitlesTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly SubtitleOcrPipeline _pipeline;
    private readonly SubtitleReprocessor _reprocessor;
    private readonly ILogger<ReprocessSubtitlesTask> _logger;

    public ReprocessSubtitlesTask(
        ILibraryManager libraryManager,
        SubtitleOcrPipeline pipeline,
        SubtitleReprocessor reprocessor,
        ILogger<ReprocessSubtitlesTask> logger)
    {
        _libraryManager = libraryManager;
        _pipeline = pipeline;
        _reprocessor = reprocessor;
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

        // Held across the whole run, so the per-item calls below go through ReOcrAsync rather than
        // ReprocessItemAsync, which would take the gate again for every item.
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
                    written += await _reprocessor.ReOcrAsync(plugin, config, item, itemRecords, updated, cancellationToken)
                        .ConfigureAwait(false);
                    refreshItems.Add(group.Key);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to reprocess item {Item}", group.Key);
                updated.AddRange(itemRecords); // keep the originals so nothing is lost from the log
            }

            progress.Report((g + 1) * 100.0 / itemGroups.Count);
        }

        store.Save(updated);

        foreach (var id in refreshItems)
        {
            _reprocessor.QueueRefresh(id);
        }

        _logger.LogInformation(
            "Reprocess complete: {Written} files written across {Items} items ({Skipped} skipped, source missing)",
            written, itemGroups.Count - skipped, skipped);
    }
}
