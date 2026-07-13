using SubtitleOcr.Core.Imaging;
using SubtitleOcr.Core.NOcr;

namespace SubtitleOcr.Core.Tests;

/// <summary>
/// Shared builders for the synthetic fixtures the decode and matcher tests rely on.
/// </summary>
internal static class TestFixtures
{
    /// <summary>
    /// Hand-encodes an 8x4 subpicture: color 1 painted across every line via one-byte
    /// "rest of line" RLE codes (00nnnncc, n=8 c=1 -> 0x21), split into top/bottom fields.
    /// </summary>
    public static byte[] BuildSpu()
    {
        var topField = new byte[] { 0x21, 0x21 };    // lines y=0, y=2
        var bottomField = new byte[] { 0x21, 0x21 };  // lines y=1, y=3

        var pixelData = new List<byte>();
        pixelData.AddRange(topField);
        pixelData.AddRange(bottomField);

        const int pixelStart = 4; // after size + control-offset header
        var topAddr = pixelStart;
        var bottomAddr = pixelStart + topField.Length;
        var controlStart = pixelStart + pixelData.Count;

        var control = new List<byte>
        {
            0x00, 0x00,                                             // delay
            (byte)(controlStart >> 8), (byte)controlStart,         // next = self, terminates chain
            0x01,                                                  // start display
            0x03, 0x32, 0x10,                                      // set color CLUT nibbles
            0x04, 0xFF, 0xF0,                                      // set contrast: colors 1..3 opaque, bg off
            0x05, 0x00, 0x00, 0x07, 0x00, 0x00, 0x03,              // display area x0=0 x1=7 y0=0 y1=3
            0x06,                                                  // pixel-data addresses
            (byte)(topAddr >> 8), (byte)topAddr,
            (byte)(bottomAddr >> 8), (byte)bottomAddr,
            0xFF,                                                  // end
        };

        var total = 4 + pixelData.Count + control.Count;
        var spu = new List<byte>
        {
            (byte)(total >> 8), (byte)total,
            (byte)(controlStart >> 8), (byte)controlStart,
        };
        spu.AddRange(pixelData);
        spu.AddRange(control);
        return spu.ToArray();
    }

    /// <summary>
    /// Rasterizes a glyph's own trained foreground lines onto a bitmap. The FG raster is a
    /// subset of the glyph's ink, so trained background lines stay off it and it matches back.
    /// </summary>
    public static SubBitmap RasterizeForeground(NOcrChar glyph)
    {
        var bmp = new SubBitmap(glyph.Width, glyph.Height);
        foreach (var line in glyph.LinesForeground)
        {
            foreach (var p in line.GetPoints())
            {
                if (p.X >= 0 && p.Y >= 0 && p.X < bmp.Width && p.Y < bmp.Height)
                {
                    bmp.SetPixel(p.X, p.Y, 255, 255, 255, 255);
                }
            }
        }

        return bmp;
    }
}
