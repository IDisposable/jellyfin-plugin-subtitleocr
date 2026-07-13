using SubtitleOcr.Core.Ocr;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class OcrPostProcessorTests
{
    [Theory]
    [InlineData("What do l know? lt began.", "What do I know? It began.")]
    public void Fix_CorrectsLowerLToCapitalI(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input));
    }

    [Fact]
    public void Fix_NonLatinScript_SkipsLatinHeuristicsButNormalizesWhitespace()
    {
        // The l/I and pipe substitutions must not touch non-Latin text; whitespace still collapses.
        var input = "l am  a|b";
        Assert.Equal("l am a|b", OcrPostProcessor.Fix(input, latinScript: false));
    }
}
