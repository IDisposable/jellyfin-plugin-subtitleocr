using SubtitleOcr.Core.NOcr;
using SubtitleOcr.Core.Ocr;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class OcrPostProcessorTests
{
    private const char Placeholder = NOcrEngineOptions.DefaultUnknownCharacter;
    private const string English = "eng";
    private const string Cyrillic = "rus";

    [Theory]
    [InlineData("What do l know? lt began.", "What do I know? It began.")]
    public void Fix_CorrectsLowerLToCapitalI(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // A speaker label's colon starts a sentence, so the l after it is an I.
    [Theory]
    [InlineData("STARBUCK: lt's a girl.", "STARBUCK: It's a girl.")]
    [InlineData("BALTAR:\nlt began.", "BALTAR:\nIt began.")]
    [InlineData("FEEBLE ANNOUNCER: lt is.", "FEEBLE ANNOUNCER: It is.")]
    public void Fix_LowercaseLAfterSpeakerLabel_BecomesCapitalI(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // An ordinary colon mid-sentence is not a speaker label, and the word after it is a real word.
    [Theory]
    [InlineData("One thing: let me finish.")]
    [InlineData("He said: look out.")]
    public void Fix_LowercaseLAfterAnOrdinaryColon_IsLeftAlone(string input)
    {
        Assert.Equal(input, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // The two-letter function words with a lowercase l for the I; no Latin word is "lt"/"ls"/"ln"/"lf".
    [Theory]
    [InlineData("lt is?", "It is?")]
    [InlineData("ls there anybody?", "Is there anybody?")]
    [InlineData("ln case you forgot.", "In case you forgot.")]
    [InlineData("lf you say so.", "If you say so.")]
    [InlineData("- lt was the dragon.", "- It was the dragon.")]
    public void Fix_TwoLetterIl_BecomesCapitalI(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // TwoLetterIl maps only a bare two-letter pair, not an l-word that merely starts with those letters.
    // Mid-sentence so the separate sentence-initial rule is not in play.
    [Theory]
    [InlineData("the list of names")]
    [InlineData("he lost lots")]
    [InlineData("an elf and a kiln")]
    [InlineData("a lift home")]
    public void Fix_LWordThatIsNotAFunctionPair_IsLeftAlone(string input)
    {
        Assert.Equal(input, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // A zero between two letters is a misread o; case follows the neighbors.
    [Theory]
    [InlineData("y0u kn0w", "you know")]
    [InlineData("l0ok ab0ut", "look about")]
    [InlineData("N0T N0W", "NOT NOW")]
    [InlineData("iP0d", "iPod")]
    public void Fix_ZeroBetweenLetters_BecomesO(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // A zero that is a real digit keeps its value: no letter on both sides.
    [Theory]
    [InlineData("Room 302")]
    [InlineData("2001")]
    [InlineData("R2D2")]
    [InlineData("Level 0")]
    public void Fix_ZeroAsDigit_IsLeftAlone(string input)
    {
        Assert.Equal(input, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // A split double quote, regardless of script.
    [Theory]
    [InlineData("''The end.''", "\"The end.\"")]
    [InlineData("He said ''go''.", "He said \"go\".")]
    public void Fix_DoubleApostrophe_BecomesDoubleQuote(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
        Assert.Equal(expected, OcrPostProcessor.Fix(input, Cyrillic, Placeholder, normalizeEllipsis: false));
    }

    // Word spacing opens a gap inside a narrow bracket; SDH cues are tight.
    [Theory]
    [InlineData("[ Sighs ]", "[Sighs]")]
    [InlineData("( Camera clicks )", "(Camera clicks)")]
    [InlineData("[Door opens]", "[Door opens]")]
    public void Fix_BracketPadding_IsTightened(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // A single apostrophe (contraction, possessive) is not a quote.
    [Theory]
    [InlineData("don't")]
    [InlineData("Wanda's")]
    public void Fix_SingleApostrophe_IsLeftAlone(string input)
    {
        Assert.Equal(input, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    [Fact]
    public void Fix_NonLatinScript_SkipsLatinHeuristicsButNormalizesWhitespace()
    {
        // The l/I and pipe substitutions must not touch non-Latin text; whitespace still collapses.
        var input = "l am  a|b";
        Assert.Equal("l am a|b", OcrPostProcessor.Fix(input, Cyrillic, Placeholder, normalizeEllipsis: false));
    }

    // A placeholder in a contraction slot is a misread apostrophe.
    [Fact]
    public void Fix_PlaceholderInContraction_BecomesApostrophe()
    {
        Assert.Equal("it's", OcrPostProcessor.Fix($"it{Placeholder}s", English, Placeholder, normalizeEllipsis: false));
    }

    [Fact]
    public void Fix_CustomPlaceholderInContraction_BecomesApostrophe()
    {
        Assert.Equal("don't", OcrPostProcessor.Fix("don#t", English, '#', normalizeEllipsis: false));
    }

    [Theory]
    [InlineData("Well... I don't know.", "Well… I don't know.")]
    [InlineData("Well . . . maybe", "Well… maybe")]
    [InlineData("Wait..", "Wait…")]
    [InlineData("...and then", "…and then")]
    [InlineData("Stop. Now.", "Stop. Now.")]
    public void Fix_NormalizeEllipsis_FoldsDotRuns(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: true));
    }

    [Fact]
    public void Fix_NormalizeEllipsisOff_LeavesDotRuns()
    {
        Assert.Equal("Well... maybe", OcrPostProcessor.Fix("Well... maybe", English, Placeholder, normalizeEllipsis: false));
    }

    [Fact]
    public void Fix_NormalizeEllipsis_AppliesToNonLatinScripts()
    {
        Assert.Equal("Да…", OcrPostProcessor.Fix("Да...", Cyrillic, Placeholder, normalizeEllipsis: true));
    }

    // Upper and lower forms of these letters are one shape at two sizes, so the matcher may downcase them
    // in an all-caps word. A bar glyph among capitals is "I".
    [Theory]
    [InlineData("(cLocK TlcKlNG)", "(CLOCK TICKING)")]
    [InlineData("TlcKlNG", "TICKING")]
    [InlineData("(EXPLOSIONS)", "(EXPLOSIONS)")]
    public void Fix_DowncasedAllCapsWord_IsRestored(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // Genuine mixed case must survive: a letter that is not a size twin proves the word is not all-caps,
    // and one capital is not enough to call it.
    [Theory]
    [InlineData("Galactica")]
    [InlineData("McDonald")]
    [InlineData("iPhone")]
    [InlineData("Ox")]
    [InlineData("I love you")]
    public void Fix_GenuineMixedCase_IsLeftAlone(string input)
    {
        Assert.Equal(input, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // A one-character run is a misclassification, and it splits the word for every stage after it.
    [Theory]
    [InlineData("<i>Q</i>uietly", "Quietly")]
    [InlineData("<i>.</i>", ".")]
    [InlineData("<i></i>", "")]
    [InlineData("word<i>,</i> next", "word, next")]
    public void Fix_SingleCharacterItalicRun_IsDropped(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    [Fact]
    public void Fix_ItalicPunctuationRun_CollapsesAfterEllipsisFold()
    {
        Assert.Equal("…", OcrPostProcessor.Fix("<i>...</i>", English, Placeholder, normalizeEllipsis: true));
    }

    // A real italic run must survive.
    [Fact]
    public void Fix_MultiCharacterItalicRun_IsKept()
    {
        Assert.Equal("<i>Galactica</i>", OcrPostProcessor.Fix("<i>Galactica</i>", English, Placeholder, normalizeEllipsis: false));
    }
}
