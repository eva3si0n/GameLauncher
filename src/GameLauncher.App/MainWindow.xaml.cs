using GameLauncher.App.ViewModels;
using GameLauncher.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace GameLauncher.App;

public sealed partial class MainWindow : Window
{
    /// <summary>Последние размер и положение в обычном (не развёрнутом) состоянии.</summary>
    private PixelRect _normalBounds;

    /// <summary>Окно хоть раз показывали. При запуске в трей до первого показа размер не сохраняем — он ещё не применён.</summary>
    private bool _shown;

    /// <summary>Закрытие по-настоящему (выход из меню трея), а не в трей.</summary>
    private bool _exiting;

    /// <summary>Данные на диске заменены резервной копией — размер окна больше не сохраняем.</summary>
    private bool _savingDisabled;

    public MainWindow(Func<Window, MainViewModel> createViewModel)
    {
        ViewModel = createViewModel(this);
        InitializeComponent();

        // Контент под строкой заголовка — чтобы Mica была и там; кнопки окна рисует система.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SetIcons();

        ApplyTheme();
        ViewModel.Settings.ThemeChanged += (_, _) => ApplyTheme();
        Root.ActualThemeChanged += (_, _) => ApplyCaptionButtonColors();

        RestorePlacement();
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += OnClosing;
        Activated += (_, _) => _shown = true;

        Root.Loaded += async (_, _) => await ViewModel.OnLoadedAsync();
    }

    public MainViewModel ViewModel { get; }

    /// <summary>Есть ли куда прятать окно (значок в трее показан). Задаёт App.</summary>
    public Func<bool> CanHideToTray { get; set; } = () => false;

    /// <summary>Окно спрятано в трей по крестику.</summary>
    public event EventHandler? HiddenToTray;

    /// <summary>Показать окно (из трея, свёрнутое или ещё ни разу не показанное) и вывести на передний план.</summary>
    public void ShowAndActivate()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
    }

    /// <summary>Больше ничего не записывать в настройки (перед перезапуском после восстановления).</summary>
    public void DisableSaving() => _savingDisabled = true;

    /// <summary>Закрыть окно по-настоящему — лаунчер завершается.</summary>
    public void CloseForExit()
    {
        _exiting = true;
        // AppWindow.Closing приходит только при закрытии средствами системы (крестик, Alt+F4), не от Close().
        if (_shown)
        {
            SavePlacement();
        }

        Close();
    }

    private PixelRect CurrentBounds => new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);

    private OverlappedPresenterState? PresenterState => (AppWindow.Presenter as OverlappedPresenter)?.State;

    /// <summary>
    /// Иконка окна (панель задач, Alt+Tab) и строки заголовка. Файлы лежат рядом с exe (Content в csproj);
    /// абсолютный путь — чтобы не зависеть от разрешения ms-appx:/// в unpackaged-приложении.
    /// </summary>
    private void SetIcons()
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        AppWindow.SetIcon(Path.Combine(assets, "GameLauncher.ico"));
        TitleBarIcon.Source = new BitmapImage(new Uri(Path.Combine(assets, "TitleBarIcon.png")));
    }

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

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_shown)
        {
            SavePlacement();
        }

        if (!_exiting && ViewModel.Settings.CloseToTray && CanHideToTray())
        {
            // Крестик — в трей: окно прячется, лаунчер и учёт времени продолжают работать.
            args.Cancel = true;
            AppWindow.Hide();
            HiddenToTray?.Invoke(this, EventArgs.Empty);
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
        if (_savingDisabled)
        {
            return;
        }

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

    /// <summary>Карточка попала в видимую область GridView — подгружаем её картинки.</summary>
    private async void OnGameContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is GameItemViewModel item)
        {
            await ViewModel.EnsureImagesLoadedAsync(item);
        }
    }

    private async void OnTopGameClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TopGameItem item)
        {
            await ViewModel.OpenFromStatsAsync(item);
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
