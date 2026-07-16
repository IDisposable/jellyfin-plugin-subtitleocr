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

    /// <summary>The line's points, stepped along its major axis.</summary>
    public PointWalker Points() => new(Start, End);

    /// <summary>The line scaled to a candidate glyph's box, then stepped. Called for every trained line of
    /// every candidate glyph on every pass, so it returns a value-type walker that never allocates.</summary>
    public PointWalker ScaledPoints(NOcrChar ocrChar, int width, int height) =>
        new(Scale(Start, ocrChar, width, height), Scale(End, ocrChar, width, height));

    private static OcrPoint Scale(OcrPoint p, NOcrChar ocrChar, int width, int height) =>
        new(
            (int)Math.Round(p.X * width / (double)ocrChar.Width, MidpointRounding.AwayFromZero),
            (int)Math.Round(p.Y * height / (double)ocrChar.Height, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Rasterizes a segment along its major axis, one point at a time, without allocating. A struct, so
    /// <c>foreach</c> binds to it by pattern and never boxes.
    /// </summary>
    public struct PointWalker
    {
        private readonly bool _xMajor;
        private readonly int _x1;
        private readonly int _y1;
        private readonly int _last;
        private readonly double _factor;
        private int _i;
        private OcrPoint _current;

        public PointWalker(OcrPoint start, OcrPoint end)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            _current = default;

            if (Math.Abs(dx) > Math.Abs(dy))
            {
                _xMajor = true;
                var (x1, y1, x2, y2) = dx > 0 ? (start.X, start.Y, end.X, end.Y) : (end.X, end.Y, start.X, start.Y);
                _x1 = x1;
                _y1 = y1;
                _last = x2;
                _factor = (double)(y2 - y1) / (x2 - x1);
                _i = x1;
            }
            else
            {
                _xMajor = false;
                var (x1, y1, x2, y2) = dy > 0 ? (start.X, start.Y, end.X, end.Y) : (end.X, end.Y, start.X, start.Y);
                _x1 = x1;
                _y1 = y1;
                _last = y2;
                _factor = (double)(x2 - x1) / (y2 - y1);
                _i = y1;
            }
        }

        public readonly OcrPoint Current => _current;

        public readonly PointWalker GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_i > _last)
            {
                return false;
            }

            _current = _xMajor
                ? new OcrPoint(_i, (int)Math.Round(_y1 + (_factor * (_i - _x1)), MidpointRounding.AwayFromZero))
                : new OcrPoint((int)Math.Round(_x1 + (_factor * (_i - _y1)), MidpointRounding.AwayFromZero), _i);
            _i++;
            return true;
        }
    }
}
