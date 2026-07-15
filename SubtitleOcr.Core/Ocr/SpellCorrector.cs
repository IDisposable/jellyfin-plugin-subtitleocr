using System.Text.RegularExpressions;
using WeCantSpell.Hunspell;

namespace SubtitleOcr.Core.Ocr;

/// <summary>Corrects residual OCR misreads. Words in <c>protectedWords</c> are never changed.</summary>
public interface ISpellCorrector
{
    string Correct(string text, IReadOnlySet<string> protectedWords);
}

/// <summary>No-op corrector used when no dictionary is available.</summary>
public sealed class NullSpellCorrector : ISpellCorrector
{
    public static readonly ISpellCorrector Instance = new NullSpellCorrector();

    private NullSpellCorrector()
    {
    }

    public string Correct(string text, IReadOnlySet<string> protectedWords) => text;
}

/// <summary>
/// Dictionary-backed correction of residual OCR misreads, with Hunspell as the word oracle. Conservative: a
/// word is only replaced when the top suggestion is a close, OCR-plausible match, so proper nouns and genuine
/// words are left alone.
/// </summary>
public sealed partial class SpellCorrector : ISpellCorrector
{
    /// <summary>More unknown glyphs than this and the word carries too little signal to correct.</summary>
    private const int MaxPlaceholders = 2;

    [GeneratedRegex(@"</?i>")]
    private static partial Regex ItalicTag();

    [GeneratedRegex(@"^(?:</?i>)*")]
    private static partial Regex LeadingTags();

    [GeneratedRegex(@"(?:</?i>)*$")]
    private static partial Regex TrailingTags();

    private readonly WordList _words;
    private readonly Regex _word;

    private readonly char _placeholder;

    private SpellCorrector(WordList words, char unknownCharacter)
    {
        _words = words;
        _placeholder = unknownCharacter;
        _word = BuildWordPattern(unknownCharacter);
    }

    public static ISpellCorrector FromWordList(WordList words, char unknownCharacter) =>
        new SpellCorrector(words, unknownCharacter);

    /// <summary>Loads a Hunspell dictionary; the matching .aff is taken from beside the .dic. Returns the
    /// no-op <see cref="NullSpellCorrector"/> when the dictionary cannot be loaded.</summary>
    public static ISpellCorrector LoadDictionary(string dictionaryPath, char unknownCharacter)
    {
        try
        {
            return new SpellCorrector(WordList.CreateFromFiles(dictionaryPath), unknownCharacter);
        }
        catch
        {
            return NullSpellCorrector.Instance;
        }
    }

    /// <summary>
    /// What counts as one word. The placeholder and italic tags must sit inside the token rather than end it:
    /// otherwise "Battl□star" splits into "Battl" and "star", and "&lt;i&gt;Q&lt;/i&gt;uietly" hands the
    /// dictionary the fragment "uietly", which it corrects to "quietly".
    /// </summary>
    private static Regex BuildWordPattern(char placeholder)
    {
        var p = Regex.Escape(placeholder.ToString());
        return new Regex($@"(?:</?i>)*\p{{L}}(?:</?i>|[\p{{L}}'{p}])*", RegexOptions.Compiled);
    }

    public string Correct(string text, IReadOnlySet<string> protectedWords) =>
        _word.Replace(text, m =>
        {
            var token = m.Value;
            var word = ItalicTag().Replace(token, string.Empty);
            if (protectedWords.Contains(word))
            {
                return token;
            }

            var corrected = CorrectWord(word, protectedWords);
            if (string.Equals(corrected, word, StringComparison.Ordinal))
            {
                return token;
            }

            // Keep the tags that wrapped the word, or an italic sentence loses its opening tag over one
            // misspelling. Any tag mid-word was a misclassification and goes.
            return LeadingTags().Match(token).Value + corrected + TrailingTags().Match(token).Value;
        });

    private string CorrectWord(string word, IReadOnlySet<string> protectedWords)
    {
        var placeholders = word.Count(c => c == _placeholder);

        // An unknown glyph reads as any character, so a word holding one may resolve against a word already
        // known to be right.
        if (placeholders > 0)
        {
            if (placeholders > MaxPlaceholders || placeholders * 2 > word.Length)
            {
                return word;
            }

            if (WildcardMatch(word, protectedWords) is { } known)
            {
                return known;
            }
        }

        // Too short to correct safely, or already a word.
        if (word.Length < 3 || _words.Check(word))
        {
            return word;
        }

        // A capitalized word is a name until proven otherwise, and a name is exactly what a dictionary
        // does not know: "ADAMA" becomes "ASAMA", "TYROL" becomes "TYRO". All-caps is a speaker label or
        // a shout; Title-case is a proper noun. A mixed-case misread ("EXPLoSloNS") is neither and is
        // still corrected. Holds even with an unknown glyph in the word: guessing at an unprotected name
        // does more harm than leaving the placeholder visible.
        if (char.IsUpper(word[0]) &&
            (word.Count(char.IsLower) == 0 || word.Count(char.IsUpper) <= word.Count(char.IsLower)))
        {
            return word;
        }

        var suggestion = _words.Suggest(word).FirstOrDefault();

        // Reject a suggestion that splits one word into several (e.g. "Battlestar" -> "Battle star").
        if (suggestion is null || suggestion.Any(char.IsWhiteSpace) || !IsCloseMatch(word, suggestion))
        {
            return word;
        }

        return MatchCase(word, suggestion);
    }

    /// <summary>The one protected word this can be, reading each placeholder as any character. Null unless
    /// exactly one matches: two candidates mean the placeholder is what tells them apart.</summary>
    private string? WildcardMatch(string word, IReadOnlySet<string> protectedWords)
    {
        string? found = null;
        foreach (var candidate in protectedWords)
        {
            if (candidate.Length != word.Length || !MatchesWithPlaceholders(word, candidate))
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            found = candidate;
        }

        return found;
    }

    private bool MatchesWithPlaceholders(string word, string candidate)
    {
        for (var i = 0; i < word.Length; i++)
        {
            if (word[i] != _placeholder && char.ToLowerInvariant(word[i]) != char.ToLowerInvariant(candidate[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Accepts a correction only within a tight, length-scaled edit distance (case-insensitive).</summary>
    private bool IsCloseMatch(string original, string suggestion)
    {
        var max = original.Length >= 8 ? 2 : 1;
        return LevenshteinAtMost(original.ToLowerInvariant(), suggestion.ToLowerInvariant(), max, _placeholder);
    }

    /// <summary>Reapplies the original word's casing. Majority case for all-caps, so "EXPLoSloNS" restores
    /// to "EXPLOSIONS".</summary>
    private static string MatchCase(string original, string suggestion)
    {
        if (original.Length > 1 && original.Count(char.IsUpper) > original.Count(char.IsLower))
        {
            return suggestion.ToUpperInvariant();
        }

        if (char.IsUpper(original[0]) && suggestion.Length > 0 && !char.IsUpper(suggestion[0]))
        {
            return char.ToUpperInvariant(suggestion[0]) + suggestion[1..];
        }

        return suggestion;
    }

    /// <summary>True when the edit distance between a and b is at most <paramref name="max"/> (early-outs).
    /// A <paramref name="placeholder"/> is free against any character: "batt□e" is one edit from "battle".</summary>
    private static bool LevenshteinAtMost(string a, string b, int max, char placeholder)
    {
        if (Math.Abs(a.Length - b.Length) > max)
        {
            return false;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] || a[i - 1] == placeholder || b[j - 1] == placeholder ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }

            if (rowMin > max)
            {
                return false;
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length] <= max;
    }
}
