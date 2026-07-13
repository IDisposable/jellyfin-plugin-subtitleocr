using SubtitleOcr.Core.Extraction;
using SubtitleOcr.Core.Pgs;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class PgsDecoderTests
{
    // A 4x2 object, all pixels palette index 1 (opaque white), placed at (10, 20).
    private static byte[] BuildShowDisplaySet(bool forced)
    {
        // ODS RLE: two lines of "4 pixels of color 1" (00 84 01) each ended by EOL (00 00).
        byte[] rle = { 0x00, 0x84, 0x01, 0x00, 0x00, 0x00, 0x84, 0x01, 0x00, 0x00 };

        var pcs = new List<byte>
        {
            0x07, 0x80,             // width 1920
            0x04, 0x38,             // height 1080
            0x10,                   // frame rate
            0x00, 0x00,             // composition number
            0x80,                   // composition state: epoch start
            0x00,                   // palette update flag
            0x00,                   // palette id
            0x01,                   // one composition object
            0x00, 0x00,             // object id 0
            0x00,                   // window id
            (byte)(forced ? 0x40 : 0x00), // forced flag
            0x00, 0x0A,             // x = 10
            0x00, 0x14,             // y = 20
        };

        var pds = new List<byte>
        {
            0x00,                   // palette id
            0x00,                   // palette version
            0x01, 235, 128, 128, 255, // entry 1: white, opaque (Y, Cr, Cb, alpha)
        };

        var ods = new List<byte>
        {
            0x00, 0x00,             // object id 0
            0x00,                   // version
            0xC0,                   // first + last in sequence
            0x00, 0x00, 0x0E,       // data length = width+height+rle = 14
            0x00, 0x04,             // width 4
            0x00, 0x02,             // height 2
        };
        ods.AddRange(rle);

        var packet = new List<byte>();
        AppendSegment(packet, 0x16, pcs);
        AppendSegment(packet, 0x14, pds);
        AppendSegment(packet, 0x15, ods);
        AppendSegment(packet, 0x80, new List<byte>()); // END
        return packet.ToArray();
    }

    private static byte[] BuildClearDisplaySet()
    {
        var pcs = new List<byte>
        {
            0x07, 0x80, 0x04, 0x38, 0x10, 0x00, 0x00, 0x80, 0x00, 0x00,
            0x00, // zero composition objects
        };
        var packet = new List<byte>();
        AppendSegment(packet, 0x16, pcs);
        AppendSegment(packet, 0x80, new List<byte>());
        return packet.ToArray();
    }

    private static void AppendSegment(List<byte> packet, byte type, List<byte> payload)
    {
        packet.Add(type);
        packet.Add((byte)(payload.Count >> 8));
        packet.Add((byte)payload.Count);
        packet.AddRange(payload);
    }

    [Fact]
    public void Decode_ShowDisplaySet_ProducesBitmap()
    {
        var set = PgsDecoder.Decode(BuildShowDisplaySet(forced: false));

        Assert.Equal(PgsDisplayKind.Show, set.Kind);
        Assert.NotNull(set.Bitmap);
        Assert.Equal(4, set.Bitmap!.Width);
        Assert.Equal(2, set.Bitmap.Height);
        Assert.False(set.Forced);
    }

    [Fact]
    public void Decode_ShowDisplaySet_PaintsOpaqueWhite()
    {
        var set = PgsDecoder.Decode(BuildShowDisplaySet(forced: false));
        Assert.NotNull(set.Bitmap);

        var opaque = 0;
        for (var y = 0; y < set.Bitmap!.Height; y++)
        {
            for (var x = 0; x < set.Bitmap.Width; x++)
            {
                var (r, g, b, a) = set.Bitmap.GetPixel(x, y);
                if (a == 255 && r == 255 && g == 255 && b == 255)
                {
                    opaque++;
                }
            }
        }

        Assert.Equal(8, opaque); // 4x2 all painted
    }

    [Fact]
    public void Decode_ReadsForcedFlag()
    {
        var set = PgsDecoder.Decode(BuildShowDisplaySet(forced: true));
        Assert.True(set.Forced);
    }

    [Fact]
    public void Decode_EmptyComposition_IsClear()
    {
        var set = PgsDecoder.Decode(BuildClearDisplaySet());
        Assert.Equal(PgsDisplayKind.Clear, set.Kind);
        Assert.Null(set.Bitmap);
    }

    [Fact]
    public void TrackDecoder_PairsShowWithFollowingClear()
    {
        var packets = new List<SubtitlePacket>
        {
            new() { Data = BuildShowDisplaySet(forced: false), Pts = TimeSpan.FromSeconds(1) },
            new() { Data = BuildClearDisplaySet(), Pts = TimeSpan.FromSeconds(3) },
        };

        var images = PgsTrackDecoder.Decode(packets);

        var image = Assert.Single(images);
        Assert.Equal(TimeSpan.FromSeconds(1), image.Start);
        Assert.Equal(TimeSpan.FromSeconds(3), image.End);
    }
}
