using SubtitleOcr.Core.Output;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class AssWriterTests
{
    private static SubtitleEvent Cue(string text, (byte R, byte G, byte B)? color = null) =>
        new() { Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(1), Text = text, Color = color };

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

    // A literal brace in the OCR text is escaped, not parsed as an override block that would eat the text.
    [Fact]
    public void Serialize_LiteralBraces_AreEscaped()
    {
        var ass = AssWriter.Serialize(new List<SubtitleEvent> { Cue("{cough}") });

        Assert.Contains("\\{cough\\}", ass, StringComparison.Ordinal);
    }

    // Centiseconds round rather than truncate, so an end time does not creep earlier.
    [Fact]
    public void Serialize_Timecode_RoundsCentiseconds()
    {
        var events = new List<SubtitleEvent> { new() { Start = TimeSpan.Zero, End = new TimeSpan(0, 0, 0, 1, 996), Text = "Hi" } };

        var ass = AssWriter.Serialize(events);

        Assert.Contains(",0:00:02.00,", ass, StringComparison.Ordinal);
    }

    // A blank cue is not written and casts no color vote.
    [Fact]
    public void Serialize_BlankCue_IsSkipped()
    {
        var ass = AssWriter.Serialize(new List<SubtitleEvent> { Cue("   "), Cue("Real") });

        Assert.DoesNotContain(",,   ", ass, StringComparison.Ordinal);
        Assert.Contains(",,Real", ass, StringComparison.Ordinal);
    }

    // A cue with no color is white, which is the style, so nothing is overridden.
    [Fact]
    public void Serialize_NoColor_IsWhiteWithNoOverride()
    {
        var ass = AssWriter.Serialize(new List<SubtitleEvent> { Cue("Hello") });

        Assert.Contains("Style: Default,Arial,20,&H00FFFFFF,", ass, StringComparison.Ordinal);
        Assert.DoesNotContain("\\c&H", ass, StringComparison.Ordinal);
    }

    // ASS orders the channels B,G,R: yellow (255,255,0) is 00FFFF, not FFFF00.
    [Fact]
    public void Serialize_UniformColor_BecomesTheStyleRatherThanEveryLine()
    {
        var yellow = ((byte)255, (byte)255, (byte)0);
        var ass = AssWriter.Serialize(new List<SubtitleEvent> { Cue("One", yellow), Cue("Two", yellow) });

        Assert.Contains("Style: Default,Arial,20,&H0000FFFF,", ass, StringComparison.Ordinal);
        Assert.DoesNotContain("\\c&H", ass, StringComparison.Ordinal);
    }

    // The majority color is the style; only the minority carries an override.
    [Fact]
    public void Serialize_MixedColors_OverridesOnlyTheOddOnesOut()
    {
        var white = ((byte)255, (byte)255, (byte)255);
        var yellow = ((byte)255, (byte)255, (byte)0);
        var ass = AssWriter.Serialize(new List<SubtitleEvent>
        {
            Cue("One", white),
            Cue("Two", white),
            Cue("Three", yellow),
        });

        Assert.Contains("Style: Default,Arial,20,&H00FFFFFF,", ass, StringComparison.Ordinal);
        Assert.Contains("{\\c&H00FFFF&}Three", ass, StringComparison.Ordinal);
        Assert.DoesNotContain("{\\c&H00FFFF&}One", ass, StringComparison.Ordinal);
    }

    // Antialiasing drifts the sampled color by a shade; that is not a second color.
    [Fact]
    public void Serialize_NearIdenticalShades_AreOneColor()
    {
        var ass = AssWriter.Serialize(new List<SubtitleEvent>
        {
            Cue("One", ((byte)255, (byte)255, (byte)255)),
            Cue("Two", ((byte)252, (byte)254, (byte)255)),
        });

        Assert.DoesNotContain("\\c&H", ass, StringComparison.Ordinal);
    }

    // The color override goes after the alignment override, so both survive.
    [Fact]
    public void Serialize_PositionedColorCue_CarriesBothOverrides()
    {
        var events = new List<SubtitleEvent>
        {
            Cue("Bottom", ((byte)255, (byte)255, (byte)255)),
            Cue("Bottom two", ((byte)255, (byte)255, (byte)255)),
            new()
            {
                Start = TimeSpan.Zero,
                End = TimeSpan.FromSeconds(1),
                Text = "Sign",
                VerticalCenter = 0.1,
                Color = ((byte)255, (byte)255, (byte)0),
            },
        };

        var ass = AssWriter.Serialize(events);

        Assert.Contains("{\\an8}{\\c&H00FFFF&}Sign", ass, StringComparison.Ordinal);
    }
}
