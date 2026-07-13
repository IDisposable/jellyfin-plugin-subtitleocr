using Jellyfin.Plugin.SubtitleOcr.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SubtitleOcr;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        NOcrDatabaseFolder = Path.Combine(DataFolderPath, "nocr");
        Directory.CreateDirectory(NOcrDatabaseFolder);
    }

    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Drop-in folder for user nOCR databases. A file named <c>{language}.nocr</c> here (e.g.
    /// <c>rus.nocr</c>) is picked up automatically for that language; per-language config entries
    /// may also reference a bare file name resolved against this folder.
    /// </summary>
    public string NOcrDatabaseFolder { get; }

    /// <summary>Per-file probe cache backing <see cref="ScheduledTasks.OcrSubtitlesTask"/>.</summary>
    public string ScanStatePath => Path.Combine(DataFolderPath, "scan-state.json");

    /// <summary>Log of written SRT files shown on the config page.</summary>
    public string ExtractionLogPath => Path.Combine(DataFolderPath, "extractions.json");

    public override string Name => "Subtitle OCR";

    public override string Description =>
        "Converts image-based subtitles (VobSub, PGS) to SRT files using nOCR.";

    public override Guid Id => Guid.Parse("b7a9c2e4-5d31-4f8a-9c06-3e2d1a8b4f70");

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
            },
        };
    }
}
