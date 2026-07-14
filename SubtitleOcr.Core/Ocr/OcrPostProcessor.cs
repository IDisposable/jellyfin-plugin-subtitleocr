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
    /// Cleans up OCR output. The l/I, X/V, pipe, and apostrophe fixes are Latin-script heuristics and would
    /// corrupt other scripts, so <paramref name="latinScript"/> gates them (pass
    /// <see cref="LanguageCodes.IsLatinScript"/>); the rest always runs. <paramref name="unknownCharacter"/>
    /// is the placeholder emitted for unmatched glyphs.
    /// </summary>
    public static string Fix(string text, bool latinScript, char unknownCharacter, bool normalizeEllipsis)
    {
        if (latinScript)
        {
            // A lone or sentence-initial "l" is "I"; a pipe is a segmentation artifact of one.
            text = LoneLowercaseL().Replace(text, "I");
            text = SentenceInitialL().Replace(text, "I");
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
