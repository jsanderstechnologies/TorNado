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
    public class TorBoxClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TorBoxClient> _log;
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public TorBoxClient(IHttpClientFactory httpClientFactory, ILogger<TorBoxClient> log)
        {
            _httpClientFactory = httpClientFactory;
            _log = log;
        }

        private HttpClient CreateClient(string? token = null)
        {
            var client = _httpClientFactory.CreateClient(nameof(TorBoxClient));
            client.Timeout = TimeSpan.FromSeconds(15);
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
            return client;
        }

        /// <summary>
        /// Search TorBox Voyager Newznab server for an item.
        /// </summary>
        public async Task<List<TorBoxUsenetSearchResult>> SearchUsenetAsync(string query, string indexerUrl, string token, CancellationToken ct)
        {
            // E.g., https://search-api.torbox.app/newznab?apikey={apikey}&q={query}&t=search&cat=2000,5000&o=json
            var url = $"{indexerUrl.TrimEnd('/')}?apikey={Uri.EscapeDataString(token)}&q={Uri.EscapeDataString(query)}&t=search&cat=2000,5000&o=json";
            try
            {
                using var client = CreateClient();
                var resp = await client.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return [];

                var content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var searchRes = JsonSerializer.Deserialize<TorBoxNewznabResponse>(content, JsonOpts);
                return searchRes?.Channel?.Item ?? [];
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "TorBox Usenet search failed for query: {Query}", query);
                return [];
            }
        }

        /// <summary>
        /// Request a direct stream download link for a given usenet download and file ID.
        /// </summary>
        public async Task<string?> RequestDownloadLinkAsync(int usenetId, int fileId, string token, CancellationToken ct)
        {
            var url = $"https://api.torbox.app/v1/api/usenet/requestdl?token={Uri.EscapeDataString(token)}&usenet_id={usenetId}&file_id={fileId}&redirect=true";
            try
            {
                using var client = CreateClient();
                var resp = await client.GetAsync(url, ct).ConfigureAwait(false);
                if (resp.StatusCode == System.Net.HttpStatusCode.Redirect || resp.StatusCode == System.Net.HttpStatusCode.MovedPermanently)
                {
                    return resp.Headers.Location?.ToString();
                }
                var content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("data", out var dataProp))
                {
                    return dataProp.GetString();
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "TorBox RequestDownloadLink failed for usenetId: {UsenetId}, fileId: {FileId}", usenetId, fileId);
            }
            return null;
        }

        /// <summary>
        /// Create a new usenet download (adds NZB link or content to queue).
        /// </summary>
        public async Task<TorBoxCreateDownloadResult?> CreateUsenetDownloadAsync(string link, string token, CancellationToken ct)
        {
            var url = "https://api.torbox.app/v1/api/usenet/createusenetdownload";
            try
            {
                using var client = CreateClient(token);
                var values = new Dictionary<string, string>
                {
                    { "link", link }
                };
                var content = new FormUrlEncodedContent(values);
                var resp = await client.PostAsync(url, content, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                var respString = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<TorBoxResponseEnvelope<TorBoxCreateDownloadResult>>(respString, JsonOpts);
                return result?.Data;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "TorBox CreateUsenetDownload failed for link: {Link}", link);
                return null;
            }
        }
    }

    public class TorBoxResponseEnvelope<T>
    {
        public bool Success { get; set; }
        public string? Detail { get; set; }
        public T? Data { get; set; }
    }

    public class TorBoxCreateDownloadResult
    {
        [JsonPropertyName("usenet_id")]
        public int UsenetId { get; set; }
        public string? Name { get; set; }
    }

    public class TorBoxNewznabResponse
    {
        public TorBoxNewznabChannel? Channel { get; set; }
    }

    public class TorBoxNewznabChannel
    {
        public List<TorBoxUsenetSearchResult>? Item { get; set; }
    }

    public class TorBoxUsenetSearchResult
    {
        public string? Title { get; set; }
        public string? Link { get; set; }
        public string? Guid { get; set; }
        public string? Comments { get; set; }
        public string? PubDate { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
    }
}

