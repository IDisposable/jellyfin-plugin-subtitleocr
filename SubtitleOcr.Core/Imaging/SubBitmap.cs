namespace SubtitleOcr.Core.Imaging;

/// <summary>
/// Minimal RGBA8888 bitmap. Avoids any external imaging dependency; SPU decode,
/// segmentation and nOCR matching only need alpha reads and rectangular copies.
/// </summary>
public sealed class SubBitmap
{
    public int Width { get; }
    public int Height { get; }

    // Layout: [x*4 + y*Width*4] = R,G,B,A
    private readonly byte[] _pixels;

    public SubBitmap(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"Invalid bitmap size {width}x{height}");
        }

        Width = width;
        Height = height;
        _pixels = new byte[width * height * 4];
    }

    public byte[] GetPixelData() => _pixels;

    public byte GetAlpha(int x, int y) => _pixels[(x + y * Width) * 4 + 3];

    public void SetPixel(int x, int y, byte r, byte g, byte b, byte a)
    {
        var i = (x + y * Width) * 4;
        _pixels[i] = r;
        _pixels[i + 1] = g;
        _pixels[i + 2] = b;
        _pixels[i + 3] = a;
    }

    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        var i = (x + y * Width) * 4;
        return (_pixels[i], _pixels[i + 1], _pixels[i + 2], _pixels[i + 3]);
    }

    /// <summary>Copies a sub-rectangle into a new bitmap.</summary>
    public SubBitmap Crop(int x, int y, int width, int height)
    {
        var result = new SubBitmap(width, height);
        for (var row = 0; row < height; row++)
        {
            Array.Copy(
                _pixels, ((y + row) * Width + x) * 4,
                result._pixels, row * width * 4,
                width * 4);
        }

        return result;
    }

    /// <summary>
    /// Reduces the image to a binary text mask: foreground pixels become opaque white,
    /// everything else fully transparent. The nOCR matcher only inspects alpha.
    /// DVD subs are typically light text with a dark outline, so foreground =
    /// opaque and bright; <paramref name="invertLuma"/> flips that for dark-on-light discs.
    /// </summary>
    public SubBitmap Binarize(byte alphaThreshold = 100, byte lumaThreshold = 128, bool invertLuma = false)
    {
        var result = new SubBitmap(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var (r, g, b, a) = GetPixel(x, y);
                if (a < alphaThreshold)
                {
                    continue;
                }

                // Rec.601 integer luma.
                var luma = (299 * r + 587 * g + 114 * b) / 1000;
                var isText = invertLuma ? luma < lumaThreshold : luma >= lumaThreshold;
                if (isText)
                {
                    result.SetPixel(x, y, 255, 255, 255, 255);
                }
            }
        }

        return result;
    }
}
