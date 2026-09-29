using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLauncher.App.Services;
using GameLauncher.Core.Artwork;
using GameLauncher.Core.GameInfo;
using GameLauncher.Core.Library;

namespace GameLauncher.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    // ERROR_CANCELLED: пользователь отказался в окне UAC.
    private const int ErrorCancelled = 1223;

    private readonly GameLibrary _library;
    private readonly IDialogService _dialogs;
    private readonly string? _corruptBackupPath;
    private readonly PlayTimeMonitor _playTime;
    private readonly ArtworkCache _artwork;
    private readonly ISecretStore _keyStore;
    private readonly HttpClient _http;
    private readonly SteamGridDbClient _steamGridDb;
    private readonly SteamStoreClient _steamStore;
    private readonly GameDetailsStore _detailsStore;
    private string? _apiKey;
    private GameItemViewModel? _selectedGame;

    public MainViewModel(
        GameLibrary library,
        IDialogService dialogs,
        PlayTimeMonitor playTime,
        ArtworkCache artwork,
        ISecretStore keyStore,
        GameDetailsStore detailsStore,
        HttpClient http,
        string? corruptBackupPath)
    {
        _library = library;
        _dialogs = dialogs;
        _playTime = playTime;
        _artwork = artwork;
        _keyStore = keyStore;
        _http = http;
        _corruptBackupPath = corruptBackupPath;
        _apiKey = keyStore.Load();
        _steamGridDb = new SteamGridDbClient(http, () => _apiKey);
        _steamStore = new SteamStoreClient(http);
        _detailsStore = detailsStore;
        _playTime.Changed += (_, _) => RefreshPlayTime();

        Games = new ObservableCollection<GameItemViewModel>(library.Games.Select(CreateItem));
        Games.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsEmptyLibraryVisible));
        };
        AddGameCommand = new AsyncRelayCommand(AddGameAsync);
        EditApiKeyCommand = new AsyncRelayCommand(EditApiKeyAsync);
        CloseDetailsCommand = new RelayCommand(CloseDetails);
    }

    /// <summary>Игра, открытая на отдельной странице; null — показана библиотека.</summary>
    public GameItemViewModel? SelectedGame
    {
        get => _selectedGame;
        private set
        {
            if (SetProperty(ref _selectedGame, value))
            {
                OnPropertyChanged(nameof(IsDetailsOpen));
                OnPropertyChanged(nameof(IsLibraryVisible));
                OnPropertyChanged(nameof(IsEmptyLibraryVisible));
            }
        }
    }

    public bool IsDetailsOpen => _selectedGame is not null;

    public bool IsLibraryVisible => _selectedGame is null;

    public IRelayCommand CloseDetailsCommand { get; }

    public async Task OpenDetailsAsync(GameItemViewModel item)
    {
        item.Details = _detailsStore.Load(item.Id);
        SelectedGame = item;
        await item.LoadHeroAsync(_artwork);
    }

    public Task ShowScreenshotAsync(ScreenshotViewModel screenshot) =>
        SelectedGame is { } game
            ? _dialogs.ShowScreenshotsAsync(game.Screenshots.Select(s => s.Screenshot.Full).ToList(), screenshot.Index)
            : Task.CompletedTask;

    private void CloseDetails()
    {
        SelectedGame?.UnloadHero();
        SelectedGame = null;
    }

    public IAsyncRelayCommand EditApiKeyCommand { get; }

    public ObservableCollection<GameItemViewModel> Games { get; }

    public bool IsEmpty => Games.Count == 0;

    /// <summary>Подсказка «Библиотека пуста» — только на экране библиотеки.</summary>
    public bool IsEmptyLibraryVisible => IsEmpty && _selectedGame is null;

    public IAsyncRelayCommand AddGameCommand { get; }

    /// <summary>Вызывается, когда окно готово показывать диалоги.</summary>
    public async Task OnLoadedAsync()
    {
        foreach (var item in Games.ToList())
        {
            await item.LoadImagesAsync(_artwork);
        }

        if (_corruptBackupPath is not null)
        {
            await _dialogs.ShowMessageAsync(
                "Файл библиотеки повреждён",
                $"Не удалось прочитать библиотеку, начата новая. Старый файл сохранён здесь:\n{_corruptBackupPath}");
        }
    }

    private void RefreshPlayTime()
    {
        foreach (var item in Games)
        {
            item.State = _playTime.GetState(item.Id);
            item.Refresh();
        }
    }

    private GameItemViewModel CreateItem(Game game) =>
        new(game, PlayAsync, RenameAsync, DeleteAsync, FindCoverAsync, RemoveCoverAsync, FetchDetailsAsync);

    private static string? DescribeOnlineError(Exception ex) =>
        ex is SteamGridDbException or SteamStoreException ? ex.Message : null;

    /// <summary>
    /// Загрузить описание из Steam. <paramref name="search"/> = true — выбрать игру в Steam вручную,
    /// иначе — обновить по уже известному AppID.
    /// </summary>
    private async Task FetchDetailsAsync(GameItemViewModel item, bool search)
    {
        var appId = item.Game.SteamAppId;
        if (search || appId is null)
        {
            var app = await _dialogs.PickFromSearchAsync(
                "Описание из Steam: выберите игру",
                item.Name,
                query => _steamStore.SearchAsync(query),
                a => a.Name,
                DescribeOnlineError);
            if (app is null)
            {
                return;
            }

            appId = app.AppId;
        }

        try
        {
            if (!await TryLoadDetailsAsync(item, appId.Value))
            {
                await _dialogs.ShowMessageAsync("Описание не найдено", "Steam не отдал данных по этой игре. Попробуйте выбрать другую.");
            }
        }
        catch (SteamStoreException ex)
        {
            await _dialogs.ShowMessageAsync("Steam", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Не удалось сохранить описание", ex.Message);
        }
    }

    /// <summary>Скачивает описание по AppID и сохраняет. False — Steam не отдал данных.</summary>
    private async Task<bool> TryLoadDetailsAsync(GameItemViewModel item, int appId)
    {
        var details = await _steamStore.GetDetailsAsync(appId);
        if (details is null)
        {
            return false;
        }

        _detailsStore.Save(item.Id, details);
        _library.SetSteamAppId(item.Id, appId);
        item.Details = details;
        return true;
    }

    /// <summary>Возвращает true, если после диалога ключ есть.</summary>
    private async Task<bool> EditApiKeyAsync()
    {
        var result = await _dialogs.EditApiKeyAsync(
            hasKey: _apiKey is not null,
            validate: async key =>
            {
                try
                {
                    // Любой поиск проверяет ключ: неверный отклоняется с 401.
                    await new SteamGridDbClient(_http, () => key).SearchGamesAsync("portal");
                    return null;
                }
                catch (SteamGridDbException ex)
                {
                    return ex.Message;
                }
            });

        try
        {
            switch (result.Action)
            {
                case ApiKeyDialogAction.Save:
                    _keyStore.Save(result.Key);
                    _apiKey = result.Key;
                    break;
                case ApiKeyDialogAction.Remove:
                    _keyStore.Save(null);
                    _apiKey = null;
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            await _dialogs.ShowMessageAsync("Не удалось сохранить ключ", ex.Message);
        }

        return _apiKey is not null;
    }

    private async Task FindCoverAsync(GameItemViewModel item)
    {
        if (_apiKey is null && !await EditApiKeyAsync())
        {
            return;
        }

        var sgdbGame = await _dialogs.PickFromSearchAsync(
            "Найти обложку: выберите игру",
            item.Name,
            query => _steamGridDb.SearchGamesAsync(query),
            g => g.Verified ? $"{g.DisplayName}  ✓" : g.DisplayName,
            DescribeOnlineError);
        if (sgdbGame is null)
        {
            return;
        }

        try
        {
            var grids = await _steamGridDb.GetGridsAsync(sgdbGame.Id);
            if (grids.Count == 0)
            {
                await _dialogs.ShowMessageAsync("Обложек нет", $"Для «{sgdbGame.Name}» в SteamGridDB нет вертикальных обложек.");
                return;
            }

            var grid = await _dialogs.PickImageAsync($"Обложка: {sgdbGame.Name}", grids.Take(30).ToList());
            if (grid is null)
            {
                return;
            }

            var gridFile = await _artwork.DownloadAsync(item.Id, ArtworkKind.Grid, grid.Url);

            // Баннер берём лучший по рейтингу; если его нет — оставляем без баннера.
            string? heroFile = null;
            var heroes = await _steamGridDb.GetHeroesAsync(sgdbGame.Id);
            if (heroes.Count > 0)
            {
                heroFile = await _artwork.DownloadAsync(item.Id, ArtworkKind.Hero, heroes[0].Url);
            }
            else
            {
                _artwork.Delete(item.Id, ArtworkKind.Hero);
            }

            _library.SetArtwork(item.Id, gridFile, heroFile);
        }
        catch (SteamGridDbException ex)
        {
            await _dialogs.ShowMessageAsync("SteamGridDB", ex.Message);
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            await _dialogs.ShowMessageAsync("Не удалось сохранить обложку", ex.Message);
            return;
        }

        await item.LoadImagesAsync(_artwork);

        // Заодно подтягиваем описание из Steam, если его ещё нет. Это необязательно: ошибки не показываем,
        // описание всегда можно загрузить вручную на странице игры.
        if (item.Game.SteamAppId is null)
        {
            try
            {
                if (await _steamGridDb.GetSteamAppIdAsync(sgdbGame.Id) is { } appId)
                {
                    await TryLoadDetailsAsync(item, appId);
                }
            }
            catch (Exception ex) when (ex is SteamGridDbException or SteamStoreException or IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private async Task RemoveCoverAsync(GameItemViewModel item)
    {
        try
        {
            _library.SetArtwork(item.Id, null, null);
            _artwork.Delete(item.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Не удалось убрать обложку", ex.Message);
        }

        await item.LoadImagesAsync(_artwork);
    }

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

        var item = CreateItem(game);
        Games.Add(item);
        await item.LoadImagesAsync(_artwork);
    }

    private async Task PlayAsync(GameItemViewModel item)
    {
        try
        {
            Process.Start(GameLaunch.CreateStartInfo(item.Game))?.Dispose();
            _playTime.OnLaunched(item.Game);
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

        if (SelectedGame == item)
        {
            CloseDetails();
        }

        Games.Remove(item);
        try
        {
            _artwork.Delete(item.Id);
            _detailsStore.Delete(item.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Картинки и описания в кэше не мешают работе — не беспокоим пользователя.
        }
    }
}
