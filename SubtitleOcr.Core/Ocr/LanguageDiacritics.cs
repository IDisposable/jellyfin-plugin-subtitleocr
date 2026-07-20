using System.Collections.Frozen;

namespace SubtitleOcr.Core.Ocr;

/// <summary>
/// The accented and special Latin letters that belong to each language's standard orthography, keyed by the
/// terminological ISO 639-2/T code <see cref="LanguageCodes.Normalize"/> produces. The diacritic fold keeps a
/// letter in this set and folds any other accented Latin letter to its base, on the reasoning that an accent
/// foreign to the track's language is an OCR misread, not real text. English is present with an empty set, so
/// it folds every accent; a language absent from the table is unknown, so its text is left untouched.
/// See DOCS/orthography.md for the sources and the judgment calls behind each set.
/// </summary>
public static class LanguageDiacritics
{
    // Lowercase only; the fold lowercases each candidate before the lookup. Sourced from each language's
    // alphabet and orthography.
    private static readonly FrozenDictionary<string, string> LegalByLanguage = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["eng"] = string.Empty,
        ["fra"] = "àâçéèêëîïôùûüÿœ",
        ["deu"] = "äöüß",
        ["spa"] = "áéíóúüñ",
        ["por"] = "áâãàçéêíóôõú",
        ["ita"] = "àèéìòóù",
        ["nld"] = "áéíóúëïöü",
        ["swe"] = "åäöé",
        ["nob"] = "æøåé",
        ["nno"] = "æøåé",
        ["nor"] = "æøåé",
        ["dan"] = "æøåé",
        ["fin"] = "äöå",
        ["isl"] = "áéíóúýþæöð",
        ["pol"] = "ąćęłńóśźż",
        ["ces"] = "áčďéěíňóřšťúůýž",
        ["slk"] = "áäčďéíĺľňóôŕšťúýž",
        ["hun"] = "áéíóöőúüű",
        // The comma-below ș/ț are the standard; the cedilla ş/ţ are the codepoints most files actually carry.
        ["ron"] = "ăâîșțşţ",
        ["hrv"] = "čćđšž",
        ["slv"] = "čšž",
        ["tur"] = "çğıöşü",
        ["cat"] = "àéèíïóòúüçŀ",
        ["est"] = "äöõüšž",
        ["lav"] = "āčēģīķļņšūž",
        ["lit"] = "ąčęėįšųūž",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The legal accented letters for <paramref name="normalizedLanguage"/> (a code from
    /// <see cref="LanguageCodes.Normalize"/>). Returns false when the language is not in the table, meaning
    /// its accents cannot be judged and nothing should be folded.
    /// </summary>
    public static bool TryGetLegalAccents(string normalizedLanguage, out string legalAccents) =>
        LegalByLanguage.TryGetValue(normalizedLanguage, out legalAccents!);
}
