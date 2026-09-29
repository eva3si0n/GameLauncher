using GameLauncher.Core.Library;

namespace GameLauncher.Core.Tests;

public sealed class GameLibraryTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private JsonLibraryStore NewStore() => new(Path.Combine(_dir.Path, "library.json"));

    [Fact]
    public void Add_UsesFileNameAsDefaultName_AndPersists()
    {
        var exe = _dir.CreateFile(Path.Combine("Games", "Witcher", "witcher3.exe"));
        var library = new GameLibrary(NewStore());

        var game = library.Add(exe, out var added);

        Assert.True(added);
        Assert.Equal("witcher3", game.Name);
        Assert.Equal(Path.GetFullPath(exe), game.ExePath);
        Assert.Single(new GameLibrary(NewStore()).Games);
    }

    [Fact]
    public void Add_WithName_TrimsIt()
    {
        var exe = _dir.CreateFile("game.exe");
        var library = new GameLibrary(NewStore());

        Assert.Equal("Моя игра", library.Add(exe, out _, "  Моя игра  ").Name);
    }

    [Fact]
    public void Add_SameExeTwice_ReturnsExistingWithoutDuplicate()
    {
        var exe = _dir.CreateFile("game.exe");
        var library = new GameLibrary(NewStore());

        var first = library.Add(exe, out _);
        var second = library.Add(exe, out var added);

        Assert.False(added);
        Assert.Same(first, second);
        Assert.Single(library.Games);
    }

    [Fact]
    public void Add_SameExeDifferentCase_IsDuplicateOnWindows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Регистронезависимые пути — только в Windows.");
        var exe = _dir.CreateFile("game.exe");
        var library = new GameLibrary(NewStore());
        library.Add(exe, out _);

        library.Add(exe.ToUpperInvariant(), out var added);

        Assert.False(added);
        Assert.Single(library.Games);
    }

    [Fact]
    public void Add_NotExe_Throws()
    {
        var file = _dir.CreateFile("readme.txt");
        var library = new GameLibrary(NewStore());

        Assert.Throws<ArgumentException>(() => library.Add(file, out _));
    }

    [Fact]
    public void Add_MissingFile_Throws()
    {
        var library = new GameLibrary(NewStore());

        Assert.Throws<FileNotFoundException>(() => library.Add(Path.Combine(_dir.Path, "nope.exe"), out _));
    }

    [Fact]
    public void Rename_TrimsAndPersists()
    {
        var library = new GameLibrary(NewStore());
        var game = library.Add(_dir.CreateFile("game.exe"), out _);

        library.Rename(game.Id, "  Новое имя ");

        Assert.Equal("Новое имя", Assert.Single(new GameLibrary(NewStore()).Games).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_BlankName_Throws(string name)
    {
        var library = new GameLibrary(NewStore());
        var game = library.Add(_dir.CreateFile("game.exe"), out _);

        Assert.Throws<ArgumentException>(() => library.Rename(game.Id, name));
        Assert.Equal("game", game.Name);
    }

    [Fact]
    public void Remove_DeletesAndPersists()
    {
        var library = new GameLibrary(NewStore());
        var keep = library.Add(_dir.CreateFile("a.exe"), out _);
        var drop = library.Add(_dir.CreateFile("b.exe"), out _);

        library.Remove(drop.Id);

        Assert.Equal(keep.Id, Assert.Single(new GameLibrary(NewStore()).Games).Id);
    }

    [Fact]
    public void Remove_UnknownId_Throws()
    {
        var library = new GameLibrary(NewStore());

        Assert.Throws<KeyNotFoundException>(() => library.Remove(Guid.NewGuid()));
    }

    [Fact]
    public void AddPlayTime_AccumulatesAndPersists()
    {
        var library = new GameLibrary(NewStore());
        var game = library.Add(_dir.CreateFile("game.exe"), out _);
        var playedAt = new DateTimeOffset(2026, 9, 29, 20, 0, 0, TimeSpan.Zero);

        library.AddPlayTime(game.Id, TimeSpan.FromMinutes(30), playedAt);
        library.AddPlayTime(game.Id, TimeSpan.FromMinutes(15), playedAt.AddHours(1));

        var reloaded = Assert.Single(new GameLibrary(NewStore()).Games);
        Assert.Equal(TimeSpan.FromMinutes(45), reloaded.TotalPlayTime);
        Assert.Equal(playedAt.AddHours(1), reloaded.LastPlayedAt);
    }

    [Fact]
    public void SetArtwork_Persists()
    {
        var library = new GameLibrary(NewStore());
        var game = library.Add(_dir.CreateFile("game.exe"), out _);

        library.SetArtwork(game.Id, "g.png", "h.jpg");

        var reloaded = Assert.Single(new GameLibrary(NewStore()).Games);
        Assert.Equal("g.png", reloaded.GridFile);
        Assert.Equal("h.jpg", reloaded.HeroFile);

        library.SetArtwork(game.Id, null, null);
        Assert.Null(Assert.Single(new GameLibrary(NewStore()).Games).GridFile);
    }

    [Fact]
    public void AddPlayTime_Negative_Throws()
    {
        var library = new GameLibrary(NewStore());
        var game = library.Add(_dir.CreateFile("game.exe"), out _);

        Assert.Throws<ArgumentOutOfRangeException>(() => library.AddPlayTime(game.Id, TimeSpan.FromSeconds(-1), default));
    }

    [Fact]
    public void FailedSave_RollsBackEveryMutation()
    {
        var store = new FlakyStore();
        var library = new GameLibrary(store);
        var game = library.Add(_dir.CreateFile("a.exe"), out _);
        store.Fail = true;

        Assert.Throws<IOException>(() => library.Add(_dir.CreateFile("b.exe"), out _));
        Assert.Throws<IOException>(() => library.Rename(game.Id, "Другое"));
        Assert.Throws<IOException>(() => library.Remove(game.Id));
        Assert.Throws<IOException>(() => library.AddPlayTime(game.Id, TimeSpan.FromMinutes(5), default));

        Assert.Same(game, Assert.Single(library.Games));
        Assert.Equal("a", game.Name);
        Assert.Equal(TimeSpan.Zero, game.TotalPlayTime);
        Assert.Null(game.LastPlayedAt);
    }

    private sealed class FlakyStore : ILibraryStore
    {
        public bool Fail { get; set; }

        public LibraryData Load() => new();

        public void Save(LibraryData data)
        {
            if (Fail)
            {
                throw new IOException("Диск недоступен.");
            }
        }
    }
}
