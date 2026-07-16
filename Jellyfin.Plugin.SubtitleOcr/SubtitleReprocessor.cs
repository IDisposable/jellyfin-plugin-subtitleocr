using System.Collections.Frozen;
using Jellyfin.Plugin.SubtitleOcr.Configuration;
using Jellyfin.Plugin.SubtitleOcr.Pipeline;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleOcr;

/// <summary>Why a reprocess wrote nothing, so the caller can say which it was.</summary>
public enum ReprocessOutcome
{
    /// <summary>The item was re-OCRed. <see cref="ReprocessResult.FilesWritten"/> may still be zero when
    /// every track failed the quality gate.</summary>
    Processed,

    /// <summary>Another Subtitle OCR run holds the pipeline.</summary>
    Busy,

    /// <summary>The item is no longer in the library, or its source file is gone. Its log records are kept,
    /// since they still point at the subtitles that were written.</summary>
    SourceMissing,
}

public readonly record struct ReprocessResult(ReprocessOutcome Outcome, int FilesWritten);

/// <summary>Re-OCRs one logged item from its source. Shared by the reprocess task, which holds the run gate
/// across a whole run, and the log page, which reprocesses a single item on demand.</summary>
public class SubtitleReprocessor
{
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly SubtitleOcrPipeline _pipeline;
    private readonly ILogger<SubtitleReprocessor> _logger;

    public SubtitleReprocessor(
        ILibraryManager libraryManager,
        IMediaEncoder mediaEncoder,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        SubtitleOcrPipeline pipeline,
        ILogger<SubtitleReprocessor> logger)
    {
        _libraryManager = libraryManager;
        _mediaEncoder = mediaEncoder;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _pipeline = pipeline;
        _logger = logger;
    }

    /// <summary>Claims the run gate, re-OCRs the one item, and rewrites its records in the log.</summary>
    public async Task<ReprocessResult> ReprocessItemAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return new ReprocessResult(ReprocessOutcome.SourceMissing, 0);
        }

        using var run = _pipeline.TryBeginRun();
        if (run is null)
        {
            return new ReprocessResult(ReprocessOutcome.Busy, 0);
        }

        var item = _libraryManager.GetItemById(itemId);
        if (item?.Path is null || !File.Exists(item.Path))
        {
            _logger.LogWarning("Source file missing for item {Item}; leaving its subtitles as they are", itemId);
            return new ReprocessResult(ReprocessOutcome.SourceMissing, 0);
        }

        var store = new ExtractionLog(plugin.ExtractionLogPath, _logger);
        var records = store.Load();
        var kept = records.FindAll(r => r.ItemId != itemId);
        var itemRecords = records.FindAll(r => r.ItemId == itemId);

        int written;
        try
        {
            written = await ReOcrAsync(plugin, plugin.Configuration, item, itemRecords, kept, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The old output is already deleted, so leaving the records would point the log at files that are
            // gone. Save what survived and let the caller report the failure.
            _logger.LogError(ex, "Failed to reprocess item {Item}", itemId);
            store.Save(kept);
            throw;
        }

        store.Save(kept);
        QueueRefresh(itemId);
        return new ReprocessResult(ReprocessOutcome.Processed, written);
    }

    /// <summary>Deletes the item's existing OCR output, re-runs OCR, and adds the fresh output to
    /// <paramref name="updated"/>. The caller holds the run gate and owns the log.</summary>
    public async Task<int> ReOcrAsync(
        Plugin plugin,
        PluginConfiguration config,
        BaseItem item,
        List<ExtractionRecord> itemRecords,
        List<ExtractionRecord> updated,
        CancellationToken cancellationToken)
    {
        foreach (var record in itemRecords)
        {
            if (!string.IsNullOrEmpty(record.SrtPath) && File.Exists(record.SrtPath))
            {
                File.Delete(record.SrtPath);
            }
        }

        var protectedWords = config.SpellCheck
            ? MetadataWords.From(item, _libraryManager)
            : FrozenSet<string>.Empty;

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

        return result.SrtFilesWritten;
    }

    /// <summary>So Jellyfin attaches the new files without waiting for a scan.</summary>
    public void QueueRefresh(Guid itemId) =>
        _providerManager.QueueRefresh(
            itemId,
            new MetadataRefreshOptions(new DirectoryService(_fileSystem)),
            RefreshPriority.Normal);
}
