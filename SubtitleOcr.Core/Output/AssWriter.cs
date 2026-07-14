using System.Globalization;
using System.Text;

namespace SubtitleOcr.Core.Output;

/// <summary>
/// Serializes subtitle events to Advanced SubStation Alpha (.ass). A single default style is emitted; on-screen
/// position from the source image is not preserved (events default to bottom-centre), but the style gives the
/// user control over font and size that SRT cannot.
/// </summary>
public static class AssWriter
{
    private const string Header =
        "[Script Info]\n" +
        "ScriptType: v4.00+\n" +
        "WrapStyle: 0\n" +
        "ScaledBorderAndShadow: yes\n" +
        "PlayResX: 384\n" +
        "PlayResY: 288\n" +
        "\n" +
        "[V4+ Styles]\n" +
        "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
        "Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1\n" +
        "\n" +
        "[Events]\n" +
        "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n";

    public static string Serialize(IReadOnlyList<SubtitleEvent> events)
    {
        var sb = new StringBuilder(Header);
        foreach (var e in events)
        {
            var text = Alignment(e.VerticalCenter) + e.Text
                .Replace("\n", "\\N", StringComparison.Ordinal)
                .Replace("<i>", "{\\i1}", StringComparison.Ordinal)
                .Replace("</i>", "{\\i0}", StringComparison.Ordinal);

            sb.Append("Dialogue: 0,").Append(Time(e.Start)).Append(',').Append(Time(e.End))
                .Append(",Default,,0,0,0,,").Append(text).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Buckets vertical placement into an alignment override: top gets \an8, mid-screen \an5, and normal
    /// bottom placement no override. Buckets avoid \pos and the PlayResX/Y mapping it would require.</summary>
    private static string Alignment(double verticalCenter) => verticalCenter switch
    {
        < 0.35 => "{\\an8}",
        < 0.60 => "{\\an5}",
        _ => string.Empty,
    };

    /// <summary>ASS timecode: H:MM:SS.cc (centiseconds).</summary>
    private static string Time(TimeSpan t) =>
        string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}.{3:00}", (int)t.TotalHours, t.Minutes, t.Seconds, t.Milliseconds / 10);
}
