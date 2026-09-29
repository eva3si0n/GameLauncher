using GameLauncher.Core.Library;
using GameLauncher.Core.PlayTime;

namespace GameLauncher.Core.Tests;

public sealed class PlaySessionTrackerTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _dir = new();
    private readonly GameLibrary _library;
    private readonly PlaySessionTracker _tracker;
    private readonly Game _game;
    private readonly string _starterExe;
    private readonly string _realGameExe;

    public PlaySessionTrackerTests()
    {
        _starterExe = _dir.CreateFile(Path.Combine("Games", "Witcher", "launcher.exe"));
        _realGameExe = _dir.CreateFile(Path.Combine("Games", "Witcher", "bin", "x64", "witcher3.exe"));
        _library = new GameLibrary(new JsonLibraryStore(Path.Combine(_dir.Path, "library.json")));
        _game = _library.Add(_starterExe, out _);
        _tracker = new PlaySessionTracker(_library);
    }

    public void Dispose() => _dir.Dispose();

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    private string[] None => [];

    private string[] Other => [Path.Combine(_dir.Path, "Games", "Other", "other.exe")];

    [Fact]
    public void Launch_StartsInStartingState_ThenPlayingWhenProcessAppears()
    {
        _tracker.Launch(_game, At(0));
        Assert.Equal(PlaySessionState.Starting, _tracker.GetState(_game.Id));

        _tracker.Tick(At(5), [_starterExe]);

        Assert.Equal(PlaySessionState.Playing, _tracker.GetState(_game.Id));
    }

    [Fact]
    public void StarterExitsAndGameContinues_WholeTimeIsCounted()
    {
        _tracker.Launch(_game, At(0));
        _tracker.Tick(At(5), [_starterExe]);
        _tracker.Tick(At(10), [_starterExe, _realGameExe]);
        for (var t = 15; t <= 600; t += 5)
        {
            _tracker.Tick(At(t), [_realGameExe]); // стартер закрылся, игра идёт
        }

        _tracker.Tick(At(605), None);
        _tracker.Tick(At(615), None);

        Assert.Equal(PlaySessionState.None, _tracker.GetState(_game.Id));
        Assert.Equal(TimeSpan.FromSeconds(595), _game.TotalPlayTime); // от первого до последнего появления
        Assert.Equal(At(600), _game.LastPlayedAt);
    }

    [Fact]
    public void ShortGapBetweenStarterAndGame_DoesNotEndSession()
    {
        _tracker.Launch(_game, At(0));
        _tracker.Tick(At(5), [_starterExe]);
        _tracker.Tick(At(10), None); // стартер закрылся, игра ещё грузится
        _tracker.Tick(At(15), [_realGameExe]);

        Assert.Equal(PlaySessionState.Playing, _tracker.GetState(_game.Id));
    }

    [Fact]
    public void ProcessesOfOtherFolders_AreIgnored()
    {
        _tracker.Launch(_game, At(0));
        _tracker.Tick(At(5), Other);

        Assert.Equal(PlaySessionState.Starting, _tracker.GetState(_game.Id));
    }

    [Fact]
    public void ProcessNeverAppears_LaunchIsDroppedAfterTimeout()
    {
        _tracker.Launch(_game, At(0));

        _tracker.Tick(At(60), None);
        Assert.Equal(PlaySessionState.Starting, _tracker.GetState(_game.Id));

        Assert.True(_tracker.Tick(At(121), None));
        Assert.Equal(PlaySessionState.None, _tracker.GetState(_game.Id));
        Assert.False(_tracker.HasSessions);
        Assert.Equal(TimeSpan.Zero, _game.TotalPlayTime);
    }

    [Fact]
    public void LongSession_IsSavedEveryMinute()
    {
        _tracker.Launch(_game, At(0));
        _tracker.Tick(At(0), [_realGameExe]);
        _tracker.Tick(At(30), [_realGameExe]);
        Assert.Equal(TimeSpan.Zero, ReloadedGame().TotalPlayTime);

        _tracker.Tick(At(60), [_realGameExe]);
        Assert.Equal(TimeSpan.FromSeconds(60), ReloadedGame().TotalPlayTime);

        _tracker.Tick(At(125), [_realGameExe]);
        Assert.Equal(TimeSpan.FromSeconds(125), ReloadedGame().TotalPlayTime);
    }

    [Fact]
    public void FlushAll_SavesTimeOfRunningSession()
    {
        _tracker.Launch(_game, At(0));
        _tracker.Tick(At(0), [_realGameExe]);
        _tracker.Tick(At(40), [_realGameExe]);

        _tracker.FlushAll(At(42));

        Assert.Equal(TimeSpan.FromSeconds(40), ReloadedGame().TotalPlayTime);
    }

    [Fact]
    public void TimeIsNotCountedTwice_AfterFlushAndSessionEnd()
    {
        _tracker.Launch(_game, At(0));
        _tracker.Tick(At(0), [_realGameExe]);
        _tracker.Tick(At(60), [_realGameExe]); // сохранено 60 с
        _tracker.Tick(At(90), [_realGameExe]);
        _tracker.Tick(At(95), None);
        _tracker.Tick(At(105), None); // конец сессии

        Assert.Equal(TimeSpan.FromSeconds(90), ReloadedGame().TotalPlayTime);
    }

    [Fact]
    public void SecondSession_AddsToTotal()
    {
        PlayFor(0, 100);
        PlayFor(1000, 50);

        Assert.Equal(TimeSpan.FromSeconds(150), _game.TotalPlayTime);
    }

    [Fact]
    public void RepeatedLaunch_DuringSession_DoesNotResetIt()
    {
        _tracker.Launch(_game, At(0));
        _tracker.Tick(At(5), [_realGameExe]);

        _tracker.Launch(_game, At(10));

        Assert.Equal(PlaySessionState.Playing, _tracker.GetState(_game.Id));
    }

    [Fact]
    public void GameRemovedDuringSession_SessionIsDropped()
    {
        _tracker.Launch(_game, At(0));
        _tracker.Tick(At(0), [_realGameExe]);
        _library.Remove(_game.Id);

        _tracker.Tick(At(60), [_realGameExe]);

        Assert.False(_tracker.HasSessions);
    }

    private void PlayFor(double start, double seconds)
    {
        _tracker.Launch(_game, At(start));
        _tracker.Tick(At(start), [_realGameExe]);
        _tracker.Tick(At(start + seconds), [_realGameExe]);
        _tracker.Tick(At(start + seconds + 5), None);
        _tracker.Tick(At(start + seconds + 15), None);
        Assert.False(_tracker.HasSessions);
    }

    private Game ReloadedGame() =>
        Assert.Single(new GameLibrary(new JsonLibraryStore(Path.Combine(_dir.Path, "library.json"))).Games);
}
