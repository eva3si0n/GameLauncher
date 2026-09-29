using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLauncher.App.Services;
using GameLauncher.Core;
using GameLauncher.Core.Artwork;
using GameLauncher.Core.GameInfo;
using GameLauncher.Core.Library;
using GameLauncher.Core.Settings;

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
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private string? _apiKey;
    private GameItemViewModel? _selectedGame;
    private bool _isSettingsOpen;
    private string _searchText = "";

    public MainViewModel(
        GameLibrary library,
        IDialogService dialogs,
        PlayTimeMonitor playTime,
        ArtworkCache artwork,
        ISecretStore keyStore,
        GameDetailsStore detailsStore,
        SettingsStore settingsStore,
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
        _settingsStore = settingsStore;
        _settings = settingsStore.Load();
        _steamStore = new SteamStoreClient(http, primaryRegion: () => _settings.SteamRegion);
        _detailsStore = detailsStore;
        _playTime.Changed += (_, _) => RefreshPlayTime();

        Games = new ObservableCollection<GameItemViewModel>(library.Games.Select(CreateItem));
        VisibleGames = new ObservableCollection<GameItemViewModel>(Games);
        Games.CollectionChanged += (_, _) =>
        {
            RebuildVisibleGames();
            OnPropertyChanged(nameof(IsEmpty));
        };
        AddGameCommand = new AsyncRelayCommand(AddGameAsync);
        EditApiKeyCommand = new AsyncRelayCommand(EditApiKeyAsync);
        CloseDetailsCommand = new RelayCommand(CloseDetails);
        OpenSettingsCommand = new RelayCommand(() => IsSettingsOpen = true);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
    }

    /// <summary>Тема изменилась в настройках — окно применяет её.</summary>
    public event EventHandler? ThemeChanged;

    // ---------- Навигация: библиотека / страница игры / настройки ----------

    public bool IsLibraryVisible => _selectedGame is null && !_isSettingsOpen;

    public bool IsDetailsOpen => _selectedGame is not null && !_isSettingsOpen;

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set
        {
            if (SetProperty(ref _isSettingsOpen, value))
            {
                NotifyNavigation();
            }
        }
    }

    public IRelayCommand OpenSettingsCommand { get; }

    public IRelayCommand CloseSettingsCommand { get; }

    private void NotifyNavigation()
    {
        OnPropertyChanged(nameof(IsLibraryVisible));
        OnPropertyChanged(nameof(IsDetailsOpen));
        OnPropertyChanged(nameof(IsEmptyLibraryVisible));
        OnPropertyChanged(nameof(IsNoSearchResultsVisible));
    }

    // ---------- Поиск ----------

    /// <summary>Игры, подходящие под поиск, в порядке библиотеки.</summary>
    public ObservableCollection<GameItemViewModel> VisibleGames { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? ""))
            {
                RebuildVisibleGames();
            }
        }
    }

    public bool IsNoSearchResultsVisible => IsLibraryVisible && Games.Count > 0 && VisibleGames.Count == 0;

    private void RebuildVisibleGames()
    {
        var matching = Games.Where(g => LibrarySearch.Matches(g.Name, _searchText)).ToList();
        if (!matching.SequenceEqual(VisibleGames))
        {
            VisibleGames.Clear();
            foreach (var game in matching)
            {
                VisibleGames.Add(game);
            }
        }

        OnPropertyChanged(nameof(IsEmptyLibraryVisible));
        OnPropertyChanged(nameof(IsNoSearchResultsVisible));
    }

    // ---------- Настройки ----------

    public AppTheme Theme => _settings.Theme;

    /// <summary>Индекс в списке «Как в системе / Светлая / Тёмная».</summary>
    public int ThemeIndex
    {
        get => (int)_settings.Theme;
        set
        {
            if (value < 0 || value == (int)_settings.Theme || !Enum.IsDefined((AppTheme)value))
            {
                return;
            }

            _settings.Theme = (AppTheme)value;
            SaveSettings();
            OnPropertyChanged();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Витрины Steam для описаний: код страны и название.</summary>
    public static IReadOnlyList<(string Code, string Name)> SteamRegions { get; } =
    [
        ("tr", "Турция"),
        ("us", "США"),
        ("ru", "Россия"),
        ("kz", "Казахстан"),
        ("ua", "Украина"),
        ("de", "Германия"),
    ];

    public IReadOnlyList<string> SteamRegionNames { get; } = SteamRegions.Select(r => r.Name).ToList();

    public int SteamRegionIndex
    {
        get => Math.Max(0, SteamRegions.ToList().FindIndex(r => string.Equals(r.Code, _settings.SteamRegion, StringComparison.OrdinalIgnoreCase)));
        set
        {
            if (value < 0 || value >= SteamRegions.Count || SteamRegions[value].Code == _settings.SteamRegion)
            {
                return;
            }

            _settings.SteamRegion = SteamRegions[value].Code;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>Размер и положение окна с прошлого запуска; null — первый запуск.</summary>
    public WindowPlacement? SavedWindowPlacement => _settings.Window;

    public void SaveWindowPlacement(WindowPlacement placement)
    {
        _settings.Window = placement;
        SaveSettings();
    }

    public string ApiKeyStatusText => _apiKey is null ? "Ключ не задан — поиск обложек недоступен." : "Ключ сохранён (зашифрован DPAPI).";

    public string DataDirectory => AppPaths.DataDirectory;

    public string AppVersion =>
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "—";

    public IRelayCommand OpenDataFolderCommand { get; }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Настройка применится до перезапуска; сообщать о каждом сбое записи незачем.
        }
    }

    private void OpenDataFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppPaths.DataDirectory);
            Process.Start("explorer.exe", $"\"{AppPaths.DataDirectory}\"")?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Игра, открытая на отдельной странице; null — показана библиотека.</summary>
    public GameItemViewModel? SelectedGame
    {
        get => _selectedGame;
        private set
        {
            if (SetProperty(ref _selectedGame, value))
            {
                NotifyNavigation();
            }
        }
    }

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
    public bool IsEmptyLibraryVisible => IsEmpty && IsLibraryVisible;

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
                await _dialogs.ShowMessageAsync(
                    "Описание не найдено",
                    $"Steam не отдал данных по игре с AppID {appId} (проверены витрины Турции, США и России). "
                    + "Возможно, игра снята с продажи или ещё не вышла. Попробуйте выбрать другую.");
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

        OnPropertyChanged(nameof(ApiKeyStatusText));

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
