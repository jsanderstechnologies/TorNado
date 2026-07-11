using System.Collections.Concurrent;
using TorNado.Config;
using TorNado.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace TorNado;

public class TorNadoPlugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasPluginImage
{
    private readonly ILogger<TorNadoPlugin> _log;
    private readonly TorNadoManager _manager;
    private ConcurrentDictionary<Guid, PluginConfiguration> UserConfigs { get; } = new();
    private readonly TorNadoStremioProviderFactory _stremioFactory;
    public PalcoCacheService PalcoCache { get; } // Migrated Palco Cache Service

    public TorNadoPlugin(
        IApplicationPaths applicationPaths,
        TorNadoManager manager,
        IXmlSerializer xmlSerializer,
        ILogger<TorNadoPlugin> log,
        TorNadoStremioProviderFactory stremioFactory,
        PalcoCacheService palcoCache
    )
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        _log = log;
        _manager = manager;
        _stremioFactory = stremioFactory;
        PalcoCache = palcoCache;
    }

    public static TorNadoPlugin? Instance { get; private set; }

    // Event fired when the plugin configuration is updated via UpdateConfiguration
    public static new event Action<PluginConfiguration>? ConfigurationChanged;

    public override string Name => "TorNado";
    public override Guid Id => Guid.Parse("85EA4E14-8163-4989-96FE-0A2094BC2D6B");
    public override string Description => "On-demand Usenet streams from TorBox Pro.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var prefix = GetType().Namespace;
        yield return new PluginPageInfo
        {
            Name = "config",
            EnableInMainMenu = true,
            EmbeddedResourcePath = prefix + ".Config.config.html",
        };
    }

    /// <inheritdoc />
    public Stream? GetPluginImage()
    {
        return GetType().Assembly.GetManifestResourceStream(GetType().Namespace + ".logo.png");
    }

    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        var cfg = (PluginConfiguration)configuration;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISABLE_P2P")))
        {
            cfg.P2PEnabled = false;
        }
        base.UpdateConfiguration(cfg);

        _manager.ClearCache();
        _stremioFactory.ClearCache();
        UserConfigs.Clear();

        // Notify subscribers that configuration changed
        try
        {
            ConfigurationChanged?.Invoke(cfg);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error while invoking ConfigurationChanged event");
        }
    }

    public PluginConfiguration GetConfig(Guid userId)
    {
        try
        {
            return UserConfigs.GetOrAdd(
                userId,
                _ =>
                {
                    var cfg = Instance?.Configuration;
                    if (userId != Guid.Empty)
                    {
                        var userConfig = Instance?.Configuration.UserConfigs.FirstOrDefault(u =>
                            u.UserId == userId
                        );
                        cfg =
                            userConfig?.ApplyOverrides(Instance?.Configuration)
                            ?? Instance?.Configuration;
                    }
                    var stremio = _stremioFactory.Create(cfg);
                    cfg.Stremio = stremio;
                    cfg.MovieFolder = _manager.TryGetMovieFolder(cfg);
                    cfg.SeriesFolder = _manager.TryGetSeriesFolder(cfg);
                    return cfg;
                }
            );
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error getting config");
            return new PluginConfiguration();
        }
    }
}

