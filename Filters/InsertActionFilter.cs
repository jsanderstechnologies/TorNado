using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace TorNado.Filters;

public class InsertActionFilter(
    TorNadoManager manager,
    IUserManager userManager,
    ILibraryManager libraryManager,
    ILogger<InsertActionFilter> log
) : IAsyncActionFilter, IOrderedFilter
{
    private readonly KeyLock _lock = new();
    public int Order => 1;

    public async Task OnActionExecutionAsync(
        ActionExecutingContext ctx,
        ActionExecutionDelegate next
    )
    {
        if (
            !ctx.IsInsertableAction()
            || !ctx.TryGetRouteGuid(out var guid)
            || !ctx.TryGetUserId(out var userId)
            || userManager.GetUserById(userId) is not { } user
        )
        {
            await next();
            return;
        }

        // Handle local (non-TorNado) series: sync or clean tree on demand
        if (libraryManager.GetItemById(guid) is Series localSeries && !localSeries.IsTorNado())
        {
            await HandleLocalSeriesAsync(userId, localSeries, ctx.HttpContext.RequestAborted);
            await next();
            return;
        }

        if (manager.GetTorNadoMeta(guid) is not { } torNadoMeta)
        {
            await next();
            return;
        }

        // Get root folder
        var isSeries = torNadoMeta.Type == TorNadoMediaType.Series;
        var root = isSeries
            ? manager.TryGetSeriesFolder(userId)
            : manager.TryGetMovieFolder(userId);
        if (root is null)
        {
            log.LogWarning("No {Type} folder configured", isSeries ? "Series" : "Movie");
            await next();
            return;
        }

        if (manager.IntoBaseItem(torNadoMeta) is { } item)
        {
            var existing = manager.FindExistingItem(item, user);
            if (existing is not null)
            {
                log.LogInformation(
                    "Media already exists; redirecting to canonical id {Id}",
                    existing.Id
                );
                ctx.ReplaceGuid(existing.Id);
                await next();
                return;
            }
        }

        // Fetch full metadata
        var cfg = TorNadoPlugin.Instance!.GetConfig(userId);
        var meta = await cfg.TorNado.GetMetaAsync(
            torNadoMeta.ImdbId ?? torNadoMeta.Id,
            torNadoMeta.Type
        );
        if (meta is null)
        {
            log.LogError(
                "aio meta not found for {Id} {Type}, maybe try aiometadata as meta addon.",
                torNadoMeta.Id,
                torNadoMeta.Type
            );
            await next();
            return;
        }

        // Insert the item
        var baseItem = await InsertMetaAsync(guid, root, meta, user);
        if (baseItem is not null)
        {
            ctx.ReplaceGuid(baseItem.Id);
            manager.RemoveTorNadoMeta(guid);
        }

        await next();
    }

    private async Task HandleLocalSeriesAsync(Guid userId, Series series, CancellationToken ct)
    {
        var cfg = TorNadoPlugin.Instance!.GetConfig(userId);

        if (cfg.ExtendLocalSeriesTrees)
        {
            var alreadySynced =
                series.Tags?.Contains(TorNadoManager.TreeSyncedTag, StringComparer.OrdinalIgnoreCase)
                ?? false;
            if (alreadySynced)
                return;

            if (cfg.TorNado is not { } torNado)
                return;

            log.LogInformation(
                "InsertActionFilter: syncing local series tree for {Name} ({Id})",
                series.Name,
                series.Id
            );

            var meta = await torNado.GetMetaAsync(series).ConfigureAwait(false);
            if (meta is null)
                return;

            await manager
                .SyncSeriesTreesAsync(cfg, meta, ct, existingSeries: series)
                .ConfigureAwait(false);
        }
        else
        {
            // Setting disabled â€” clean any virtual items that may exist for this series
            manager.CleanVirtualTreeItem(series, ct);
        }
    }

    public async Task<BaseItem?> InsertMetaAsync(
        Guid guid,
        Folder root,
        TorNadoMeta meta,
        User user
    )
    {
        BaseItem? baseItem = null;
        var created = false;

        await _lock.RunQueuedAsync(
            guid,
            async ct =>
            {
                meta.Guid = guid;
                (baseItem, created) = await manager.InsertMeta(
                    root,
                    meta,
                    user,
                    false,
                    true,
                    meta.Type is TorNadoMediaType.Series,
                    ct
                );
            }
        );

        if (baseItem is not null && created)
            log.LogInformation("inserted new media: {Name}", baseItem.Name);

        return baseItem;
    }
}

