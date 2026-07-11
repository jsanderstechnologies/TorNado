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
            _tmdbClient = new TmdbClient(http, log);
            _torBoxClient = new TorBoxClient(http, log);
        }

        public async Task<bool> IsReady()
        {
            return true;
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
    }
}

