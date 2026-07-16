using System.Text.RegularExpressions;
using SubtitleOcr.Core.Output;

namespace SubtitleOcr.Core.Ocr;

/// <summary>
/// Capitalizes a cue that begins a sentence. The matcher cannot tell a lowercase size twin from its capital
/// ("cOME ON" for "COME ON"), and the classifier only sees one cue at a time, so a cue opening with one has
/// nothing in itself to say which it is. The previous cue does: if it ended a sentence, this one starts one.
/// </summary>
public static partial class SentenceCase
{
    /// <summary>Trailing markup and space, which sit between the punctuation and the end of the line.</summary>
    [GeneratedRegex(@"(?:</?i>|\s)+$")]
    private static partial Regex TrailingNoise();

    /// <summary>Leading markup, space, and the marks that open a line rather than start it: a dash for a new
    /// speaker, an opening quote or bracket. The first letter after them is the sentence's first letter.</summary>
    [GeneratedRegex(@"^(?:</?i>|[\s\-‐‑–—""'“‘¿¡(\[])+")]
    private static partial Regex LeadingNoise();

    /// <summary>A dot run is a deliberately unfinished line, not a full stop, whether or not it was folded
    /// into an ellipsis. This is why the last character alone cannot be trusted.</summary>
    [GeneratedRegex(@"(?:\.\s*){2,}$|…$")]
    private static partial Regex Continuation();

    /// <summary>Capitalizes the first letter of every cue that starts a sentence, in place. Cues must be in
    /// display order, since each one is judged by the one before it.</summary>
    public static void Apply(IReadOnlyList<SubtitleEvent> events, char placeholder)
    {
        for (var i = 0; i < events.Count; i++)
        {
            // The first cue has no predecessor, and a track opens on a sentence.
            if (i == 0 || EndsSentence(events[i - 1].Text, placeholder))
            {
                events[i].Text = Capitalize(events[i].Text);
            }
        }
    }

    /// <summary>
    /// Whether this cue finished its sentence. A closing bracket counts: a self-contained sound cue
    /// ("[door slams]") ends whatever it was. The placeholder never does, because an unread glyph is a
    /// character we know nothing about, including whether it was a full stop.
    /// </summary>
    private static bool EndsSentence(string text, char placeholder)
    {
        var trimmed = TrailingNoise().Replace(text, string.Empty);
        if (trimmed.Length == 0 || Continuation().IsMatch(trimmed))
        {
            return false;
        }

        var last = trimmed[^1];
        if (last == placeholder)
        {
            return false;
        }

        // Strip one closing bracket or quote to reach the punctuation it wraps ("He left." -> .), but let a
        // bracket that closes a sound cue stand on its own.
        if (last is ')' or ']' or '"' or '\'' or '”' or '’')
        {
            if (last is ')' or ']')
            {
                return true;
            }

            trimmed = trimmed[..^1];
            if (trimmed.Length == 0)
            {
                return false;
            }

            last = trimmed[^1];
        }

        return last is '.' or '!' or '?';
    }

    private static string Capitalize(string text)
    {
        var start = LeadingNoise().Match(text).Length;
        if (start >= text.Length)
        {
            return text;
        }

        var c = text[start];
        if (!char.IsLower(c))
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, start), char.ToUpperInvariant(c).ToString(), text.AsSpan(start + 1));
    }
}
