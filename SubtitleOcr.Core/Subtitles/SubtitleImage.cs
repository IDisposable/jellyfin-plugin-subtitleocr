using SubtitleOcr.Core.Imaging;

namespace SubtitleOcr.Core.Subtitles;

/// <summary>
/// One rendered subtitle image with its display window, independent of source format
/// (VobSub SPU or PGS display set). The OCR pipeline consumes these uniformly.
/// </summary>
public sealed class SubtitleImage
{
    public required SubBitmap Bitmap { get; init; }
    public TimeSpan Start { get; init; }

    /// <summary>End of display; equal to Start when unknown, left for timing normalization to bound.</summary>
    public TimeSpan End { get; init; }

    public bool Forced { get; init; }
}
