namespace SubtitleOcr.Core.VobSub;

/// <summary>
/// 16-entry RGB CLUT for dvdsub. ffmpeg carries the .idx-style text block
/// ("palette: 000000, f0f0f0, ...") as codec extradata, which is where this parses from.
/// </summary>
public sealed class SpuPalette
{
    public IReadOnlyList<(byte R, byte G, byte B)> Colors { get; }

    private SpuPalette(List<(byte, byte, byte)> colors) => Colors = colors;

    /// <summary>ffmpeg's default dvdsub palette, used when extradata carries none.</summary>
    public static SpuPalette Default { get; } = FromRgbValues(new uint[]
    {
        0x000000, 0x0000FF, 0x00FF00, 0xFF0000,
        0xFFFF00, 0xFF00FF, 0x00FFFF, 0xFFFFFF,
        0x808000, 0x8080FF, 0x800080, 0x80FF80,
        0x008080, 0xFF8080, 0x555555, 0xAAAAAA,
    });

    public static SpuPalette FromRgbValues(IReadOnlyList<uint> rgb)
    {
        var colors = new List<(byte, byte, byte)>(16);
        foreach (var v in rgb)
        {
            colors.Add(((byte)(v >> 16), (byte)(v >> 8), (byte)v));
        }

        while (colors.Count < 16)
        {
            colors.Add((0, 0, 0));
        }

        return new SpuPalette(colors);
    }

    /// <summary>
    /// Parses the idx-style extradata text. Returns Default when no palette line is present.
    /// </summary>
    public static SpuPalette FromExtradataText(string? extradata)
    {
        if (string.IsNullOrWhiteSpace(extradata))
        {
            return Default;
        }

        foreach (var rawLine in extradata.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("palette:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = new List<uint>(16);
            foreach (var token in line["palette:".Length..].Split(','))
            {
                if (uint.TryParse(token.Trim(), System.Globalization.NumberStyles.HexNumber, null, out var v))
                {
                    values.Add(v);
                }
            }

            if (values.Count > 0)
            {
                return FromRgbValues(values);
            }
        }

        return Default;
    }
}
