using SubtitleOcr.Core.Imaging;

namespace SubtitleOcr.Core.VobSub;

/// <summary>Result of decoding one SPU packet.</summary>
public sealed class DecodedSubPicture
{
    public required SubBitmap Bitmap { get; init; }
    public bool Forced { get; init; }

    /// <summary>Display duration from the StopDisplay command delay; null when the packet has none.</summary>
    public TimeSpan? Duration { get; init; }
}

/// <summary>
/// Decodes a raw DVD subpicture unit (SPU) as delivered by ffprobe/ffmpeg demuxers:
/// 2-byte size, 2-byte control-sequence offset, RLE pixel data, control sequences.
/// Format reference: http://www.mpucoder.com/DVD/spu.html.
/// RLE/control handling cross-checked against Subtitle Edit's SubPicture.cs (MIT).
/// </summary>
public static class SubPictureDecoder
{
    private const byte CmdForcedStart = 0x00;
    private const byte CmdStart = 0x01;
    private const byte CmdStop = 0x02;
    private const byte CmdSetColor = 0x03;
    private const byte CmdSetContrast = 0x04;
    private const byte CmdSetDisplayArea = 0x05;
    private const byte CmdSetPixelDataAddress = 0x06;
    private const byte CmdChangeColorAndContrast = 0x07;
    private const byte CmdEnd = 0xFF;

    public static DecodedSubPicture? Decode(byte[] spu, SpuPalette palette)
    {
        if (spu.Length < 6)
        {
            return null;
        }

        var controlOffset = spu[2] << 8 | spu[3];
        if (controlOffset >= spu.Length)
        {
            return null;
        }

        // Four active colors, filled by SetColor/SetContrast. Index 0 = background.
        var colors = new (byte R, byte G, byte B, byte A)[4];
        var forced = false;
        TimeSpan? duration = null;
        int x0 = 0, y0 = 0, x1 = -1, y1 = -1;
        var topFieldAddress = 0;
        var bottomFieldAddress = 0;

        // Control sequences chain via next-pointer; last one points at itself.
        var seq = controlOffset;
        var lastSeq = -1;
        var guard = 0;
        while (seq > lastSeq && seq + 4 <= spu.Length && guard++ < 64)
        {
            var delay = spu[seq] << 8 | spu[seq + 1];
            var next = spu[seq + 2] << 8 | spu[seq + 3];
            var i = seq + 4;

            var commands = 0;
            while (i < spu.Length && spu[i] != CmdEnd && commands++ < 1000)
            {
                switch (spu[i])
                {
                    case CmdForcedStart:
                        forced = true;
                        i++;
                        break;
                    case CmdStart:
                        i++;
                        break;
                    case CmdStop:
                        // Delay unit is 90kHz/1024 ticks.
                        duration = TimeSpan.FromMilliseconds((delay << 10) / 90.0);
                        i++;
                        break;
                    case CmdSetColor when i + 2 < spu.Length:
                        SetColor(colors, 3, spu[i + 1] >> 4, palette);
                        SetColor(colors, 2, spu[i + 1] & 0x0F, palette);
                        SetColor(colors, 1, spu[i + 2] >> 4, palette);
                        SetColor(colors, 0, spu[i + 2] & 0x0F, palette);
                        i += 3;
                        break;
                    case CmdSetContrast when i + 2 < spu.Length:
                        // Nibble alpha 0..15 scaled to 0..255.
                        colors[3].A = (byte)((spu[i + 1] >> 4) * 17);
                        colors[2].A = (byte)((spu[i + 1] & 0x0F) * 17);
                        colors[1].A = (byte)((spu[i + 2] >> 4) * 17);
                        colors[0].A = (byte)((spu[i + 2] & 0x0F) * 17);
                        i += 3;
                        break;
                    case CmdSetDisplayArea when i + 6 < spu.Length:
                        if (x1 < 0)
                        {
                            x0 = (spu[i + 1] << 8 | spu[i + 2]) >> 4;
                            x1 = (spu[i + 2] & 0x0F) << 8 | spu[i + 3];
                            y0 = (spu[i + 4] << 8 | spu[i + 5]) >> 4;
                            y1 = (spu[i + 5] & 0x0F) << 8 | spu[i + 6];
                        }

                        i += 7;
                        break;
                    case CmdSetPixelDataAddress when i + 4 < spu.Length:
                        topFieldAddress = spu[i + 1] << 8 | spu[i + 2];
                        bottomFieldAddress = spu[i + 3] << 8 | spu[i + 4];
                        i += 5;
                        break;
                    case CmdChangeColorAndContrast when i + 2 < spu.Length:
                        // Skip parameter area; low byte of size is sufficient for real discs.
                        i += Math.Max(2, (int)spu[i + 2]);
                        break;
                    default:
                        i++;
                        break;
                }
            }

            lastSeq = seq;
            seq = next;
        }

        var width = x1 - x0 + 1;
        var height = y1 - y0 + 1;
        if (width <= 0 || height <= 0 || width > 2048 || height > 2048 || topFieldAddress == 0)
        {
            return null;
        }

        var bmp = new SubBitmap(width, height);
        DecodeField(spu, bmp, startY: 0, topFieldAddress, colors);
        DecodeField(spu, bmp, startY: 1, bottomFieldAddress, colors);

        return new DecodedSubPicture { Bitmap = bmp, Forced = forced, Duration = duration };
    }

    private static void SetColor((byte R, byte G, byte B, byte A)[] colors, int index, int clutIndex, SpuPalette palette)
    {
        if (clutIndex < palette.Colors.Count)
        {
            var (r, g, b) = palette.Colors[clutIndex];
            colors[index].R = r;
            colors[index].G = g;
            colors[index].B = b;
        }
    }

    /// <summary>Decodes one interlaced field (even or odd scanlines).</summary>
    private static void DecodeField(byte[] data, SubBitmap bmp, int startY, int dataAddress, (byte R, byte G, byte B, byte A)[] colors)
    {
        var index = 0;
        var onlyHalf = false;
        var y = startY;
        var x = 0;

        while (y < bmp.Height && dataAddress + index + 2 < data.Length)
        {
            index += DecodeRle(dataAddress + index, data, out var color, out var runLength, ref onlyHalf, out var restOfLine);
            if (restOfLine)
            {
                runLength = bmp.Width - x;
            }

            var c = colors[color];
            for (var i = 0; i < runLength; i++, x++)
            {
                if (x >= bmp.Width - 1)
                {
                    // End of scanline: run codes are nibble-aligned per line, so re-align.
                    if (y < bmp.Height && x < bmp.Width && color != 0)
                    {
                        bmp.SetPixel(x, y, c.R, c.G, c.B, c.A);
                    }

                    if (onlyHalf)
                    {
                        onlyHalf = false;
                        index++;
                    }

                    x = 0;
                    y += 2;
                    break;
                }

                if (y < bmp.Height && color != 0)
                {
                    bmp.SetPixel(x, y, c.R, c.G, c.B, c.A);
                }
            }
        }
    }

    /// <summary>
    /// Variable-length RLE codes (n=length bits, c=color bits):
    ///   1-3:    nncc              (nibble)
    ///   4-15:   00nnnncc          (byte)
    ///   16-63:  0000nnnnnncc      (1.5 bytes)
    ///   64-255: 000000nnnnnnnncc  (2 bytes); length 0 = fill to end of line.
    /// Returns bytes consumed; <paramref name="onlyHalf"/> tracks nibble alignment.
    /// </summary>
    private static int DecodeRle(int index, byte[] data, out int color, out int runLength, ref bool onlyHalf, out bool restOfLine)
    {
        restOfLine = false;
        var b1 = data[index];
        var b2 = data[index + 1];

        if (onlyHalf)
        {
            var b3 = data[index + 2];
            b1 = (byte)(((b1 & 0x0F) << 4) | ((b2 & 0xF0) >> 4));
            b2 = (byte)(((b2 & 0x0F) << 4) | ((b3 & 0xF0) >> 4));
        }

        if (b1 >> 2 == 0)
        {
            runLength = (b1 << 6) | (b2 >> 2);
            color = b2 & 0x03;
            if (runLength == 0)
            {
                restOfLine = true;
                if (onlyHalf)
                {
                    onlyHalf = false;
                    return 3;
                }
            }

            return 2;
        }

        if (b1 >> 4 == 0)
        {
            runLength = (b1 << 2) | (b2 >> 6);
            color = (b2 & 0x30) >> 4;
            if (onlyHalf)
            {
                onlyHalf = false;
                return 2;
            }

            onlyHalf = true;
            return 1;
        }

        if (b1 >> 6 == 0)
        {
            runLength = b1 >> 2;
            color = b1 & 0x03;
            return 1;
        }

        runLength = b1 >> 6;
        color = (b1 & 0x30) >> 4;
        if (onlyHalf)
        {
            onlyHalf = false;
            return 1;
        }

        onlyHalf = true;
        return 0;
    }
}
