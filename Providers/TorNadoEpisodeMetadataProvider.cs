using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace TorNado.Providers;

public sealed class TorNadoEpisodeMetadataProvider(
    ILogger<TorNadoEpisodeMetadataProvider> log,
    TorNadoManager manager
) : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
{
    public string Name => "TorNado";
    public int Order => 0;

    public async Task<MetadataResult<Episode>> GetMetadata(
        EpisodeInfo info,
        CancellationToken cancellationToken
    )
    {
        var result = new MetadataResult<Episode> { HasMetadata = false, QueriedById = true };

        // Episode meta requires a series IMDB id + season + episode numbers
        info.ProviderIds.TryGetValue(MetadataProvider.Imdb.ToString(), out var seriesImdbId);
        if (string.IsNullOrWhiteSpace(seriesImdbId))
            info.SeriesProviderIds.TryGetValue(MetadataProvider.Imdb.ToString(), out seriesImdbId);

        var season = info.ParentIndexNumber;
        var episode = info.IndexNumber;

        if (string.IsNullOrWhiteSpace(seriesImdbId) || season is null || episode is null)
        {
            log.LogDebug(
                "TorNadoEpisodeMetadataProvider: missing series IMDB id or season/episode numbers for {Name}",
                info.Name
            );
            return result;
        }

        var stremio = TorNadoPlugin.Instance?.Configuration.Stremio;
        if (stremio is null)
            return result;

        var seriesId = seriesImdbId;
        StremioMeta? seriesMeta;
        try
        {
            seriesMeta = await stremio
                .GetMetaAsync(seriesId, StremioMediaType.Series)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.LogWarning(
                ex,
                "TorNadoEpisodeMetadataProvider: failed to fetch series meta for {Id}",
                seriesId
            );
            return result;
        }

        if (seriesMeta is null || !seriesMeta.IsValid())
            return result;

        var epMeta = seriesMeta.Videos?.FirstOrDefault(v =>
            v.Season == season && (v.Episode ?? v.Number) == episode
        );

        if (epMeta is null)
        {
            log.LogDebug(
                "TorNadoEpisodeMetadataProvider: no episode meta found for S{Season}E{Episode} in {SeriesId}",
                season,
                episode,
                seriesId
            );
            return result;
        }

        epMeta.Type = StremioMediaType.Episode;

        if (manager.IntoBaseItem(epMeta) is not Episode ep)
            return result;

        ep.ProviderIds.Remove("Stremio");
        result.HasMetadata = true;
        result.Item = ep;
        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
        EpisodeInfo searchInfo,
        CancellationToken cancellationToken
    ) => Task.FromResult<IEnumerable<RemoteSearchResult>>([]);

    public Task<HttpResponseMessage> GetImageResponse(
        string url,
        CancellationToken cancellationToken
    ) => throw new NotImplementedException();
}

