using GameLauncher.App.ViewModels;
using Microsoft.UI.Xaml;

namespace GameLauncher.App;

public sealed partial class MainWindow : Window
{
    public MainWindow(Func<Window, MainViewModel> createViewModel)
    {
        ViewModel = createViewModel(this);
        InitializeComponent();
        Root.Loaded += async (_, _) => await ViewModel.OnLoadedAsync();
    }

    public MainViewModel ViewModel { get; }
}
