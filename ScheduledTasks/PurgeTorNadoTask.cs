using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace TorNado.ScheduledTasks;

public sealed class PurgeTorNadoTask(
    ILibraryManager libraryManager,
    ILogger<PurgeTorNadoTask> log,
    TorNadoManager manager
) : IScheduledTask
{
    public string Name => "WARNING: purge all TorNado items";
    public string Key => "PurgeTorNadoTask";
    public string Description => "Removes all TorNado items (local items are kept)";
    public string Category => "TorNado Maintenance";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken ct)
    {
        var items = libraryManager
            .GetItemList(
                new InternalItemsQuery
                {
                    IncludeItemTypes =
                    [
                        BaseItemKind.Episode,
                        BaseItemKind.Season,
                        BaseItemKind.Movie,
                        BaseItemKind.Series,
                        BaseItemKind.BoxSet,
                    ],
                    Recursive = true,
                    HasAnyProviderId = new Dictionary<string, string>
                    {
                        { "Stremio", string.Empty },
                        { "stremio", string.Empty },
                    },
                    GroupByPresentationUniqueKey = false,
                    GroupBySeriesPresentationUniqueKey = false,
                    CollapseBoxSetItems = false,
                    IsDeadPerson = true,
                }
            )
            .Where(item =>
            {
                if (!item.IsTorNado())
                {
                    return false;
                }

                if (File.Exists(item.Path) || Directory.Exists(item.Path))
                {
                    log.LogWarning("Skipping item {ItemId} with local path {Path}", item.Id, item.Path);
                    return false;
                }

                return true;
            })
            .OrderBy(item => item.GetBaseItemKind() switch
            {
                BaseItemKind.Episode => 0,
                BaseItemKind.Season => 1,
                _ => 2,
            })
            .ToList();

        var stats = items
            .GroupBy(i => i.GetBaseItemKind())
            .ToDictionary(g => g.Key, g => g.Count());

        const int batchSize = 250;
        var totalItems = items.Count;
        var processed = 0;

        foreach (var batch in items.Chunk(batchSize))
        {
            libraryManager.DeleteItemsUnsafeFast(batch);
            processed += batch.Length;
            progress?.Report((double)processed / totalItems * 100);
        }

        manager.ClearCache();
        progress?.Report(100.0);

        var parts = stats.Select(kv => $"{kv.Key}={kv.Value}");
        log.LogInformation("Deleted: {Stats} (Total={Total})", string.Join(", ", parts), stats.Values.Sum());
        return Task.CompletedTask;
    }
}

