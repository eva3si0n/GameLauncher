using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLauncher.Core.Library;

namespace GameLauncher.App.ViewModels;

/// <summary>Карточка игры в сетке.</summary>
public sealed class GameItemViewModel : ObservableObject
{
    private readonly Game _game;

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

    public IAsyncRelayCommand PlayCommand { get; }

    public IAsyncRelayCommand RenameCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    /// <summary>Сообщить UI, что данные игры изменились (например, после переименования).</summary>
    public void Refresh() => OnPropertyChanged(nameof(Name));
}
