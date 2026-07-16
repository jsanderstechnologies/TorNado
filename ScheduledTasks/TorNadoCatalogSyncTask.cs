using TorNado.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace TorNado.ScheduledTasks;

public sealed class TorNadoCatalogItemsSyncTask(
    ILogger<TorNadoCatalogItemsSyncTask> log,
    CatalogImportService importService
) : IScheduledTask
{
    public string Name => "Import Catalogs";
    public string Key => "TorNadoCatalogItemsSync";
    public string Description => "Imports items from enabled TorNado catalogs into Jellyfin.";
    public string Category => "TorNado";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken ct)
    {
        log.LogInformation("Starting TorNado catalog sync task...");
        await importService.SyncAllEnabledAsync(ct, progress).ConfigureAwait(false);
        log.LogInformation("TorNado catalog sync task finished.");
    }
}

