using SubtitleOcr.Core.Output;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class SrtWriterTests
{
    [Fact]
    public void NormalizeTimings_BoundsEventEndAgainstSuccessorStart()
    {
        var events = new List<SubtitleEvent>
        {
            new() { Start = TimeSpan.FromSeconds(1), End = TimeSpan.Zero, Text = "First" },
            new() { Start = TimeSpan.FromSeconds(3), End = TimeSpan.FromSeconds(6), Text = "Second\nline" },
        };

        SrtWriter.NormalizeTimings(events);

        Assert.True(events[0].End < events[1].Start);
    }

    // Two near-coincident cues: the first is left touching the second, never extended past it.
    [Fact]
    public void NormalizeTimings_NearCoincidentCues_TouchNeverOverlap()
    {
        var events = new List<SubtitleEvent>
        {
            new() { Start = TimeSpan.FromSeconds(10), End = TimeSpan.FromSeconds(10), Text = "A" },
            new() { Start = TimeSpan.FromSeconds(10) + TimeSpan.FromMilliseconds(1), End = TimeSpan.FromSeconds(12), Text = "B" },
        };

        SrtWriter.NormalizeTimings(events);

        Assert.True(events[0].End <= events[1].Start, "first cue must not overlap the second");
    }

    // A too-short cue is held to the readable minimum when there is room after it.
    [Fact]
    public void NormalizeTimings_ShortCueWithRoom_ExtendsToMinDisplay()
    {
        var events = new List<SubtitleEvent>
        {
            new() { Start = TimeSpan.FromSeconds(10), End = TimeSpan.FromSeconds(10) + TimeSpan.FromMilliseconds(150), Text = "Flash" },
            new() { Start = TimeSpan.FromSeconds(20), End = TimeSpan.FromSeconds(22), Text = "Later" },
        };

        SrtWriter.NormalizeTimings(events);

        Assert.Equal(TimeSpan.FromSeconds(10) + SrtWriter.MinDisplay, events[0].End);
    }

    // A re-sent identical caption within the gap collapses to one spanning cue.
    [Fact]
    public void CoalesceDuplicates_AdjacentIdenticalCues_Merge()
    {
        var events = new List<SubtitleEvent>
        {
            new() { Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(2), Text = "Hello" },
            new() { Start = TimeSpan.FromSeconds(2) + TimeSpan.FromMilliseconds(50), End = TimeSpan.FromSeconds(4), Text = "Hello" },
        };

        SrtWriter.CoalesceDuplicates(events);

        Assert.Single(events);
        Assert.Equal(TimeSpan.Zero, events[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(4), events[0].End);
    }

    // The same line genuinely said twice, seconds apart, is left as two cues.
    [Fact]
    public void CoalesceDuplicates_IdenticalCuesFarApart_StayApart()
    {
        var events = new List<SubtitleEvent>
        {
            new() { Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(2), Text = "No." },
            new() { Start = TimeSpan.FromSeconds(5), End = TimeSpan.FromSeconds(7), Text = "No." },
        };

        SrtWriter.CoalesceDuplicates(events);

        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void Serialize_WritesSrtHeaderAndMultiLineBody()
    {
        var events = new List<SubtitleEvent>
        {
            new() { Start = TimeSpan.FromSeconds(1), End = TimeSpan.Zero, Text = "First" },
            new() { Start = TimeSpan.FromSeconds(3), End = TimeSpan.FromSeconds(6), Text = "Second\nline" },
        };
        SrtWriter.NormalizeTimings(events);

        var srt = SrtWriter.Serialize(events);

        Assert.StartsWith("1\r\n00:00:01,000 --> ", srt, StringComparison.Ordinal);
        Assert.Contains("Second\r\nline", srt, StringComparison.Ordinal);
    }
}
