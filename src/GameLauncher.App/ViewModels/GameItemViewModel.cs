using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLauncher.App.Services;
using GameLauncher.Core.Artwork;
using GameLauncher.Core.GameInfo;
using GameLauncher.Core.Library;
using GameLauncher.Core.PlayTime;
using Microsoft.UI.Xaml.Media;

namespace GameLauncher.App.ViewModels;

/// <summary>Карточка игры в сетке.</summary>
public sealed class GameItemViewModel : ObservableObject
{
    private readonly Game _game;
    private PlaySessionState _state;
    private ImageSource? _cover;
    private ImageSource? _icon;
    private ImageSource? _hero;
    private GameDetails? _details;
    private IReadOnlyList<ScreenshotViewModel> _screenshots = [];

    public GameItemViewModel(
        Game game,
        Func<GameItemViewModel, Task> play,
        Func<GameItemViewModel, Task> rename,
        Func<GameItemViewModel, Task> delete,
        Func<GameItemViewModel, Task> findCover,
        Func<GameItemViewModel, Task> removeCover,
        Func<GameItemViewModel, bool, Task> fetchDetails)
    {
        _game = game;
        PlayCommand = new AsyncRelayCommand(() => play(this));
        RenameCommand = new AsyncRelayCommand(() => rename(this));
        DeleteCommand = new AsyncRelayCommand(() => delete(this));
        FindCoverCommand = new AsyncRelayCommand(() => findCover(this));
        RemoveCoverCommand = new AsyncRelayCommand(() => removeCover(this), () => _game.GridFile is not null);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        FindDetailsCommand = new AsyncRelayCommand(() => fetchDetails(this, true));
        RefreshDetailsCommand = new AsyncRelayCommand(() => fetchDetails(this, false), () => _game.SteamAppId is not null);
    }

    public Game Game => _game;

    public Guid Id => _game.Id;

    public string Name => _game.Name;

    public string ExePath => _game.ExePath;

    public string PlayTimeText => PlayTimeFormat.Format(_game.TotalPlayTime);

    public string LastPlayedText => PlayTimeFormat.FormatDate(_game.LastPlayedAt, TimeZoneInfo.Local);

    public string AddedText => PlayTimeFormat.FormatDate(_game.AddedAt, TimeZoneInfo.Local, "—");

    /// <summary>Широкий баннер для страницы игры. Загружается при открытии страницы.</summary>
    public ImageSource? Hero
    {
        get => _hero;
        private set
        {
            if (SetProperty(ref _hero, value))
            {
                OnPropertyChanged(nameof(HasHero));
                OnPropertyChanged(nameof(HasNoHero));
            }
        }
    }

    public bool HasHero => _hero is not null;

    public bool HasNoHero => _hero is null;

    public PlaySessionState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public string StatusText => _state switch
    {
        PlaySessionState.Starting => "Запуск…",
        PlaySessionState.Playing => "Идёт игра",
        _ => string.Empty,
    };

    public bool HasStatus => _state != PlaySessionState.None;

    /// <summary>Вертикальная обложка из SteamGridDB.</summary>
    public ImageSource? Cover
    {
        get => _cover;
        private set
        {
            if (SetProperty(ref _cover, value))
            {
                NotifyImageFlags();
            }
        }
    }

    /// <summary>Иконка из exe — когда обложки нет.</summary>
    public ImageSource? Icon
    {
        get => _icon;
        private set
        {
            if (SetProperty(ref _icon, value))
            {
                NotifyImageFlags();
            }
        }
    }

    public bool HasCover => _cover is not null;

    public bool ShowIcon => _cover is null && _icon is not null;

    public bool ShowPlaceholder => _cover is null && _icon is null;

    public IAsyncRelayCommand PlayCommand { get; }

    public IAsyncRelayCommand RenameCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    public IAsyncRelayCommand FindCoverCommand { get; }

    public IAsyncRelayCommand RemoveCoverCommand { get; }

    public IRelayCommand OpenFolderCommand { get; }

    /// <summary>Найти игру в Steam вручную и загрузить описание.</summary>
    public IAsyncRelayCommand FindDetailsCommand { get; }

    /// <summary>Перезагрузить описание по уже известному Steam AppID.</summary>
    public IAsyncRelayCommand RefreshDetailsCommand { get; }

    /// <summary>Описание из Steam; null — не загружено.</summary>
    public GameDetails? Details
    {
        get => _details;
        set
        {
            if (SetProperty(ref _details, value))
            {
                _screenshots = value?.Screenshots.Select((s, i) => new ScreenshotViewModel(s, i)).ToList() ?? [];
                OnPropertyChanged(nameof(HasDetails));
                OnPropertyChanged(nameof(HasNoDetails));
                OnPropertyChanged(nameof(ShortDescription));
                OnPropertyChanged(nameof(About));
                OnPropertyChanged(nameof(HasAbout));
                OnPropertyChanged(nameof(GenresText));
                OnPropertyChanged(nameof(DevelopersText));
                OnPropertyChanged(nameof(PublishersText));
                OnPropertyChanged(nameof(SteamReleaseDate));
                OnPropertyChanged(nameof(DetailsSourceText));
                OnPropertyChanged(nameof(Screenshots));
                OnPropertyChanged(nameof(HasScreenshots));
                RefreshDetailsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasDetails => _details is not null;

    public bool HasNoDetails => _details is null;

    public string ShortDescription => _details?.ShortDescription ?? "";

    public string About => _details?.About ?? "";

    public bool HasAbout => !string.IsNullOrWhiteSpace(_details?.About);

    public string GenresText => JoinOrDash(_details?.Genres);

    public string DevelopersText => JoinOrDash(_details?.Developers);

    public string PublishersText => JoinOrDash(_details?.Publishers);

    public string SteamReleaseDate => string.IsNullOrWhiteSpace(_details?.ReleaseDate) ? "—" : _details.ReleaseDate;

    public string DetailsSourceText => _details is null
        ? ""
        : $"Описание из Steam: {_details.SteamName} (AppID {_details.SteamAppId}), загружено {PlayTimeFormat.FormatDate(_details.FetchedAt, TimeZoneInfo.Local)}";

    public IReadOnlyList<ScreenshotViewModel> Screenshots => _screenshots;

    public bool HasScreenshots => _screenshots.Count > 0;

    private static string JoinOrDash(IReadOnlyList<string>? values) =>
        values is { Count: > 0 } ? string.Join(", ", values) : "—";

    /// <summary>Сообщить UI, что данные игры изменились (например, после переименования).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(PlayTimeText));
        OnPropertyChanged(nameof(LastPlayedText));
    }

    /// <summary>(Пере)загрузить обложку из кэша; если её нет — иконку exe. Вызывать из UI-потока.</summary>
    public async Task LoadImagesAsync(ArtworkCache cache)
    {
        RemoveCoverCommand.NotifyCanExecuteChanged();
        Cover = _game.GridFile is { } gridFile
            ? await ImageLoader.FromFileAsync(cache.GetPath(gridFile), decodeWidth: 352)
            : null;

        if (Cover is null && Icon is null)
        {
            Icon = await ImageLoader.ExeIconAsync(_game.ExePath);
        }

        // Баннер держим в памяти, только если он уже был загружен для страницы игры.
        if (_hero is not null || _game.HeroFile is null)
        {
            await LoadHeroAsync(cache);
        }
    }

    /// <summary>(Пере)загрузить баннер. Вызывать из UI-потока.</summary>
    public async Task LoadHeroAsync(ArtworkCache cache)
    {
        Hero = _game.HeroFile is { } heroFile
            ? await ImageLoader.FromFileAsync(cache.GetPath(heroFile), decodeWidth: 1920)
            : null;
    }

    /// <summary>Выгрузить баннер из памяти, когда страница игры закрыта.</summary>
    public void UnloadHero() => Hero = null;

    private void OpenFolder()
    {
        try
        {
            // Открывает Проводник с выделенным exe.
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_game.ExePath}\"")?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Проводник не запустился — ничего страшного.
        }
    }

    private void NotifyImageFlags()
    {
        OnPropertyChanged(nameof(HasCover));
        OnPropertyChanged(nameof(ShowIcon));
        OnPropertyChanged(nameof(ShowPlaceholder));
    }
}
