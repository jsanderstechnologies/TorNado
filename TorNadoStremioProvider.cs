using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TorNado.Config;
using TorNado.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace TorNado
{
    public class TorNadoStremioProvider
    {
        private readonly string _baseUrl;
        private readonly IHttpClientFactory _http;
        private readonly ILogger<TorNadoStremioProvider> _log;
        private readonly TmdbClient _tmdbClient;
        private readonly TorBoxClient _torBoxClient;

        public TorNadoStremioProvider(string baseUrl, IHttpClientFactory http, ILogger<TorNadoStremioProvider> log)
        {
            _baseUrl = baseUrl;
            _http = http;
            _log = log;
            
            // Create specific typed loggers for inner clients to satisfy constructor parameters
            var tmdbLogger = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => {}).CreateLogger<TmdbClient>();
            var torBoxLogger = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => {}).CreateLogger<TorBoxClient>();
            _tmdbClient = new TmdbClient(http, tmdbLogger);
            _torBoxClient = new TorBoxClient(http, torBoxLogger);
        }

        public async Task<bool> IsReady()
        {
            return true;
        }

        public async Task<StremioMeta?> GetMetaAsync(string id, StremioMediaType mediaType)
        {
            var config = TorNadoPlugin.Instance?.Configuration;
            if (config == null || string.IsNullOrWhiteSpace(config.TmdbApiKey))
                return null;

            // Strip "tmdb:" prefix if present for querying
            var lookupId = id.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase) ? id["tmdb:".Length..] : id;

            return new StremioMeta
            {
                Id = id,
                Type = mediaType,
                Name = "Metadata Stub",
                Overview = "Metadata populated on-demand"
            };
        }

        public async Task<StremioMeta?> GetMetaAsync(BaseItem item)
        {
            var imdbId = item.GetProviderId(MetadataProvider.Imdb);
            var tmdbId = item.GetProviderId(MetadataProvider.Tmdb);
            var mediaType = item is Series ? StremioMediaType.Series : StremioMediaType.Movie;
            return await GetMetaAsync(imdbId ?? $"tmdb:{tmdbId}", mediaType).ConfigureAwait(false);
        }

        public async Task EnrichDigitalReleaseDateAsync(StremioMeta meta, CancellationToken ct)
        {
            // Optional digital release date enrichment
        }

        /// <summary>
        /// Translates Stremio-style meta search into TMDB search
        /// </summary>
        public async Task<IReadOnlyList<StremioMeta>> SearchAsync(string query, StremioMediaType mediaType)
        {
            var config = TorNadoPlugin.Instance?.Configuration;
            if (config == null || string.IsNullOrWhiteSpace(config.TmdbApiKey))
            {
                _log.LogWarning("SearchAsync: TMDB API Key is not configured.");
                return [];
            }

            var results = new List<StremioMeta>();
            if (mediaType == StremioMediaType.Movie)
            {
                var tmdbMovies = await _tmdbClient.SearchMovieAsync(query, config.TmdbApiKey, CancellationToken.None);
                foreach (var tmdb in tmdbMovies)
                {
                    results.Add(new StremioMeta
                    {
                        Id = $"tmdb:{tmdb.Id}",
                        Type = StremioMediaType.Movie,
                        Name = tmdb.Title ?? tmdb.Name,
                        Overview = tmdb.Overview,
                        Description = tmdb.Overview,
                        Poster = string.IsNullOrWhiteSpace(tmdb.PosterPath) ? null : $"https://image.tmdb.org/t/p/w500{tmdb.PosterPath}",
                        ReleaseInfo = tmdb.ReleaseDate
                    });
                }
            }
            else if (mediaType == StremioMediaType.Series)
            {
                var tmdbShows = await _tmdbClient.SearchTvAsync(query, config.TmdbApiKey, CancellationToken.None);
                foreach (var tmdb in tmdbShows)
                {
                    results.Add(new StremioMeta
                    {
                        Id = $"tmdb:{tmdb.Id}",
                        Type = StremioMediaType.Series,
                        Name = tmdb.Title ?? tmdb.Name,
                        Overview = tmdb.Overview,
                        Description = tmdb.Overview,
                        Poster = string.IsNullOrWhiteSpace(tmdb.PosterPath) ? null : $"https://image.tmdb.org/t/p/w500{tmdb.PosterPath}",
                        ReleaseInfo = tmdb.FirstAirDate
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Gets streams for a media item. In TorNado, we search the TorBox Usenet network.
        /// </summary>
        public async Task<List<StremioStream>> GetStreamsAsync(StremioUri uri)
        {
            var config = TorNadoPlugin.Instance?.Configuration;
            if (config == null || string.IsNullOrWhiteSpace(config.TorBoxApiKey))
            {
                _log.LogWarning("GetStreamsAsync: TorBox API key is missing.");
                return [];
            }

            // Generate a search query based on IMDb / TMDB / Name
            var imdbId = uri.ExternalId;
            var query = imdbId;

            // Call TorBox Usenet search using voyager/newznab endpoint
            var usenetResults = await _torBoxClient.SearchUsenetAsync(query, config.TorBoxUsenetServer, config.TorBoxApiKey, CancellationToken.None);
            
            var streams = new List<StremioStream>();
            foreach (var res in usenetResults)
            {
                if (string.IsNullOrWhiteSpace(res.Link)) continue;

                streams.Add(new StremioStream
                {
                    Name = $"TorBox Usenet - {res.Title}",
                    Description = $"Category: {res.Category} | Date: {res.PubDate}",
                    Url = res.Link, // Storing NZB download link as the stream URL temporarily
                    BehaviorHints = new StremioBehaviorHints
                    {
                        Filename = res.Title
                    }
                });
            }

            return streams;
        }

        public async Task<List<StremioSubtitle>> GetSubtitlesAsync(string id, StremioMediaType mediaType)
        {
            return [];
        }

        public async Task<List<StremioMeta>> GetCatalogMetasAsync(string catalogId, string type, string? search = null, int skip = 0)
        {
            return [];
        }

        public Task<StremioManifest?> GetManifestAsync(bool force = false)
        {
            return Task.FromResult<StremioManifest?>(new StremioManifest());
        }
    }

    public enum StremioMediaType
    {
        Unknown,
        Movie,
        Series,
        Episode
    }

    public enum StremioStatus
    {
        Unknown,
        Continuing,
        Ended,
        Upcoming
    }

    public class StremioStreamsResponse
    {
        public List<StremioStream>? Streams { get; set; }
    }

    public class StremioStream
    {
        public string? Name { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Url { get; set; }
        public string? InfoHash { get; set; }
        public int? FileIdx { get; set; }
        public List<string>? Sources { get; set; }
        public StremioBehaviorHints? BehaviorHints { get; set; }

        public bool IsValid() => !string.IsNullOrWhiteSpace(Url) || !string.IsNullOrWhiteSpace(InfoHash);
        public bool IsTorrent() => !string.IsNullOrWhiteSpace(InfoHash);
        public bool IsFile() => !string.IsNullOrWhiteSpace(Url);
        public Guid GetGuid() => Guid.NewGuid();
    }

    public class StremioBehaviorHints
    {
        public string? BingeGroup { get; set; }
        public string? Filename { get; set; }
    }

    public class StremioSubtitleResponse
    {
        public List<StremioSubtitle>? Subtitles { get; set; }
    }

    public class StremioSubtitle
    {
        public string? Id { get; set; }
        public string? Url { get; set; }
        public string? Lang { get; set; }
        public string? Title { get; set; }
        public bool? FromTrusted { get; set; }
        public bool? AiTranslated { get; set; }

        public string TwoLetterISOLanguageName() => Lang ?? "en";
    }

    public class StremioManifest
    {
        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public string Version { get; set; } = "";
        public string? Description { get; set; }
        public List<StremioCatalog> Catalogs { get; set; } = new();
    }

    public class StremioCatalog
    {
        public string Type { get; set; } = "";
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        public bool IsImportable() => true;
    }

    public class StremioMeta
    {
        public required string Id { get; set; }
        public StremioMediaType Type { get; set; } = StremioMediaType.Unknown;
        public string? Name { get; set; }
        public string? Title { get; set; }
        public string? Poster { get; set; }
        public List<string>? Genres { get; set; }
        public string? ReleaseInfo { get; set; }
        public string? Description { get; set; }
        public string? Overview { get; set; }
        public string? Background { get; set; }
        public string? Logo { get; set; }
        public List<StremioMeta>? Videos { get; set; }
        public string? Runtime { get; set; }
        public string? Country { get; set; }
        public float? ImdbRating { get; set; }
        public StremioBehaviorHints? BehaviorHints { get; set; }
        public List<string>? Genre { get; set; }
        public string? ImdbId { get; set; }
        public DateTime? Released { get; set; }
        public StremioStatus? Status { get; set; } = StremioStatus.Unknown;
        public int? Year { get; set; }
        public string? Slug { get; set; }
        public StremioAppExtras? App_Extras { get; set; }
        public string? Thumbnail { get; set; }
        public int? Episode { get; set; }
        public int? Season { get; set; }
        public int? Number { get; set; }
        public DateTime? FirstAired { get; set; }
        public Guid? Guid { get; set; }

        public string? Director { get; set; }
        public string? Writer { get; set; }
        public string? LandscapePoster { get; set; }

        public string? TvdbEpisodeId() => null;
        public string GetName() => Title ?? Name ?? "";
        public Dictionary<string, string> GetProviderIds()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(Id))
            {
                if (Id.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase))
                    dict["Tmdb"] = Id["tmdb:".Length..];
                else if (Id.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
                    dict["Imdb"] = Id;
            }
            if (!string.IsNullOrWhiteSpace(ImdbId))
                dict["Imdb"] = ImdbId;
            return dict;
        }

        public int? GetYear()
        {
            if (Year is not null) return Year;
            if (Released is { } dt) return dt.Year;
            if (!string.IsNullOrWhiteSpace(ReleaseInfo) && ReleaseInfo.Length >= 4 && int.TryParse(ReleaseInfo.AsSpan(0, 4), out var y))
                return y;
            return null;
        }

        public DateTime? GetPremiereDate()
        {
            if (Released is { } dt) return dt;
            var y = GetYear();
            return y.HasValue ? new DateTime(y.Value, 1, 1) : null;
        }

        public DateTime? GetDigitalReleaseDate() => null;
        public bool IsValid() => !string.IsNullOrWhiteSpace(Id) && !Id.Contains("error");
        public bool IsReleased(int bufferDays = 0) => true;
        public StremioStatus? GetStatus() => Status;
    }

    public class StremioCast
    {
        public string? Name { get; set; }
        public string? Character { get; set; }
        public string? Photo { get; set; }
    }

    public class StremioAppExtras
    {
        public List<string?>? SeasonPosters { get; set; }
        public string? Certification { get; set; }
        public TmdbReleaseDatesContainer? ReleaseDates { get; set; }
        public List<StremioCast>? Cast { get; set; }
        public List<StremioCast>? Directors { get; set; }
        public List<StremioCast>? Writers { get; set; }
    }

    public class TmdbReleaseDatesContainer
    {
        public List<TmdbReleaseDateCountry>? Results { get; set; }
    }

    public class TmdbReleaseDateCountry
    {
        public string? Iso31661 { get; set; }
        public List<TmdbReleaseDateItem>? ReleaseDates { get; set; }
    }

    public class TmdbReleaseDateItem
    {
        public DateTime? ReleaseDate { get; set; }
        public int Type { get; set; }
        public string? Certification { get; set; }
    }
}
