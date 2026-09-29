using GameLauncher.App.ViewModels;
using GameLauncher.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace GameLauncher.App;

public sealed partial class MainWindow : Window
{
    /// <summary>Последние размер и положение в обычном (не развёрнутом) состоянии.</summary>
    private PixelRect _normalBounds;

    public MainWindow(Func<Window, MainViewModel> createViewModel)
    {
        ViewModel = createViewModel(this);
        InitializeComponent();

        // Контент под строкой заголовка — чтобы Mica была и там; кнопки окна рисует система.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        ApplyTheme();
        ViewModel.Settings.ThemeChanged += (_, _) => ApplyTheme();
        Root.ActualThemeChanged += (_, _) => ApplyCaptionButtonColors();

        RestorePlacement();
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += (_, _) => SavePlacement();

        Root.Loaded += async (_, _) => await ViewModel.OnLoadedAsync();
    }

    public MainViewModel ViewModel { get; }

    private PixelRect CurrentBounds => new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);

    private OverlappedPresenterState? PresenterState => (AppWindow.Presenter as OverlappedPresenter)?.State;

    /// <summary>Размер и положение с прошлого запуска, вписанные в рабочую область монитора.</summary>
    private void RestorePlacement()
    {
        _normalBounds = CurrentBounds;
        if (ViewModel.Settings.SavedWindowPlacement is not { } saved)
        {
            return;
        }

        // Монитор, на котором было окно; если его больше нет — основной.
        var display = DisplayArea.GetFromRect(new RectInt32(saved.X, saved.Y, saved.Width, saved.Height), DisplayAreaFallback.Primary);
        var area = display.WorkArea;
        var fitted = saved.FitInto(new PixelRect(area.X, area.Y, area.Width, area.Height));
        AppWindow.MoveAndResize(new RectInt32(fitted.X, fitted.Y, fitted.Width, fitted.Height));
        _normalBounds = fitted;

        if (saved.IsMaximized)
        {
            // Разворачиваем после первого показа окна — так обычный размер остаётся запомненным для «Восстановить».
            void MaximizeOnce(object sender, WindowActivatedEventArgs args)
            {
                Activated -= MaximizeOnce;
                (AppWindow.Presenter as OverlappedPresenter)?.Maximize();
            }

            Activated += MaximizeOnce;
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if ((args.DidPositionChange || args.DidSizeChange) && PresenterState == OverlappedPresenterState.Restored)
        {
            _normalBounds = CurrentBounds;
        }
    }

    private void SavePlacement()
    {
        var state = PresenterState;
        var bounds = state == OverlappedPresenterState.Restored ? CurrentBounds : _normalBounds;
        ViewModel.Settings.SaveWindowPlacement(new WindowPlacement(
            bounds.X, bounds.Y, bounds.Width, bounds.Height, IsMaximized: state == OverlappedPresenterState.Maximized));
    }

    private void ApplyTheme()
    {
        Root.RequestedTheme = ViewModel.Settings.Theme switch
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
