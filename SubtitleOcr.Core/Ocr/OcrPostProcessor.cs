using System.Text.RegularExpressions;
using SubtitleOcr.Core.NOcr;

namespace SubtitleOcr.Core.Ocr;

/// <summary>
/// Character-level fixes for the classic OCR confusions. Dictionary correction is a separate stage
/// (<see cref="SpellCorrector"/>, <see cref="OcrFixReplaceList"/>) and runs after this.
/// </summary>
public static partial class OcrPostProcessor
{
    [GeneratedRegex(@"\bl\b")]
    private static partial Regex LoneLowercaseL();

    [GeneratedRegex(@"(?<=[.!?]\s|^)l(?=[a-z])", RegexOptions.Multiline)]
    private static partial Regex SentenceInitialL();

    [GeneratedRegex(@"\|")]
    private static partial Regex Pipe();

    // Letters whose upper and lower forms are one shape at two sizes. The matcher scales a trained glyph
    // to the candidate, so it cannot tell them apart and may pick either case.
    private const string SizeTwins = "cosuvwxz";

    /// <summary>The l-to-I rules hold for English only.</summary>
    private const string English = "eng";

    [GeneratedRegex(@"\p{L}[\p{L}']*")]
    private static partial Regex Word();

    // A capital X/V/I after a lowercase letter is the lowercase glyph, same shape but larger.
    [GeneratedRegex(@"(?<=\p{Ll})X")]
    private static partial Regex MidWordX();

    [GeneratedRegex(@"(?<=\p{Ll})V")]
    private static partial Regex MidWordV();

    // The whole run at once; matched one at a time, the lookbehind still sees the original uppercase.
    [GeneratedRegex(@"(?<=\p{Ll})I+")]
    private static partial Regex MidWordI();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex RepeatedSpaces();

    // Two or more dots, however spaced. The leading [ \t]* closes up the space before them.
    [GeneratedRegex(@"[ \t]*\.[ \t]*\.(?:[ \t]*\.)*")]
    private static partial Regex DotRun();

    // Nothing is italic for exactly one character, and the tags split the word for later stages.
    [GeneratedRegex(@"<i>([^<]?)</i>")]
    private static partial Regex SingleCharacterItalic();

    // The placeholder is only known at run time, so the pattern cannot be source-generated. Built for
    // the default on first touch of the class, rebuilt only for a different one.
    private static ContractionFix _contractionFix = ContractionFix.For(NOcrEngineOptions.DefaultUnknownCharacter);

    private sealed record ContractionFix(char Placeholder, Regex Pattern)
    {
        public static ContractionFix For(char placeholder) => new(
            placeholder,
            new Regex(
                $@"(?<=\p{{L}}){Regex.Escape(placeholder.ToString())}(?=(s|t|ll|ve|re|d|m)\b)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled));
    }

    private static Regex ContractionPattern(char unknownCharacter)
    {
        var cached = _contractionFix;
        if (cached.Placeholder != unknownCharacter)
        {
            cached = ContractionFix.For(unknownCharacter);
            _contractionFix = cached;
        }

        return cached.Pattern;
    }

    /// <summary>
    /// A word carrying two or more capitals whose every lowercase letter is a size twin is an all-caps word
    /// the matcher downcased ("cLocK" is "CLOCK"). A bar glyph among capitals is "I", never "l".
    /// </summary>
    private static string RestoreAllCaps(string text) => Word().Replace(text, static m =>
    {
        var word = m.Value;
        var capitals = 0;
        foreach (var c in word)
        {
            if (char.IsUpper(c))
            {
                capitals++;
            }
            else if (char.IsLower(c) && c != 'l' && !SizeTwins.Contains(c, StringComparison.Ordinal))
            {
                return word;
            }
        }

        if (capitals < 2)
        {
            return word;
        }

        return string.Create(word.Length, word, static (span, w) =>
        {
            for (var i = 0; i < w.Length; i++)
            {
                span[i] = w[i] == 'l' ? 'I' : char.ToUpperInvariant(w[i]);
            }
        });
    });

    /// <summary>
    /// Cleans up OCR output. Most fixes are Latin-script heuristics and would corrupt another script, and a
    /// couple are English facts that would corrupt another Latin language, so
    /// <paramref name="normalizedLanguage"/> (a code from <see cref="LanguageCodes.Normalize"/>) picks which
    /// run. <paramref name="unknownCharacter"/> is the placeholder emitted for unmatched glyphs.
    /// </summary>
    public static string Fix(string text, string normalizedLanguage, char unknownCharacter, bool normalizeEllipsis)
    {
        if (LanguageCodes.IsLatinScript(normalizedLanguage))
        {
            // Before the mid-word rules, which would otherwise read a downcased twin as real lowercase.
            text = RestoreAllCaps(text);

            // "A lone l is I" is true of English only. French elides the article ("l'un") and starts lines
            // with it ("la", "les"), so these would rewrite every one of them.
            if (string.Equals(normalizedLanguage, English, StringComparison.OrdinalIgnoreCase))
            {
                text = LoneLowercaseL().Replace(text, "I");
                text = SentenceInitialL().Replace(text, "I");
            }

            // Pipes never occur in dialogue; they are a segmentation artifact of I or l.
            text = Pipe().Replace(text, "I");

            text = MidWordX().Replace(text, "x");
            text = MidWordV().Replace(text, "v");
            text = MidWordI().Replace(text, static m => new string('l', m.Length));

            // A placeholder in a contraction slot ("it□s", "don□t") is a misread apostrophe.
            text = ContractionPattern(unknownCharacter).Replace(text, "'");
        }

        if (normalizeEllipsis)
        {
            text = DotRun().Replace(text, "…");
        }

        // After the fold, so "<i>...</i>" collapses to "…" and not "<i>…</i>".
        text = SingleCharacterItalic().Replace(text, "$1");

        text = RepeatedSpaces().Replace(text, " ");

        return text.Trim();
    }
}
