using System.Text.Json.Serialization;
using System.Xml.Serialization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Logging;

namespace TorNado.Config;

public class PluginConfiguration : BasePluginConfiguration
{
    public string MoviePath { get; set; } = Path.Combine(Path.GetTempPath(), "tornado", "movies");
    public string SeriesPath { get; set; } = Path.Combine(Path.GetTempPath(), "tornado", "series");
    public int StreamTTL { get; set; } = 3600;
    public int CatalogMaxItems { get; set; } = 100;
    
    // TMDB and TorBox configurations
    public string TmdbApiKey { get; set; } = "";
    public string TorBoxApiKey { get; set; } = "";
    public string TorBoxUsenetServer { get; set; } = "https://search-api.torbox.app/newznab";
    public string NntpHost { get; set; } = "nntp.torbox.app";
    public int NntpPort { get; set; } = 563;
    public string NntpUsername { get; set; } = "";
    public string NntpPassword { get; set; } = "";

    public bool EnableMixed { get; set; } = false;
    public bool ExtendLocalSeriesTrees { get; set; } = false;
    public bool FilterUnreleased { get; set; } = false;
    public int FilterUnreleasedBufferDays { get; set; } = 0;
    public bool DisableSourceCount { get; set; } = true;
    public bool P2PEnabled { get; set; } = false;
    public int P2PDLSpeed { get; set; } = 0;
    public int P2PULSpeed { get; set; } = 0;
    public string FFmpegAnalyzeDuration { get; set; } = "5M";
    public string FFmpegProbeSize { get; set; } = "40M";
    public bool CreateCollections { get; set; } = false;
    public int MaxCollectionItems { get; set; } = 100;
    public bool DisableSearch { get; set; } = false;
    public bool EnableJavaScriptInjection { get; set; } = false;
    public bool LazyImages { get; set; } = false;
    public List<CatalogConfig> Catalogs { get; set; } = [];
    public List<UserConfig> UserConfigs { get; set; } = [];

    [JsonIgnore]
    [XmlIgnore]
    public TorNadoDataProvider? TorNado;

    [JsonIgnore]
    [XmlIgnore]
    public Folder? MovieFolder;

    [JsonIgnore]
    [XmlIgnore]
    public Folder? SeriesFolder;

    public PluginConfiguration GetEffectiveConfig(Guid userId)
    {
        var userConfig = UserConfigs.FirstOrDefault(u => u.UserId == userId);
        return userConfig is null ? this : userConfig.ApplyOverrides(this);
    }
}

public class UserConfig
{
    public Guid UserId { get; set; }
    public string MoviePath { get; set; } = "";
    public string SeriesPath { get; set; } = "";
    public bool DisableSearch { get; set; } = false;

    /// <summary>
    /// Apply user overrides to base configuration - replaces all overridable fields
    /// </summary>
    public PluginConfiguration ApplyOverrides(PluginConfiguration baseConfig)
    {
        return new PluginConfiguration
        {
            // User overridable fields - all required, no fallback to baseConfig
            MoviePath = MoviePath,
            SeriesPath = SeriesPath,
            DisableSearch = DisableSearch,
            TmdbApiKey = baseConfig.TmdbApiKey,
            TorBoxApiKey = baseConfig.TorBoxApiKey,
            TorBoxUsenetServer = baseConfig.TorBoxUsenetServer,
            NntpHost = baseConfig.NntpHost,
            NntpPort = baseConfig.NntpPort,
            NntpUsername = baseConfig.NntpUsername,
            NntpPassword = baseConfig.NntpPassword,

            // All other fields from base config
            StreamTTL = baseConfig.StreamTTL,
            CatalogMaxItems = baseConfig.CatalogMaxItems,
            EnableMixed = baseConfig.EnableMixed,
            ExtendLocalSeriesTrees = baseConfig.ExtendLocalSeriesTrees,
            FilterUnreleased = baseConfig.FilterUnreleased,
            FilterUnreleasedBufferDays = baseConfig.FilterUnreleasedBufferDays,
            DisableSourceCount = baseConfig.DisableSourceCount,
            P2PEnabled = baseConfig.P2PEnabled,
            P2PDLSpeed = baseConfig.P2PDLSpeed,
            P2PULSpeed = baseConfig.P2PULSpeed,
            FFmpegAnalyzeDuration = baseConfig.FFmpegAnalyzeDuration,
            FFmpegProbeSize = baseConfig.FFmpegProbeSize,
            CreateCollections = baseConfig.CreateCollections,
            MaxCollectionItems = baseConfig.MaxCollectionItems,
            UserConfigs = baseConfig.UserConfigs,
        };
    }
}

public class TorNadoDataProviderFactory(IHttpClientFactory http, ILoggerFactory log)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        TorNadoDataProvider
    > _cache = new(StringComparer.OrdinalIgnoreCase);

    public TorNadoDataProvider Create(Guid userId)
    {
        var cfg = TorNadoPlugin.Instance!.Configuration.GetEffectiveConfig(userId);
        return Create(cfg);
    }

    public TorNadoDataProvider Create(PluginConfiguration cfg)
    {
        var key = cfg.TorBoxApiKey;
        return _cache.GetOrAdd(
            key,
            _ => new TorNadoDataProvider(cfg.TorBoxUsenetServer, http, log.CreateLogger<TorNadoDataProvider>())
        );
    }

    public void ClearCache() => _cache.Clear();
}

public class CatalogConfig
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "movie";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = false;

    /// <summary>0 means "use global CatalogMaxItems".</summary>
    public int MaxItems { get; set; } = 0;
    public bool CreateCollection { get; set; } = false;
    public string Url { get; set; } = "";
}

