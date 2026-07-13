namespace SubtitleOcr.Core.NOcr;

public readonly record struct OcrPoint(int X, int Y);

/// <summary>
/// One trained test line. Point interpolation and scaling must be bit-identical to
/// Subtitle Edit's NOcrLine (MIT) or existing databases mismatch — keep AwayFromZero rounding.
/// </summary>
public sealed class NOcrLine
{
    public OcrPoint Start { get; }
    public OcrPoint End { get; }

    public NOcrLine(OcrPoint start, OcrPoint end)
    {
        Start = start;
        End = end;
    }

    public IEnumerable<OcrPoint> GetPoints() => GetPoints(Start, End);

    public IEnumerable<OcrPoint> ScaledGetPoints(NOcrChar ocrChar, int width, int height) =>
        GetPoints(Scale(Start, ocrChar, width, height), Scale(End, ocrChar, width, height));

    private static OcrPoint Scale(OcrPoint p, NOcrChar ocrChar, int width, int height) =>
        new(
            (int)Math.Round(p.X * width / (double)ocrChar.Width, MidpointRounding.AwayFromZero),
            (int)Math.Round(p.Y * height / (double)ocrChar.Height, MidpointRounding.AwayFromZero));

    /// <summary>Rasterizes the segment stepping along its major axis.</summary>
    public static IEnumerable<OcrPoint> GetPoints(OcrPoint start, OcrPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;

        if (Math.Abs(dx) > Math.Abs(dy))
        {
            var (x1, y1, x2, y2) = dx > 0 ? (start.X, start.Y, end.X, end.Y) : (end.X, end.Y, start.X, start.Y);
            var factor = (double)(y2 - y1) / (x2 - x1);
            for (var i = x1; i <= x2; i++)
            {
                yield return new OcrPoint(i, (int)Math.Round(y1 + factor * (i - x1), MidpointRounding.AwayFromZero));
            }
        }
        else
        {
            var (x1, y1, x2, y2) = dy > 0 ? (start.X, start.Y, end.X, end.Y) : (end.X, end.Y, start.X, start.Y);
            var factor = (double)(x2 - x1) / (y2 - y1);
            for (var i = y1; i <= y2; i++)
            {
                yield return new OcrPoint((int)Math.Round(x1 + factor * (i - y1), MidpointRounding.AwayFromZero), i);
            }
        }
    }
}
