using System.Globalization;
using System.Text;

namespace SubtitleOcr.Core.Output;

public sealed class SubtitleEvent
{
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public required string Text { get; set; }
}

public static class SrtWriter
{
    /// <summary>Applied when a subpicture carries no StopDisplay delay and no successor bounds it.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);

    /// <summary>Minimum gap enforced between consecutive events.</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(1);

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

    /// <summary>Clamps missing/overlapping end times against the following event.</summary>
    public static void NormalizeTimings(List<SubtitleEvent> events)
    {
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.End <= e.Start)
            {
                e.End = e.Start + DefaultDuration;
            }

            if (i + 1 < events.Count && e.End > events[i + 1].Start - MinGap)
            {
                e.End = events[i + 1].Start - MinGap;
                if (e.End <= e.Start)
                {
                    e.End = e.Start + TimeSpan.FromMilliseconds(500);
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
