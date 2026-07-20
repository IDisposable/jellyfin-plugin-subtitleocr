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

    // A one between two letters is a misread l/I; case follows the neighbors.
    [Theory]
    [InlineData("wi1l you", "will you")]
    [InlineData("N1CE", "NICE")]
    public void Fix_OneBetweenLetters_BecomesLOrI(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // A one that is a real digit keeps its value: no letter on both sides.
    [Theory]
    [InlineData("Room 101")]
    [InlineData("B12")]
    [InlineData("2001")]
    public void Fix_OneAsDigit_IsLeftAlone(string input)
    {
        Assert.Equal(input, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // The splitter parts an enclitic contraction at its apostrophe; rejoin only the closed enclitics.
    [Theory]
    [InlineData("do n't", "don't")]
    [InlineData("I 've got it", "I've got it")]
    [InlineData("it 's here", "it's here")]
    public void Fix_SplitContraction_Rejoins(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // A leading-apostrophe word legitimately follows a space and must not be joined to the prior word.
    [Theory]
    [InlineData("get 'em")]
    [InlineData("just 'cause")]
    public void Fix_LeadingApostropheWord_IsLeftAlone(string input)
    {
        Assert.Equal(input, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // The splitter opens a gap before narrow punctuation; no orthography writes a space before a comma or period.
    [Theory]
    [InlineData("Hello , world .", "Hello, world.")]
    [InlineData("the end .", "the end.")]
    public void Fix_SpaceBeforePunctuation_IsClosed(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // A dot run stays a dot run: the period rule never touches a dot beside another dot.
    [Fact]
    public void Fix_SpaceBeforePeriod_LeavesDotRuns()
    {
        Assert.Equal("Wait . ..", OcrPostProcessor.Fix("Wait . ..", English, Placeholder, normalizeEllipsis: false));
    }

    // A speaker dash at the line start gets its space; a mid-word hyphen and an em dash do not.
    [Theory]
    [InlineData("-Yeah.\n-No.", "- Yeah.\n- No.")]
    [InlineData("well-known", "well-known")]
    [InlineData("—and then", "—and then")]
    public void Fix_LeadingSpeakerDash_GetsSpace(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // Trailing whitespace on an inner line of a multi-line cue is removed.
    [Fact]
    public void Fix_TrailingLineWhitespace_IsTrimmed()
    {
        Assert.Equal("Hello\nworld", OcrPostProcessor.Fix("Hello \nworld", English, Placeholder, normalizeEllipsis: false));
    }

    // Typographic quotes fold to the straight forms; guillemets are a real quote mark and are left alone.
    [Theory]
    [InlineData("don’t", "don't")]
    [InlineData("“The end.”", "\"The end.\"")]
    [InlineData("‘‘go’’", "\"go\"")]
    [InlineData("«Bonjour»", "«Bonjour»")]
    public void Fix_TypographicQuotes_FoldToStraight(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // The aggressive digit twins fire only when opted in; case follows the neighbors.
    [Theory]
    [InlineData("cla5s", "class")]
    [InlineData("TA8LE", "TABLE")]
    public void Fix_AggressiveDigitTwins_WhenEnabled(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false, protectedWords: null, foldForeignDiacritics: true, aggressiveFixes: true));
    }

    // Off by default, so a real digit between letters is untouched.
    [Theory]
    [InlineData("cla5s")]
    [InlineData("R2D2")]
    public void Fix_AggressiveDigitTwins_OffByDefault(string input)
    {
        Assert.Equal(input, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // A single apostrophe (contraction, possessive) is not a quote.
    [Theory]
    [InlineData("don't")]
    [InlineData("Wanda's")]
    public void Fix_SingleApostrophe_IsLeftAlone(string input)
    {
        Assert.Equal(input, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // English has no diacritics, so accented Latin letters there are misreads and fold to their base.
    [Theory]
    [InlineData("Nothińg", "Nothing")]
    [InlineData("ćafe över there", "cafe over there")]
    [InlineData("naïve résumé", "naive resume")]
    // Stroked letters and ligatures carry no combining mark to drop, so they are mapped by hand.
    [InlineData("cłan", "clan")]
    [InlineData("øver the møon", "over the moon")]
    [InlineData("æther", "aether")]
    public void Fix_AccentedLatinInEnglish_FoldsToBase(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // Codifies the DOCS/orthography.md table: each language's own accented letters survive the fold. If the
    // LanguageDiacritics set for a language drops one of these, that letter would fold here and this fails, so
    // the code table, the docs table, and this test stay in lock step.
    [Theory]
    [InlineData("fra", "àâçéèêëîïôùûüÿœ")]
    [InlineData("deu", "äöüß")]
    [InlineData("spa", "áéíóúüñ")]
    [InlineData("por", "áâãàçéêíóôõú")]
    [InlineData("ita", "àèéìòóù")]
    [InlineData("nld", "áéíóúëïöü")]
    [InlineData("swe", "åäöé")]
    [InlineData("nob", "æøåé")]
    [InlineData("dan", "æøåé")]
    [InlineData("fin", "äöå")]
    [InlineData("isl", "áéíóúýþæöð")]
    [InlineData("pol", "ąćęłńóśźż")]
    [InlineData("ces", "áčďéěíňóřšťúůýž")]
    [InlineData("slk", "áäčďéíĺľňóôŕšťúýž")]
    [InlineData("hun", "áéíóöőúüű")]
    [InlineData("ron", "ăâîșț")]
    [InlineData("hrv", "čćđšž")]
    [InlineData("slv", "čšž")]
    [InlineData("tur", "çğıöşü")]
    [InlineData("cat", "àéèíïóòúüçŀ")]
    [InlineData("est", "äöõüšž")]
    [InlineData("lav", "āčēģīķļņšūž")]
    [InlineData("lit", "ąčęėįšųūž")]
    public void Fix_LegalAccentsForLanguage_AllSurvive(string language, string legalAccents)
    {
        Assert.Equal(legalAccents, OcrPostProcessor.Fix(legalAccents, language, Placeholder, normalizeEllipsis: false));
    }

    // The counterpart: an accent none of these languages writes folds away everywhere.
    [Theory]
    [InlineData("fra")]
    [InlineData("deu")]
    [InlineData("swe")]
    public void Fix_AccentForeignToEveryTestedLanguage_Folds(string language)
    {
        // Vietnamese ơ is in no European legal set, so it always folds to o.
        Assert.Equal("o", OcrPostProcessor.Fix("ơ", language, Placeholder, normalizeEllipsis: false));
    }

    // The two Unicode forms of s-comma / t-comma are canonicalized to the track language's own before the
    // fold, so a file encoded in the other convention keeps the letter instead of folding it to a bare s/t.
    [Theory]
    // Romanian standard is comma-below; the cedilla lookalikes rewrite to it.
    [InlineData("ron", "Iaşi Constanţa", "Iași Constanța")]
    [InlineData("ron", "Iași Constanța", "Iași Constanța")]
    // Turkish standard is the cedilla; a comma-below form rewrites to it.
    [InlineData("tur", "kuș yaș", "kuş yaş")]
    [InlineData("tur", "kuş yaş", "kuş yaş")]
    public void Fix_AmbiguousCommaCedilla_CanonicalizesToLanguageForm(string language, string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, language, Placeholder, normalizeEllipsis: false));
    }

    // An accent foreign to the track's language is a misread and folds, while that language's own accents stay:
    // Swedish writes å and ö, but not the Portuguese ã.
    [Fact]
    public void Fix_ForeignAccentInLatinLanguage_FoldsButKeepsLegalOnes()
    {
        Assert.Equal("Håkan Sao", OcrPostProcessor.Fix("Håkan São", "swe", Placeholder, normalizeEllipsis: false));
    }

    // A language absent from the legal-accent table cannot be judged, so nothing is folded.
    [Fact]
    public void Fix_UnknownLatinLanguage_FoldsNothing()
    {
        Assert.Equal("São Håkan", OcrPostProcessor.Fix("São Håkan", "afr", Placeholder, normalizeEllipsis: false));
    }

    // The fold is a setting; off, even an English accent survives.
    [Fact]
    public void Fix_FoldDisabled_KeepsAccents()
    {
        Assert.Equal(
            "Nothińg",
            OcrPostProcessor.Fix("Nothińg", English, Placeholder, normalizeEllipsis: false, protectedWords: null, foldForeignDiacritics: false));
    }

    // A cast or character name whose accent is real (it is in the metadata) is not a misread, so it survives
    // the English fold; an ordinary accented word beside it still folds.
    [Fact]
    public void Fix_AccentedProperNoun_IsProtectedFromFold()
    {
        var protectedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "José" };
        Assert.Equal(
            "José and Renee cafe",
            OcrPostProcessor.Fix("José and Renée café", English, Placeholder, normalizeEllipsis: false, protectedWords));
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

    // Padding belongs outside the tags; a lone letter then collapses, closing the run entirely.
    [Theory]
    [InlineData("Sausag<i>e </i>Lover", "Sausage Lover")]
    [InlineData("purs<i>e </i>kitchen", "purse kitchen")]
    public void Fix_SpaceBeforeItalicClose_MovesOutAndCollapsesLoneLetter(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }

    // A real multi-letter run keeps its tags; only the padding moves out.
    [Theory]
    [InlineData("He said<i> no</i>", "He said <i>no</i>")]
    [InlineData("<i>Galactica </i>lives", "<i>Galactica</i> lives")]
    public void Fix_PaddedItalicRun_TrimsPaddingButKeepsTags(string input, string expected)
    {
        Assert.Equal(expected, OcrPostProcessor.Fix(input, English, Placeholder, normalizeEllipsis: false));
    }
}
