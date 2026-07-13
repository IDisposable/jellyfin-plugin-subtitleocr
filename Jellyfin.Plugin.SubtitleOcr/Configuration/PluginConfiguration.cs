using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SubtitleOcr.Configuration;

/// <summary>Maps one subtitle language to a .nocr database. Serialized as an array (XmlSerializer-friendly, unlike a dictionary).</summary>
public class LanguageDatabaseEntry
{
    /// <summary>ISO 639 language code (any of 639-1, 639-2/B, 639-2/T; normalized before lookup).</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Path to the .nocr database used for that language.</summary>
    public string Path { get; set; } = string.Empty;
}

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Path to a custom .nocr database; empty = bundled Latin database.</summary>
    public string NOcrDatabasePath { get; set; } = string.Empty;

    /// <summary>
    /// Per-language database overrides, matched on the stream language before falling back to
    /// <see cref="NOcrDatabasePath"/> then the bundled Latin database. Point these at Subtitle
    /// Edit's Cyrillic/Greek/etc. .nocr files to OCR non-Latin scripts.
    /// </summary>
    public LanguageDatabaseEntry[] LanguageDatabases { get; set; } = Array.Empty<LanguageDatabaseEntry>();

    /// <summary>Skips items whose target SRT file already exists.</summary>
    public bool OverwriteExisting { get; set; } = false;

    /// <summary>Placeholder for glyphs with no database match.</summary>
    public string UnknownCharacter { get; set; } = "*";

    /// <summary>Inter-glyph pixel gap treated as a word space.</summary>
    public int SpaceMinGap { get; set; } = 6;

    /// <summary>Per-glyph error budget for loose matching passes.</summary>
    public int MaxWrongPixels { get; set; } = 25;

    /// <summary>Enables the slowest, widest matching pass.</summary>
    public bool DeepSeek { get; set; } = true;

    /// <summary>Dark-text-on-light-background discs need inverted binarization.</summary>
    public bool InvertLuma { get; set; } = false;

    /// <summary>Skips subpictures flagged forced-only (usually foreign-dialogue overlays).</summary>
    public bool SkipForcedOnly { get; set; } = false;

    /// <summary>
    /// OCRs only image tracks whose language has no existing text-based subtitle (embedded text
    /// stream or external sidecar). On by default; disable to convert every image track.
    /// </summary>
    public bool SkipLanguagesWithTextSubtitle { get; set; } = true;

    /// <summary>Events with a higher unknown-glyph ratio are dropped rather than emitted as noise.</summary>
    public double MaxUnknownRatio { get; set; } = 0.4;
}
