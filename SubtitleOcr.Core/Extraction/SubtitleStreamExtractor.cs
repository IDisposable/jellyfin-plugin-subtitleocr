using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SubtitleOcr.Core.Extraction;

/// <summary>
/// Pulls every image-based subtitle stream out of a file in one pass over the container: ffprobe indexes the
/// packets (stream, timestamp, duration, size) and ffmpeg copies the payloads out raw, one file per stream.
///
/// The alternative, ffprobe -show_data per stream, hands back the same bytes as an xxd hex dump: 4x the size,
/// and one full demux of the file for every stream. A seven-track Blu-ray costs 1.5GB of ASCII and seven
/// sweeps that way, against 322MB and one sweep here.
///
/// Payloads land in <c>tempFolder</c> rather than memory: a stream is read and sliced only when its turn
/// comes, so peak memory follows how many streams are recognized at once, not how many the file has. The
/// caller passes a real one (the host's temp folder), since the system default is often RAM-backed and a
/// Blu-ray's tracks run to hundreds of megabytes.
/// </summary>
public sealed class SubtitleStreamExtractor : IDisposable
{
    private readonly string _directory;
    private readonly Dictionary<int, List<PacketRecord>> _index;

    private SubtitleStreamExtractor(string directory, Dictionary<int, List<PacketRecord>> index)
    {
        _directory = directory;
        _index = index;
    }

    /// <summary>Stream indexes that yielded packets.</summary>
    public IEnumerable<int> StreamIndexes => _index.Keys;

    public static async Task<SubtitleStreamExtractor> ExtractAsync(
        string ffprobePath,
        string ffmpegPath,
        string mediaPath,
        string tempFolder,
        IReadOnlyList<int> streamIndexes,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(tempFolder, "subtitleocr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        if (streamIndexes.Count == 0)
        {
            return new SubtitleStreamExtractor(directory, new Dictionary<int, List<PacketRecord>>());
        }

        // One -map per stream against one input: ffmpeg reads the container once and demuxes them together.
        // The maps come from the caller, so the copy does not wait on the index; both read the file at once.
        var arguments = new StringBuilder($"-v error -nostdin -i \"{mediaPath}\"");
        foreach (var streamIndex in streamIndexes)
        {
            arguments.Append(CultureInfo.InvariantCulture, $" -map 0:{streamIndex} -c copy -f data \"{PayloadPath(directory, streamIndex)}\"");
        }

        arguments.Append(" -y");

        var indexTask = ReadIndexAsync(ffprobePath, mediaPath, streamIndexes, cancellationToken);
        var copyTask = RunAsync(ffmpegPath, arguments.ToString(), cancellationToken);
        await Task.WhenAll(indexTask, copyTask).ConfigureAwait(false);

        return new SubtitleStreamExtractor(directory, await indexTask.ConfigureAwait(false));
    }

    /// <summary>The stream's packets, sliced out of its payload by the indexed sizes.</summary>
    public List<SubtitlePacket> Read(int streamIndex)
    {
        if (!_index.TryGetValue(streamIndex, out var records))
        {
            return new List<SubtitlePacket>();
        }

        var bytes = File.ReadAllBytes(PayloadPath(_directory, streamIndex));
        var packets = new List<SubtitlePacket>(records.Count);
        var offset = 0;
        foreach (var record in records)
        {
            if (offset + record.Size > bytes.Length)
            {
                break;
            }

            packets.Add(new SubtitlePacket
            {
                Data = bytes.AsSpan(offset, record.Size).ToArray(),
                Pts = record.Pts,
                Duration = record.Duration,
            });
            offset += record.Size;
        }

        return packets;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A payload still held open is worth leaking to a temp sweep, not worth failing a run over.
        }
    }

    private static string PayloadPath(string directory, int streamIndex) =>
        Path.Combine(directory, streamIndex.ToString(CultureInfo.InvariantCulture) + ".bin");

    /// <summary>csv=p=0 prints the requested fields in order: stream_index,pts_time,duration_time,size.</summary>
    private static async Task<Dictionary<int, List<PacketRecord>>> ReadIndexAsync(
        string ffprobePath, string mediaPath, IReadOnlyList<int> streamIndexes, CancellationToken cancellationToken)
    {
        var wanted = new HashSet<int>(streamIndexes);
        var csv = await RunAsync(
            ffprobePath,
            $"-v error -select_streams s -show_entries packet=stream_index,pts_time,duration_time,size -of csv=p=0 \"{mediaPath}\"",
            cancellationToken).ConfigureAwait(false);

        var index = new Dictionary<int, List<PacketRecord>>();
        foreach (var line in csv.Split('\n'))
        {
            var fields = line.Trim().Split(',');
            if (fields.Length < 4 ||
                !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var streamIndex) ||
                !wanted.Contains(streamIndex) ||
                !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
            {
                continue;
            }

            if (!index.TryGetValue(streamIndex, out var records))
            {
                records = new List<PacketRecord>();
                index[streamIndex] = records;
            }

            records.Add(new PacketRecord(ParseSeconds(fields[1]) ?? TimeSpan.Zero, ParseSeconds(fields[2]), size));
        }

        return index;
    }

    private static TimeSpan? ParseSeconds(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;

    private static async Task<string> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();

        // Drain both pipes concurrently: awaiting one while the other fills its buffer deadlocks the child.
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)} exited {process.ExitCode}: {(await stderr.ConfigureAwait(false)).Trim()}");
        }

        return await stdout.ConfigureAwait(false);
    }

    private readonly record struct PacketRecord(TimeSpan Pts, TimeSpan? Duration, int Size);
}
