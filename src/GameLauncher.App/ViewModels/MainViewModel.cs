using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLauncher.App.Services;
using GameLauncher.Core.Library;

namespace GameLauncher.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    // ERROR_CANCELLED: пользователь отказался в окне UAC.
    private const int ErrorCancelled = 1223;

    private readonly GameLibrary _library;
    private readonly IDialogService _dialogs;
    private readonly string? _corruptBackupPath;

    public MainViewModel(GameLibrary library, IDialogService dialogs, string? corruptBackupPath)
    {
        _library = library;
        _dialogs = dialogs;
        _corruptBackupPath = corruptBackupPath;

        Games = new ObservableCollection<GameItemViewModel>(library.Games.Select(CreateItem));
        Games.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        AddGameCommand = new AsyncRelayCommand(AddGameAsync);
    }

    public ObservableCollection<GameItemViewModel> Games { get; }

    public bool IsEmpty => Games.Count == 0;

    public IAsyncRelayCommand AddGameCommand { get; }

    /// <summary>Вызывается, когда окно готово показывать диалоги.</summary>
    public async Task OnLoadedAsync()
    {
        if (_corruptBackupPath is not null)
        {
            await _dialogs.ShowMessageAsync(
                "Файл библиотеки повреждён",
                $"Не удалось прочитать библиотеку, начата новая. Старый файл сохранён здесь:\n{_corruptBackupPath}");
        }
    }

    private GameItemViewModel CreateItem(Game game) => new(game, PlayAsync, RenameAsync, DeleteAsync);

    private async Task AddGameAsync()
    {
        var path = await _dialogs.PickExeAsync();
        if (path is null)
        {
            return;
        }

        Game game;
        bool added;
        try
        {
            game = _library.Add(path, out added);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await _dialogs.ShowMessageAsync("Не удалось добавить игру", ex.Message);
            return;
        }

        if (!added)
        {
            await _dialogs.ShowMessageAsync("Игра уже в библиотеке", $"«{game.Name}» уже добавлена:\n{game.ExePath}");
            return;
        }

        Games.Add(CreateItem(game));
    }

    private async Task PlayAsync(GameItemViewModel item)
    {
        try
        {
            Process.Start(GameLaunch.CreateStartInfo(item.Game))?.Dispose();
        }
        catch (FileNotFoundException)
        {
            await _dialogs.ShowMessageAsync(
                "Файл игры не найден",
                $"Игра могла быть перемещена или удалена:\n{item.ExePath}");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // Пользователь отменил запрос прав администратора — это не ошибка.
        }
        catch (Win32Exception ex)
        {
            await _dialogs.ShowMessageAsync("Не удалось запустить игру", ex.Message);
        }
    }

    private async Task RenameAsync(GameItemViewModel item)
    {
        var newName = await _dialogs.PromptRenameAsync(item.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == item.Name)
        {
            return;
        }

        try
        {
            _library.Rename(item.Id, newName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Не удалось переименовать игру", ex.Message);
            return;
        }

        item.Refresh();
    }

    private async Task DeleteAsync(GameItemViewModel item)
    {
        if (!await _dialogs.ConfirmDeleteAsync(item.Name))
        {
            return;
        }

        try
        {
            _library.Remove(item.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Не удалось удалить игру", ex.Message);
            return;
        }

        Games.Remove(item);
    }
}
