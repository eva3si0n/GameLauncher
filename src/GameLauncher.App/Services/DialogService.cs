using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? textBox.Text : null;
    }

    public async Task<bool> ConfirmDeleteAsync(string gameName)
    {
        var dialog = CreateDialog($"Удалить «{gameName}» из библиотеки?");
        dialog.Content = "Файлы игры на диске не будут затронуты.";
        dialog.PrimaryButtonText = "Удалить";
        dialog.CloseButtonText = "Отмена";
        dialog.DefaultButton = ContentDialogButton.Close;

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = CreateDialog(title);
        dialog.Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        dialog.CloseButtonText = "OK";
        await dialog.ShowAsync();
    }

    private ContentDialog CreateDialog(string title) => new()
    {
        Title = title,
        XamlRoot = window.Content.XamlRoot,
        Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
    };
}
