using SubtitleOcr.Core.Extraction;
using SubtitleOcr.Core.Subtitles;

namespace SubtitleOcr.Core.Pgs;

/// <summary>
/// Turns a PGS subtitle track's packets into timed images. Unlike VobSub, a PGS subtitle spans
/// display sets: a "show" set puts it on screen and a later "clear" (or the next "show") sets the
/// end time. Palette-only fade updates in between are ignored so they neither create nor end a
/// subtitle. One demuxed packet is assumed to carry one complete display set.
/// </summary>
public static class PgsTrackDecoder
{
    public static List<SubtitleImage> Decode(IReadOnlyList<SubtitlePacket> packets)
    {
        var images = new List<SubtitleImage>();
        PgsDisplaySet? pending = null;
        var pendingStart = TimeSpan.Zero;

        foreach (var packet in packets)
        {
            var set = PgsDecoder.Decode(packet.Data);
            if (set.Kind == PgsDisplayKind.Ignore)
            {
                continue;
            }

            // Any show or clear boundary ends the subtitle currently on screen.
            if (pending is not null)
            {
                images.Add(new SubtitleImage
                {
                    Bitmap = pending.Bitmap!,
                    VerticalCenter = pending.VerticalCenter,
                    HorizontalCenter = pending.HorizontalCenter,
                    Start = pendingStart,
                    End = packet.Pts,
                    Forced = pending.Forced,
                });
                pending = null;
            }

            if (set.Kind == PgsDisplayKind.Show)
            {
                pending = set;
                pendingStart = packet.Pts;
            }
        }

        // A show with no trailing clear: leave End == Start for the timing pass to apply a default.
        if (pending is not null)
        {
            images.Add(new SubtitleImage
            {
                Bitmap = pending.Bitmap!,
                Start = pendingStart,
                End = pendingStart,
                Forced = pending.Forced,
            });
        }

        return images;
    }
}
