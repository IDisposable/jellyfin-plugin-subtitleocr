using System.Text.RegularExpressions;

namespace SubtitleOcr.Core.Ocr;

/// <summary>
/// Guesses the language of OCR'd subtitle text by counting common function words per language, so an
/// untagged ("und") image track can be labelled with its real language. Latin-script languages only:
/// the bundled database OCRs those readably, and their function words survive OCR (mostly ASCII), which
/// is what makes this work even on text still peppered with unknown-glyph placeholders.
/// </summary>
public static partial class LanguageDetector
{
    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();

    // ISO 639-1 code -> distinctive common words (lowercase). Kept short and distinctive; heavy overlap
    // words (de, la, que) still count but the dominance check below is what separates close languages.
    private static readonly (string Code, string[] Words)[] Languages =
    {
        ("en", new[] { "the", "and", "you", "that", "was", "for", "are", "with", "they", "this", "have", "what", "your", "not", "his" }),
        ("da", new[] { "og", "er", "jeg", "ikke", "det", "til", "har", "som", "den", "med", "dig", "mig", "hvad", "skal", "være" }),
        ("no", new[] { "og", "er", "jeg", "ikke", "det", "til", "har", "som", "den", "med", "deg", "meg", "hva", "være", "ikkje" }),
        ("sv", new[] { "och", "är", "jag", "inte", "att", "det", "som", "på", "med", "för", "den", "har", "vad", "dig", "mig" }),
        ("fi", new[] { "että", "on", "ei", "ja", "hän", "minä", "mutta", "niin", "kuin", "mitä", "ole", "tämä", "siitä", "täällä" }),
        ("nl", new[] { "het", "een", "van", "en", "dat", "niet", "ik", "de", "wat", "met", "voor", "maar", "zijn", "hebben", "je" }),
        ("de", new[] { "und", "der", "die", "das", "ist", "nicht", "ein", "ich", "sie", "mit", "auch", "sich", "wir", "was", "für" }),
        ("fr", new[] { "les", "est", "pas", "vous", "une", "que", "qui", "dans", "pour", "je", "nous", "avec", "mais", "ce", "sur" }),
        ("es", new[] { "que", "los", "en", "un", "por", "con", "para", "una", "más", "como", "pero", "esto", "está", "muy", "bien" }),
        ("it", new[] { "che", "il", "non", "per", "una", "sono", "con", "come", "questo", "cosa", "bene", "mi", "ti", "ma", "gli" }),
        ("pt", new[] { "não", "os", "uma", "com", "para", "você", "isso", "mais", "como", "mas", "então", "está", "tudo", "seu", "aqui" }),
        ("pl", new[] { "nie", "się", "jest", "że", "co", "jak", "ale", "mnie", "tak", "czy", "jestem", "tylko", "tego", "jego" }),
        ("cs", new[] { "se", "na", "že", "je", "ne", "co", "jak", "ale", "tak", "jsem", "prosím", "první", "den", "tady", "ještě" }),
        ("hr", new[] { "sam", "što", "nije", "ovo", "za", "ne", "je", "se", "ali", "kako", "mi", "dobro", "ovdje", "ovaj" }),
        ("ro", new[] { "să", "nu", "este", "pentru", "cu", "ce", "mai", "dar", "aici", "acum", "trebuie", "vreau", "poate", "bine" }),
        ("hu", new[] { "hogy", "nem", "az", "egy", "van", "és", "ez", "de", "még", "csak", "vagy", "mit", "lesz", "tudom" }),
        ("tr", new[] { "için", "bir", "bu", "ne", "çok", "daha", "ben", "sen", "evet", "hayır", "önce", "burada", "efendim", "değil" }),
    };

    /// <summary>
    /// Returns the detected 639-1 code, or null when no language clears the thresholds. Requires at least
    /// <paramref name="minMatches"/> hits and the winner to lead the runner-up by <paramref name="dominance"/>x,
    /// so ambiguous text stays undetermined rather than being mislabelled.
    /// </summary>
    public static string? Detect(string text, int minMatches = 8, double dominance = 1.4)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in Word().Matches(text))
        {
            var w = m.Value.ToLowerInvariant();
            counts[w] = counts.GetValueOrDefault(w) + 1;
        }

        var best = 0;
        var second = 0;
        string? bestCode = null;
        foreach (var (code, words) in Languages)
        {
            var score = 0;
            foreach (var w in words)
            {
                score += counts.GetValueOrDefault(w);
            }

            if (score > best)
            {
                (second, best, bestCode) = (best, score, code);
            }
            else if (score > second)
            {
                second = score;
            }
        }

        return best >= minMatches && best >= second * dominance ? bestCode : null;
    }
}
