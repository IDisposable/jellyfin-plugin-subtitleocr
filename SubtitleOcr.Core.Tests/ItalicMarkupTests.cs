using SubtitleOcr.Core.Output;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class ItalicMarkupTests
{
    [Fact]
    public void Parse_SplitsTagsIntoCleanTextAndSpans()
    {
        var (text, spans) = ItalicMarkup.Parse("a<i>bc</i>d");

        Assert.Equal("abcd", text);
        Assert.Equal(new[] { new ItalicSpan(1, 2) }, spans);
    }

    [Fact]
    public void Parse_NoTags_ReturnsTextAndNoSpans()
    {
        var (text, spans) = ItalicMarkup.Parse("plain text");

        Assert.Equal("plain text", text);
        Assert.Empty(spans);
    }

    [Fact]
    public void Parse_MultipleAndAbuttingRuns()
    {
        var (text, spans) = ItalicMarkup.Parse("<i>a</i><i>b</i>c<i>d</i>");

        Assert.Equal("abcd", text);
        Assert.Equal(new[] { new ItalicSpan(0, 1), new ItalicSpan(1, 1), new ItalicSpan(3, 1) }, spans);
    }

    [Theory]
    [InlineData("<i>", "</i>", "a<i>bc</i>d")]
    [InlineData("{\\i1}", "{\\i0}", "a{\\i1}bc{\\i0}d")]
    public void Emit_ReinsertsMarkersAtSpanBoundaries(string open, string close, string expected)
    {
        var result = ItalicMarkup.Emit("abcd", new[] { new ItalicSpan(1, 2) }, open, close);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Emit_NoSpans_ReturnsTextUnchanged()
        => Assert.Equal("abcd", ItalicMarkup.Emit("abcd", System.Array.Empty<ItalicSpan>(), "<i>", "</i>"));

    // Parse then emit with the same tags reproduces the original inline markup.
    [Theory]
    [InlineData("The <i>Enterprise</i> is here.")]
    [InlineData("<i>All italic.</i>")]
    [InlineData("No italics at all.")]
    [InlineData("Line one\n<i>line two</i>")]
    public void ParseThenEmit_RoundTrips(string inline)
    {
        var (text, spans) = ItalicMarkup.Parse(inline);

        Assert.Equal(inline, ItalicMarkup.Emit(text, spans, "<i>", "</i>"));
    }
}
