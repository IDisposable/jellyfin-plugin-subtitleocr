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

    /// <summary>Tag inserted into generated file names ({base}.{lang}.{tag}.srt), shown as the subtitle title in
    /// Jellyfin and marking the file as plugin-created. Because output only ever goes to this tagged path, the
    /// plugin never overwrites or deletes source or hand-made subtitles. Blank reverts to {base}.{lang}.srt and
    /// gives up that guarantee.</summary>
    public string SubtitleTitleTag { get; set; } = "OCR";

    /// <summary>Placeholder for glyphs with no database match.</summary>
    public string UnknownCharacter { get; set; } = "*";

    /// <summary>Absolute floor (px) for the word-space gap; the effective threshold is the larger of this and <see cref="SpaceGapFactor"/> times the line height.</summary>
    public int SpaceMinGap { get; set; } = 6;

    /// <summary>Word-space gap as a fraction of subtitle line height, so the threshold scales with resolution.</summary>
    public double SpaceGapFactor { get; set; } = 0.30;

    /// <summary>Only OCR these subtitle languages (ISO 639, e.g. eng, und). Empty extracts every language.</summary>
    public string[] Languages { get; set; } = Array.Empty<string>();

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

    /// <summary>A stream whose dropped-event ratio exceeds this is discarded entirely (no SRT written; a stale one is deleted), since the track OCR'd too poorly to be useful.</summary>
    public double MaxDroppedRatio { get; set; } = 0.25;

    /// <summary>Days before an unchanged, already-probed file is probed again. 0 re-probes only files that changed.</summary>
    public int RescanIntervalDays { get; set; } = 30;

    /// <summary>Probes every file on the next run, ignoring the scan cache; cleared automatically once that run starts.</summary>
    public bool ForceRescan { get; set; } = false;
}
