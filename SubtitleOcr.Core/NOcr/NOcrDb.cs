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
            NOcrChar? best = null;
            var bestErrors = int.MaxValue;
            var bestFit = int.MaxValue;

            foreach (var oc in OcrCharacters)
            {
                if (!PassFilter(bitmap, heightToWidthPercent, oc, topMargin, pass))
                {
                    continue;
                }

                var errors = MatchErrors(bitmap, oc, errorsAllowed);
                if (errors < 0)
                {
                    continue;
                }

                // Fewest wrong pixels wins; ties go to the closest fit. A trained glyph is scaled to the
                // candidate before matching, so "C" and "c" are one shape and only their size tells them
                // apart.
                var fit = Math.Abs(oc.MarginTop - topMargin) + Math.Abs(oc.Height - bitmap.Height) + Math.Abs(oc.Width - bitmap.Width);
                if (errors < bestErrors || (errors == bestErrors && fit < bestFit))
                {
                    best = oc;
                    bestErrors = errors;
                    bestFit = fit;
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    /// <summary>Largest blob run any expanded glyph spans; 0 when the database has none.</summary>
    public int MaxExpandCount => _maxExpandCount ??= OcrCharactersExpanded.Count == 0
        ? 0
        : OcrCharactersExpanded.Max(c => c.ExpandCount);

    private int? _maxExpandCount;

    /// <summary>
    /// Matches a merged run of <paramref name="expandCount"/> blobs against the glyphs trained to span that
    /// many. The segmenter splits these apart (a double quote into two marks, "ø" into three), so they are
    /// unmatchable one blob at a time.
    ///
    /// Deliberately not the single-glyph cascade. That cascade screens on <c>MarginTop</c> in raw pixels and
    /// never relaxes past 17, which rejects these outright twice over: a glyph drawn at twice its trained
    /// size sits at twice the offset, so the proportionally correct margin is already too far away to be
    /// believed, and the bundled "%" is trained at 27 and 150, offsets that describe what else shared its
    /// training line rather than anything about "%". Nor is size worth screening on, since the trained glyph
    /// is scaled to the candidate before it is compared.
    ///
    /// What is left is shape, so shape decides: aspect to screen and the error count to choose. Those can be
    /// strict here because there are only 19 entries to walk, where the cascade's filters exist to keep 671
    /// off the error counter. Strictness is the safety: a wrong hit here swallows two or three blobs and
    /// invents a word, where a miss only leaves glyphs to be read one at a time as before.
    /// </summary>
    public NOcrChar? GetExpandedMatch(SubBitmap bitmap, int topMargin, int expandCount, bool deepSeek, int maxWrongPixels)
    {
        var heightToWidthPercent = bitmap.Height * 100.0 / bitmap.Width;

        foreach (var pass in ExpandedMatchPasses)
        {
            var errorsAllowed = Math.Min(pass.ErrorsAllowed(maxWrongPixels), maxWrongPixels);
            NOcrChar? best = null;
            var bestErrors = int.MaxValue;
            var bestAspect = double.MaxValue;

            foreach (var oc in OcrCharactersExpanded)
            {
                if (oc.ExpandCount != expandCount)
                {
                    continue;
                }

                var aspectDelta = Math.Abs(heightToWidthPercent - oc.HeightToWidthPercent);
                if (aspectDelta >= pass.AspectMaxDelta)
                {
                    continue;
                }

                var errors = MatchErrors(bitmap, oc, errorsAllowed);
                if (errors < 0)
                {
                    continue;
                }

                // Fewest wrong pixels wins, ties to the nearer shape.
                if (errors < bestErrors || (errors == bestErrors && aspectDelta < bestAspect))
                {
                    best = oc;
                    bestErrors = errors;
                    bestAspect = aspectDelta;
                }
            }

            if (best is not null)
            {
                return best;
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
    public static bool IsMatch(SubBitmap bitmap, NOcrChar oc, int errorsAllowed) =>
        MatchErrors(bitmap, oc, errorsAllowed) >= 0;

    /// <summary>Wrong pixels for this glyph, or -1 once the budget is blown (it stops counting there).</summary>
    private static int MatchErrors(SubBitmap bitmap, NOcrChar oc, int errorsAllowed)
    {
        if (oc.LinesForeground.Count + oc.LinesBackground.Count < MinLinesForSingleMatch)
        {
            return -1;
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
                    return -1;
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
                    return -1;
                }
            }
        }

        return errors;
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

    /// <summary>
    /// The cascade for multi-blob glyphs: shape only, and tight. Two rows, because an exact hit should not
    /// have to survive the same error budget a degraded one needs. See <see cref="GetExpandedMatch"/> for why
    /// the margin and size screens are absent rather than merely loosened.
    /// </summary>
    private static readonly MatchPass[] ExpandedMatchPasses =
    {
        new()
        {
            AspectMaxDelta = 15, SizeMaxDelta = int.MaxValue, MarginTopMaxDelta = int.MaxValue,
            ErrorsAllowed = _ => 0,
        },
        new()
        {
            AspectMaxDelta = 20, SizeMaxDelta = int.MaxValue, MarginTopMaxDelta = int.MaxValue,
            ErrorsAllowed = max => Math.Min(3, max),
        },
    };

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
