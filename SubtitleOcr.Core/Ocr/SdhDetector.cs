using System.Text.RegularExpressions;

namespace SubtitleOcr.Core.Ocr;

/// <summary>
/// Identifies a hearing-impaired (SDH) track from its recognized text, for the common remux that carries no
/// disposition flags. SDH describes sound ("[engine roaring]", "(EXPLOSIONS)"); dialogue does not, so the two
/// separate on how often a cue opens a bracket.
/// </summary>
public static partial class SdhDetector
{
    /// <summary>Measured: an SDH track runs 10-25%, a dialogue track effectively 0%.</summary>
    public const double DefaultRatio = 0.05;

    /// <summary>Below this the ratio is noise.</summary>
    public const int DefaultMinimumCues = 20;

    // A sound cue opens its line with the bracket ("[DOOR SLAMS]", "MAN: [shouting]"), so anchor to the line
    // start past an optional speaker dash and an all-caps speaker label. An inline aside ("Come here (John).")
    // is dialogue, not SDH, and does not count. The closer is not required: OCR drops it often enough that a
    // pair would lose real matches.
    [GeneratedRegex(@"(?m)^[ \t]*[-–—]?[ \t]*(?:\p{Lu}[\p{Lu} ]*:[ \t]*)?[\[(][ \t]*\p{L}")]
    private static partial Regex SoundCue();

    // Italic tags are presentation, not content; strip them so detection reads the words, not the markup.
    [GeneratedRegex(@"</?i>")]
    private static partial Regex ItalicTag();

    /// <summary>Whether these cue texts look like an SDH track. Pass <see cref="DefaultRatio"/> and
    /// <see cref="DefaultMinimumCues"/> unless a caller has a reason to differ.</summary>
    public static bool IsHearingImpaired(IReadOnlyList<string> texts, double ratio, int minimumCues)
    {
        if (texts.Count < minimumCues)
        {
            return false;
        }

        var withSoundCue = 0;
        foreach (var text in texts)
        {
            if (SoundCue().IsMatch(ItalicTag().Replace(text, string.Empty)))
            {
                withSoundCue++;
            }
        }

        return (double)withSoundCue / texts.Count >= ratio;
    }
}
