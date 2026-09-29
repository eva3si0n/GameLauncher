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

/// <summary>
/// Главное окно: навигация (библиотека / страница игры / настройки), поиск и действия с играми.
/// Логика обложек, описаний и удаления — в сервисах Core; здесь только диалоги и состояние экрана.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    // ERROR_CANCELLED: пользователь отказался в окне UAC.
    private const int ErrorCancelled = 1223;

    private readonly GameLibrary _library;
    private readonly IDialogService _dialogs;
    private readonly PlayTimeMonitor _playTime;
    private readonly ArtworkCache _artwork;
    private readonly SteamGridDbClient _steamGridDb;
    private readonly CoverService _covers;
    private readonly DetailsService _details;
    private readonly GameRemover _remover;
    private readonly string? _corruptBackupPath;
    private GameItemViewModel? _selectedGame;
    private bool _isSettingsOpen;
    private string _searchText = "";

    public MainViewModel(
        GameLibrary library,
        IDialogService dialogs,
        PlayTimeMonitor playTime,
        ArtworkCache artwork,
        SteamGridDbClient steamGridDb,
        CoverService covers,
        DetailsService details,
        GameRemover remover,
        SettingsViewModel settings,
        string? corruptBackupPath)
    {
        _library = library;
        _dialogs = dialogs;
        _playTime = playTime;
        _artwork = artwork;
        _steamGridDb = steamGridDb;
        _covers = covers;
        _details = details;
        _remover = remover;
        _corruptBackupPath = corruptBackupPath;
        Settings = settings;
        _playTime.Changed += (_, _) => RefreshPlayTime();

        Games = new ObservableCollection<GameItemViewModel>(library.Games.Select(CreateItem));
        VisibleGames = new ObservableCollection<GameItemViewModel>(LibrarySort.Sort(Games, g => g.Game, settings.LibrarySort));
        Games.CollectionChanged += (_, _) =>
        {
            RebuildVisibleGames();
            OnPropertyChanged(nameof(IsEmpty));
        };

        AddGameCommand = new AsyncRelayCommand(AddGameAsync);
        CloseDetailsCommand = new RelayCommand(CloseDetails);
        OpenSettingsCommand = new RelayCommand(() => IsSettingsOpen = true);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
    }

    public SettingsViewModel Settings { get; }

    public ObservableCollection<GameItemViewModel> Games { get; }

    public bool IsEmpty => Games.Count == 0;

    public IAsyncRelayCommand AddGameCommand { get; }

    // ---------- Навигация: библиотека / страница игры / настройки ----------

    public bool IsLibraryVisible => _selectedGame is null && !_isSettingsOpen;

    public bool IsDetailsOpen => _selectedGame is not null && !_isSettingsOpen;

    /// <summary>Подсказка «Библиотека пуста» — только на экране библиотеки.</summary>
    public bool IsEmptyLibraryVisible => IsEmpty && IsLibraryVisible;

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

    public IRelayCommand OpenSettingsCommand { get; }

    public IRelayCommand CloseSettingsCommand { get; }

    public IRelayCommand CloseDetailsCommand { get; }

    public async Task OpenDetailsAsync(GameItemViewModel item)
    {
        item.Details = _details.Load(item.Id);
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

    private void NotifyNavigation()
    {
        OnPropertyChanged(nameof(IsLibraryVisible));
        OnPropertyChanged(nameof(IsDetailsOpen));
        OnPropertyChanged(nameof(IsEmptyLibraryVisible));
        OnPropertyChanged(nameof(IsNoSearchResultsVisible));
    }

    // ---------- Поиск ----------

    /// <summary>Игры, подходящие под поиск, в выбранном порядке.</summary>
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

    /// <summary>Индекс в списке сортировки: «Недавние / По времени в игре / По названию / По дате добавления».</summary>
    public int SortIndex
    {
        get => (int)Settings.LibrarySort;
        set
        {
            if (value < 0 || value == SortIndex || !Enum.IsDefined((LibrarySortMode)value))
            {
                return;
            }

            Settings.LibrarySort = (LibrarySortMode)value;
            OnPropertyChanged();
            RebuildVisibleGames();
        }
    }

    private void RebuildVisibleGames()
    {
        var matching = LibrarySort.Sort(Games.Where(g => LibrarySearch.Matches(g.Name, _searchText)), g => g.Game, Settings.LibrarySort).ToList();
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

    // ---------- Запуск окна ----------

    /// <summary>Вызывается, когда окно готово показывать диалоги.</summary>
    public async Task OnLoadedAsync()
    {
        if (_corruptBackupPath is not null)
        {
            await _dialogs.ShowMessageAsync(
                "Файл библиотеки повреждён",
                $"Не удалось прочитать библиотеку, начата новая. Старый файл сохранён здесь:\n{_corruptBackupPath}");
        }

        // Картинки карточек грузятся при появлении карточки на экране (EnsureImagesLoaded).
    }

    /// <summary>Карточка появилась на экране — загрузить её картинки, если ещё не загружены.</summary>
    public Task EnsureImagesLoadedAsync(GameItemViewModel item) => item.EnsureImagesLoadedAsync(_artwork);

    private void RefreshPlayTime()
    {
        foreach (var item in Games)
        {
            item.State = _playTime.GetState(item.Id);
            item.Refresh();
        }

        // Время и дата последнего запуска изменились — порядок «Недавние» и «По времени» мог сдвинуться.
        if (Settings.LibrarySort is LibrarySortMode.LastPlayed or LibrarySortMode.PlayTime)
        {
            RebuildVisibleGames();
        }
    }

    private GameItemViewModel CreateItem(Game game) =>
        new(game, PlayAsync, RenameAsync, DeleteAsync, FindCoverAsync, RemoveCoverAsync, FetchDetailsAsync);

    private static string? DescribeOnlineError(Exception ex) =>
        ex is SteamGridDbException or SteamStoreException ? ex.Message : null;

    // ---------- Действия с игрой ----------

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
        RebuildVisibleGames(); // новое название может подходить или не подходить под текущий поиск
    }

    private async Task DeleteAsync(GameItemViewModel item)
    {
        if (!await _dialogs.ConfirmDeleteAsync(item.Name))
        {
            return;
        }

        try
        {
            _remover.Remove(item.Id);
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
    }

    // ---------- Обложки ----------

    private async Task FindCoverAsync(GameItemViewModel item)
    {
        if (Settings.ApiKey is null && !await Settings.EditApiKeyAsync())
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

        CoverResult result;
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

            result = await _covers.ApplyAsync(item.Id, sgdbGame.Id, grid);
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

        if (result.Details is not null)
        {
            item.Details = result.Details;
        }

        await item.LoadImagesAsync(_artwork);
    }

    private async Task RemoveCoverAsync(GameItemViewModel item)
    {
        try
        {
            _covers.Remove(item.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Не удалось убрать обложку", ex.Message);
        }

        await item.LoadImagesAsync(_artwork);
    }

    // ---------- Описания из Steam ----------

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
                query => _details.SearchAsync(query),
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
            if (await _details.DownloadAsync(item.Id, appId.Value) is { } details)
            {
                item.Details = details;
            }
            else
            {
                await _dialogs.ShowMessageAsync(
                    "Описание не найдено",
                    $"Steam не отдал данных по игре с AppID {appId} (проверены основная витрина из настроек, США и Россия). "
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
}
