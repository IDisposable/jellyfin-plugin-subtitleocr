using System.Text;
using SubtitleOcr.Core.Imaging;

namespace SubtitleOcr.Core.NOcr;

public sealed class NOcrEngineOptions
{
    /// <summary>Absolute floor (px) for the word-space gap. The effective threshold is the larger of this
    /// and <see cref="SpaceGapFactor"/> times the text line height.</summary>
    public int SpaceMinGap { get; init; } = 6;

    /// <summary>Word-space gap as a fraction of the text line height, so the threshold scales with subtitle
    /// resolution (a fixed pixel gap splits large Blu-ray text between every letter).</summary>
    public double SpaceGapFactor { get; init; } = 0.30;

    /// <summary>Errors tolerated per glyph in loose cascade passes.</summary>
    public int MaxWrongPixels { get; init; } = 25;

    /// <summary>Enables the widest cascade pass (slower, catches degraded glyphs).</summary>
    public bool DeepSeek { get; init; } = true;

    /// <summary>
    /// Emitted for unmatched glyphs. Not Subtitle Edit's "*": dialogue censors words with that, so an unread
    /// glyph could not be told from source text. U+25A1 is the conventional missing-glyph mark.
    /// </summary>
    public const char DefaultUnknownCharacter = '□';

    /// <summary>Emitted for unmatched glyphs. One character, always: the later stages match on it.</summary>
    public char UnknownCharacter { get; init; } = DefaultUnknownCharacter;

    /// <summary>Wraps italic glyph runs in &lt;i&gt; tags.</summary>
    public bool EmitItalicTags { get; init; } = true;
}

public sealed class NOcrResult
{
    public required string Text { get; init; }
    public int GlyphCount { get; init; }
    public int UnknownCount { get; init; }
}

/// <summary>Segments a binarized subtitle bitmap and matches each glyph against the database.</summary>
public sealed class NOcrEngine
{
    private readonly NOcrDb _db;
    private readonly NOcrEngineOptions _options;

    public NOcrEngine(NOcrDb db, NOcrEngineOptions? options = null)
    {
        _db = db;
        _options = options ?? new NOcrEngineOptions();
    }

    public NOcrResult Recognize(SubBitmap binarized)
    {
        var items = ImageSplitter.Split(binarized);
        var sb = new StringBuilder();
        var unknown = 0;
        var inItalic = false;

        foreach (var item in items)
        {
            if (item.NewLine && sb.Length > 0)
            {
                CloseItalic(sb, ref inItalic);
                sb.Append('\n');
            }
            else if (item.GapBefore >= Math.Max(_options.SpaceMinGap, (int)Math.Round(_options.SpaceGapFactor * item.LineHeight)))
            {
                sb.Append(' ');
            }

            var match = _db.GetMatch(item.Bitmap, item.TopMargin, _options.DeepSeek, _options.MaxWrongPixels);
            if (match is null)
            {
                unknown++;
                CloseItalic(sb, ref inItalic);
                sb.Append(_options.UnknownCharacter);
                continue;
            }

            // Punctuation carries no italic signal (a comma is the same shape either way), so it inherits
            // the current state instead of opening a run of its own or breaking one that spans it.
            if (_options.EmitItalicTags && match.Italic != inItalic && HasLetterOrDigit(match.Text))
            {
                sb.Append(match.Italic ? "<i>" : "</i>");
                inItalic = match.Italic;
            }

            sb.Append(match.Text);
        }

        CloseItalic(sb, ref inItalic);

        return new NOcrResult
        {
            Text = sb.ToString(),
            GlyphCount = items.Count,
            UnknownCount = unknown,
        };
    }

    private static bool HasLetterOrDigit(string text)
    {
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                return true;
            }
        }

        return false;
    }

    private static void CloseItalic(StringBuilder sb, ref bool inItalic)
    {
        if (inItalic)
        {
            sb.Append("</i>");
            inItalic = false;
        }
    }
}
