using GameLauncher.App.ViewModels;
using GameLauncher.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GameLauncher.App;

public sealed partial class MainWindow : Window
{
    public MainWindow(Func<Window, MainViewModel> createViewModel)
    {
        ViewModel = createViewModel(this);
        InitializeComponent();

        // Контент под строкой заголовка — чтобы Mica была и там; кнопки окна рисует система.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        ApplyTheme();
        ViewModel.ThemeChanged += (_, _) => ApplyTheme();
        Root.ActualThemeChanged += (_, _) => ApplyCaptionButtonColors();

        Root.Loaded += async (_, _) => await ViewModel.OnLoadedAsync();
    }

    public MainViewModel ViewModel { get; }

    private void ApplyTheme()
    {
        Root.RequestedTheme = ViewModel.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        ApplyCaptionButtonColors();
    }

    /// <summary>Кнопки окна (свернуть/развернуть/закрыть) рисует система — подгоняем их цвета под тему приложения.</summary>
    private void ApplyCaptionButtonColors()
    {
        var dark = Root.ActualTheme == ElementTheme.Dark;
        var foreground = dark ? Colors.White : Colors.Black;
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = dark ? ColorHelper.FromArgb(0xFF, 0x80, 0x80, 0x80) : ColorHelper.FromArgb(0xFF, 0x90, 0x90, 0x90);
        titleBar.ButtonHoverBackgroundColor = dark ? ColorHelper.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x1A, 0x00, 0x00, 0x00);
        titleBar.ButtonPressedBackgroundColor = dark ? ColorHelper.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x33, 0x00, 0x00, 0x00);
    }

    private void OnSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(FocusState.Keyboard);
    }

    private async void OnScreenshotClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ScreenshotViewModel screenshot)
        {
            await ViewModel.ShowScreenshotAsync(screenshot);
        }
    }

    private async void OnGameClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GameItemViewModel item)
        {
            await ViewModel.OpenDetailsAsync(item);
        }
    }
}
