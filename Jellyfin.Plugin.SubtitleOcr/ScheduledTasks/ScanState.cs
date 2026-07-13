using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleOcr.ScheduledTasks;

/// <summary>One item's last probe: the file's modified time, when we probed, and whether it had image subs.</summary>
public sealed class ScanRecord
{
    public long FileModifiedTicks { get; set; }

    public long LastProbedTicks { get; set; }

    public bool HadImageStreams { get; set; }
}

/// <summary>Loads and saves the per-file probe cache as JSON in the plugin data folder.</summary>
public sealed class ScanStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    private readonly string _path;
    private readonly ILogger _logger;

    public ScanStateStore(string path, ILogger logger)
    {
        _path = path;
        _logger = logger;
    }

    public Dictionary<Guid, ScanRecord> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var state = JsonSerializer.Deserialize<Dictionary<Guid, ScanRecord>>(json, SerializerOptions);
                if (state is not null)
                {
                    return state;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read scan state {Path}; starting fresh", _path);
        }

        return new Dictionary<Guid, ScanRecord>();
    }

    /// <summary>Writes to a temp file then replaces, so a crash mid-write cannot corrupt the cache.</summary>
    public void Save(Dictionary<Guid, ScanRecord> state)
    {
        try
        {
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, SerializerOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write scan state {Path}", _path);
        }
    }
}
