using ShowUp.Core.Catalog;
using ShowUp.Core.Infrastructure;
using ShowUp.Core.Marking;
using ShowUp.Core.Services;
using ShowUp.Core.Storage;
using ShowUp.Core.Tracking;
using ShowUp.Core.ViewModels;
using ShowUp.Infrastructure;
using ShowUp.Services.Dialogs;
using ShowUp.Services.Marking;
using ShowUp.Services.Navigation;
using ShowUp.Services.Rating;
using ShowUp.Services.Settings;
using ShowUp.Services.Storage;
using ShowUp.Services.Theming;
using Uno.Resizetizer;

namespace ShowUp;

public partial class App : Application, IApplication
{
    public static new App Current => (App)Application.Current;

    public IServiceProvider Services => Host!.Services;

    public string AppVersion
    {
        get
        {
            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public string? WorktreeName =>
        string.IsNullOrEmpty(AppEnvironment.WorktreeName) ? null : AppEnvironment.WorktreeName;

    public App()
    {
        this.InitializeComponent();
#if !HAS_UNO
        // Our HighContrast theme dictionary already maps every custom brush to system colors.
        HighContrastAdjustment = ApplicationHighContrastAdjustment.None;
#endif
    }

    protected Window? MainWindow { get; private set; }
    protected IHost? Host { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var builder = this.CreateBuilder(args)
            .Configure(host => host
#if DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .UseLogging(ConfigureLogging, enableUnoLogging: true)
                .UseConfiguration(configure: configBuilder =>
                    configBuilder
                        .EmbeddedSource<App>()
                        .Section<AppConfig>()
                )
                .UseLocalization()
                .UseDefaultServiceProvider((context, options) =>
                {
                    options.ValidateScopes = true;
                    options.ValidateOnBuild = true;
                })
                .UseHttp((context, services) =>
                {
#if DEBUG
                    services.AddTransient<DelegatingHandler, DebugHttpHandler>();
#endif
                })
                .ConfigureServices(RegisterServices)
            );

        MainWindow = builder.Window;

#if DEBUG
        MainWindow.UseStudio();
#endif
        MainWindow.SetWindowIcon();

        Host = builder.Build();
        IoC.SetProvider(Host.Services);

        // Run app lifecycle updates
        var appPreferences = Host.Services.GetRequiredService<IAppPreferences>();
        var appUpdater = Host.Services.GetRequiredService<IAppUpdater>();
        await appUpdater.EnsureAppUpToDateAsync();
        appPreferences.LaunchCount++;

        // Create WindowShell as root content
        if (MainWindow.Content is not WindowShell)
        {
            var shell = new WindowShell(Host.Services, MainWindow);
            MainWindow.Content = shell;
        }

        MainWindow.Activate();
    }

    private static void RegisterServices(HostBuilderContext context, IServiceCollection services)
    {
        // Singleton services
        services.AddSingleton<IApplication>(sp => Current);
        services.AddSingleton<MZikmund.Toolkit.WinUI.Services.IPreferences, Preferences>();
        services.AddSingleton<IAppPreferences, AppPreferences>();
        services.AddSingleton<IDisplayRequestManager, DisplayRequestManager>();
        services.AddSingleton<IAppUpdater, Infrastructure.AppUpdater>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IUserScope, AnonymousUserScope>();
        services.AddSingleton<IDeviceIdProvider, PreferencesDeviceIdProvider>();
        services.AddSingleton(_ => new ShowUpDatabase(
            Path.Combine(ApplicationData.Current.LocalFolder.Path, "showup.db"),
            // WASM only: IDBFS loads asynchronously and any StorageFolder await waits for it.
            OperatingSystem.IsBrowser()
                ? async () => await ApplicationData.Current.LocalFolder.CreateFolderAsync("db", CreationCollisionOption.OpenIfExists)
                : null));
        services.AddSingleton<ITrackingRepository, SqliteTrackingRepository>();
        services.AddSingleton<IBundledCatalogSource, Services.Catalog.BundledCatalogSource>();
        services.AddSingleton<ICatalogService, CatalogService>();
        services.AddSingleton<IMarkingFeedback, MarkingFeedback>();
        services.AddScoped<IAppRatingService, AppRatingService>();

        // Per-window scoped services
        services.AddScoped<IThemeManager, ThemeManager>();
        services.AddScoped<WindowShellProvider>();
        services.AddScoped<IWindowShellProvider>(sp => sp.GetRequiredService<WindowShellProvider>());
        services.AddScoped<IXamlRootProvider>(sp => sp.GetRequiredService<WindowShellProvider>());
        services.AddScoped<IFrameProvider, FrameProvider>();
        services.AddScoped<IDialogCoordinator, DialogCoordinator>();
        services.AddScoped<IDialogService, DialogService>();
        services.AddScoped<IConfirmationDialogService, ConfirmationDialogService>();
        services.AddScoped<ILauncherService, LauncherService>();
        services.AddScoped<IShareService, ShareService>();
        services.AddScoped<ICrossMoment, CrossMomentService>();
        services.AddScoped<INavigationService>(sp =>
        {
            var service = new NavigationService(sp.GetRequiredService<IFrameProvider>());
            service.RegisterView(typeof(Views.TodayView), typeof(TodayViewModel));
            service.RegisterView(typeof(Views.ProgramCalendarView), typeof(ProgramCalendarViewModel));
            service.RegisterView(typeof(Views.ProgramsView), typeof(ProgramsViewModel));
            service.RegisterView(typeof(Views.HistoryView), typeof(HistoryViewModel));
            service.RegisterView(typeof(Views.SettingsView), typeof(SettingsViewModel));
            return service;
        });

        // Scoped ViewModels
        services.AddScoped<WindowShellViewModel>();

        // Transient ViewModels (new instance per navigation)
        services.AddTransient<TodayViewModel>();
        services.AddTransient<ProgramCalendarViewModel>();
        services.AddTransient<ProgramsViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<SettingsViewModel>();
    }

    private static void ConfigureLogging(HostBuilderContext context, ILoggingBuilder logBuilder)
    {
        logBuilder
            .SetMinimumLevel(
                context.HostingEnvironment.IsDevelopment() ?
                    LogLevel.Information :
                    LogLevel.Warning)
            .CoreLogLevel(LogLevel.Warning);
    }
}
