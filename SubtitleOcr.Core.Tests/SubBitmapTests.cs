using SubtitleOcr.Core.Imaging;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class SubBitmapTests
{
    [Fact]
    public void Binarize_KeepsBrightOpaquePixelsAsForeground()
    {
        var bmp = new SubBitmap(2, 1);
        bmp.SetPixel(0, 0, 255, 255, 255, 255); // bright, opaque -> foreground
        bmp.SetPixel(1, 0, 10, 10, 10, 255);    // dark, opaque -> background

        var binary = bmp.Binarize();

        Assert.Equal((byte)255, binary.GetAlpha(0, 0));
        Assert.Equal((byte)0, binary.GetAlpha(1, 0));
    }

    [Fact]
    public void Binarize_InvertLuma_KeepsDarkPixelsAsForeground()
    {
        var bmp = new SubBitmap(2, 1);
        bmp.SetPixel(0, 0, 255, 255, 255, 255); // bright
        bmp.SetPixel(1, 0, 10, 10, 10, 255);    // dark

        var binary = bmp.Binarize(invertLuma: true);

        Assert.Equal((byte)0, binary.GetAlpha(0, 0));
        Assert.Equal((byte)255, binary.GetAlpha(1, 0));
    }

    [Fact]
    public void Binarize_DropsTransparentPixels()
    {
        var bmp = new SubBitmap(1, 1);
        bmp.SetPixel(0, 0, 255, 255, 255, 10); // bright but transparent

        var binary = bmp.Binarize();

        Assert.Equal((byte)0, binary.GetAlpha(0, 0));
    }
}
