using System.IO.Compression;
using System.Reflection;
using System.Text;
using SubtitleOcr.Core.Imaging;

namespace SubtitleOcr.Core.NOcr;

/// <summary>
/// nOCR glyph database, file-compatible with Subtitle Edit's .nocr format (V1 and V2 read,
/// V2 write) so databases trained/corrected in SE's GUI drop straight in.
/// Match cascade thresholds ported from SE's NOcrDb (MIT).
/// </summary>
public sealed class NOcrDb
{
    private const string Version = "V2";

    // Entries with fewer test lines match almost anything of similar dimensions.
    private const int MinLinesForSingleMatch = 1;

    public List<NOcrChar> OcrCharacters { get; } = new();
    public List<NOcrChar> OcrCharactersExpanded { get; } = new();

    public int TotalCharacterCount => OcrCharacters.Count + OcrCharactersExpanded.Count;

    public static NOcrDb LoadFile(string fileName)
    {
        using var stream = File.OpenRead(fileName);
        return Load(stream);
    }

    /// <summary>Loads the bundled Latin database (from Subtitle Edit, MIT; see NOTICE.md).</summary>
    public static NOcrDb LoadEmbeddedLatin()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("SubtitleOcr.Core.Assets.Latin.nocr")
            ?? throw new InvalidOperationException("Embedded Latin.nocr resource missing");
        return Load(stream);
    }

    public static NOcrDb Load(Stream gzipStream)
    {
        byte[] buffer;
        using (var memory = new MemoryStream())
        {
            using (var gz = new GZipStream(gzipStream, CompressionMode.Decompress, leaveOpen: true))
            {
                gz.CopyTo(memory);
            }

            buffer = memory.ToArray();
        }

        var db = new NOcrDb();
        var versionBytes = Encoding.ASCII.GetBytes(Version);
        var isVersion2 = buffer.Length >= versionBytes.Length &&
                         buffer.AsSpan(0, versionBytes.Length).SequenceEqual(versionBytes);
        var position = isVersion2 ? versionBytes.Length : 0;

        while (true)
        {
            var ocrChar = new NOcrChar(ref position, buffer, isVersion2);
            if (!ocrChar.LoadedOk)
            {
                break;
            }

            (ocrChar.ExpandCount > 0 ? db.OcrCharactersExpanded : db.OcrCharacters).Add(ocrChar);
        }

        return db;
    }

    public void Save(string fileName)
    {
        var tempFileName = fileName + ".tmp";
        using (var gz = new GZipStream(File.Create(tempFileName), CompressionMode.Compress))
        {
            var versionBytes = Encoding.ASCII.GetBytes(Version);
            gz.Write(versionBytes, 0, versionBytes.Length);
            foreach (var c in OcrCharacters)
            {
                c.Save(gz);
            }

            foreach (var c in OcrCharactersExpanded)
            {
                c.Save(gz);
            }
        }

        File.Move(tempFileName, fileName, overwrite: true);
    }

    public NOcrChar? GetMatch(SubBitmap bitmap, int topMargin, bool deepSeek, int maxWrongPixels)
    {
        var exact = GetExactMatch(bitmap, topMargin);
        if (exact != null)
        {
            return exact;
        }

        var heightToWidthPercent = bitmap.Height * 100.0 / bitmap.Width;

        foreach (var pass in MatchPasses)
        {
            if (pass.RequireDeepSeek && !deepSeek)
            {
                continue;
            }

            if (maxWrongPixels < pass.MinAllowance)
            {
                continue;
            }

            var errorsAllowed = pass.ErrorsAllowed(maxWrongPixels);
            foreach (var oc in OcrCharacters)
            {
                if (PassFilter(bitmap, heightToWidthPercent, oc, topMargin, pass) &&
                    IsMatch(bitmap, oc, errorsAllowed))
                {
                    return oc;
                }
            }
        }

        return null;
    }

    private NOcrChar? GetExactMatch(SubBitmap bitmap, int topMargin)
    {
        foreach (var oc in OcrCharacters)
        {
            if (bitmap.Width == oc.Width && bitmap.Height == oc.Height &&
                Math.Abs(oc.MarginTop - topMargin) < 5 && IsMatch(bitmap, oc, 0))
            {
                return oc;
            }
        }

        return null;
    }

    private static bool PassFilter(SubBitmap bitmap, double heightToWidthPercent, NOcrChar oc, int topMargin, in MatchPass pass)
    {
        if (pass.AspectMaxDelta != int.MaxValue &&
            Math.Abs(heightToWidthPercent - oc.HeightToWidthPercent) >= pass.AspectMaxDelta)
        {
            return false;
        }

        if (pass.SizeMaxDelta != int.MaxValue &&
            (Math.Abs(bitmap.Width - oc.Width) >= pass.SizeMaxDelta ||
             Math.Abs(bitmap.Height - oc.Height) >= pass.SizeMaxDelta))
        {
            return false;
        }

        if (Math.Abs(oc.MarginTop - topMargin) >= pass.MarginTopMaxDelta)
        {
            return false;
        }

        if (pass.MinLineCount > 0 &&
            oc.LinesForeground.Count + oc.LinesBackground.Count < pass.MinLineCount)
        {
            return false;
        }

        return pass.Sensitivity switch
        {
            SensitivityFilter.OnlySensitive => oc.IsSensitive,
            SensitivityFilter.NotSensitive => !oc.IsSensitive,
            _ => true,
        };
    }

    /// <summary>
    /// Tests glyph lines against the bitmap alpha channel: foreground points must land on
    /// text (alpha &gt; 150), background points must not; up to errorsAllowed misses.
    /// </summary>
    public static bool IsMatch(SubBitmap bitmap, NOcrChar oc, int errorsAllowed)
    {
        if (oc.LinesForeground.Count + oc.LinesBackground.Count < MinLinesForSingleMatch)
        {
            return false;
        }

        var errors = 0;
        var width = bitmap.Width;
        var height = bitmap.Height;

        foreach (var line in oc.LinesForeground)
        {
            foreach (var point in line.ScaledGetPoints(oc, width, height))
            {
                if ((uint)point.X < (uint)width && (uint)point.Y < (uint)height &&
                    bitmap.GetAlpha(point.X, point.Y) <= 150 && ++errors > errorsAllowed)
                {
                    return false;
                }
            }
        }

        foreach (var line in oc.LinesBackground)
        {
            foreach (var point in line.ScaledGetPoints(oc, width, height))
            {
                if ((uint)point.X < (uint)width && (uint)point.Y < (uint)height &&
                    bitmap.GetAlpha(point.X, point.Y) > 150 && ++errors > errorsAllowed)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private enum SensitivityFilter
    {
        Either,
        NotSensitive,
        OnlySensitive,
    }

    /// <summary>One row in the match cascade; earlier rows are stricter and win.</summary>
    private readonly struct MatchPass
    {
        public int MinAllowance { get; init; }
        public bool RequireDeepSeek { get; init; }
        public int AspectMaxDelta { get; init; }
        public int SizeMaxDelta { get; init; }
        public int MarginTopMaxDelta { get; init; }
        public int MinLineCount { get; init; }
        public SensitivityFilter Sensitivity { get; init; }
        public required Func<int, int> ErrorsAllowed { get; init; }
    }

    private static readonly MatchPass[] MatchPasses =
    {
        // Exact-ish: size + aspect screened, zero errors.
        new()
        {
            AspectMaxDelta = 15, SizeMaxDelta = 5, MarginTopMaxDelta = 5,
            ErrorsAllowed = _ => 0,
        },
        new()
        {
            MinAllowance = 1,
            AspectMaxDelta = int.MaxValue, SizeMaxDelta = 4, MarginTopMaxDelta = 8,
            ErrorsAllowed = _ => 1,
        },
        new()
        {
            MinAllowance = 1,
            AspectMaxDelta = int.MaxValue, SizeMaxDelta = 8, MarginTopMaxDelta = 8,
            ErrorsAllowed = _ => 1,
        },
        new()
        {
            MinAllowance = 2,
            AspectMaxDelta = 20, SizeMaxDelta = int.MaxValue, MarginTopMaxDelta = 15,
            ErrorsAllowed = max => Math.Min(3, max),
        },
        new()
        {
            MinAllowance = 10,
            AspectMaxDelta = 20, SizeMaxDelta = int.MaxValue, MarginTopMaxDelta = 15,
            MinLineCount = 41, Sensitivity = SensitivityFilter.NotSensitive,
            ErrorsAllowed = max => Math.Min(20, max),
        },
        new()
        {
            MinAllowance = 10,
            AspectMaxDelta = 30, SizeMaxDelta = int.MaxValue, MarginTopMaxDelta = 15,
            MinLineCount = 41, Sensitivity = SensitivityFilter.OnlySensitive,
            ErrorsAllowed = _ => 10,
        },
        new()
        {
            RequireDeepSeek = true,
            AspectMaxDelta = 60, SizeMaxDelta = int.MaxValue, MarginTopMaxDelta = 17,
            MinLineCount = 51,
            ErrorsAllowed = max => max,
        },
    };
}
