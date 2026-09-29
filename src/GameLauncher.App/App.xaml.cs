using GameLauncher.App.Services;
using GameLauncher.App.ViewModels;
using GameLauncher.Core;
using GameLauncher.Core.Artwork;
using GameLauncher.Core.GameInfo;
using GameLauncher.Core.Library;
using GameLauncher.Core.PlayTime;
using GameLauncher.Core.Settings;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace GameLauncher.App;

public partial class App : Application
{
    private static readonly HttpClient Http = CreateHttpClient();

    private Window? _window;
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => CrashLog.Write(e.Exception);
    }

    /// <summary>Показать окно поверх остальных — при повторном запуске лаунчера. Можно вызывать из любого потока.</summary>
    public void BringToFront() => _dispatcher?.TryEnqueue(() =>
    {
        if (_window is null)
        {
            return;
        }

        if (_window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        _window.Activate();
    });

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        var store = new JsonLibraryStore(AppPaths.LibraryFilePath);
        GameLibrary library;
        try
        {
            library = new GameLibrary(store);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Файл занят или недоступен. Окна ещё нет — показываем системное сообщение и выходим, ничего не перезаписав.
            MessageBox(
                IntPtr.Zero,
                $"Не удалось открыть библиотеку игр:\n{AppPaths.LibraryFilePath}\n\n{ex.Message}\n\n"
                + "Возможно, файл занят другой программой (например, антивирусом). Попробуйте запустить лаунчер ещё раз.",
                "GameLauncher",
                0x10); // MB_ICONERROR
            Exit();
            return;
        }

        var playTime = new PlayTimeMonitor(
            new PlaySessionTracker(library),
            new WindowsRunningProcesses(),
            _dispatcher);

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GameLauncher/1.0");
        return http;
    }
}
