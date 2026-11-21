using CommunityToolkit.Mvvm.Messaging;
using ShowUp.Core.Catalog;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Infrastructure;
using ShowUp.Core.Marking;
using ShowUp.Core.Reminders;
using ShowUp.Core.Services;
using ShowUp.Core.Storage;
using ShowUp.Core.Tracking;
using ShowUp.Core.ViewModels;
using ShowUp.Infrastructure;
using ShowUp.Services.Dialogs;
using ShowUp.Services.Entitlements;
using ShowUp.Services.Marking;
using ShowUp.Services.Navigation;
using ShowUp.Services.Rating;
using ShowUp.Services.Reminders;
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

        // Store and RevenueCat services capture the UI dispatcher when built: build them here, on the UI thread.
        Host.Services.GetRequiredService<IEntitlementService>();

        // Run app lifecycle updates
        var appPreferences = Host.Services.GetRequiredService<IAppPreferences>();
        var appUpdater = Host.Services.GetRequiredService<IAppUpdater>();
        await appUpdater.EnsureAppUpToDateAsync();
        appPreferences.LaunchCount++;
        Host.Services.GetRequiredService<MarkerBrushUpdater>().Apply();

        // Create WindowShell as root content
        if (MainWindow.Content is not WindowShell)
        {
            var shell = new WindowShell(Host.Services, MainWindow);
            MainWindow.Content = shell;
        }

        var entitlements = Host.Services.GetRequiredService<IEntitlementService>();
        MainWindow.Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                _ = entitlements.RefreshAsync();
            }
        };
        _ = entitlements.RefreshAsync();

#if !HAS_UNO
        ShowUp.Platforms.Windows.Reminders.WindowsReminderScheduler.RegisterActivation(Host.Services.GetRequiredService<IMessenger>());
#endif
        var reminders = Host.Services.GetRequiredService<ReminderCoordinator>();
        _ = reminders.StartAsync();
        MainWindow.Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                _ = reminders.ResumeAsync();
            }
        };

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
        services.AddSingleton<INotesRepository, SqliteNotesRepository>();
        services.AddSingleton<IMetricsRepository, SqliteMetricsRepository>();
        services.AddSingleton<IReminderRepository, SqliteReminderRepository>();
        services.AddSingleton<ICustomProgramRepository, SqliteCustomProgramRepository>();
        services.AddSingleton<ISyncedSettings, SqliteSyncedSettings>();
        services.AddSingleton<IBundledCatalogSource, Services.Catalog.BundledCatalogSource>();
        services.AddSingleton<ICatalogService, CatalogService>();
        services.AddSingleton<IMarkingFeedback, MarkingFeedback>();
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        services.AddSingleton<MarkerBrushUpdater>();
        services.AddSingleton<IActiveEnrollmentService, ActiveEnrollmentService>();
        RegisterEntitlements(context, services);
#if DEBUG
        DecorateEntitlementsForDebug(services);
#endif
        services.AddSingleton(new ProLinks(
            new Uri(context.Configuration["Legal:TermsUrl"] is { Length: > 0 } terms ? terms : "https://github.com/MartinZikmund/show-up/blob/main/docs/terms.md"),
            new Uri(context.Configuration["Legal:PrivacyUrl"] is { Length: > 0 } privacy ? privacy : "https://github.com/MartinZikmund/show-up/blob/main/docs/privacy.md")));
        services.AddSingleton<ShowUp.Core.CustomPrograms.ICustomProgramService, ShowUp.Core.CustomPrograms.CustomProgramService>();
        services.AddSingleton<IJournalService, JournalService>();
        services.AddSingleton<IScheduleQuery, ScheduleQuery>();
        services.AddSingleton<LapseCoordinator>();
        RegisterReminderScheduler(services);
        services.AddSingleton<IReminderDayProvider, ReminderDayProvider>();
        services.AddSingleton<ReminderMessageBuilder>(sp => new ReminderMessageBuilder(sp.GetRequiredService<IStringLocalizer>()));
        services.AddSingleton<ReminderCoordinator>();
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
        services.AddScoped<ICalendarPrompts, CalendarPrompts>();
        services.AddScoped<IBonusWorkoutDialog, BonusWorkoutDialogService>();
        services.AddScoped<JournalSaveGuard>();
        services.AddScoped<IJournalDialogs, JournalDialogService>();
        services.AddScoped<ICrossingCoordinator, CrossingCoordinator>();
        services.AddScoped<IPrimaryProgramPrompt, PrimaryProgramPrompt>();
        services.AddScoped<ISectionNavigator>(sp => sp.GetRequiredService<WindowShellViewModel>());
        services.AddScoped<INavigationService>(sp =>
        {
            var service = new NavigationService(sp.GetRequiredService<IFrameProvider>());
            service.RegisterView(typeof(Views.TodayView), typeof(TodayViewModel));
            service.RegisterView(typeof(Views.ProgramCalendarView), typeof(ProgramCalendarViewModel));
            service.RegisterView(typeof(Views.ProgramsView), typeof(ProgramsViewModel));
            service.RegisterView(typeof(Views.ProgramDetailView), typeof(ProgramDetailViewModel));
            service.RegisterView(typeof(Views.ProgramResultsView), typeof(ProgramResultsViewModel));
            service.RegisterView(typeof(Views.HistoryView), typeof(HistoryViewModel));
            service.RegisterView(typeof(Views.SettingsView), typeof(SettingsViewModel));
            service.RegisterView(typeof(Views.ProView), typeof(ProViewModel));
            service.RegisterView(typeof(Views.RemindersView), typeof(RemindersViewModel));
            return service;
        });

        // Scoped ViewModels
        services.AddScoped<WindowShellViewModel>();

        // Transient ViewModels (new instance per navigation)
        services.AddTransient<TodayViewModel>();
        services.AddTransient<ProgramCalendarViewModel>();
        services.AddTransient<ProgramsViewModel>();
        services.AddTransient<ProgramDetailViewModel>();
        services.AddTransient<ProgramResultsViewModel>();
        services.AddTransient<DayDetailViewModel>();
        services.AddTransient<LogResultsViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProViewModel>();
        services.AddTransient<RemindersViewModel>();
    }

#if DEBUG
    // Settings > Debugging can then simulate a lapse on heads where Pro is simply unlocked.
    private static void DecorateEntitlementsForDebug(IServiceCollection services)
    {
        var real = services.Last(d => d.ServiceType == typeof(IEntitlementService));
        services.Remove(real);
        services.AddSingleton(sp => new DebugEntitlementOverride(real switch
        {
            { ImplementationInstance: IEntitlementService instance } => instance,
            { ImplementationFactory: { } factory } => (IEntitlementService)factory(sp),
            { ImplementationType: { } type } => (IEntitlementService)ActivatorUtilities.CreateInstance(sp, type),
            _ => throw new InvalidOperationException("The entitlement registration has no implementation."),
        }));
        services.AddSingleton<IEntitlementService>(sp => sp.GetRequiredService<DebugEntitlementOverride>());
    }
#endif

    private static void RegisterReminderScheduler(IServiceCollection services)
    {
#if __ANDROID__
        services.AddSingleton<IReminderScheduler, ShowUp.Droid.Reminders.AndroidReminderScheduler>();
#elif __IOS__
        services.AddSingleton<IReminderScheduler, ShowUp.iOS.Reminders.IosReminderScheduler>();
#elif !HAS_UNO
        services.AddSingleton<IReminderScheduler, ShowUp.Platforms.Windows.Reminders.WindowsReminderScheduler>();
#elif __WASM__
        services.AddSingleton<IReminderScheduler, ShowUp.Platforms.WebAssembly.Reminders.BrowserReminderScheduler>();
#elif __DESKTOP__
        services.AddSingleton<InAppReminderScheduler>();
        services.AddSingleton<IReminderScheduler>(sp => sp.GetRequiredService<InAppReminderScheduler>());
#else
        services.AddSingleton<IReminderScheduler, NullReminderScheduler>();
#endif
    }

    private static void RegisterEntitlements(HostBuilderContext context, IServiceCollection services)
    {
#if __ANDROID__ || __IOS__
        Uno.RevenueCat.RevenueCatExtension.AddRevenueCat(services);
#if __ANDROID__
        var apiKey = context.Configuration["RevenueCat:AndroidApiKey"] ?? "";
#else
        var apiKey = context.Configuration["RevenueCat:AppleApiKey"] ?? "";
#endif
        services.AddSingleton<IEntitlementService>(sp => new RevenueCatEntitlementService(
            sp.GetRequiredService<Uno.RevenueCat.Services.IRevenueCatBilling>(),
            sp.GetRequiredService<IAppPreferences>(),
            sp.GetRequiredService<IStringLocalizer>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<RevenueCatEntitlementService>>(),
            apiKey));
#elif !HAS_UNO
        services.AddSingleton<IEntitlementService>(sp => new MicrosoftStoreEntitlementService(
            context.Configuration["MicrosoftStore:ProMonthlyStoreId"] ?? "",
            context.Configuration["MicrosoftStore:ProYearlyStoreId"] ?? "",
            context.Configuration["MicrosoftStore:ProLifetimeStoreId"] ?? "",
            () => WinRT.Interop.WindowNative.GetWindowHandle(Current.MainWindow),
            sp.GetRequiredService<IAppPreferences>(),
            sp.GetRequiredService<IStringLocalizer>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<MicrosoftStoreEntitlementService>>()));
#else
        services.AddSingleton<IEntitlementService, UnlockedEntitlementService>();
#endif
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
