using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLauncher.Core.Library;
using GameLauncher.Core.PlayTime;

namespace GameLauncher.App.ViewModels;

/// <summary>Карточка игры в сетке.</summary>
public sealed class GameItemViewModel : ObservableObject
{
    private readonly Game _game;
    private PlaySessionState _state;

    public GameItemViewModel(
        Game game,
        Func<GameItemViewModel, Task> play,
        Func<GameItemViewModel, Task> rename,
        Func<GameItemViewModel, Task> delete)
    {
        _game = game;
        PlayCommand = new AsyncRelayCommand(() => play(this));
        RenameCommand = new AsyncRelayCommand(() => rename(this));
        DeleteCommand = new AsyncRelayCommand(() => delete(this));
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

    public IAsyncRelayCommand PlayCommand { get; }

    public IAsyncRelayCommand RenameCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    /// <summary>Сообщить UI, что данные игры изменились (например, после переименования).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(PlayTimeText));
    }
}
