using GameLauncher.App.Services;
using GameLauncher.App.ViewModels;
using GameLauncher.Core;
using GameLauncher.Core.Library;
using GameLauncher.Core.PlayTime;
using Microsoft.UI.Xaml;

namespace GameLauncher.App;

public partial class App : Application
{
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
            new MainViewModel(library, new DialogService(window), playTime, store.CorruptBackupPath));
        // Ограничение первой версии: время считается, только пока лаунчер открыт.
        _window.Closed += (_, _) => playTime.Stop();
        _window.Activate();
    }
}
