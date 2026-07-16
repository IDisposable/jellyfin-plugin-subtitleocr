using SubtitleOcr.Core.Imaging;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class SubBitmapTests
{
    // The pool hands back whatever the last renter wrote. Rent must zero it, or a cue inherits the previous
    // cue's glyphs and OCRs into nonsense; big enough to come from a real pool bucket, not a fresh array.
    [Fact]
    public void Rent_AfterAnotherBitmapDirtiedAndReturnedThePixels_IsZeroed()
    {
        const int width = 200;
        const int height = 200;

        var first = SubBitmap.Rent(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                first.SetPixel(x, y, 255, 255, 255, 255);
            }
        }

        first.Dispose();

        using var second = SubBitmap.Rent(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Assert.Equal(0, second.GetAlpha(x, y));
            }
        }
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var bitmap = SubBitmap.Rent(8, 8);
        bitmap.Dispose();
        bitmap.Dispose();
    }

    // A bitmap that owns its memory must not hand anything to the pool, or the next renter gets an array
    // that is still in use somewhere else.
    [Fact]
    public void Dispose_OnAnOwnedBitmap_IsHarmless()
    {
        var owned = new SubBitmap(4, 4);
        owned.SetPixel(1, 1, 255, 255, 255, 255);
        owned.Dispose();

        Assert.Equal(255, owned.GetAlpha(1, 1));
    }

    [Fact]
    public void Binarize_KeepsBrightOpaquePixelsAsForeground()
    {
        var bmp = new SubBitmap(2, 1);
        bmp.SetPixel(0, 0, 255, 255, 255, 255); // bright, opaque -> foreground
        bmp.SetPixel(1, 0, 10, 10, 10, 255);    // dark, opaque -> background

        var binary = bmp.Binarize().Mask;

        Assert.Equal((byte)255, binary.GetAlpha(0, 0));
        Assert.Equal((byte)0, binary.GetAlpha(1, 0));
    }

    [Fact]
    public void Binarize_InvertLuma_KeepsDarkPixelsAsForeground()
    {
        var bmp = new SubBitmap(2, 1);
        bmp.SetPixel(0, 0, 255, 255, 255, 255); // bright
        bmp.SetPixel(1, 0, 10, 10, 10, 255);    // dark

        var binary = bmp.Binarize(invertLuma: true).Mask;

        Assert.Equal((byte)0, binary.GetAlpha(0, 0));
        Assert.Equal((byte)255, binary.GetAlpha(1, 0));
    }

    [Fact]
    public void Binarize_DropsTransparentPixels()
    {
        var bmp = new SubBitmap(1, 1);
        bmp.SetPixel(0, 0, 255, 255, 255, 10); // bright but transparent

        var binary = bmp.Binarize().Mask;

        Assert.Equal((byte)0, binary.GetAlpha(0, 0));
    }
}
