namespace SubtitleOcr.Core.Imaging;

/// <summary>One segmented glyph candidate with position context for matching and spacing.</summary>
public sealed class SplitterItem
{
    public required SubBitmap Bitmap { get; init; }

    /// <summary>X of the glyph's left edge within the full subtitle image.</summary>
    public int X { get; init; }

    /// <summary>Glyph top relative to its text line's top (feeds NOcrChar.MarginTop).</summary>
    public int TopMargin { get; init; }

    /// <summary>Pixel gap to the previous glyph on the same line; -1 for the first glyph.</summary>
    public int GapBefore { get; init; }

    /// <summary>Height (px) of the text line band this glyph belongs to; scales the word-space threshold.</summary>
    public int LineHeight { get; init; }

    public bool NewLine { get; init; }
}

/// <summary>
/// Projection-profile segmentation of a binarized subtitle image: horizontal bands with any
/// opaque pixel form text lines; vertical bands within a line form glyphs. Simpler than
/// Subtitle Edit's splitter — no italic-overlap handling — which is the main quality gap
/// to close later (see README).
/// </summary>
public static class ImageSplitter
{
    // minLineHeight floors a text band at 2px so a line of only short punctuation (a lone "...", "-", or ",")
    // survives, while a 1px row of binarization noise is still rejected.
    public static List<SplitterItem> Split(SubBitmap bitmap, int minLineHeight = 2, int minGlyphWidth = 1)
    {
        var items = new List<SplitterItem>();

        foreach (var (lineTop, lineBottom) in FindBands(y => RowHasText(bitmap, y), bitmap.Height, minLineHeight))
        {
            var first = true;
            var previousRight = 0;

            foreach (var (glyphLeft, glyphRight) in FindBands(
                         x => ColumnHasText(bitmap, x, lineTop, lineBottom), bitmap.Width, minGlyphWidth))
            {
                // Tighten vertical bounds to the glyph itself for correct MarginTop and aspect.
                var top = lineTop;
                var bottom = lineBottom;
                while (top < bottom && !RowHasText(bitmap, top, glyphLeft, glyphRight))
                {
                    top++;
                }

                while (bottom > top && !RowHasText(bitmap, bottom, glyphLeft, glyphRight))
                {
                    bottom--;
                }

                items.Add(new SplitterItem
                {
                    Bitmap = bitmap.Crop(glyphLeft, top, glyphRight - glyphLeft + 1, bottom - top + 1),
                    X = glyphLeft,
                    TopMargin = top - lineTop,
                    GapBefore = first ? -1 : glyphLeft - previousRight - 1,
                    LineHeight = lineBottom - lineTop + 1,
                    NewLine = first,
                });

                first = false;
                previousRight = glyphRight;
            }
        }

        return items;
    }

    /// <summary>Finds contiguous index ranges where the predicate holds.</summary>
    private static IEnumerable<(int Start, int End)> FindBands(Func<int, bool> hasContent, int length, int minSize)
    {
        var start = -1;
        for (var i = 0; i < length; i++)
        {
            if (hasContent(i))
            {
                if (start < 0)
                {
                    start = i;
                }
            }
            else if (start >= 0)
            {
                if (i - start >= minSize)
                {
                    yield return (start, i - 1);
                }

                start = -1;
            }
        }

        if (start >= 0 && length - start >= minSize)
        {
            yield return (start, length - 1);
        }
    }

    private static bool RowHasText(SubBitmap bitmap, int y, int fromX = 0, int toX = int.MaxValue)
    {
        var end = Math.Min(toX, bitmap.Width - 1);
        for (var x = fromX; x <= end; x++)
        {
            if (bitmap.GetAlpha(x, y) > 150)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ColumnHasText(SubBitmap bitmap, int x, int fromY, int toY)
    {
        for (var y = fromY; y <= toY; y++)
        {
            if (bitmap.GetAlpha(x, y) > 150)
            {
                return true;
            }
        }

        return false;
    }
}
