using GameLauncher.App.Services;
using GameLauncher.App.ViewModels;
using GameLauncher.Core;
using GameLauncher.Core.Library;
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

        _window = new MainWindow(window =>
            new MainViewModel(library, new DialogService(window), store.CorruptBackupPath));
        _window.Activate();
    }
}
