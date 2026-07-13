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
