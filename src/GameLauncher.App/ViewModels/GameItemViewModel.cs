using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLauncher.App.Services;
using GameLauncher.Core.Artwork;
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

    public GameItemViewModel(
        Game game,
        Func<GameItemViewModel, Task> play,
        Func<GameItemViewModel, Task> rename,
        Func<GameItemViewModel, Task> delete,
        Func<GameItemViewModel, Task> findCover,
        Func<GameItemViewModel, Task> removeCover)
    {
        _game = game;
        PlayCommand = new AsyncRelayCommand(() => play(this));
        RenameCommand = new AsyncRelayCommand(() => rename(this));
        DeleteCommand = new AsyncRelayCommand(() => delete(this));
        FindCoverCommand = new AsyncRelayCommand(() => findCover(this));
        RemoveCoverCommand = new AsyncRelayCommand(() => removeCover(this), () => _game.GridFile is not null);
    }

    public Game Game => _game;

    public Guid Id => _game.Id;

    public string Name => _game.Name;

    public string ExePath => _game.ExePath;

    public string PlayTimeText => PlayTimeFormat.Format(_game.TotalPlayTime);

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

    /// <summary>Сообщить UI, что данные игры изменились (например, после переименования).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(PlayTimeText));
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
    }

    private void NotifyImageFlags()
    {
        OnPropertyChanged(nameof(HasCover));
        OnPropertyChanged(nameof(ShowIcon));
        OnPropertyChanged(nameof(ShowPlaceholder));
    }
}
