using GameLauncher.App.Services;
using GameLauncher.App.ViewModels;
using GameLauncher.Core;
using GameLauncher.Core.Artwork;
using GameLauncher.Core.Backup;
using GameLauncher.Core.GameInfo;
using GameLauncher.Core.Library;
using GameLauncher.Core.PlayTime;
using GameLauncher.Core.Settings;
using GameLauncher.Core.Startup;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace GameLauncher.App;

public partial class App : Application, IAppLifecycle
{
    private static readonly HttpClient Http = CreateHttpClient();

    private MainWindow? _window;
    private TrayIcon? _tray;
    private PlayTimeMonitor? _playTime;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _backupTimer;
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;

    public App()
    {
        InitializeComponent();
        CrashLog.Install(this);
    }

    bool IAppLifecycle.HasRunningGames => _playTime?.HasSessions == true;

    /// <summary>Показать окно поверх остальных (в том числе из трея) — при повторном запуске лаунчера. Можно вызывать из любого потока.</summary>
    public void BringToFront() => _dispatcher?.TryEnqueue(() => _window?.ShowAndActivate());

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        var options = StartupOptions.Parse(Environment.GetCommandLineArgs().Skip(1));

        var store = new JsonLibraryStore(AppPaths.LibraryFilePath);
        GameLibrary library;
        PlayHistory history;
        try
        {
            library = new GameLibrary(store);
            history = new PlayHistory(AppPaths.SessionsFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Файл занят или недоступен. Окна ещё нет — показываем системное сообщение и выходим, ничего не перезаписав.
            MessageBox(
                IntPtr.Zero,
                $"Не удалось открыть данные лаунчера:\n{AppPaths.DataDirectory}\n\n{ex.Message}\n\n"
                + "Возможно, файл занят другой программой (например, антивирусом). Попробуйте запустить лаунчер ещё раз.",
                "GameLauncher",
                0x10); // MB_ICONERROR
            Exit();
            return;
        }

        var playTime = _playTime = new PlayTimeMonitor(
            new PlaySessionTracker(library, history),
            new WindowsRunningProcesses(),
            _dispatcher);

        var artwork = new ArtworkCache(AppPaths.ArtworkDirectory, Http);
        var detailsStore = new GameDetailsStore(AppPaths.GameDetailsDirectory);
        var backups = new BackupService(AppPaths.DataDirectory);
        StartDailyBackups(backups);
        var autostart = new Autostart(new RegistryAutostartStore(), Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "GameLauncher.exe"));
        try
        {
            autostart.RepairIfMoved();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Автозапуск останется со старым путём; переключатель в настройках его перезапишет.
        }

        var mainWindow = _window = new MainWindow(window =>
        {
            var dialogs = new DialogService(window);
            var settings = new SettingsViewModel(
                new SettingsStore(AppPaths.SettingsFilePath),
                new DpapiSecretStore(AppPaths.SteamGridDbKeyPath),
                dialogs,
                Http,
                autostart,
                backups,
                this);
            var steamGridDb = new SteamGridDbClient(Http, () => settings.ApiKey);
            var details = new DetailsService(library, detailsStore, new SteamStoreClient(Http, primaryRegion: () => settings.SteamRegion));
            return new MainViewModel(
                library,
                dialogs,
                playTime,
                artwork,
                steamGridDb,
                new CoverService(library, artwork, steamGridDb, details),
                details,
                new GameRemover(library, artwork, detailsStore, history),
                settings,
                history,
                new ShellShortcutResolver(),
                store.CorruptBackupPath);
        });

        _tray = CreateTray();
        mainWindow.CanHideToTray = () => _tray?.IsAdded == true;
        mainWindow.HiddenToTray += (_, _) =>
        {
            if (mainWindow.ViewModel.Settings.TryMarkTrayHintShown())
            {
                _tray?.ShowNotification("GameLauncher работает в трее", "Время игр продолжает считаться. Выход — правый клик по значку → «Выход».");
            }
        };
        mainWindow.Closed += (_, _) =>
        {
            playTime.Stop();
            _tray?.Dispose();
            _tray = null;
        };

        // Автозапуск стартует сразу в трей; без значка в трее окно всё-таки показываем, иначе до лаунчера не добраться.
        if (!options.StartInTray || _tray?.IsAdded != true)
        {
            mainWindow.Activate();
        }
    }

    private TrayIcon? CreateTray()
    {
        TrayIcon tray;
        try
        {
            tray = new TrayIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "GameLauncher.ico"), "GameLauncher", _dispatcher!);
        }
        catch (InvalidOperationException ex)
        {
            // Без трея лаунчер работает как раньше: крестик закрывает его.
            CrashLog.Write(ex);
            return null;
        }

        tray.OpenRequested += (_, _) => _window?.ShowAndActivate();
        tray.ExitRequested += (_, _) => ExitLauncher();
        // Выключение или перезагрузка: сохранить время идущих игр, пока процесс не убили.
        tray.SessionEnding += (_, _) => _playTime?.Stop();
        return tray;
    }

    /// <summary>
    /// Ежедневная автокопия библиотеки и настроек: при запуске и раз в час — лаунчер может сутками жить в трее.
    /// В фоне: файлы читаются так, что запись библиотеки не мешает.
    /// </summary>
    private void StartDailyBackups(BackupService backups)
    {
        void Run() => Task.Run(() =>
        {
            try
            {
                backups.EnsureDailyBackup();
            }
            catch (Exception ex) when (ex is BackupException or IOException or UnauthorizedAccessException)
            {
                // Не вышло сегодня — попробуем через час; основные данные это не затрагивает.
            }
        });

        Run();
        _backupTimer = _dispatcher!.CreateTimer();
        _backupTimer.Interval = TimeSpan.FromHours(1);
        _backupTimer.IsRepeating = true;
        _backupTimer.Tick += (_, _) => Run();
        _backupTimer.Start();
    }

    /// <summary>
    /// После восстановления из копии: запустить новый экземпляр (он дождётся завершения этого) и выйти,
    /// ничего не записав. Свой перезапуск, а не AppInstance.Restart, — чтобы порядок «старый вышел → новый
    /// занял ключ экземпляра» был гарантирован.
    /// </summary>
    void IAppLifecycle.RestartWithoutSaving()
    {
        _window?.DisableSaving();
        _backupTimer?.Stop();
        try
        {
            var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "GameLauncher.exe");
            Process.Start(new ProcessStartInfo(exe, $"{StartupOptions.WaitForProcessPrefix}{Environment.ProcessId}") { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            CrashLog.Write(ex);
            MessageBox(IntPtr.Zero, "Данные восстановлены. Запустите лаунчер заново.", "GameLauncher", 0x40); // MB_ICONINFORMATION
        }

        // Учёт времени пуст (восстановление запрещено, пока идёт игра) — Stop ничего не запишет.
        _playTime?.Stop();
        _tray?.Dispose();
        _tray = null;
        _window?.CloseForExit();
        Exit();
    }

    /// <summary>«Выход» в меню трея: сохранить время и закрыть лаунчер.</summary>
    private void ExitLauncher()
    {
        _playTime?.Stop();
        _window?.CloseForExit();
        Exit();
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
