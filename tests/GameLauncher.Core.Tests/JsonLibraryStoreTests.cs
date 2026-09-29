using GameLauncher.Core.Library;

namespace GameLauncher.Core.Tests;

public sealed class JsonLibraryStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private string LibraryPath => Path.Combine(_dir.Path, "data", "library.json");

    [Fact]
    public void Load_MissingFile_ReturnsEmptyLibrary()
    {
        var store = new JsonLibraryStore(LibraryPath);

        var data = store.Load();

        Assert.Empty(data.Games);
        Assert.Null(store.CorruptBackupPath);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsGames()
    {
        var store = new JsonLibraryStore(LibraryPath);
        var game = new Game
        {
            Id = Guid.NewGuid(),
            Name = "Игра",
            ExePath = @"C:\Games\Game\game.exe",
            AddedAt = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero),
        };

        store.Save(new LibraryData { Games = [game] });
        var loaded = new JsonLibraryStore(LibraryPath).Load();

        var single = Assert.Single(loaded.Games);
        Assert.Equal(game.Id, single.Id);
        Assert.Equal(game.Name, single.Name);
        Assert.Equal(game.ExePath, single.ExePath);
        Assert.Equal(game.AddedAt, single.AddedAt);
        Assert.Equal(LibraryData.CurrentVersion, loaded.Version);
    }

    [Fact]
    public void Save_CreatesDirectoryAndLeavesNoTempFile()
    {
        var store = new JsonLibraryStore(LibraryPath);

        store.Save(new LibraryData());
        store.Save(new LibraryData());

        Assert.True(File.Exists(LibraryPath));
        Assert.False(File.Exists(LibraryPath + ".tmp"));
    }

    [Fact]
    public void Load_CorruptFile_MovesItAsideAndReturnsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath)!);
        File.WriteAllText(LibraryPath, "{ это не json");
        var store = new JsonLibraryStore(LibraryPath);

        var data = store.Load();

        Assert.Empty(data.Games);
        Assert.NotNull(store.CorruptBackupPath);
        Assert.True(File.Exists(store.CorruptBackupPath));
        Assert.Equal("{ это не json", File.ReadAllText(store.CorruptBackupPath));
        Assert.False(File.Exists(LibraryPath));
    }

    [Fact]
    public void Load_NullJson_TreatedAsCorrupt()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath)!);
        File.WriteAllText(LibraryPath, "null");
        var store = new JsonLibraryStore(LibraryPath);

        Assert.Empty(store.Load().Games);
        Assert.NotNull(store.CorruptBackupPath);
    }
}
