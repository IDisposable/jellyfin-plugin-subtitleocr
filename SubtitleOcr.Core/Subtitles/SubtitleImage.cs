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

    /// <summary>Normalized vertical center on screen (0 top, 1 bottom); ~0.9 for normal bottom placement,
    /// lower for a positioned subtitle. Defaults to bottom for sources without position (VobSub).</summary>
    public double VerticalCenter { get; init; } = 0.9;

    /// <summary>Normalized horizontal center on screen (0 left, 1 right); 0.5 for centered dialogue. Only PGS
    /// supplies a real value; VobSub lacks the frame width, so it stays centered.</summary>
    public double HorizontalCenter { get; init; } = 0.5;
}
