using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SubtitleOcr.Core.Extraction;

public sealed class SubtitlePacket
{
    public required byte[] Data { get; init; }
    public TimeSpan Pts { get; init; }
    public TimeSpan? Duration { get; init; }
}

public enum SubtitleFormat
{
    /// <summary>DVD VobSub (dvd_subtitle / dvdsub / vobsub).</summary>
    DvdSub,

    /// <summary>Blu-ray PGS (hdmv_pgs_subtitle / pgssub).</summary>
    Pgs,
}

public sealed class ImageSubtitleStream
{
    public int StreamIndex { get; init; }
    public SubtitleFormat Format { get; init; }
    public string? Language { get; init; }

    /// <summary>Stream title tag (e.g. "Director Commentary", "SDH"), when the source sets one.</summary>
    public string? Title { get; init; }

    public bool Forced { get; init; }

    public bool HearingImpaired { get; init; }

    public bool Commentary { get; init; }

    /// <summary>idx-style palette text; VobSub only (PGS carries its palette in-band).</summary>
    public string? ExtradataText { get; init; }
}

/// <summary>One header probe's results: the image-based subtitle streams and the words from the file's tags.</summary>
public sealed record FfprobeHeader(List<ImageSubtitleStream> Streams, HashSet<string> MetadataWords);

/// <summary>
/// Reads image-based subtitle packets (VobSub and PGS) from any container via ffprobe JSON output.
/// Demuxers hand ffprobe fully assembled SPUs / PGS display sets, which sidesteps PES/idx handling
/// and works uniformly for MKV, VOB/PS and M2TS sources.
/// </summary>
public sealed partial class FfprobeSubtitleReader
{
    [GeneratedRegex(@"\p{L}{2,}")]
    private static partial Regex MetadataWord();

    // Keyed Ordinal: the lookup lowercases ffprobe's codec_name first.
    private static readonly FrozenDictionary<string, SubtitleFormat> ImageSubtitleCodecs = new Dictionary<string, SubtitleFormat>(StringComparer.Ordinal)
    {
        ["dvd_subtitle"] = SubtitleFormat.DvdSub,
        ["dvdsub"] = SubtitleFormat.DvdSub,
        ["vobsub"] = SubtitleFormat.DvdSub,
        ["hdmv_pgs_subtitle"] = SubtitleFormat.Pgs,
        ["pgssub"] = SubtitleFormat.Pgs,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly string _ffprobePath;

    public FfprobeSubtitleReader(string ffprobePath) => _ffprobePath = ffprobePath;

    /// <summary>
    /// One probe pass over the file header: lists image-based subtitle streams (with VobSub palette extradata,
    /// title, and disposition) and collects words from the file's textual tags (every stream title, container
    /// format tags, and chapter titles), so proper nouns embedded in the file can be protected from spell
    /// correction. The per-stream packet reads for OCR are a separate pass (<see cref="GetPacketsAsync"/>).
    /// </summary>
    public async Task<FfprobeHeader> ReadHeaderAsync(string mediaPath, CancellationToken cancellationToken)
    {
        var json = await RunFfprobeAsync(
            $"-v error -show_streams -show_format -show_chapters -show_data -of json \"{mediaPath}\"",
            cancellationToken).ConfigureAwait(false);

        var streams = new List<ImageSubtitleStream>();
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("streams", out var streamArray))
        {
            foreach (var stream in streamArray.EnumerateArray())
            {
                string? language = null;
                string? title = null;
                if (stream.TryGetProperty("tags", out var tags))
                {
                    if (tags.TryGetProperty("language", out var lang))
                    {
                        language = lang.GetString();
                    }

                    if (tags.TryGetProperty("title", out var t))
                    {
                        title = t.GetString();
                        AddWords(words, title);
                    }
                }

                var codec = stream.TryGetProperty("codec_name", out var c) ? c.GetString() : null;
                if (codec is null || !ImageSubtitleCodecs.TryGetValue(codec.ToLowerInvariant(), out var format))
                {
                    continue;
                }

                var forced = false;
                var hearingImpaired = false;
                var commentary = false;
                if (stream.TryGetProperty("disposition", out var disp))
                {
                    forced = disp.TryGetProperty("forced", out var f) && f.GetInt32() == 1;
                    hearingImpaired = disp.TryGetProperty("hearing_impaired", out var h) && h.GetInt32() == 1;
                    commentary = disp.TryGetProperty("comment", out var cm) && cm.GetInt32() == 1;
                }

                string? extradataText = null;
                if (format == SubtitleFormat.DvdSub && stream.TryGetProperty("extradata", out var extradata))
                {
                    var bytes = ParseHexDump(extradata.GetString());
                    extradataText = Encoding.ASCII.GetString(bytes);
                }

                streams.Add(new ImageSubtitleStream
                {
                    StreamIndex = stream.GetProperty("index").GetInt32(),
                    Format = format,
                    Language = language,
                    Title = title,
                    Forced = forced,
                    HearingImpaired = hearingImpaired,
                    Commentary = commentary,
                    ExtradataText = extradataText,
                });
            }
        }

        if (root.TryGetProperty("format", out var formatElement) && formatElement.TryGetProperty("tags", out var formatTags))
        {
            AddContentTagWords(words, formatTags);
        }

        if (root.TryGetProperty("chapters", out var chapters))
        {
            foreach (var chapter in chapters.EnumerateArray())
            {
                if (chapter.TryGetProperty("tags", out var chapterTags) && chapterTags.TryGetProperty("title", out var chapterTitle))
                {
                    AddWords(words, chapterTitle.GetString());
                }
            }
        }

        return new FfprobeHeader(streams, words);
    }

    private static void AddContentTagWords(HashSet<string> words, JsonElement tags)
    {
        foreach (var tag in tags.EnumerateObject())
        {
            var name = tag.Name.ToLowerInvariant();

            // Skip technical/encoder tags; their values are not content proper nouns.
            if (name is "encoder" or "encoder_options" or "creation_time" or "duration" or "language"
                    or "filename" or "mimetype" or "bps"
                || name.StartsWith('_') || name.Contains("statistics", StringComparison.Ordinal))
            {
                continue;
            }

            AddWords(words, tag.Value.ValueKind == JsonValueKind.String ? tag.Value.GetString() : null);
        }
    }

    private static void AddWords(HashSet<string> words, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (Match m in MetadataWord().Matches(text))
        {
            words.Add(m.Value);
        }
    }

    /// <summary>Reads all packets (assembled SPUs) for one subtitle stream.</summary>
    public async Task<List<SubtitlePacket>> GetPacketsAsync(string mediaPath, int streamIndex, CancellationToken cancellationToken)
    {
        var json = await RunFfprobeAsync(
            $"-v error -select_streams {streamIndex} -show_packets -show_data -of json \"{mediaPath}\"",
            cancellationToken).ConfigureAwait(false);

        var packets = new List<SubtitlePacket>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("packets", out var packetArray))
        {
            return packets;
        }

        foreach (var packet in packetArray.EnumerateArray())
        {
            if (!packet.TryGetProperty("data", out var data))
            {
                continue;
            }

            var bytes = ParseHexDump(data.GetString());
            if (bytes.Length < 6)
            {
                continue;
            }

            packets.Add(new SubtitlePacket
            {
                Data = bytes,
                Pts = ReadSeconds(packet, "pts_time") ?? ReadSeconds(packet, "dts_time") ?? TimeSpan.Zero,
                Duration = ReadSeconds(packet, "duration_time"),
            });
        }

        return packets;
    }

    private static TimeSpan? ReadSeconds(JsonElement packet, string property)
    {
        if (packet.TryGetProperty(property, out var value) &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    /// <summary>
    /// Parses ffprobe's xxd-style dump (print_data_xxd in fftools): per line,
    /// "%08x: " offset, up to 16 bytes as 4-hex-char groups ("0a1b 2c3d ..."),
    /// space padding to column 41, then a raw ASCII gutter. The gutter may contain
    /// spaces and hex-lookalike characters, so parsing relies on the two invariants
    /// that disambiguate it: max 16 bytes per line, and 2+ consecutive spaces only
    /// occur in padding (group separators are single; full 16-byte lines pad with a
    /// single space, but the byte cap stops before their gutter is reached).
    /// </summary>
    public static byte[] ParseHexDump(string? dump)
    {
        if (string.IsNullOrEmpty(dump))
        {
            return Array.Empty<byte>();
        }

        var bytes = new List<byte>(dump.Length / 3);
        foreach (var rawLine in dump.Split('\n'))
        {
            var line = rawLine;
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon >= 0)
            {
                line = line[(colon + 1)..];
            }

            var lineBytes = 0;
            var i = 0;
            while (i < line.Length && lineBytes < 16)
            {
                if (line[i] == ' ')
                {
                    // Double space marks the padding before the ASCII gutter.
                    if (i + 1 < line.Length && line[i + 1] == ' ')
                    {
                        break;
                    }

                    i++;
                    continue;
                }

                if (i + 1 < line.Length &&
                    Uri.IsHexDigit(line[i]) && Uri.IsHexDigit(line[i + 1]))
                {
                    bytes.Add((byte)((Uri.FromHex(line[i]) << 4) | Uri.FromHex(line[i + 1])));
                    lineBytes++;
                    i += 2;
                }
                else
                {
                    break;
                }
            }
        }

        return bytes.ToArray();
    }

    private async Task<string> RunFfprobeAsync(string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ffprobePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();

        // Drain both pipes concurrently. -show_data makes stdout large; awaiting it before
        // stderr would let a full stderr buffer block the child and deadlock.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffprobe exited {process.ExitCode}: {stderr}");
        }

        return stdout;
    }
}
