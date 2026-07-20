using System.Buffers;
using System.Runtime.InteropServices;

namespace SubtitleOcr.Core.Imaging;

/// <summary>
/// What one pass over a subtitle image yields: the text mask the matcher reads, and the color that text was
/// drawn in. They come together because they come from the same pixels.
///
/// Dispose it once the mask has been read. The mask is pooled, and holding one after disposing hands the
/// same pixels to the next cue.
/// </summary>
/// <param name="Mask">Foreground opaque white, everything else transparent.</param>
/// <param name="ForegroundColor">The text's fill color, or null when no one color dominates it.</param>
public readonly record struct BinarizedImage(SubBitmap Mask, (byte R, byte G, byte B)? ForegroundColor) : IDisposable
{
    public void Dispose() => Mask.Dispose();
}

/// <summary>
/// Minimal RGBA8888 bitmap. Avoids any external imaging dependency; SPU decode,
/// segmentation and nOCR matching only need alpha reads and rectangular copies.
///
/// A full-size cue runs to hundreds of kilobytes, well over the 85KB the runtime sends to the large object
/// heap, so a bitmap with a short and provable life can be rented from the pool instead (<see cref="Rent"/>)
/// and returned by <see cref="Dispose"/>. Measured on a 757x208 cue, allocating one outright costs a gen2
/// collection every five cues, and a gen2 stops the whole server, playback included. Bitmaps that outlive
/// their caller (a decoded subtitle image) own their memory and dispose to nothing.
/// </summary>
public sealed class SubBitmap : IDisposable
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>How far the commonest foreground color must lead the next one to count as the fill. Set from
    /// real discs: a soft font's brightest antialiasing shade runs about two thirds of the fill's own count.</summary>
    private const double ColorMarginFactor = 1.5;

    /// <summary>How far edge and interior luma must part before <see cref="LooksDarkOnLight"/> will call it.
    /// Real tracks separate by 123 or more, so this leaves room to be sure while refusing a close call.</summary>
    private const int PolarityMargin = 40;

    /// <summary>Too few pixels of either kind and the means are noise, not a reading.</summary>
    private const int MinimumPolarityPixels = 20;

    // Layout: [x*4 + y*Width*4] = R,G,B,A. A rented array is longer than that; nothing reads its length.
    private byte[] _pixels;
    private readonly bool _pooled;

    public SubBitmap(int width, int height)
    {
        ValidateSize(width, height);
        Width = width;
        Height = height;
        _pixels = new byte[width * height * 4];
    }

    private SubBitmap(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
        _pooled = true;
    }

    /// <summary>
    /// A bitmap borrowed from the pool, zeroed and ready to write. Only for one whose life the caller can
    /// prove ends before it lets go: <see cref="Dispose"/> hands the pixels to whoever rents next, so a
    /// reference kept past that reads another cue's image. Anything stored, returned, or shared must use the
    /// constructor instead.
    /// </summary>
    public static SubBitmap Rent(int width, int height)
    {
        ValidateSize(width, height);
        var length = width * height * 4;

        // Oversized requests fall back to a plain allocation inside the pool, and Return drops them, so this
        // stays correct for a bitmap too big to pool rather than needing a size check here.
        var pixels = ArrayPool<byte>.Shared.Rent(length);
        Array.Clear(pixels, 0, length);
        return new SubBitmap(width, height, pixels);
    }

    /// <summary>Returns a rented bitmap's pixels to the pool. A no-op for one that owns its memory, and safe
    /// to call twice.</summary>
    public void Dispose()
    {
        if (_pooled && _pixels.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_pixels);
            _pixels = Array.Empty<byte>();
        }
    }

    private static void ValidateSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"Invalid bitmap size {width}x{height}");
        }
    }

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
    ///
    /// The text's own color comes back with the mask, because this is the pass that destroys the evidence
    /// for it: the foreground pixels are in hand here, and any later sampler would have to walk the whole
    /// image again to find the same ones. See <see cref="BinarizedImage.ForegroundColor"/>.
    /// </summary>
    public BinarizedImage Binarize(byte alphaThreshold = 100, byte lumaThreshold = 128, bool invertLuma = false)
    {
        var mask = Rent(Width, Height);
        var buckets = new Dictionary<int, (int Count, int SumR, int SumG, int SumB)>();

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var (r, g, b, a) = GetPixel(x, y);
                if (a < alphaThreshold || !IsForeground(r, g, b, lumaThreshold, invertLuma))
                {
                    continue;
                }

                mask.SetPixel(x, y, 255, 255, 255, 255);

                // One probe, not the two a read-then-write pair would cost: this runs per foreground pixel,
                // and the tally is 39% of this pass measured against a real cue.
                var key = ColorBucket.Key(r, g, b);
                ref var bucket = ref CollectionsMarshal.GetValueRefOrAddDefault(buckets, key, out _);
                bucket.Count++;
                bucket.SumR += r;
                bucket.SumG += g;
                bucket.SumB += b;
            }
        }

        return new BinarizedImage(mask, DominantColor(buckets));
    }

    /// <summary>
    /// The fill color of the text, from a histogram of the pixels <see cref="Binarize"/> kept. Shades are
    /// bucketed before counting and the winner is the mean of its bucket.
    ///
    /// The commonest, not the majority: a soft font's antialiasing ramps the fill down to the outline across
    /// many shades, and real discs put barely a third of their bright pixels in any one of them. Each ramp
    /// shade is still rarer than the one flat interior, so the mode is the fill. What that cannot survive is
    /// a tie, so a runner-up within <see cref="ColorMarginFactor"/> of the winner returns null instead: a cue
    /// split evenly between two colors has no one color, and picking either would invent a distinction.
    /// </summary>
    private static (byte R, byte G, byte B)? DominantColor(
        Dictionary<int, (int Count, int SumR, int SumG, int SumB)> buckets)
    {
        var best = (Count: 0, SumR: 0, SumG: 0, SumB: 0);
        var runnerUp = 0;
        foreach (var bucket in buckets.Values)
        {
            if (bucket.Count > best.Count)
            {
                runnerUp = best.Count;
                best = bucket;
            }
            else if (bucket.Count > runnerUp)
            {
                runnerUp = bucket.Count;
            }
        }

        if (best.Count == 0 || best.Count < runnerUp * ColorMarginFactor)
        {
            return null;
        }

        return (
            (byte)(best.SumR / best.Count),
            (byte)(best.SumG / best.Count),
            (byte)(best.SumB / best.Count));
    }

    /// <summary>
    /// Whether this cue is dark text in a light halo rather than the usual light text in a dark outline.
    ///
    /// Decided by geometry, never by brightness, which is the only way to answer without presupposing it: the
    /// opaque pixels touching transparency are the outside of the outline, whatever color that happens to be,
    /// and the rest is the text plus the outline's interior. Light text in a dark outline therefore reads
    /// darker at the edge than inside; dark text in a light halo reads the other way about. Asking which
    /// pixels are bright, as <see cref="Binarize"/> must, would just repeat the assumption being tested.
    ///
    /// Null unless the margin is decisive. Getting this backwards renders a cue unreadable, and the cost is
    /// not symmetric: the usual polarity is common and the answer for it is already correct, so an unsure
    /// reading is worth nothing and a confident wrong one is worth less. Measured across 30 real PGS and
    /// VobSub tracks, every one of them light-on-dark, the edge ran 123 to 167 luma below the interior, so a
    /// cue has to lean hard the other way to be believed.
    /// </summary>
    public bool? LooksDarkOnLight(byte alphaThreshold = 100)
    {
        long interiorSum = 0, edgeSum = 0;
        var interiorCount = 0;
        var edgeCount = 0;

        for (var y = 1; y < Height - 1; y++)
        {
            for (var x = 1; x < Width - 1; x++)
            {
                if (GetAlpha(x, y) < alphaThreshold)
                {
                    continue;
                }

                var (r, g, b, _) = GetPixel(x, y);
                var luma = ((299 * r) + (587 * g) + (114 * b)) / 1000;

                if (GetAlpha(x - 1, y) < alphaThreshold || GetAlpha(x + 1, y) < alphaThreshold ||
                    GetAlpha(x, y - 1) < alphaThreshold || GetAlpha(x, y + 1) < alphaThreshold)
                {
                    edgeSum += luma;
                    edgeCount++;
                }
                else
                {
                    interiorSum += luma;
                    interiorCount++;
                }
            }
        }

        if (interiorCount < MinimumPolarityPixels || edgeCount < MinimumPolarityPixels)
        {
            return null;
        }

        var difference = (edgeSum / edgeCount) - (interiorSum / interiorCount);
        if (Math.Abs(difference) < PolarityMargin)
        {
            return null;
        }

        return difference > 0;
    }

    /// <summary>Rec.601 integer luma against the text threshold.</summary>
    private static bool IsForeground(byte r, byte g, byte b, byte lumaThreshold, bool invertLuma)
    {
        var luma = ((299 * r) + (587 * g) + (114 * b)) / 1000;
        return invertLuma ? luma < lumaThreshold : luma >= lumaThreshold;
    }
}
