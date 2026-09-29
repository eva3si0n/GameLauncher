using GameLauncher.App.Services;
using GameLauncher.App.ViewModels;
using GameLauncher.Core;
using GameLauncher.Core.Artwork;
using GameLauncher.Core.GameInfo;
using GameLauncher.Core.Library;
using GameLauncher.Core.PlayTime;
using GameLauncher.Core.Settings;
using Microsoft.UI.Xaml;

namespace GameLauncher.App;

public partial class App : Application
{
    private static readonly HttpClient Http = CreateHttpClient();

    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => CrashLog.Write(e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var store = new JsonLibraryStore(AppPaths.LibraryFilePath);
        var library = new GameLibrary(store);

        var playTime = new PlayTimeMonitor(
            new PlaySessionTracker(library),
            new WindowsRunningProcesses(),
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());

        _window = new MainWindow(window =>
            new MainViewModel(
                library,
                new DialogService(window),
                playTime,
                new ArtworkCache(AppPaths.ArtworkDirectory, Http),
                new DpapiSecretStore(AppPaths.SteamGridDbKeyPath),
                new GameDetailsStore(AppPaths.GameDetailsDirectory),
                new SettingsStore(AppPaths.SettingsFilePath),
                Http,
                store.CorruptBackupPath));
        // Ограничение первой версии: время считается, только пока лаунчер открыт.
        _window.Closed += (_, _) => playTime.Stop();
        _window.Activate();
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GameLauncher/1.0");
        return http;
    }
}
