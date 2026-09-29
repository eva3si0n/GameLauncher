using GameLauncher.Core.Library;
using GameLauncher.Core.PlayTime;

namespace GameLauncher.Core.Tests;

public sealed class PlayHistoryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid GameA = Guid.NewGuid();
    private static readonly Guid GameB = Guid.NewGuid();

    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private string HistoryPath => Path.Combine(_dir.Path, "sessions.json");

    private static PlaySession S(Guid game, double startHours, double hours, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), game, T0.AddHours(startHours), T0.AddHours(startHours + hours));

    [Fact]
    public void Load_MissingFile_IsEmpty()
    {
        Assert.Empty(new PlayHistory(HistoryPath).Sessions);
    }

    [Fact]
    public void Record_PersistsAndRoundTrips()
    {
        var history = new PlayHistory(HistoryPath);
        var session = S(GameA, 0, 1.5);

        history.Record(session);

        var loaded = new PlayHistory(HistoryPath);
        Assert.Equal(session, Assert.Single(loaded.Sessions));
        Assert.Equal(TimeSpan.FromHours(1.5), loaded.Sessions[0].Duration);
        Assert.False(File.Exists(HistoryPath + ".tmp"));
    }

    [Fact]
    public void Record_SameId_UpdatesInsteadOfAdding()
    {
        var history = new PlayHistory(HistoryPath);
        var id = Guid.NewGuid();

        history.Record(S(GameA, 0, 0.5, id));
        history.Record(S(GameA, 0, 2, id));

        Assert.Equal(TimeSpan.FromHours(2), Assert.Single(new PlayHistory(HistoryPath).Sessions).Duration);
    }

    [Fact]
    public void Record_EndBeforeStart_Throws()
    {
        var history = new PlayHistory(HistoryPath);

        Assert.Throws<ArgumentException>(() => history.Record(new PlaySession(Guid.NewGuid(), GameA, T0, T0.AddMinutes(-1))));
        Assert.Empty(history.Sessions);
    }

    [Fact]
    public void Record_RaisesChanged()
    {
        var history = new PlayHistory(HistoryPath);
        var raised = 0;
        history.Changed += (_, _) => raised++;

        history.Record(S(GameA, 0, 1));

        Assert.Equal(1, raised);
    }

    [Fact]
    public void RemoveGame_RemovesOnlyThatGame()
    {
        var history = new PlayHistory(HistoryPath);
        history.Record(S(GameA, 0, 1));
        history.Record(S(GameB, 2, 1));
        history.Record(S(GameA, 4, 1));

        history.RemoveGame(GameA);

        Assert.Equal(GameB, Assert.Single(new PlayHistory(HistoryPath).Sessions).GameId);
    }

    [Fact]
    public void Load_CorruptFile_MovedAsideAndEmpty()
    {
        File.WriteAllText(HistoryPath, "{ битый");

        var history = new PlayHistory(HistoryPath);

        Assert.Empty(history.Sessions);
        Assert.NotNull(history.CorruptBackupPath);
        Assert.True(File.Exists(history.CorruptBackupPath));
        Assert.False(File.Exists(HistoryPath));
    }

    [Fact]
    public void Tracker_RecordsSessionWithTime_AndUpdatesSameRecordOnEachFlush()
    {
        var exe = _dir.CreateFile(Path.Combine("Games", "A", "a.exe"));
        var library = new GameLibrary(new JsonLibraryStore(Path.Combine(_dir.Path, "library.json")));
        var game = library.Add(exe, out _);
        var history = new PlayHistory(HistoryPath);
        var tracker = new PlaySessionTracker(library, history);

        tracker.Launch(game, T0);
        tracker.Tick(T0.AddSeconds(5), [exe]);
        for (var t = 10; t <= 185; t += 5)
        {
            tracker.Tick(T0.AddSeconds(t), [exe]); // сохранения каждую минуту — запись обновляется
        }

        Assert.Single(history.Sessions);
        tracker.Tick(T0.AddSeconds(190), []);
        tracker.Tick(T0.AddSeconds(200), []); // игра закрыта — сессия завершена

        var session = Assert.Single(new PlayHistory(HistoryPath).Sessions);
        Assert.Equal(game.Id, session.GameId);
        Assert.Equal(T0.AddSeconds(5), session.Start);
        Assert.Equal(T0.AddSeconds(185), session.End);
        Assert.Equal(game.TotalPlayTime, session.Duration);
    }

    [Fact]
    public void Tracker_TwoLaunches_TwoSessions()
    {
        var exe = _dir.CreateFile(Path.Combine("Games", "A", "a.exe"));
        var library = new GameLibrary(new JsonLibraryStore(Path.Combine(_dir.Path, "library.json")));
        var game = library.Add(exe, out _);
        var history = new PlayHistory(HistoryPath);
        var tracker = new PlaySessionTracker(library, history);

        foreach (var offset in new[] { 0, 3600 })
        {
            tracker.Launch(game, T0.AddSeconds(offset));
            tracker.Tick(T0.AddSeconds(offset + 5), [exe]);
            tracker.Tick(T0.AddSeconds(offset + 65), [exe]);
            tracker.Tick(T0.AddSeconds(offset + 70), []);
            tracker.Tick(T0.AddSeconds(offset + 80), []);
        }

        Assert.Equal(2, history.Sessions.Count);
        Assert.All(history.Sessions, s => Assert.Equal(TimeSpan.FromSeconds(60), s.Duration));
    }

    [Fact]
    public void Remover_DeletesGameHistory()
    {
        var exe = _dir.CreateFile(Path.Combine("Games", "A", "a.exe"));
        var library = new GameLibrary(new JsonLibraryStore(Path.Combine(_dir.Path, "library.json")));
        var game = library.Add(exe, out _);
        var history = new PlayHistory(HistoryPath);
        history.Record(S(game.Id, 0, 1));
        history.Record(S(GameB, 0, 1));
        var artwork = new Artwork.ArtworkCache(Path.Combine(_dir.Path, "artwork"), new HttpClient());
        var details = new GameInfo.GameDetailsStore(Path.Combine(_dir.Path, "info"));

        new GameRemover(library, artwork, details, history).Remove(game.Id);

        Assert.Equal(GameB, Assert.Single(history.Sessions).GameId);
    }
}
