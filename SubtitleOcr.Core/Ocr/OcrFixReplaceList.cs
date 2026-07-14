using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SubtitleOcr.Core.Ocr;

/// <summary>
/// Applies a Subtitle Edit-format OCR fix replace list (the {lang}_OCRFixReplaceList.xml files). Only the
/// safe, self-contained sections are used: whole-word token swaps and regular expressions. The partial-word
/// sections are skipped, since Subtitle Edit gates those behind its own dictionary and applying them blindly
/// corrupts real words. The <see cref="Empty"/> instance is the no-op used when no list is available.
/// </summary>
public sealed class OcrFixReplaceList
{
    public static readonly OcrFixReplaceList Empty = new(
        FrozenDictionary<string, string>.Empty,
        Array.Empty<(Regex, string)>());

    private readonly FrozenDictionary<string, string> _wholeWords;
    private readonly IReadOnlyList<(Regex Find, string Replace)> _regularExpressions;

    private OcrFixReplaceList(
        FrozenDictionary<string, string> wholeWords,
        IReadOnlyList<(Regex, string)> regularExpressions)
    {
        _wholeWords = wholeWords;
        _regularExpressions = regularExpressions;
    }

    public bool IsEmpty => _wholeWords.Count == 0 && _regularExpressions.Count == 0;

    /// <summary>Loads a Subtitle Edit OCRFixReplaceList.xml. Returns <see cref="Empty"/> on any failure.</summary>
    public static OcrFixReplaceList LoadFile(string path)
    {
        try
        {
            var doc = XDocument.Load(path);
            var root = doc.Root;
            if (root is null)
            {
                return Empty;
            }

            var wholeWords = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (from, to) in Pairs(root, "WholeWords", "Word"))
            {
                wholeWords[from] = to;
            }

            var regexes = new List<(Regex, string)>();
            var expressions = root.Element("RegularExpressions");
            if (expressions is not null)
            {
                foreach (var re in expressions.Elements("RegularExpression"))
                {
                    var find = (string?)re.Attribute("find");
                    var replace = (string?)re.Attribute("replaceWith");
                    if (!string.IsNullOrEmpty(find) && replace is not null)
                    {
                        try
                        {
                            regexes.Add((new Regex(find), replace));
                        }
                        catch (ArgumentException)
                        {
                            // Skip an invalid pattern rather than fail the whole list.
                        }
                    }
                }
            }

            return new OcrFixReplaceList(wholeWords.ToFrozenDictionary(StringComparer.Ordinal), regexes);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    public string Apply(string text)
    {
        if (IsEmpty || string.IsNullOrEmpty(text))
        {
            return text;
        }

        foreach (var (find, replace) in _regularExpressions)
        {
            text = find.Replace(text, replace);
        }

        var sb = new StringBuilder(text.Length);
        foreach (var line in text.Split('\n'))
        {
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            AppendFixedLine(sb, line);
        }

        return sb.ToString();
    }

    private void AppendFixedLine(StringBuilder sb, string line)
    {
        var tokens = line.Split(' ');
        for (var i = 0; i < tokens.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(FixToken(tokens[i]));
        }
    }

    private string FixToken(string token)
    {
        if (token.Length == 0)
        {
            return token;
        }

        if (_wholeWords.TryGetValue(token, out var whole))
        {
            return whole;
        }

        // Retry the token without trailing punctuation (leading punctuation is significant to many keys).
        var end = token.Length;
        while (end > 0 && !char.IsLetterOrDigit(token[end - 1]))
        {
            end--;
        }

        if (end > 0 && end < token.Length && _wholeWords.TryGetValue(token[..end], out var core))
        {
            return core + token[end..];
        }

        return token;
    }

    private static IEnumerable<(string From, string To)> Pairs(XElement root, string section, string element)
    {
        var container = root.Element(section);
        if (container is null)
        {
            yield break;
        }

        foreach (var item in container.Elements(element))
        {
            var from = (string?)item.Attribute("from");
            var to = (string?)item.Attribute("to");
            if (!string.IsNullOrEmpty(from) && to is not null)
            {
                yield return (from, to);
            }
        }
    }
}
