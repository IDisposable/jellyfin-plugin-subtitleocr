using System.Text;

namespace SubtitleOcr.Core.Output;

/// <summary>A run of italic text in a cue, as a character range over the cue's clean (markup-free) text.</summary>
public readonly record struct ItalicSpan(int Start, int Length);

/// <summary>
/// Converts between a cue's clean text plus italic spans and the inline &lt;i&gt; form the recognizer and the
/// correction stages work in. Recognition and correction keep the tags inline (they ride along the text as it
/// is edited); the pipeline parses to clean-text-plus-spans once correction is done, and the writers apply the
/// format's own markup at output. So no decision or serializer reads presentation markup out of the text.
/// </summary>
public static class ItalicMarkup
{
    private const string Open = "<i>";
    private const string Close = "</i>";

    /// <summary>Splits inline &lt;i&gt;...&lt;/i&gt; text into clean text and the italic spans over it.</summary>
    public static (string Text, IReadOnlyList<ItalicSpan> Spans) Parse(string inline)
    {
        if (inline.IndexOf(Open, StringComparison.Ordinal) < 0)
        {
            return (inline, Array.Empty<ItalicSpan>());
        }

        var sb = new StringBuilder(inline.Length);
        var spans = new List<ItalicSpan>();
        var openAt = -1;
        var i = 0;
        while (i < inline.Length)
        {
            if (string.CompareOrdinal(inline, i, Open, 0, Open.Length) == 0)
            {
                openAt = sb.Length;
                i += Open.Length;
            }
            else if (string.CompareOrdinal(inline, i, Close, 0, Close.Length) == 0)
            {
                if (openAt >= 0 && sb.Length > openAt)
                {
                    spans.Add(new ItalicSpan(openAt, sb.Length - openAt));
                }

                openAt = -1;
                i += Close.Length;
            }
            else
            {
                sb.Append(inline[i]);
                i++;
            }
        }

        if (openAt >= 0 && sb.Length > openAt)
        {
            spans.Add(new ItalicSpan(openAt, sb.Length - openAt));
        }

        return (sb.ToString(), spans);
    }

    /// <summary>
    /// Re-inserts markup into clean text at the span boundaries, using the format's own open and close markers
    /// (&lt;i&gt;/&lt;/i&gt; for SRT, {\i1}/{\i0} for ASS). Spans are non-overlapping and ordered.
    /// </summary>
    public static string Emit(string text, IReadOnlyList<ItalicSpan> spans, string open, string close)
    {
        if (spans.Count == 0)
        {
            return text;
        }

        var opensAt = new bool[text.Length + 1];
        var closesAt = new bool[text.Length + 1];
        foreach (var span in spans)
        {
            opensAt[span.Start] = true;
            closesAt[span.Start + span.Length] = true;
        }

        var sb = new StringBuilder(text.Length + spans.Count * (open.Length + close.Length));
        for (var p = 0; p <= text.Length; p++)
        {
            // Close before open at the same offset, so two abutting runs read close-then-open.
            if (closesAt[p])
            {
                sb.Append(close);
            }

            if (p < text.Length)
            {
                if (opensAt[p])
                {
                    sb.Append(open);
                }

                sb.Append(text[p]);
            }
        }

        return sb.ToString();
    }
}
