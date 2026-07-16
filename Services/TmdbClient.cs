using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TorNado.Services
{
    public class TmdbClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TmdbClient> _log;
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public TmdbClient(IHttpClientFactory httpClientFactory, ILogger<TmdbClient> log)
        {
            _httpClientFactory = httpClientFactory;
            _log = log;
        }

        private HttpClient CreateClient()
        {
            var client = _httpClientFactory.CreateClient(nameof(TmdbClient));
            client.Timeout = TimeSpan.FromSeconds(15);
            return client;
        }

        public async Task<List<TmdbSearchResult>> SearchMovieAsync(string query, string apiKey, CancellationToken ct)
        {
            var url = $"https://api.themoviedb.org/3/search/movie?api_key={Uri.EscapeDataString(apiKey)}&query={Uri.EscapeDataString(query)}";
            try
            {
                using var client = CreateClient();
                var resp = await client.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return [];

                var content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var searchRes = JsonSerializer.Deserialize<TmdbSearchResponse<TmdbSearchResult>>(content, JsonOpts);
                return searchRes?.Results ?? [];
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "TMDB Movie Search failed for query: {Query}", query);
                return [];
            }
        }

        public async Task<List<TmdbSearchResult>> SearchTvAsync(string query, string apiKey, CancellationToken ct)
        {
            var url = $"https://api.themoviedb.org/3/search/tv?api_key={Uri.EscapeDataString(apiKey)}&query={Uri.EscapeDataString(query)}";
            try
            {
                using var client = CreateClient();
                var resp = await client.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return [];

                var content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var searchRes = JsonSerializer.Deserialize<TmdbSearchResponse<TmdbSearchResult>>(content, JsonOpts);
                return searchRes?.Results ?? [];
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "TMDB TV Search failed for query: {Query}", query);
                return [];
            }
        }

        public async Task<string?> GetImdbIdAsync(string tmdbId, bool isTv, string apiKey, CancellationToken ct)
        {
            var type = isTv ? "tv" : "movie";
            var url = $"https://api.themoviedb.org/3/{type}/{Uri.EscapeDataString(tmdbId)}/external_ids?api_key={Uri.EscapeDataString(apiKey)}";
            try
            {
                using var client = CreateClient();
                var resp = await client.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                var content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("imdb_id", out var imdbProp))
                {
                    return imdbProp.GetString();
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "TMDB GetImdbId failed for tmdbId: {TmdbId}", tmdbId);
            }
            return null;
        }

        public async Task<TmdbTvDetails?> GetTvDetailsAsync(string tmdbId, string apiKey, CancellationToken ct)
        {
            var url = $"https://api.themoviedb.org/3/tv/{Uri.EscapeDataString(tmdbId)}?api_key={Uri.EscapeDataString(apiKey)}";
            try
            {
                using var client = CreateClient();
                var resp = await client.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                var content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return JsonSerializer.Deserialize<TmdbTvDetails>(content, JsonOpts);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "TMDB GetTvDetails failed for tmdbId: {TmdbId}", tmdbId);
                return null;
            }
        }

        public async Task<TmdbTvSeason?> GetTvSeasonAsync(string tmdbId, int seasonNumber, string apiKey, CancellationToken ct)
        {
            var url = $"https://api.themoviedb.org/3/tv/{Uri.EscapeDataString(tmdbId)}/season/{seasonNumber}?api_key={Uri.EscapeDataString(apiKey)}";
            try
            {
                using var client = CreateClient();
                var resp = await client.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                var content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return JsonSerializer.Deserialize<TmdbTvSeason>(content, JsonOpts);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "TMDB GetTvSeason failed for tmdbId: {TmdbId}, season: {Season}", tmdbId, seasonNumber);
                return null;
            }
        }
    }

    public class TmdbSearchResponse<T>
    {
        public List<T>? Results { get; set; }
    }

    public class TmdbSearchResult
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? Name { get; set; }
        
        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }
        
        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; set; }
        
        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }
        
        public string? Overview { get; set; }
        
        [JsonPropertyName("vote_average")]
        public float VoteAverage { get; set; }
    }

    public class TmdbTvDetails
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Overview { get; set; }
        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }
        [JsonPropertyName("number_of_seasons")]
        public int NumberOfSeasons { get; set; }
    }

    public class TmdbTvSeason
    {
        [JsonPropertyName("season_number")]
        public int SeasonNumber { get; set; }
        public List<TmdbEpisode>? Episodes { get; set; }
    }

    public class TmdbEpisode
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Overview { get; set; }
        [JsonPropertyName("episode_number")]
        public int EpisodeNumber { get; set; }
        [JsonPropertyName("air_date")]
        public DateTime? AirDate { get; set; }
    }
}

