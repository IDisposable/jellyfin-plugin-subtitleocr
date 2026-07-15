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
        if (item is Episode episode)
        {
            Add(words, episode.SeriesName);
        }

        // Only what the dialogue can actually say: the characters. Crew names are never spoken, and an
        // actor's own name is only in the script when they play themselves.
        foreach (var person in libraryManager.GetPeople(item))
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
        }
    }
}
