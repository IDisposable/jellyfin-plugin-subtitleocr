using Jellyfin.Plugin.SubtitleOcr.Pipeline;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SubtitleOcr;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Singleton keeps the loaded nOCR database across task runs.
        serviceCollection.AddSingleton<SubtitleOcrPipeline>();
    }
}
