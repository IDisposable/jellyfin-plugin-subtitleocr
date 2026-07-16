using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.SubtitleOcr;

/// <summary>Collects words from an item's own metadata (title, series, overview, character names) so
/// spell-correction can leave show and character names intact.</summary>
public static partial class MetadataWords
{
    [GeneratedRegex(@"\p{L}{2,}")]
    private static partial Regex Word();

    // TMDB writes "Self", "Self - Host" and the like; other sources use the reflexive pronouns.
    [GeneratedRegex(@"^\s*(self|himself|herself|themself|themselves)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PlaysSelf();

    public static IReadOnlySet<string> From(BaseItem item, ILibraryManager libraryManager)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(words, item.Name);
        Add(words, item.OriginalTitle);
        Add(words, item.Overview);

        // An episode's own People are guest stars and crew; the main cast hangs off the Series, and those
        // are the names every speaker label carries.
        var people = new List<PersonInfo>(libraryManager.GetPeople(item));
        if (item is Episode episode)
        {
            Add(words, episode.SeriesName);
            if (episode.Series is { } series)
            {
                Add(words, series.Name);
                Add(words, series.Overview);
                people.AddRange(libraryManager.GetPeople(series));
            }
        }

        // Only what the dialogue can actually say: the characters. Crew names are never spoken, and an
        // actor's own name is only in the script when they play themselves.
        foreach (var person in people)
        {
            if (person.Type is not (PersonKind.Actor or PersonKind.GuestStar))
            {
                continue;
            }

            Add(words, person.Role);
            if (PlaysSelf().IsMatch(person.Role ?? string.Empty))
            {
                Add(words, person.Name);
            }
        }

        return words;
    }

    private static void Add(HashSet<string> words, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (Match m in Word().Matches(text))
        {
            words.Add(m.Value);
            AddPlural(words, m.Value);
        }
    }

    /// <summary>
    /// Protects a name in the number the metadata did not happen to use: the cast list says "Cylons" and the
    /// dialogue says "Cylon", or the other way about, and only one of them was covered. Just the s-plural,
    /// because the point is names the dictionary lacks, and those take the regular form.
    ///
    /// Over-protecting is nearly free: spell-check only ever touches a word the dictionary does not know, so
    /// an invented variant of an ordinary word is a word it would not have corrected anyway.
    /// </summary>
    private static void AddPlural(HashSet<string> words, string word)
    {
        if (word.Length < 3)
        {
            return;
        }

        if (!word.EndsWith('s'))
        {
            words.Add(word + "s");
        }
        else if (word.Length > 3 && !word.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
        {
            // "Ross" keeps its second s; "Cylons" gives up its only one.
            words.Add(word[..^1]);
        }
    }
}
