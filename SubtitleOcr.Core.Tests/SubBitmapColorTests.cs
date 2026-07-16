using SubtitleOcr.Core.Imaging;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class SubBitmapColorTests
{
    private static SubBitmap Filled(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        var bitmap = new SubBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, r, g, b, a);
            }
        }

        return bitmap;
    }

    [Fact]
    public void DominantForegroundColor_FlatFill_IsThatColor()
    {
        var color = Filled(10, 10, 255, 255, 0).Binarize().ForegroundColor;

        Assert.Equal(((byte)255, (byte)255, (byte)0), color);
    }

    // The pixels Binarize discards must not vote: a transparent background is not the text color.
    [Fact]
    public void DominantForegroundColor_IgnoresTransparentPixels()
    {
        var bitmap = Filled(10, 10, 0, 255, 0, a: 0);
        for (var x = 0; x < 3; x++)
        {
            bitmap.SetPixel(x, 0, 255, 255, 0, 255);
        }

        Assert.Equal(((byte)255, (byte)255, (byte)0), bitmap.Binarize().ForegroundColor);
    }

    // The dark outline is opaque but not text, so luma keeps it out of the vote even though it is
    // the commonest opaque color in the image.
    [Fact]
    public void DominantForegroundColor_IgnoresTheDarkOutline()
    {
        var bitmap = Filled(10, 10, 0, 0, 0);
        for (var y = 4; y < 6; y++)
        {
            for (var x = 0; x < 10; x++)
            {
                bitmap.SetPixel(x, y, 255, 255, 0, 255);
            }
        }

        Assert.Equal(((byte)255, (byte)255, (byte)0), bitmap.Binarize().ForegroundColor);
    }

    [Fact]
    public void DominantForegroundColor_NoForeground_IsNull()
    {
        Assert.Null(Filled(10, 10, 0, 0, 0).Binarize().ForegroundColor);
    }

    // Two colors split evenly: neither is "the" color, and guessing one would invent a distinction.
    [Fact]
    public void DominantForegroundColor_TwoColorsTied_IsNull()
    {
        var bitmap = Filled(10, 10, 255, 255, 0);
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 10; x++)
            {
                bitmap.SetPixel(x, y, 0, 255, 255, 255);
            }
        }

        Assert.Null(bitmap.Binarize().ForegroundColor);
    }

    // A soft font ramps the fill down to the outline through many shades, so the fill leads without ever
    // holding half the bright pixels. Measured on a real Danish PGS track: white fill 3285 px, next shade
    // 1465, and a long tail below that.
    [Fact]
    public void DominantForegroundColor_SoftFontWithNoMajority_IsStillTheFill()
    {
        var bitmap = new SubBitmap(20, 20);
        var ramp = new byte[] { 0xE0, 0xD8, 0xD0, 0xC8, 0xC0, 0xB8, 0xB0, 0xA8, 0xA0, 0x98 };
        var index = 0;

        // 100 px of flat white against 300 px spread over ten antialiasing shades.
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 20; x++, index++)
            {
                if (index < 100)
                {
                    bitmap.SetPixel(x, y, 255, 255, 255, 255);
                }
                else
                {
                    var shade = ramp[((index - 100) / 30) % ramp.Length];
                    bitmap.SetPixel(x, y, shade, shade, shade, 255);
                }
            }
        }

        Assert.Equal(((byte)255, (byte)255, (byte)255), bitmap.Binarize().ForegroundColor);
    }

    // A flat interior outvotes its antialiased edge, and the answer is the interior's exact color.
    [Fact]
    public void DominantForegroundColor_AntialiasedEdge_DoesNotShiftTheAnswer()
    {
        var bitmap = Filled(10, 10, 255, 255, 0);
        for (var x = 0; x < 10; x++)
        {
            bitmap.SetPixel(x, 0, 250, 248, 30, 255);
            bitmap.SetPixel(x, 9, 245, 252, 20, 255);
        }

        Assert.Equal(((byte)255, (byte)255, (byte)0), bitmap.Binarize().ForegroundColor);
    }

    /// <summary>A glyph as a disc draws it: a text core in an outline, on transparency. The outline is what
    /// touches the transparency, so it is the edge and the core is the interior.</summary>
    private static SubBitmap Glyph(byte textLuma, byte outlineLuma)
    {
        var bitmap = new SubBitmap(24, 24);
        for (var y = 2; y < 22; y++)
        {
            for (var x = 2; x < 22; x++)
            {
                var core = x >= 6 && x < 18 && y >= 6 && y < 18;
                var v = core ? textLuma : outlineLuma;
                bitmap.SetPixel(x, y, v, v, v, 255);
            }
        }

        return bitmap;
    }

    [Fact]
    public void LooksDarkOnLight_LightTextInADarkOutline_IsFalse()
    {
        Assert.False(Glyph(textLuma: 255, outlineLuma: 0).LooksDarkOnLight());
    }

    [Fact]
    public void LooksDarkOnLight_DarkTextInALightHalo_IsTrue()
    {
        Assert.True(Glyph(textLuma: 0, outlineLuma: 255).LooksDarkOnLight());
    }

    // The whole point of the geometric test: the answer must not depend on the luma threshold that
    // binarization would apply, so a cue whose every pixel is above it still reads as normal polarity.
    [Fact]
    public void LooksDarkOnLight_AllBrightPixels_StillReadsTheOutline()
    {
        Assert.False(Glyph(textLuma: 255, outlineLuma: 150).LooksDarkOnLight());
    }

    // A close call is not a call: inverting a track that did not ask for it destroys it.
    [Fact]
    public void LooksDarkOnLight_EdgeAndInteriorAlike_IsNull()
    {
        Assert.Null(Glyph(textLuma: 200, outlineLuma: 190).LooksDarkOnLight());
    }

    [Fact]
    public void LooksDarkOnLight_TooFewPixels_IsNull()
    {
        var bitmap = new SubBitmap(4, 4);
        bitmap.SetPixel(1, 1, 255, 255, 255, 255);
        bitmap.SetPixel(2, 2, 255, 255, 255, 255);

        Assert.Null(bitmap.LooksDarkOnLight());
    }

    // InvertLuma flips which pixels are text, so it must flip which color is sampled.
    [Fact]
    public void DominantForegroundColor_InvertLuma_SamplesTheDarkText()
    {
        var bitmap = Filled(10, 10, 255, 255, 255);
        for (var y = 4; y < 6; y++)
        {
            for (var x = 0; x < 10; x++)
            {
                bitmap.SetPixel(x, y, 0, 0, 128, 255);
            }
        }

        Assert.Equal(((byte)0, (byte)0, (byte)128), bitmap.Binarize(invertLuma: true).ForegroundColor);
    }
}
