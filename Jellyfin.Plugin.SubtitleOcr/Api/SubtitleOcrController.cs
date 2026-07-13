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
    private readonly ILogger<SubtitleOcrController> _logger;

    public SubtitleOcrController(ILogger<SubtitleOcrController> logger) => _logger = logger;

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
