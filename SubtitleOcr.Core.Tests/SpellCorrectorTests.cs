using SubtitleOcr.Core.NOcr;
using SubtitleOcr.Core.Ocr;
using WeCantSpell.Hunspell;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class SpellCorrectorTests
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private static readonly ISpellCorrector Corrector = SpellCorrector.FromWordList(
        WordList.CreateFromWords(new[]
        {
            "prove", "explosions", "galactica", "still", "form", "follows", "function", "world",
            "quietly", "battle", "quality",
        }),
        NOcrEngineOptions.DefaultUnknownCharacter);

    [Theory]
    [InlineData("EXPLoSloNS", "EXPLOSIONS")] // mostly-upper misread restores to all caps
    [InlineData("galaxtica", "galactica")]   // single-letter misread corrected
    [InlineData("prove", "prove")]           // valid word untouched
    [InlineData("Prove", "Prove")]           // valid (capitalized) word untouched
    [InlineData("Xyzzyx", "Xyzzyx")]         // no close suggestion -> untouched
    [InlineData("Battlestar", "Battlestar")] // Title-case proper noun -> not "corrected"
    [InlineData("form follows function", "form follows function")] // valid sentence untouched
    public void Correct_ReturnsExpected(string input, string expected)
        => Assert.Equal(expected, Corrector.Correct(input, None));

    // A dictionary does not know names, and all-caps is where they concentrate: speaker labels. The case
    // fixes upstream already repair a downcased sound cue, so nothing here needs to touch all-caps.
    [Theory]
    [InlineData("TYROL")]
    [InlineData("GALACTICA")]
    [InlineData("BATTLESTAR")]
    public void Correct_AllCapsWord_IsLeftAlone(string input)
        => Assert.Equal(input, Corrector.Correct(input, None));

    [Fact]
    public void Correct_PreservesItalicTags()
        => Assert.Equal("<i>galactica</i>", Corrector.Correct("<i>galaxtica</i>", None));

    [Fact]
    public void Correct_ProtectedWord_IsUnchanged()
        => Assert.Equal("galaxtica", Corrector.Correct("galaxtica", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "galaxtica" }));

    // The dictionary must see the whole word, or it corrects the fragment "uietly" into "quietly".
    [Fact]
    public void Correct_ItalicTagInsideWord_DoesNotCorrectTheFragment()
        => Assert.Equal("<i>Q</i>uietly", Corrector.Correct("<i>Q</i>uietly", None));

    // An italic sentence whose first word needs fixing must keep its opening tag.
    [Fact]
    public void Correct_WordOpeningAnItalicRun_KeepsTheTag()
        => Assert.Equal("<i>galactica is here</i>", Corrector.Correct("<i>galaxtica is here</i>", None));

    // The unknown-glyph placeholder reads as any character, so it costs nothing in the distance.
    [Theory]
    [InlineData("batt□e", "battle")]
    [InlineData("qua□ity", "quality")]
    [InlineData("expl□sions", "explosions")]
    public void Correct_PlaceholderIsAFreeWildcard(string input, string expected)
        => Assert.Equal(expected, Corrector.Correct(input, None));

    // Too little left to be sure of; better a visible placeholder than a wrong guess.
    [Theory]
    [InlineData("b□□□le")]
    [InlineData("q□a□i□y")]
    public void Correct_MostlyPlaceholders_IsLeftAlone(string input)
        => Assert.Equal(input, Corrector.Correct(input, None));

    // How a name the dictionary does not know survives OCR damage.
    [Fact]
    public void Correct_PlaceholderMatchesProtectedWord_IsRestored()
        => Assert.Equal("Galactica", Corrector.Correct("Ga□actica", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Galactica" }));
}
