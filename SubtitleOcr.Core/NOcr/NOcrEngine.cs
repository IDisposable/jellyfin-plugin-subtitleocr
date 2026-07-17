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
    /// <summary>Upper and lower forms are one shape at two sizes; only height tells them apart.</summary>
    private const string SizeTwins = "cosuvwxz";

    /// <summary>Lowercase letters that sit within the x-height and are not twins, so a match on one is a
    /// reliable sample of it.</summary>
    private const string XHeightLetters = "aemnr";

    /// <summary>Letters that reach the cap line (capitals and ascenders), none of them twins.</summary>
    private const string TallLetters = "bdfhklt" + "ABDEFGHIJKLMNPQRTY";

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
        var matched = new List<Matched>(items.Count);
        var unknown = 0;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            // A glyph the segmenter split apart (a quote into two marks, "ø" into three) can only match as
            // the whole run, so try the widest run first and fall back to this blob alone.
            var match = MatchExpanded(items, i, out var consumed);
            if (match is null)
            {
                match = _db.GetMatch(item.Bitmap, item.TopMargin, _options.DeepSeek, _options.MaxWrongPixels);
            }
            else
            {
                i += consumed - 1;
            }

            if (match is null && TrySplit(item, out var left, out var right))
            {
                matched.Add(new Matched(item, left));
                matched.Add(new Matched(
                    new SplitterItem
                    {
                        Bitmap = item.Bitmap,
                        X = item.X,
                        TopMargin = item.TopMargin,
                        GapBefore = 0,
                        LineHeight = item.LineHeight,
                        NewLine = false,
                    },
                    right));
                continue;
            }

            if (match is null)
            {
                unknown++;
            }

            matched.Add(new Matched(item, match));
        }

        var text = CaseByLineMetrics(matched);
        return new NOcrResult
        {
            Text = text,
            GlyphCount = items.Count,
            UnknownCount = unknown,
        };
    }

    /// <summary>
    /// Reads the case of the size twins off the image's own letters. A trained glyph is scaled to the
    /// candidate before matching, so "w" and "W" are one shape and the matcher can only guess; but letters
    /// whose two forms are different shapes ("a", "n", "d", "k") are never ambiguous, and they measure this
    /// font's x-height and cap height. A twin is then whichever it stands closer to. Nothing is changed when
    /// the image does not show both heights, as in an all-caps sound cue.
    /// </summary>
    private string CaseByLineMetrics(List<Matched> matched)
    {
        var xHeights = new List<int>();
        var capHeights = new List<int>();
        foreach (var m in matched)
        {
            if (m.Match is null || m.Match.Text.Length != 1)
            {
                continue;
            }

            var c = m.Match.Text[0];
            if (XHeightLetters.Contains(c, StringComparison.Ordinal))
            {
                xHeights.Add(m.Item.Bitmap.Height);
            }
            else if (TallLetters.Contains(c, StringComparison.Ordinal))
            {
                capHeights.Add(m.Item.Bitmap.Height);
            }
        }

        var xHeight = Median(xHeights);
        var capHeight = Median(capHeights);
        var canJudge = xHeight > 0 && capHeight > 0 && capHeight > xHeight;

        var sb = new StringBuilder();
        var inItalic = false;
        foreach (var m in matched)
        {
            if (m.Item.NewLine && sb.Length > 0)
            {
                CloseItalic(sb, ref inItalic);
                sb.Append('\n');
            }
            else if (m.Item.GapBefore >= Math.Max(_options.SpaceMinGap, (int)Math.Round(_options.SpaceGapFactor * m.Item.LineHeight)))
            {
                sb.Append(' ');
            }

            var match = m.Match;
            if (match is null)
            {
                // An unread glyph carries no italic signal, so it inherits the run rather than ending it:
                // closing here turns "<i>WOMAN</i>" into "<i>WO</i>□<i>N</i>" for one bad glyph.
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

            var text = match.Text;
            if (canJudge && text.Length == 1 && SizeTwins.Contains(char.ToLowerInvariant(text[0]), StringComparison.Ordinal))
            {
                var height = m.Item.Bitmap.Height;
                var upper = Math.Abs(height - capHeight) < Math.Abs(height - xHeight);
                text = upper ? text.ToUpperInvariant() : text.ToLowerInvariant();
            }

            sb.Append(text);
        }

        CloseItalic(sb, ref inItalic);
        return sb.ToString();
    }

    private static int Median(List<int> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        values.Sort();
        return values[values.Count / 2];
    }

    private readonly record struct Matched(SplitterItem Item, NOcrChar? Match);

    /// <summary>
    /// A blob that matches nothing and is wide for its line is usually two kerned letters the splitter could
    /// not part on an empty column ("re" reading as one shape, "rn" as none). Cutting it at each interior
    /// column and matching both halves recovers them; only a cut where both sides match is taken, so a
    /// genuinely unreadable glyph still comes out as the placeholder. Runs only on a failed match.
    /// </summary>
    private bool TrySplit(SplitterItem item, out NOcrChar? left, out NOcrChar? right)
    {
        left = null;
        right = null;
        var bitmap = item.Bitmap;

        // Two letters of this line's text are about this wide; one is not.
        if (bitmap.Width < 6 || bitmap.Width < item.LineHeight / 2)
        {
            return false;
        }

        // Prefer the thinnest column: where two letters touch, the ink is at its narrowest.
        var columns = new List<(int Ink, int X)>();
        for (var x = 2; x <= bitmap.Width - 3; x++)
        {
            var ink = 0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetAlpha(x, y) > 150)
                {
                    ink++;
                }
            }

            columns.Add((ink, x));
        }

        columns.Sort((a, b) => a.Ink != b.Ink ? a.Ink.CompareTo(b.Ink) : a.X.CompareTo(b.X));
        foreach (var (_, x) in columns)
        {
            var l = _db.GetMatch(bitmap.Crop(0, 0, x, bitmap.Height), item.TopMargin, _options.DeepSeek, _options.MaxWrongPixels);
            if (l is null)
            {
                continue;
            }

            var r = _db.GetMatch(bitmap.Crop(x, 0, bitmap.Width - x, bitmap.Height), item.TopMargin, _options.DeepSeek, _options.MaxWrongPixels);
            if (r is null)
            {
                continue;
            }

            left = l;
            right = r;
            return true;
        }

        return false;
    }

    /// <summary>Merges the next N blobs and matches them as one glyph, widest run first. Only blobs on the
    /// same text line are merged; a run that matches nothing leaves the caller to match this blob alone.</summary>
    private NOcrChar? MatchExpanded(List<SplitterItem> items, int index, out int consumed)
    {
        consumed = 0;
        for (var n = Math.Min(_db.MaxExpandCount, items.Count - index); n >= 2; n--)
        {
            var spansLine = false;
            for (var k = 1; k < n; k++)
            {
                if (items[index + k].NewLine)
                {
                    spansLine = true;
                    break;
                }
            }

            if (spansLine)
            {
                continue;
            }

            // Aspect pre-screen off the cheap bounding box: skip the rent-and-fill for a run no expanded entry
            // could match, which is almost every run since almost none are ligatures.
            var (left, right, top, bottom) = RunBounds(items, index, n);
            var width = right - left;
            if (width <= 0 || !_db.CouldExpandedMatch(n, (bottom - top) * 100.0 / width))
            {
                continue;
            }

            // Rented and disposed per attempt: this runs for every blob, and GetExpandedMatch only reads the
            // bitmap, so it need not outlive the call.
            using var merged = Merge(items, index, n, out var topMargin);
            var match = _db.GetExpandedMatch(merged, topMargin, n, _options.DeepSeek, _options.MaxWrongPixels);
            if (match is not null)
            {
                consumed = n;
                return match;
            }
        }

        return null;
    }

    /// <summary>The pixel box a run of blobs spans, in the source image's coordinates.</summary>
    private static (int Left, int Right, int Top, int Bottom) RunBounds(List<SplitterItem> items, int index, int count)
    {
        var left = items[index].X;
        var right = left;
        var top = items[index].TopMargin;
        var bottom = top;
        for (var k = 0; k < count; k++)
        {
            var it = items[index + k];
            right = Math.Max(right, it.X + it.Bitmap.Width);
            top = Math.Min(top, it.TopMargin);
            bottom = Math.Max(bottom, it.TopMargin + it.Bitmap.Height);
        }

        return (left, right, top, bottom);
    }

    /// <summary>Rebuilds the run as one bitmap from each blob's position in the source image; only the alpha
    /// channel is read downstream, so the copy carries no color. Rented: the caller disposes it.</summary>
    private static SubBitmap Merge(List<SplitterItem> items, int index, int count, out int topMargin)
    {
        var (left, right, top, bottom) = RunBounds(items, index, count);
        topMargin = top;
        var merged = SubBitmap.Rent(right - left, bottom - top);
        for (var k = 0; k < count; k++)
        {
            var it = items[index + k];
            var offsetX = it.X - left;
            var offsetY = it.TopMargin - top;
            for (var y = 0; y < it.Bitmap.Height; y++)
            {
                for (var x = 0; x < it.Bitmap.Width; x++)
                {
                    if (it.Bitmap.GetAlpha(x, y) > 150)
                    {
                        merged.SetPixel(offsetX + x, offsetY + y, 255, 255, 255, 255);
                    }
                }
            }
        }

        return merged;
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
