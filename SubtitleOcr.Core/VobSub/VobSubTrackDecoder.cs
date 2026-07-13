using SubtitleOcr.Core.Extraction;
using SubtitleOcr.Core.Subtitles;

namespace SubtitleOcr.Core.VobSub;

/// <summary>
/// Turns a VobSub subtitle track's packets into timed images. Each SPU packet is self-contained,
/// so timing is per-packet: the SPU's own StopDisplay delay wins, then the container packet
/// duration, then the next packet's start; an unresolved end is left equal to start for the
/// timing-normalization pass to bound.
/// </summary>
public static class VobSubTrackDecoder
{
    public static List<SubtitleImage> Decode(IReadOnlyList<SubtitlePacket> packets, SpuPalette palette)
    {
        var images = new List<SubtitleImage>();

        for (var i = 0; i < packets.Count; i++)
        {
            var packet = packets[i];
            var decoded = SubPictureDecoder.Decode(packet.Data, palette);
            if (decoded is null)
            {
                continue;
            }

            images.Add(new SubtitleImage
            {
                Bitmap = decoded.Bitmap,
                Start = packet.Pts,
                End = ResolveEnd(packet, decoded.Duration, i, packets),
                Forced = decoded.Forced,
            });
        }

        return images;
    }

    private static TimeSpan ResolveEnd(SubtitlePacket packet, TimeSpan? spuDuration, int index, IReadOnlyList<SubtitlePacket> packets)
    {
        if (spuDuration is { } d && d > TimeSpan.Zero)
        {
            return packet.Pts + d;
        }

        if (packet.Duration is { } pd && pd > TimeSpan.Zero)
        {
            return packet.Pts + pd;
        }

        if (index + 1 < packets.Count && packets[index + 1].Pts > packet.Pts)
        {
            return packets[index + 1].Pts;
        }

        return packet.Pts;
    }
}
