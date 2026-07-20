using System.Globalization;
using System.Text;

namespace SubtitleOcr.Core.Output;

/// <summary>
/// Serializes subtitle events to Advanced SubStation Alpha (.ass). A single default style is emitted; on-screen
/// position from the source image is not preserved (events default to bottom-center), but the style gives the
/// user control over font and size that SRT cannot.
/// </summary>
public static class AssWriter
{
    /// <summary>What a subtitle is when the source did not say otherwise.</summary>
    private static readonly (byte R, byte G, byte B) White = (255, 255, 255);

    // Split where the style's PrimaryColour goes, which the track decides.
    private const string HeaderBeforeColor =
        "[Script Info]\n" +
        "ScriptType: v4.00+\n" +
        "WrapStyle: 0\n" +
        "ScaledBorderAndShadow: yes\n" +
        "PlayResX: 384\n" +
        "PlayResY: 288\n" +
        "\n" +
        "[V4+ Styles]\n" +
        "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
        "Style: Default,Arial,20,&H00";

    private const string HeaderAfterColor =
        ",&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1\n" +
        "\n" +
        "[Events]\n" +
        "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n";

    public static string Serialize(IReadOnlyList<SubtitleEvent> events)
    {
        // The track's own commonest color becomes the style, so only the cues that differ carry an override
        // and a wholly yellow track reads as a style rather than as a tag on every line.
        var styleColor = ModalColor(events);
        var sb = new StringBuilder(HeaderBeforeColor).Append(Bgr(styleColor)).Append(HeaderAfterColor);

        foreach (var e in events)
        {
            if (string.IsNullOrWhiteSpace(e.Text))
            {
                continue;
            }

            // Escape any literal brace from the OCR text first, so "{cough}" is shown and not parsed as an
            // override block; the style tags injected after add the only real braces.
            var text = Alignment(e.VerticalCenter) + ColorOverride(e.Color, styleColor) + e.Text
                .Replace("{", "\\{", StringComparison.Ordinal)
                .Replace("}", "\\}", StringComparison.Ordinal)
                .Replace("\n", "\\N", StringComparison.Ordinal)
                .Replace("<i>", "{\\i1}", StringComparison.Ordinal)
                .Replace("</i>", "{\\i0}", StringComparison.Ordinal);

            sb.Append("Dialogue: 0,").Append(Time(e.Start)).Append(',').Append(Time(e.End))
                .Append(",Default,,0,0,0,,").Append(text).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>The commonest cue color, shades bucketed together so antialiasing does not split the vote.
    /// White when no cue carries one.</summary>
    private static (byte R, byte G, byte B) ModalColor(IReadOnlyList<SubtitleEvent> events)
    {
        var counts = new Dictionary<int, (int Count, (byte R, byte G, byte B) Color)>();
        foreach (var e in events)
        {
            // A cue that will not be written casts no color vote.
            if (string.IsNullOrWhiteSpace(e.Text) || e.Color is not { } c)
            {
                continue;
            }

            var key = Key(c);
            counts.TryGetValue(key, out var seen);
            counts[key] = (seen.Count + 1, c);
        }

        var best = (Count: 0, Color: White);
        foreach (var entry in counts.Values)
        {
            if (entry.Count > best.Count)
            {
                best = entry;
            }
        }

        return best.Color;
    }

    /// <summary>Colors within a bucket of each other are the same color; the source palette is exact, but
    /// the sampled mean of an antialiased glyph lands a shade or two off.</summary>
    private static int Key((byte R, byte G, byte B) c) => ((c.R >> 3) << 10) | ((c.G >> 3) << 5) | (c.B >> 3);

    private static string ColorOverride((byte R, byte G, byte B)? color, (byte R, byte G, byte B) styleColor) =>
        color is { } c && Key(c) != Key(styleColor)
            ? "{\\c&H" + Bgr(c) + "&}"
            : string.Empty;

    /// <summary>ASS orders the channels backwards from HTML.</summary>
    private static string Bgr((byte R, byte G, byte B) c) =>
        string.Concat(
            c.B.ToString("X2", CultureInfo.InvariantCulture),
            c.G.ToString("X2", CultureInfo.InvariantCulture),
            c.R.ToString("X2", CultureInfo.InvariantCulture));

    /// <summary>Buckets vertical placement into an alignment override: top gets \an8, mid-screen \an5, and normal
    /// bottom placement no override. Buckets avoid \pos and the PlayResX/Y mapping it would require.</summary>
    private static string Alignment(double verticalCenter) => verticalCenter switch
    {
        < 0.35 => "{\\an8}",
        < 0.60 => "{\\an5}",
        _ => string.Empty,
    };

    /// <summary>ASS timecode H:MM:SS.cc, the centiseconds rounded to the nearest so a time lands on the closer
    /// boundary, not up to 9 ms early.</summary>
    private static string Time(TimeSpan t)
    {
        var cs = (long)Math.Round(t.TotalMilliseconds / 10.0, MidpointRounding.AwayFromZero);
        if (cs < 0)
        {
            cs = 0;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}.{3:00}", cs / 360000, cs / 6000 % 60, cs / 100 % 60, cs % 100);
    }
}
