using System.Collections.Frozen;

namespace SubtitleOcr.Core.Ocr;

/// <summary>
/// Normalizes subtitle language codes and answers the one question the OCR pipeline needs:
/// is this a Latin-script language? ffprobe emits ISO 639-2, inconsistently bibliographic (/B,
/// e.g. "ger", "gre") or terminological (/T, e.g. "deu", "ell"), and containers sometimes carry
/// 639-1 two-letter codes. Everything is folded to a canonical lowercase 639-2/T code so database
/// map lookups and the script check compare apples to apples.
/// </summary>
public static class LanguageCodes
{
    public const string Undetermined = "und";

    // ISO 639-2/B -> /T, differing codes only.
    private static readonly FrozenDictionary<string, string> BibliographicToTerminological = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["alb"] = "sqi", ["arm"] = "hye", ["baq"] = "eus", ["bur"] = "mya", ["chi"] = "zho",
        ["cze"] = "ces", ["dut"] = "nld", ["fre"] = "fra", ["geo"] = "kat", ["ger"] = "deu",
        ["gre"] = "ell", ["ice"] = "isl", ["mac"] = "mkd", ["mao"] = "mri", ["may"] = "msa",
        ["per"] = "fas", ["rum"] = "ron", ["slo"] = "slk", ["tib"] = "bod", ["wel"] = "cym",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // ISO 639-1 -> 639-2/T for the languages that realistically show up in video media files.
    private static readonly FrozenDictionary<string, string> TwoLetterToTerminological = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["en"] = "eng", ["fr"] = "fra", ["de"] = "deu", ["es"] = "spa", ["it"] = "ita",
        ["pt"] = "por", ["nl"] = "nld", ["sv"] = "swe", ["no"] = "nor", ["da"] = "dan",
        ["fi"] = "fin", ["pl"] = "pol", ["cs"] = "ces", ["sk"] = "slk", ["hu"] = "hun",
        ["ro"] = "ron", ["el"] = "ell", ["ru"] = "rus", ["bg"] = "bul", ["uk"] = "ukr",
        ["sr"] = "srp", ["hr"] = "hrv", ["mk"] = "mkd", ["be"] = "bel", ["tr"] = "tur",
        ["ar"] = "ara", ["he"] = "heb", ["fa"] = "fas", ["zh"] = "zho", ["ja"] = "jpn",
        ["ko"] = "kor", ["th"] = "tha", ["hi"] = "hin", ["is"] = "isl", ["et"] = "est",
        ["lv"] = "lav", ["lt"] = "lit", ["sl"] = "slv", ["ca"] = "cat", ["eu"] = "eus",
        ["gl"] = "glg",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // Non-Latin-script languages (639-2/T). Bias toward inclusion: the Latin path only adds
    // English l/I heuristics, so a false "Latin" corrupts non-Latin text, while a false
    // "non-Latin" merely skips a cosmetic fix. Serbian (srp) is treated as Cyrillic for that reason.
    private static readonly FrozenSet<string> NonLatinScript = new HashSet<string>(StringComparer.Ordinal)
    {
        "ell",                                                          // Greek
        "rus", "bul", "ukr", "srp", "mkd", "bel", "kaz", "kir", "tgk", "mon", // Cyrillic
        "heb", "yid",                                                   // Hebrew
        "ara", "fas", "urd", "pus",                                     // Arabic script
        "zho", "jpn", "kor",                                            // CJK
        "tha", "lao", "khm", "mya",                                     // SE Asian
        "hin", "ben", "tam", "tel", "kan", "mal", "guj", "pan", "ori", "sin", // Indic
        "hye", "kat", "amh", "tir",                                     // Armenian, Georgian, Ethiopic
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> TerminologicalToTwoLetter =
        TwoLetterToTerminological.ToFrozenDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    /// <summary>Maps a language to its ISO 639-1 two-letter code, or null when there is no mapping.</summary>
    public static string? ToTwoLetter(string? code) => TerminologicalToTwoLetter.GetValueOrDefault(Normalize(code));

    /// <summary>Folds any supported form to a canonical lowercase 639-2/T code; null/empty become "und".</summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Undetermined;
        }

        var c = code.Trim().ToLowerInvariant();

        if (c.Length == 2 && TwoLetterToTerminological.TryGetValue(c, out var fromTwo))
        {
            return fromTwo;
        }

        return BibliographicToTerminological.GetValueOrDefault(c, c);
    }

    /// <summary>
    /// True when the language uses Latin script (the default for unknown codes, preserving the
    /// bundled Latin database's behavior). Gates the English-specific OCR post-processing.
    /// </summary>
    public static bool IsLatinScript(string? code) => !NonLatinScript.Contains(Normalize(code));
}
