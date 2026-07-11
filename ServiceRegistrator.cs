using TorNado.Config;
using TorNado.Decorators;
using TorNado.Filters;
using TorNado.Providers;
using TorNado.ScheduledTasks;
using TorNado.Services;
//using IntroDbPlugin.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TorNado;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost host)
    {
        services.AddSingleton<InsertActionFilter>();
        services.AddSingleton<SearchActionFilter>();
        services.AddSingleton<PlaybackInfoFilter>();
        services.AddSingleton<ImageResourceFilter>();
        services.AddSingleton<DeleteResourceFilter>();
        services.AddSingleton<DownloadFilter>();
        services.AddSingleton<TorNadoManager>();
        services.DecorateSingle<IItemRepository, TorNadoItemRepository>();
        services.AddSingleton(sp => (TorNadoItemRepository)sp.GetRequiredService<IItemRepository>());
        services.AddSingleton<TorNadoStremioProviderFactory>();
        services.AddSingleton(sp => new Lazy<TorNadoManager>(sp.GetRequiredService<TorNadoManager>));
        services.AddSingleton<CatalogService>();
        services.AddSingleton<CatalogImportService>();
        services.AddSingleton<PalcoCacheService>();
        services.AddSingleton<IHostedService, TorNadoJavaScriptRegistrationService>();
        services.AddSingleton<SubtitleProvider>();
        services.AddSingleton<ISubtitleProvider>(sp => sp.GetRequiredService<SubtitleProvider>());
        services.AddSingleton(sp => new Lazy<SubtitleProvider>(
            sp.GetRequiredService<SubtitleProvider>
        ));
        
        // Register Clients
        services.AddSingleton<TmdbClient>();
        services.AddSingleton<TorBoxClient>();

        // Metadata providers
        services.AddSingleton<TorNadoSeriesProvider>();
        services.AddSingleton<IRemoteMetadataProvider>(sp =>
            sp.GetRequiredService<TorNadoSeriesProvider>()
        );
        services.AddSingleton<TorNadoMovieMetadataProvider>();
        services.AddSingleton<IRemoteMetadataProvider>(sp =>
            sp.GetRequiredService<TorNadoMovieMetadataProvider>()
        );
        services.AddSingleton<TorNadoEpisodeMetadataProvider>();
        services.AddSingleton<IRemoteMetadataProvider>(sp =>
            sp.GetRequiredService<TorNadoEpisodeMetadataProvider>()
        );
        services.AddSingleton<TorNadoSeasonMetadataProvider>();
        services.AddSingleton<IRemoteMetadataProvider>(sp =>
            sp.GetRequiredService<TorNadoSeasonMetadataProvider>()
        );

        // Image provider
        services.AddSingleton<TorNadoImageProvider>();
        services.AddSingleton<IRemoteImageProvider>(sp =>
            sp.GetRequiredService<TorNadoImageProvider>()
        );

        // Register HttpClient for IntroDbClient
        services.AddHttpClient<IntroDbClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.introdb.app");
            client.Timeout = TimeSpan.FromSeconds(IntroDbClient.DefaultTimeoutSeconds);
        });
        services.AddSingleton<IMediaSegmentProvider, IntroDbSegmentProvider>();

        services.AddHostedService<TorNadoService>();
        services
            .DecorateSingle<IDtoService, DtoServiceDecorator>()
            .DecorateSingle<IMediaSourceManager, MediaSourceManagerDecorator>()
            .DecorateSingle<ICollectionManager, CollectionManagerDecorator>()
            .DecorateSingle<IPlaylistManager, PlaylistManagerDecorator>()
            .DecorateSingle<ISubtitleManager, SubtitleManagerDecorator>()
            .DecorateSingle<IProviderManager, ProviderManagerDecorator>()
            .DecorateSingle<IImageProcessor, ImageProcessorDecorator>();
        // Expose the concrete decorator as Lazy so ImageProcessorDecorator can call SaveImageDirect
        // without introducing a circular dependency at construction time.
        services.AddSingleton(sp => new Lazy<ProviderManagerDecorator>(
            () => (ProviderManagerDecorator)sp.GetRequiredService<IProviderManager>()));
        services.AddSingleton(sp => new Lazy<ILibraryManager>(
            sp.GetRequiredService<ILibraryManager>));
        services.AddSingleton(sp => new Lazy<ISubtitleManager>(
            sp.GetRequiredService<ISubtitleManager>
        ));

        services.PostConfigure<Microsoft.AspNetCore.Mvc.MvcOptions>(o =>
        {
            o.Filters.AddService<InsertActionFilter>(order: 1);
            o.Filters.AddService<SearchActionFilter>(order: 2);
            o.Filters.AddService<PlaybackInfoFilter>(order: 3);
            o.Filters.AddService<ImageResourceFilter>();
            o.Filters.AddService<DeleteResourceFilter>();
            o.Filters.AddService<DownloadFilter>();
        });
    }

    public class TorNadoService(IConfiguration config, ILogger<TorNadoService> log) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var analyze = TorNadoPlugin.Instance?.Configuration?.FFmpegAnalyzeDuration ?? "5M";
            var probe = TorNadoPlugin.Instance?.Configuration?.FFmpegProbeSize ?? "40M";

            config["FFmpeg:probesize"] = probe;
            config["FFmpeg:analyzeduration"] = analyze;

            log.LogInformation(
                "TorNado: set FFmpeg:probesize={Probe}, FFmpeg:analyzeduration={Analyze}",
                probe,
                analyze
            );
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

public static class ServiceCollectionDecorationExtensions
{
    private static object BuildInner(IServiceProvider sp, ServiceDescriptor d)
    {
        if (d.ImplementationInstance is not null)
            return d.ImplementationInstance;
        if (d.ImplementationFactory is not null)
            return d.ImplementationFactory(sp);
        return ActivatorUtilities.CreateInstance(sp, d.ImplementationType!);
    }

    public static IServiceCollection DecorateSingle<TService, TDecorator>(
        this IServiceCollection services
    )
        where TDecorator : class, TService
    {
        var original = services.LastOrDefault(sd => sd.ServiceType == typeof(TService));
        if (original is null)
            return services; // nothing to decorate

        services.Remove(original);

        services.Add(
            new ServiceDescriptor(
                typeof(TService),
                sp =>
                {
                    var inner = (TService)BuildInner(sp, original);
                    return ActivatorUtilities.CreateInstance<TDecorator>(sp, inner);
                },
                original.Lifetime
            )
        );

        return services;
    }
}

