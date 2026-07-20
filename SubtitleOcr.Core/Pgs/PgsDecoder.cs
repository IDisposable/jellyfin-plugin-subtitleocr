using SubtitleOcr.Core.Imaging;

namespace SubtitleOcr.Core.Pgs;

/// <summary>Whether a PGS display set shows a subtitle, clears the screen, or is a no-op (e.g. palette-only fade).</summary>
public enum PgsDisplayKind
{
    /// <summary>Palette-only update or references to objects not present in this packet; carries no new content.</summary>
    Ignore,

    /// <summary>A renderable composition; <see cref="PgsDisplaySet.Bitmap"/> is set.</summary>
    Show,

    /// <summary>An empty composition; marks the end of the previous subtitle.</summary>
    Clear,
}

/// <summary>Result of decoding one PGS display set (one demuxed packet).</summary>
public sealed class PgsDisplaySet
{
    public required PgsDisplayKind Kind { get; init; }
    public SubBitmap? Bitmap { get; init; }
    public bool Forced { get; init; }

    /// <summary>Normalized vertical center of the subtitle on screen (0 top, 1 bottom); ~0.9 for normal
    /// bottom placement. Lower values indicate a positioned subtitle (e.g. a top sign) that SRT would move.</summary>
    public double VerticalCenter { get; init; } = 0.9;

    /// <summary>Normalized horizontal center of the subtitle on screen (0 left, 1 right); 0.5 for centered
    /// dialogue. Off-center values indicate a positioned sign that only ASS can place.</summary>
    public double HorizontalCenter { get; init; } = 0.5;

    public static PgsDisplaySet Clear { get; } = new() { Kind = PgsDisplayKind.Clear };
    public static PgsDisplaySet Ignore { get; } = new() { Kind = PgsDisplayKind.Ignore };
}

/// <summary>
/// Decodes a Blu-ray Presentation Graphic Stream (PGS/HDMV) display set as delivered by
/// ffmpeg/ffprobe demuxers: a concatenation of segments, each [type:1][length:2][payload].
/// The PG magic and per-segment PTS of the .sup file format are not present; timing comes from
/// the packet. Palette (PDS) and objects (ODS) are self-contained in the set, so no external
/// palette is needed. Cross-checked against libavcodec's pgssubdec.c and the US 7,912,305 spec.
/// </summary>
public static class PgsDecoder
{
    private const byte SegPalette = 0x14;
    private const byte SegObject = 0x15;
    private const byte SegPresentation = 0x16;
    private const byte SegWindow = 0x17;
    private const byte SegEnd = 0x80;

    private const byte CompForcedFlag = 0x40;
    private const byte CompCroppedFlag = 0x80;

    public static PgsDisplaySet Decode(byte[] packet)
    {
        var palette = new (byte R, byte G, byte B, byte A)[256];
        var objects = new Dictionary<int, PgsObject>();
        var composition = new List<CompositionObject>();
        var hasPresentation = false;
        var paletteUpdateOnly = false;
        var screenWidth = 0;
        var screenHeight = 0;

        var pos = 0;
        while (pos + 3 <= packet.Length)
        {
            var type = packet[pos];
            var length = (packet[pos + 1] << 8) | packet[pos + 2];
            pos += 3;
            if (pos + length > packet.Length)
            {
                break;
            }

            var payload = pos;
            switch (type)
            {
                case SegPresentation:
                    hasPresentation = true;
                    ParsePresentation(packet, payload, length, composition, out paletteUpdateOnly, out screenWidth, out screenHeight);
                    break;
                case SegPalette:
                    ParsePalette(packet, payload, length, palette);
                    break;
                case SegObject:
                    ParseObject(packet, payload, length, objects);
                    break;
                case SegWindow:
                case SegEnd:
                default:
                    break;
            }

            pos += length;
        }

        if (!hasPresentation || composition.Count == 0)
        {
            return PgsDisplaySet.Clear;
        }

        // A palette-only update (fade) or a composition whose objects were sent in an earlier packet
        // carries no renderable object here; leave the current subtitle untouched.
        var renderable = composition.FindAll(c => objects.TryGetValue(c.ObjectId, out var o) && o.IsComplete);
        if (renderable.Count == 0 || paletteUpdateOnly)
        {
            return PgsDisplaySet.Ignore;
        }

        var bitmap = Composite(renderable, objects, palette, out var contentTop, out var contentBottom, out var contentLeft, out var contentRight);
        if (bitmap is null)
        {
            return PgsDisplaySet.Ignore;
        }

        return new PgsDisplaySet
        {
            Kind = PgsDisplayKind.Show,
            Bitmap = bitmap,
            Forced = renderable.Exists(c => c.Forced),
            VerticalCenter = screenHeight > 0 ? (contentTop + contentBottom) / 2.0 / screenHeight : 0.9,
            HorizontalCenter = screenWidth > 0 ? (contentLeft + contentRight) / 2.0 / screenWidth : 0.5,
        };
    }

    private static void ParsePresentation(byte[] b, int pos, int length, List<CompositionObject> composition, out bool paletteUpdateOnly, out int screenWidth, out int screenHeight)
    {
        composition.Clear();
        paletteUpdateOnly = false;
        screenWidth = 0;
        screenHeight = 0;
        if (length < 11)
        {
            return;
        }

        screenWidth = (b[pos] << 8) | b[pos + 1];
        screenHeight = (b[pos + 2] << 8) | b[pos + 3];

        // width(2) height(2) frameRate(1) compositionNumber(2) compositionState(1)
        paletteUpdateOnly = b[pos + 8] != 0; // palette_update_flag
        // paletteId(1) at pos+9
        var count = b[pos + 10];
        var i = pos + 11;
        var end = pos + length;

        for (var n = 0; n < count && i + 8 <= end; n++)
        {
            var objectId = (b[i] << 8) | b[i + 1];
            var flags = b[i + 3];
            var x = (b[i + 4] << 8) | b[i + 5];
            var y = (b[i + 6] << 8) | b[i + 7];
            i += 8;

            var cropped = (flags & CompCroppedFlag) != 0;
            int cropX = 0, cropY = 0, cropW = 0, cropH = 0;
            if (cropped)
            {
                if (i + 8 > end)
                {
                    break;
                }

                cropX = (b[i] << 8) | b[i + 1];
                cropY = (b[i + 2] << 8) | b[i + 3];
                cropW = (b[i + 4] << 8) | b[i + 5];
                cropH = (b[i + 6] << 8) | b[i + 7];
                i += 8;
            }

            composition.Add(new CompositionObject
            {
                ObjectId = objectId,
                Forced = (flags & CompForcedFlag) != 0,
                X = x,
                Y = y,
                Cropped = cropped,
                CropX = cropX,
                CropY = cropY,
                CropWidth = cropW,
                CropHeight = cropH,
            });
        }
    }

    private static void ParsePalette(byte[] b, int pos, int length, (byte R, byte G, byte B, byte A)[] palette)
    {
        // palette_id(1) palette_version(1) then 5-byte entries: id, Y, Cr, Cb, alpha.
        for (var i = pos + 2; i + 5 <= pos + length; i += 5)
        {
            var id = b[i];
            var (red, green, blue) = YCbCrToRgb(b[i + 1], b[i + 2], b[i + 3]);
            palette[id] = (red, green, blue, b[i + 4]);
        }
    }

    private static void ParseObject(byte[] b, int pos, int length, Dictionary<int, PgsObject> objects)
    {
        if (length < 4)
        {
            return;
        }

        var objectId = (b[pos] << 8) | b[pos + 1];
        var sequenceFlag = b[pos + 3];
        var isFirst = (sequenceFlag & 0x80) != 0;
        var isLast = (sequenceFlag & 0x40) != 0;

        if (isFirst)
        {
            if (length < 11)
            {
                return;
            }

            // dataLength(3) width(2) height(2) then RLE.
            var width = (b[pos + 7] << 8) | b[pos + 8];
            var height = (b[pos + 9] << 8) | b[pos + 10];
            var obj = new PgsObject { Width = width, Height = height };
            obj.Rle.AddRange(new ArraySegment<byte>(b, pos + 11, length - 11));
            obj.IsComplete = isLast;
            objects[objectId] = obj;
        }
        else if (objects.TryGetValue(objectId, out var existing))
        {
            existing.Rle.AddRange(new ArraySegment<byte>(b, pos + 4, length - 4));
            existing.IsComplete = isLast;
        }
    }

    private static SubBitmap? Composite(List<CompositionObject> composition, Dictionary<int, PgsObject> objects, (byte R, byte G, byte B, byte A)[] palette, out int contentTop, out int contentBottom, out int contentLeft, out int contentRight)
    {
        contentTop = 0;
        contentBottom = 0;
        contentLeft = 0;
        contentRight = 0;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var c in composition)
        {
            var obj = objects[c.ObjectId];
            var w = c.Cropped ? c.CropWidth : obj.Width;
            var h = c.Cropped ? c.CropHeight : obj.Height;
            if (w <= 0 || h <= 0)
            {
                continue;
            }

            minX = Math.Min(minX, c.X);
            minY = Math.Min(minY, c.Y);
            maxX = Math.Max(maxX, c.X + w);
            maxY = Math.Max(maxY, c.Y + h);
        }

        if (maxX <= minX || maxY <= minY)
        {
            return null;
        }

        contentTop = minY;
        contentBottom = maxY;
        contentLeft = minX;
        contentRight = maxX;

        var canvas = new SubBitmap(maxX - minX, maxY - minY);
        foreach (var c in composition)
        {
            var obj = objects[c.ObjectId];
            var pixels = obj.DecodePixels();
            var srcX = c.Cropped ? c.CropX : 0;
            var srcY = c.Cropped ? c.CropY : 0;
            var w = c.Cropped ? c.CropWidth : obj.Width;
            var h = c.Cropped ? c.CropHeight : obj.Height;

            for (var row = 0; row < h; row++)
            {
                var oy = srcY + row;
                if (oy < 0 || oy >= obj.Height)
                {
                    continue;
                }

                for (var col = 0; col < w; col++)
                {
                    var ox = srcX + col;
                    if (ox < 0 || ox >= obj.Width)
                    {
                        continue;
                    }

                    var (r, g, b, a) = palette[pixels[oy * obj.Width + ox]];
                    if (a > 0)
                    {
                        canvas.SetPixel(c.X - minX + col, c.Y - minY + row, r, g, b, a);
                    }
                }
            }
        }

        return canvas;
    }

    /// <summary>BT.709 limited-range YCbCr to RGB, matching Blu-ray HD content.</summary>
    private static (byte R, byte G, byte B) YCbCrToRgb(byte y, byte cr, byte cb)
    {
        var c = y - 16;
        var d = cb - 128;
        var e = cr - 128;
        var r = (298 * c + 459 * e + 128) >> 8;
        var g = (298 * c - 55 * d - 136 * e + 128) >> 8;
        var b = (298 * c + 541 * d + 128) >> 8;
        return (Clamp(r), Clamp(g), Clamp(b));
    }

    private static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

    private sealed class PgsObject
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public List<byte> Rle { get; } = new();
        public bool IsComplete { get; set; }

        /// <summary>Expands the accumulated RLE into an index-per-pixel buffer (Width*Height).</summary>
        public byte[] DecodePixels()
        {
            var pixels = new byte[Width * Height];
            var data = Rle;
            int x = 0, y = 0, pos = 0;

            while (pos < data.Count && y < Height)
            {
                var b0 = data[pos++];
                if (b0 != 0)
                {
                    if (x < Width)
                    {
                        pixels[y * Width + x++] = b0;
                    }

                    continue;
                }

                if (pos >= data.Count)
                {
                    break;
                }

                var b1 = data[pos++];
                if (b1 == 0)
                {
                    x = 0;
                    y++;
                    continue;
                }

                int run;
                byte color;
                switch (b1 & 0xC0)
                {
                    case 0x00:
                        run = b1 & 0x3F;
                        color = 0;
                        break;
                    case 0x40:
                        if (pos >= data.Count)
                        {
                            return pixels;
                        }

                        run = ((b1 & 0x3F) << 8) | data[pos++];
                        color = 0;
                        break;
                    case 0x80:
                        if (pos >= data.Count)
                        {
                            return pixels;
                        }

                        run = b1 & 0x3F;
                        color = data[pos++];
                        break;
                    default:
                        if (pos + 1 >= data.Count)
                        {
                            return pixels;
                        }

                        run = ((b1 & 0x3F) << 8) | data[pos++];
                        color = data[pos++];
                        break;
                }

                for (var i = 0; i < run && x < Width; i++)
                {
                    pixels[y * Width + x++] = color;
                }
            }

            return pixels;
        }
    }

    private readonly struct CompositionObject
    {
        public int ObjectId { get; init; }
        public bool Forced { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public bool Cropped { get; init; }
        public int CropX { get; init; }
        public int CropY { get; init; }
        public int CropWidth { get; init; }
        public int CropHeight { get; init; }
    }
}
