using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleOcr;

/// <summary>One written SRT: the item it belongs to (for a click-through link) and what was produced.</summary>
public sealed class ExtractionRecord
{
    public Guid ItemId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public string SrtPath { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public int Events { get; set; }

    /// <summary>UTC ticks when the file was written.</summary>
    public long WhenTicks { get; set; }
}

/// <summary>Loads and saves the extraction log (JSON in the plugin data folder), newest entries kept.</summary>
public sealed class ExtractionLog
{
    private const int MaxRecords = 10_000;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    private readonly string _path;
    private readonly ILogger _logger;

    public ExtractionLog(string path, ILogger logger)
    {
        _path = path;
        _logger = logger;
    }

    public List<ExtractionRecord> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var records = JsonSerializer.Deserialize<List<ExtractionRecord>>(File.ReadAllText(_path), SerializerOptions);
                if (records is not null)
                {
                    return records;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read extraction log {Path}; starting fresh", _path);
        }

        return new List<ExtractionRecord>();
    }

    /// <summary>Keeps only the most recent <see cref="MaxRecords"/> entries; writes atomically.</summary>
    public void Save(List<ExtractionRecord> records)
    {
        try
        {
            if (records.Count > MaxRecords)
            {
                records.RemoveRange(0, records.Count - MaxRecords);
            }

            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(records, SerializerOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write extraction log {Path}", _path);
        }
    }
}
