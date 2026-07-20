using System.Globalization;
using System.Text;

namespace SubtitleOcr.Core.Output;

public sealed class SubtitleEvent
{
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public required string Text { get; set; }

    /// <summary>Normalized vertical center on screen (0 top, 1 bottom); ~0.9 for normal bottom placement.
    /// Used to pick an ASS alignment bucket and to trigger Auto-format selection. SRT ignores it.</summary>
    public double VerticalCenter { get; set; } = 0.9;

    /// <summary>Fill color of the source text, when one dominates the cue. Used to trigger Auto-format
    /// selection and written as an ASS color override. Null when the source did not say; SRT ignores it.</summary>
    public (byte R, byte G, byte B)? Color { get; set; }
}

public static class SrtWriter
{
    /// <summary>Applied when a subpicture carries no StopDisplay delay and no successor bounds it.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);

    /// <summary>Minimum gap enforced between consecutive events.</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(1);

    /// <summary>Shortest a cue is held so it stays readable, never extended into the next cue.</summary>
    public static readonly TimeSpan MinDisplay = TimeSpan.FromMilliseconds(750);

    public static string Serialize(IReadOnlyList<SubtitleEvent> events)
    {
        var sb = new StringBuilder();
        var index = 1;
        foreach (var e in events)
        {
            if (string.IsNullOrWhiteSpace(e.Text))
            {
                continue;
            }

            sb.Append(index++.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append(Format(e.Start)).Append(" --> ").Append(Format(e.End)).Append("\r\n");
            sb.Append(e.Text.Replace("\n", "\r\n", StringComparison.Ordinal)).Append("\r\n\r\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Fixes each cue's end time so it is positive, at least <see cref="MinDisplay"/> long when there is room,
    /// and never past the next cue's start. The one invariant kept throughout is that a cue never ends after
    /// the next one begins: when a cue is jammed against its successor it is left touching it, not overlapping.
    /// </summary>
    public static void NormalizeTimings(List<SubtitleEvent> events)
    {
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.End <= e.Start)
            {
                e.End = e.Start + DefaultDuration;
            }

            var hasNext = i + 1 < events.Count;
            var nextStart = hasNext ? events[i + 1].Start : TimeSpan.MaxValue;

            // Hold a too-short cue to the readable minimum.
            if (e.End - e.Start < MinDisplay)
            {
                e.End = e.Start + MinDisplay;
            }

            // Pull it back out of the next cue, then, if that left no room at all, touch the next cue rather
            // than overlap it.
            if (hasNext && e.End > nextStart - MinGap)
            {
                e.End = nextStart - MinGap;
                if (e.End <= e.Start)
                {
                    e.End = nextStart;
                }
            }
        }
    }

    private static string Format(TimeSpan t) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0:00}:{1:00}:{2:00},{3:000}",
            (int)t.TotalHours, t.Minutes, t.Seconds, t.Milliseconds);
}
