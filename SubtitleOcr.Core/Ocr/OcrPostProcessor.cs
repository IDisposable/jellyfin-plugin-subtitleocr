using System.Text.RegularExpressions;

namespace SubtitleOcr.Core.Ocr;

/// <summary>
/// Conservative fixes for the classic dvdsub OCR confusions. Deliberately small:
/// aggressive dictionary correction belongs in a later pass (or Subtitle Edit itself).
/// </summary>
public static partial class OcrPostProcessor
{
    [GeneratedRegex(@"\bl\b")]
    private static partial Regex LoneLowercaseL();

    [GeneratedRegex(@"(?<=[.!?]\s|^)l(?=[a-z])", RegexOptions.Multiline)]
    private static partial Regex SentenceInitialL();

    [GeneratedRegex(@"\|")]
    private static partial Regex Pipe();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex RepeatedSpaces();

    /// <summary>
    /// Cleans up OCR output. The l/I and pipe substitutions are English/Latin heuristics and would
    /// corrupt other scripts, so they are skipped when <paramref name="latinScript"/> is false;
    /// whitespace normalization always runs. Pass the stream language through
    /// <see cref="LanguageCodes.IsLatinScript"/>.
    /// </summary>
    public static string Fix(string text, bool latinScript = true)
    {
        if (latinScript)
        {
            // Lone "l" as a word is essentially always "I".
            text = LoneLowercaseL().Replace(text, "I");

            // Sentence-initial "l" followed by lowercase ("lt was...") is "I".
            text = SentenceInitialL().Replace(text, "I");

            // Pipes never occur in dialogue; segmentation artifacts of I/l.
            text = Pipe().Replace(text, "I");
        }

        text = RepeatedSpaces().Replace(text, " ");

        return text.Trim();
    }
}
