using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using MyTwitterBackup.Core.Database;
using MyTwitterBackup.Core.Services;
using MyTwitterBackup.Desktop.Services;
using MyTwitterBackup.Desktop.ViewModels;
using Serilog;

namespace MyTwitterBackup.Desktop;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
        UnhandledException += OnUnhandledException;
    }

    public static IServiceProvider Services { get; private set; } = null!;

    public static Window Window { get; private set; } = null!;

    /// <summary>UI thread dispatcher (fully qualified to avoid ambiguity with Windows.System.DispatcherQueue).</summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue MainDispatcher { get; private set; } = null!;

    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>Error that happened during startup (e.g. database folder not accessible).</summary>
    public static string? StartupError { get; private set; }

    public static T GetService<T>() where T : notnull => Services.GetRequiredService<T>();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        ConfigureLogging();

        try
        {
            GetService<DatabaseInitializer>().EnsureCreated();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database initialization failed");
            StartupError = $"Could not open the database: {ex.Message} Change the database folder in Settings.";
        }

        // Resumes interrupted and pending downloads from the previous session.
        GetService<DownloadQueueService>().Start();

        Window = new MainWindow();
        Window.Activate();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IAppSettings, AppSettings>();
        services.AddSingleton<IAppDbContextFactory, AppDbContextFactory>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<TweetCache>();
        services.AddSingleton<MediaDownloader>();
        services.AddSingleton<DownloadQueueService>();
        services.AddSingleton<PostRepository>();

        services.AddSingleton<BrowserViewModel>();
        services.AddTransient<QueueViewModel>();
        services.AddTransient<DownloadedViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services.BuildServiceProvider();
    }

    private static void ConfigureLogging()
    {
        var settings = GetService<IAppSettings>();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(AppSettings.GetLogsFolder(settings), "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .WriteTo.Debug()
            .CreateLogger();
        Log.Information("MyTwitterBackup started");
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled exception");
        Log.CloseAndFlush();
    }
}
