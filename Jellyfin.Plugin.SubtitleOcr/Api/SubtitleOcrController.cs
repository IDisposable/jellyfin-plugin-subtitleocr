using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleOcr.Api;

/// <summary>Serves the extraction log to the plugin configuration page.</summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Plugins/SubtitleOcr")]
[Produces(MediaTypeNames.Application.Json)]
public class SubtitleOcrController : ControllerBase
{
    private readonly SubtitleReprocessor _reprocessor;
    private readonly ILogger<SubtitleOcrController> _logger;

    public SubtitleOcrController(SubtitleReprocessor reprocessor, ILogger<SubtitleOcrController> logger)
    {
        _reprocessor = reprocessor;
        _logger = logger;
    }

    /// <summary>Written SRT files, newest first.</summary>
    [HttpGet("Extractions")]
    public ActionResult<IReadOnlyList<ExtractionRecord>> GetExtractions()
    {
        var path = Plugin.Instance?.ExtractionLogPath;
        if (path is null)
        {
            return Ok(Array.Empty<ExtractionRecord>());
        }

        var records = new ExtractionLog(path, _logger).Load();
        records.Reverse();
        return Ok(records);
    }

    /// <summary>Re-OCRs one logged item from its source with the current settings.</summary>
    [HttpPost("Extractions/{itemId}/Reprocess")]
    public async Task<ActionResult<ReprocessResult>> ReprocessItem(
        [FromRoute] Guid itemId, CancellationToken cancellationToken)
    {
        var result = await _reprocessor.ReprocessItemAsync(itemId, cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            ReprocessOutcome.Busy => Conflict("A Subtitle OCR task is running. Try again when it finishes."),
            ReprocessOutcome.SourceMissing => NotFound("The source file for this item is gone."),
            _ => Ok(result),
        };
    }

    /// <summary>Clears the extraction log.</summary>
    [HttpDelete("Extractions")]
    public ActionResult ClearExtractions()
    {
        var path = Plugin.Instance?.ExtractionLogPath;
        if (path is not null)
        {
            new ExtractionLog(path, _logger).Save(new List<ExtractionRecord>());
        }

        return NoContent();
    }
}
