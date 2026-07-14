using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.SubtitleOcr;

/// <summary>Collects words from an item's own metadata (title, series, overview, cast and character names)
/// so spell-correction can leave show and character names intact.</summary>
public static partial class MetadataWords
{
    [GeneratedRegex(@"\p{L}{2,}")]
    private static partial Regex Word();

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

        foreach (var person in libraryManager.GetPeople(item))
        {
            Add(words, person.Name);
            Add(words, person.Role);
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
