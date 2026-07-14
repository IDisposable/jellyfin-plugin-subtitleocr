using MediaBrowser.Model.Plugins;
using SubtitleOcr.Core.NOcr;

namespace Jellyfin.Plugin.SubtitleOcr.Configuration;

/// <summary>Output subtitle file format.</summary>
public enum SubtitleOutputFormat
{
    /// <summary>SubRip (.srt): maximum compatibility.</summary>
    Srt,

    /// <summary>Advanced SubStation Alpha (.ass): style control (no source positioning).</summary>
    Ass,

    /// <summary>Per track: ASS when any cue is positioned away from the bottom, otherwise SRT.</summary>
    Auto,
}

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

    /// <summary>Output subtitle format: SubRip (.srt) or Advanced SubStation Alpha (.ass).</summary>
    public SubtitleOutputFormat OutputFormat { get; set; } = SubtitleOutputFormat.Srt;

    /// <summary>Skips items whose target SRT file already exists.</summary>
    public bool OverwriteExisting { get; set; } = false;

    /// <summary>Tag inserted into generated file names ({base}.{lang}.{tag}.srt), shown as the subtitle title in
    /// Jellyfin and marking the file as plugin-created. Because output only ever goes to this tagged path, the
    /// plugin never overwrites or deletes source or hand-made subtitles. Blank reverts to {base}.{lang}.srt and
    /// gives up that guarantee.</summary>
    public string SubtitleTitleTag { get; set; } = "OCR";

    /// <summary>Placeholder for glyphs with no database match; the first character is used. Avoid "*":
    /// dialogue censors words with it, so an unread glyph could not be told from the source text.</summary>
    public string UnknownCharacter { get; set; } = NOcrEngineOptions.DefaultUnknownCharacter.ToString();

    /// <summary>The placeholder as the single character the later stages match on. Blank falls back to the
    /// default rather than meaning "none".</summary>
    public char Placeholder =>
        string.IsNullOrEmpty(UnknownCharacter) ? NOcrEngineOptions.DefaultUnknownCharacter : UnknownCharacter[0];

    /// <summary>Fold dot runs ("...", ". . .", "..") into a single ellipsis character.</summary>
    public bool NormalizeEllipsis { get; set; } = true;

    /// <summary>Tags a track whose text describes sound ("[door slams]") as hearing-impaired when the source
    /// flags nothing, as a remuxed disc usually does not.</summary>
    public bool DetectHearingImpaired { get; set; } = true;

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

    /// <summary>Corrects residual OCR misreads with a Hunspell dictionary, when a {language}.dic is present in the dictionaries folder.</summary>
    public bool SpellCheck { get; set; } = true;

    /// <summary>Auto-downloads a missing Hunspell dictionary for a language from <see cref="DictionaryDownloadUrl"/>.</summary>
    public bool DownloadDictionaries { get; set; } = false;

    /// <summary>URL template for dictionary download; {code} is the ISO 639-1 code, and .dic/.aff are appended.</summary>
    public string DictionaryDownloadUrl { get; set; } = "https://raw.githubusercontent.com/wooorm/dictionaries/main/dictionaries/{code}/index";

    /// <summary>Applies a Subtitle Edit OCR fix replace list ({language}_OCRFixReplaceList.xml) when present in the dictionaries folder.</summary>
    public bool UseOcrFixList { get; set; } = true;

    /// <summary>URL template for the OCR fix replace list; {code} is the ISO 639-2/T code (e.g. eng, deu).</summary>
    public string OcrFixListDownloadUrl { get; set; } = "https://raw.githubusercontent.com/SubtitleEdit/subtitleedit/main/Dictionaries/{code}_OCRFixReplaceList.xml";

    /// <summary>Re-download a cached dictionary or fix list older than this many days. 0 never refreshes.</summary>
    public int AssetRefreshDays { get; set; } = 30;

    /// <summary>A stream whose dropped-event ratio exceeds this is discarded entirely (no SRT written; a stale one is deleted), since the track OCR'd too poorly to be useful.</summary>
    public double MaxDroppedRatio { get; set; } = 0.25;

    /// <summary>Days before an unchanged, already-probed file is probed again. 0 re-probes only files that changed.</summary>
    public int RescanIntervalDays { get; set; } = 30;

    /// <summary>Probes every file on the next run, ignoring the scan cache; cleared automatically once that run starts.</summary>
    public bool ForceRescan { get; set; } = false;
}
