using SubtitleOcr.Core.NOcr;
using SubtitleOcr.Core.Ocr;
using SubtitleOcr.Core.Output;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class SentenceCaseTests
{
    private const char Placeholder = NOcrEngineOptions.DefaultUnknownCharacter;

    private static List<string> Apply(params string[] texts)
    {
        var events = texts.Select(t => new SubtitleEvent { Text = t }).ToList();
        SentenceCase.Apply(events, Placeholder);
        return events.ConvertAll(e => e.Text);
    }

    // A track opens on a sentence, so its first cue is capitalized with nothing before it.
    [Fact]
    public void Apply_FirstCue_IsCapitalized()
    {
        Assert.Equal(new[] { "Come on." }, Apply("come on."));
    }

    [Theory]
    [InlineData("He left.")]
    [InlineData("Get out!")]
    [InlineData("Who is it?")]
    public void Apply_AfterTerminalPunctuation_Capitalizes(string previous)
    {
        Assert.Equal(previous, Apply(previous, "come on.")[0]);
        Assert.Equal("Come on.", Apply(previous, "come on.")[1]);
    }

    // A sound cue in brackets is self-contained: whatever came before it, the next line starts a sentence.
    [Fact]
    public void Apply_AfterAClosingBracket_Capitalizes()
    {
        Assert.Equal("Come on.", Apply("(CLOCK TICKING)", "come on.")[1]);
    }

    // The line was deliberately left unfinished, so the next cue continues it and its first word is lowercase.
    [Theory]
    [InlineData("Well...")]
    [InlineData("Well…")]
    [InlineData("Well . . .")]
    public void Apply_AfterAContinuation_LeavesItAlone(string previous)
    {
        Assert.Equal("and then we left.", Apply(previous, "and then we left.")[1]);
    }

    // An unread glyph tells us nothing, including whether it was a full stop.
    [Fact]
    public void Apply_AfterAPlaceholder_LeavesItAlone()
    {
        Assert.Equal("come on.", Apply($"I saw the{Placeholder}", "come on.")[1]);
    }

    [Fact]
    public void Apply_MidSentence_LeavesItAlone()
    {
        Assert.Equal("and we ran.", Apply("He turned around", "and we ran.")[1]);
    }

    // The markup is not the sentence: the letter inside it is.
    [Fact]
    public void Apply_ItalicOpeningTag_CapitalizesTheLetterInside()
    {
        Assert.Equal("<i>come on.</i>", Apply("He left", "<i>come on.</i>")[1]);
        Assert.Equal("<i>Come on.</i>", Apply("He left.", "<i>come on.</i>")[1]);
    }

    // Terminal punctuation still terminates with markup after it.
    [Fact]
    public void Apply_TerminalPunctuationInsideItalics_StillCounts()
    {
        Assert.Equal("Come on.", Apply("<i>He left.</i>", "come on.")[1]);
    }

    // A dash opens a second speaker's line; the sentence starts after it.
    [Fact]
    public void Apply_LeadingDash_CapitalizesAfterIt()
    {
        Assert.Equal("- Come on.", Apply("He left.", "- come on.")[1]);
    }

    [Fact]
    public void Apply_LeadingQuote_CapitalizesInsideIt()
    {
        Assert.Equal("\"Come on.\"", Apply("He left.", "\"come on.\"")[1]);
    }

    // A quoted sentence ends the sentence: the quote wraps the full stop.
    [Fact]
    public void Apply_QuotedTerminalPunctuation_Counts()
    {
        Assert.Equal("Come on.", Apply("He said \"go away.\"", "come on.")[1]);
    }

    [Fact]
    public void Apply_AlreadyCapitalized_IsUnchanged()
    {
        Assert.Equal(new[] { "Come on.", "Get out." }, Apply("Come on.", "Get out."));
    }

    [Fact]
    public void Apply_EmptyCue_DoesNotThrow()
    {
        Assert.Equal(new[] { "", "" }, Apply("", ""));
    }
}
