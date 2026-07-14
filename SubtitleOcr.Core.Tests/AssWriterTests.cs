using SubtitleOcr.Core.Output;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class AssWriterTests
{
    [Fact]
    public void Serialize_EmitsHeaderAndDialogue()
    {
        var events = new List<SubtitleEvent>
        {
            new() { Start = new TimeSpan(0, 0, 0, 1, 500), End = new TimeSpan(0, 0, 0, 2, 250), Text = "Line one\n<i>italic</i>" },
        };

        var ass = AssWriter.Serialize(events);

        Assert.Contains("[Script Info]", ass, StringComparison.Ordinal);
        Assert.Contains("[V4+ Styles]", ass, StringComparison.Ordinal);
        Assert.Contains("Dialogue: 0,0:00:01.50,0:00:02.25,Default,,0,0,0,,", ass, StringComparison.Ordinal);
        Assert.Contains("Line one\\N{\\i1}italic{\\i0}", ass, StringComparison.Ordinal);
    }
}
