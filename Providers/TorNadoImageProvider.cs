using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace TorNado.Providers;

public sealed class TorNadoImageProvider(ILogger<TorNadoImageProvider> log)
    : IRemoteImageProvider,
        IHasOrder
{
    public string Name => "TorNado";
    public int Order => 0;

    public bool Supports(BaseItem item) => item is Movie or Series;

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) =>
        [ImageType.Primary, ImageType.Backdrop, ImageType.Logo, ImageType.Thumb];

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(
        BaseItem item,
        CancellationToken cancellationToken
    )
    {
        var id = ResolveId(item);
        if (id is null)
        {
            log.LogDebug("TorNadoImageProvider: no usable ID for {Name}", item.Name);
            return [];
        }

        var torNado = TorNadoPlugin.Instance?.Configuration.TorNado;
        if (torNado is null)
            return [];

        var mediaType = item is Movie ? TorNadoMediaType.Movie : TorNadoMediaType.Series;
        TorNadoMeta? meta;
        try
        {
            meta = await torNado.GetMetaAsync(id, mediaType).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "TorNadoImageProvider: failed to fetch meta for {Id}", id);
            return [];
        }

        if (meta is null || !meta.IsValid())
            return [];

        return BuildImages(meta);
    }

    public Task<HttpResponseMessage> GetImageResponse(
        string url,
        CancellationToken cancellationToken
    ) => throw new NotImplementedException();

    private static IEnumerable<RemoteImageInfo> BuildImages(TorNadoMeta meta)
    {
        var images = new List<RemoteImageInfo>();

        if (!string.IsNullOrWhiteSpace(meta.Poster))
            images.Add(
                new RemoteImageInfo
                {
                    ProviderName = "TorNado",
                    Type = ImageType.Primary,
                    Url = meta.Poster,
                }
            );

        if (!string.IsNullOrWhiteSpace(meta.Background))
            images.Add(
                new RemoteImageInfo
                {
                    ProviderName = "TorNado",
                    Type = ImageType.Backdrop,
                    Url = meta.Background,
                }
            );

        if (!string.IsNullOrWhiteSpace(meta.Logo))
            images.Add(
                new RemoteImageInfo
                {
                    ProviderName = "TorNado",
                    Type = ImageType.Logo,
                    Url = meta.Logo,
                }
            );

        if (!string.IsNullOrWhiteSpace(meta.LandscapePoster))
            images.Add(
                new RemoteImageInfo
                {
                    ProviderName = "TorNado",
                    Type = ImageType.Thumb,
                    Url = meta.LandscapePoster,
                }
            );

        return images;
    }

    private static string? ResolveId(BaseItem item)
    {
        var imdb = item.GetProviderId(MetadataProvider.Imdb);
        if (!string.IsNullOrWhiteSpace(imdb))
            return imdb;

        var tmdb = item.GetProviderId(MetadataProvider.Tmdb);
        if (!string.IsNullOrWhiteSpace(tmdb))
            return $"tmdb:{tmdb}";

        return null;
    }
}

