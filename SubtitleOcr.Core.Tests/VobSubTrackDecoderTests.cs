using SubtitleOcr.Core.Extraction;
using SubtitleOcr.Core.VobSub;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class VobSubTrackDecoderTests
{
    // The hand-encoded SPU carries no StopDisplay delay, so timing falls back through the chain.
    [Fact]
    public void Decode_NoSpuDuration_BoundsEndByNextPacketStart()
    {
        var spu = TestFixtures.BuildSpu();
        var packets = new List<SubtitlePacket>
        {
            new() { Data = spu, Pts = TimeSpan.FromSeconds(1) },
            new() { Data = spu, Pts = TimeSpan.FromSeconds(3) },
        };

        var images = VobSubTrackDecoder.Decode(packets, SpuPalette.Default);

        Assert.Equal(2, images.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), images[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(3), images[0].End); // next packet start
        // Last packet has no successor: End left equal to Start for the timing pass to default.
        Assert.Equal(images[1].Start, images[1].End);
    }

    [Fact]
    public void Decode_PacketDuration_UsedWhenSpuHasNoStopDisplay()
    {
        var packets = new List<SubtitlePacket>
        {
            new() { Data = TestFixtures.BuildSpu(), Pts = TimeSpan.FromSeconds(1), Duration = TimeSpan.FromSeconds(2) },
        };

        var images = VobSubTrackDecoder.Decode(packets, SpuPalette.Default);

        var image = Assert.Single(images);
        Assert.Equal(TimeSpan.FromSeconds(3), image.End); // Pts + packet duration
    }
}
