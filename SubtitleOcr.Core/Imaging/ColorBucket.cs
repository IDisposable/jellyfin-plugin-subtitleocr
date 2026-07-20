namespace SubtitleOcr.Core.Imaging;

/// <summary>
/// Buckets a color to a 5-bit-per-channel key, so shades a few levels apart count as one color: the mean
/// sampled from an antialiased glyph drifts a shade or two between cues, but the source palette is exact.
/// Shared by the foreground-color sampler, the ASS style vote, and the Auto-format color check, which all
/// have to bucket identically or they would disagree about how many colors a track uses.
/// </summary>
public static class ColorBucket
{
    public static int Key(int r, int g, int b) => ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);

    public static int Key((byte R, byte G, byte B) color) => Key(color.R, color.G, color.B);
}
