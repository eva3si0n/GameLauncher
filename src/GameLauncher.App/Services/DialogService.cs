using GameLauncher.Core.Artwork;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GameLauncher.App.Services;

public sealed class DialogService(Window window) : IDialogService
{
    public async Task<string?> PickExeAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".exe");
        // Unpackaged-приложению нужно явно привязать пикер к окну.
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> PromptRenameAsync(string currentName)
    {
        var textBox = new TextBox { Text = currentName, SelectionStart = currentName.Length };
        var dialog = CreateDialog("Переименовать игру");
        dialog.Content = textBox;
        dialog.PrimaryButtonText = "Сохранить";
        dialog.CloseButtonText = "Отмена";
        dialog.DefaultButton = ContentDialogButton.Primary;
        textBox.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(textBox.Text);
        textBox.Loaded += (_, _) => textBox.Focus(FocusState.Programmatic);

        return await ShowQueuedAsync(dialog) == ContentDialogResult.Primary ? textBox.Text : null;
    }

    public async Task<bool> ConfirmDeleteAsync(string gameName)
    {
        var dialog = CreateDialog($"Удалить «{gameName}» из библиотеки?");
        dialog.Content = "Файлы игры на диске не будут затронуты.";
        dialog.PrimaryButtonText = "Удалить";
        dialog.CloseButtonText = "Отмена";
        dialog.DefaultButton = ContentDialogButton.Close;

        return await ShowQueuedAsync(dialog) == ContentDialogResult.Primary;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = CreateDialog(title);
        dialog.Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        dialog.CloseButtonText = "OK";
        await ShowQueuedAsync(dialog);
    }

    public async Task<ApiKeyDialogResult> EditApiKeyAsync(bool hasKey, Func<string, Task<string?>> validate)
    {
        var keyBox = new PasswordBox { PlaceholderText = hasKey ? "Ключ сохранён — введите новый, чтобы заменить" : "API-ключ" };
        var error = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var progress = new ProgressRing { IsActive = false, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Spacing = 12, MinWidth = 380 };
        panel.Children.Add(new TextBlock
        {
            Text = "Ключ нужен для поиска обложек. Он хранится на этом компьютере в зашифрованном виде (DPAPI) и никуда, кроме SteamGridDB, не отправляется.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new HyperlinkButton
        {
            Content = "Получить ключ на steamgriddb.com",
            NavigateUri = new Uri("https://www.steamgriddb.com/profile/preferences/api"),
            Padding = new Thickness(0),
        });
        panel.Children.Add(keyBox);
        panel.Children.Add(progress);
        panel.Children.Add(error);

        var dialog = CreateDialog("API-ключ SteamGridDB");
        dialog.Content = panel;
        dialog.PrimaryButtonText = "Сохранить";
        dialog.CloseButtonText = "Отмена";
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.IsPrimaryButtonEnabled = false;
        if (hasKey)
        {
            dialog.SecondaryButtonText = "Удалить ключ";
        }

        keyBox.PasswordChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(keyBox.Password);

        // Проверяем ключ запросом к SteamGridDB, не закрывая диалог.
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                error.Visibility = Visibility.Collapsed;
                progress.IsActive = true;
                dialog.IsPrimaryButtonEnabled = false;
                var message = await validate(keyBox.Password.Trim());
                if (message is not null)
                {
                    error.Text = message;
                    error.Visibility = Visibility.Visible;
                    args.Cancel = true;
                }
            }
            finally
            {
                progress.IsActive = false;
                dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(keyBox.Password);
                deferral.Complete();
            }
        };

        return await ShowQueuedAsync(dialog) switch
        {
            ContentDialogResult.Primary => new ApiKeyDialogResult(ApiKeyDialogAction.Save, keyBox.Password.Trim()),
            ContentDialogResult.Secondary => new ApiKeyDialogResult(ApiKeyDialogAction.Remove),
            _ => new ApiKeyDialogResult(ApiKeyDialogAction.Cancel),
        };
    }

    public async Task<T?> PickFromSearchAsync<T>(
        string title,
        string initialQuery,
        Func<string, Task<IReadOnlyList<T>>> search,
        Func<T, string> display,
        Func<Exception, string?> describeError)
        where T : class
    {
        IReadOnlyList<T> results = [];
        var queryBox = new TextBox { Text = initialQuery, PlaceholderText = "Название игры" };
        var searchButton = new Button { Content = "Найти" };
        var list = new ListView { Height = 300, SelectionMode = ListViewSelectionMode.Single };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        var progress = new ProgressRing { IsActive = false, Width = 20, Height = 20 };

        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(searchButton, 1);
        Grid.SetColumn(progress, 2);
        searchRow.Children.Add(queryBox);
        searchRow.Children.Add(searchButton);
        searchRow.Children.Add(progress);

        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };
        panel.Children.Add(searchRow);
        panel.Children.Add(status);
        panel.Children.Add(list);

        var dialog = CreateDialog(title);
        dialog.Content = panel;
        dialog.PrimaryButtonText = "Далее";
        dialog.CloseButtonText = "Отмена";
        // Без кнопки по умолчанию: Enter в поле поиска запускает поиск, а не «Далее».
        dialog.DefaultButton = ContentDialogButton.None;
        dialog.IsPrimaryButtonEnabled = false;
        list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedIndex >= 0;

        async Task RunSearchAsync()
        {
            if (string.IsNullOrWhiteSpace(queryBox.Text))
            {
                return;
            }

            searchButton.IsEnabled = false;
            progress.IsActive = true;
            status.Text = string.Empty;
            list.Items.Clear();
            try
            {
                results = await search(queryBox.Text);
                foreach (var result in results)
                {
                    list.Items.Add(display(result));
                }

                status.Text = results.Count == 0 ? "Ничего не найдено. Попробуйте другое название, например на английском." : string.Empty;
                if (results.Count > 0)
                {
                    list.SelectedIndex = 0;
                }
            }
            catch (Exception ex) when (describeError(ex) is { } message)
            {
                status.Text = message;
            }
            finally
            {
                searchButton.IsEnabled = true;
                progress.IsActive = false;
            }
        }

        searchButton.Click += async (_, _) => await RunSearchAsync();
        queryBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await RunSearchAsync();
            }
        };
        dialog.Opened += async (_, _) => await RunSearchAsync();

        return await ShowQueuedAsync(dialog) == ContentDialogResult.Primary && list.SelectedIndex >= 0
            ? results[list.SelectedIndex]
            : null;
    }

    public async Task<SteamGridDbImage?> PickImageAsync(string title, IReadOnlyList<SteamGridDbImage> images)
    {
        var grid = new GridView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 460 };
        foreach (var image in images)
        {
            grid.Items.Add(new Image
            {
                Width = 120,
                Height = 180,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                Source = new BitmapImage(image.Thumb) { DecodePixelWidth = 240 },
            });
        }

        var dialog = CreateDialog(title);
        dialog.Content = grid;
        dialog.PrimaryButtonText = "Выбрать";
        dialog.CloseButtonText = "Отмена";
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.IsPrimaryButtonEnabled = images.Count > 0;
        if (images.Count > 0)
        {
            grid.SelectedIndex = 0;
        }

        grid.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = grid.SelectedIndex >= 0;
        grid.DoubleTapped += (_, _) =>
        {
            if (grid.SelectedIndex >= 0)
            {
                // Hide() даёт результат None — отмечаем выбор отдельно.
                _pickedByDoubleTap = true;
                dialog.Hide();
            }
        };

        _pickedByDoubleTap = false;
        var result = await ShowQueuedAsync(dialog);
        return (result == ContentDialogResult.Primary || _pickedByDoubleTap) && grid.SelectedIndex >= 0
            ? images[grid.SelectedIndex]
            : null;
    }

    public async Task ShowScreenshotsAsync(IReadOnlyList<Uri> images, int startIndex)
    {
        // Размер просмотра — по размеру окна, с полями.
        var size = window.Content.XamlRoot.Size;
        var width = Math.Max(480, size.Width * 0.85);
        var height = Math.Max(270, Math.Min(width * 9 / 16, size.Height * 0.7));

        var flip = new FlipView { Width = width, Height = height };
        foreach (var uri in images)
        {
            flip.Items.Add(new Image
            {
                Source = new BitmapImage(uri),
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            });
        }

        flip.SelectedIndex = Math.Clamp(startIndex, 0, Math.Max(0, images.Count - 1));

        var counter = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center };
        void UpdateCounter() => counter.Text = $"{flip.SelectedIndex + 1} из {images.Count}";
        flip.SelectionChanged += (_, _) => UpdateCounter();
        UpdateCounter();

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(flip);
        panel.Children.Add(counter);

        var dialog = CreateDialog("Скриншоты");
        // Стандартная ширина ContentDialog слишком мала для скриншотов.
        dialog.Resources["ContentDialogMaxWidth"] = width + 100;
        dialog.Resources["ContentDialogMaxHeight"] = height + 250;
        dialog.Content = panel;
        dialog.CloseButtonText = "Закрыть";
        dialog.DefaultButton = ContentDialogButton.Close;
        await ShowQueuedAsync(dialog);
    }

    private bool _pickedByDoubleTap;

    /// <summary>
    /// WinUI разрешает только один открытый ContentDialog — второй бросает исключение и роняет приложение.
    /// Поэтому диалоги показываются по очереди.
    /// </summary>
    private readonly SemaphoreSlim _dialogQueue = new(1, 1);

    private async Task<ContentDialogResult> ShowQueuedAsync(ContentDialog dialog)
    {
        await _dialogQueue.WaitAsync();
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            _dialogQueue.Release();
        }
    }

    private ContentDialog CreateDialog(string title) => new()
    {
        Title = title,
        XamlRoot = window.Content.XamlRoot,
        // Диалог живёт во всплывающем слое и не наследует тему окна — задаём явно.
        RequestedTheme = ((FrameworkElement)window.Content).ActualTheme,
        Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
    };
}
