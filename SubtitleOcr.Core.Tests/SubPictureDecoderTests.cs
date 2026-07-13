using SubtitleOcr.Core.VobSub;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class SubPictureDecoderTests
{
    [Fact]
    public void Decode_HandEncodedSpu_ProducesBitmap()
    {
        var decoded = SubPictureDecoder.Decode(TestFixtures.BuildSpu(), SpuPalette.Default);
        Assert.NotNull(decoded);
    }

    [Fact]
    public void Decode_HandEncodedSpu_HasExpectedDimensions()
    {
        var decoded = SubPictureDecoder.Decode(TestFixtures.BuildSpu(), SpuPalette.Default);
        Assert.NotNull(decoded);
        Assert.Equal(8, decoded!.Bitmap.Width);
        Assert.Equal(4, decoded.Bitmap.Height);
    }

    [Fact]
    public void Decode_HandEncodedSpu_PaintsEveryPixel()
    {
        var decoded = SubPictureDecoder.Decode(TestFixtures.BuildSpu(), SpuPalette.Default);
        Assert.NotNull(decoded);

        var opaque = 0;
        for (var y = 0; y < decoded!.Bitmap.Height; y++)
        {
            for (var x = 0; x < decoded.Bitmap.Width; x++)
            {
                if (decoded.Bitmap.GetAlpha(x, y) > 150)
                {
                    opaque++;
                }
            }
        }

        // Width-8 runs exercise the x >= Width-1 wrap path (paints the last pixel, then wraps).
        Assert.Equal(32, opaque);
    }
}
