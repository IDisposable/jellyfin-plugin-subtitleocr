using SubtitleOcr.Core.NOcr;
using SubtitleOcr.Core.Ocr;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class OcrPostProcessorTests
{
    private const char Placeholder = NOcrEngineOptions.DefaultUnknownCharacter;

    [Theory]
    [InlineData("What do l know? lt began.", "What do I know? It began.")]
    public void Fix_CorrectsLowerLToCapitalI(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, latinScript: true, Placeholder, normalizeEllipsis: false));
    }

    [Fact]
    public void Fix_NonLatinScript_SkipsLatinHeuristicsButNormalizesWhitespace()
    {
        // The l/I and pipe substitutions must not touch non-Latin text; whitespace still collapses.
        var input = "l am  a|b";
        Assert.Equal("l am a|b", OcrPostProcessor.Fix(input, latinScript: false, Placeholder, normalizeEllipsis: false));
    }

    // A placeholder in a contraction slot is a misread apostrophe.
    [Fact]
    public void Fix_PlaceholderInContraction_BecomesApostrophe()
    {
        Assert.Equal("it's", OcrPostProcessor.Fix($"it{Placeholder}s", latinScript: true, Placeholder, normalizeEllipsis: false));
    }

    [Fact]
    public void Fix_CustomPlaceholderInContraction_BecomesApostrophe()
    {
        Assert.Equal("don't", OcrPostProcessor.Fix("don#t", latinScript: true, '#', normalizeEllipsis: false));
    }

    [Theory]
    [InlineData("Well... I don't know.", "Well… I don't know.")]
    [InlineData("Well . . . maybe", "Well… maybe")]
    [InlineData("Wait..", "Wait…")]
    [InlineData("...and then", "…and then")]
    [InlineData("Stop. Now.", "Stop. Now.")]
    public void Fix_NormalizeEllipsis_FoldsDotRuns(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, latinScript: true, Placeholder, normalizeEllipsis: true));
    }

    [Fact]
    public void Fix_NormalizeEllipsisOff_LeavesDotRuns()
    {
        Assert.Equal("Well... maybe", OcrPostProcessor.Fix("Well... maybe", latinScript: true, Placeholder, normalizeEllipsis: false));
    }

    [Fact]
    public void Fix_NormalizeEllipsis_AppliesToNonLatinScripts()
    {
        Assert.Equal("Да…", OcrPostProcessor.Fix("Да...", latinScript: false, Placeholder, normalizeEllipsis: true));
    }

    // A one-character run is a misclassification, and it splits the word for every stage after it.
    [Theory]
    [InlineData("<i>Q</i>uietly", "Quietly")]
    [InlineData("<i>.</i>", ".")]
    [InlineData("<i></i>", "")]
    [InlineData("word<i>,</i> next", "word, next")]
    public void Fix_SingleCharacterItalicRun_IsDropped(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, latinScript: true, Placeholder, normalizeEllipsis: false));
    }

    [Fact]
    public void Fix_ItalicPunctuationRun_CollapsesAfterEllipsisFold()
    {
        Assert.Equal("…", OcrPostProcessor.Fix("<i>...</i>", latinScript: true, Placeholder, normalizeEllipsis: true));
    }

    // A real italic run must survive.
    [Fact]
    public void Fix_MultiCharacterItalicRun_IsKept()
    {
        Assert.Equal("<i>Galactica</i>", OcrPostProcessor.Fix("<i>Galactica</i>", latinScript: true, Placeholder, normalizeEllipsis: false));
    }
}
