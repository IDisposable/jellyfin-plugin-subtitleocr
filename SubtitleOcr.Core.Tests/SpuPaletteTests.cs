using SubtitleOcr.Core.VobSub;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class SpuPaletteTests
{
    [Fact]
    public void FromExtradataText_ParsesPaletteColors()
    {
        var palette = SpuPalette.FromExtradataText(
            "size: 720x480\npalette: 000000, ff0000, 00ff00, 0000ff, ffffff, 828282\n");

        Assert.Equal((byte)255, palette.Colors[1].R);
        Assert.Equal((byte)0, palette.Colors[1].G);
        Assert.Equal((byte)0, palette.Colors[1].B);
        Assert.Equal(((byte)255, (byte)255, (byte)255), palette.Colors[4]);
    }

    [Fact]
    public void FromExtradataText_PadsTo16Entries()
    {
        var palette = SpuPalette.FromExtradataText(
            "palette: 000000, ff0000, 00ff00, 0000ff, ffffff, 828282\n");

        Assert.Equal(16, palette.Colors.Count);
    }
}
