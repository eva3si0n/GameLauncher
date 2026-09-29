using GameLauncher.Core.GameInfo;

namespace GameLauncher.Core.Tests;

public sealed class GameDetailsStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void SaveLoadDelete_RoundTrip()
    {
        var store = new GameDetailsStore(Path.Combine(_dir.Path, "info"));
        var id = Guid.NewGuid();
        var details = new GameDetails
        {
            SteamAppId = 620,
            SteamName = "Portal 2",
            ShortDescription = "Описание",
            Genres = ["Экшен"],
            Screenshots = [new Screenshot(new Uri("https://a/t.jpg"), new Uri("https://a/f.jpg"))],
        };

        Assert.Null(store.Load(id));
        store.Save(id, details);
        var loaded = store.Load(id);

        Assert.NotNull(loaded);
        Assert.Equal(620, loaded.SteamAppId);
        Assert.Equal("Описание", loaded.ShortDescription);
        Assert.Equal(details.Screenshots, loaded.Screenshots);

        store.Delete(id);
        Assert.Null(store.Load(id));
    }

    [Fact]
    public void Load_CorruptFile_ReturnsNull()
    {
        var store = new GameDetailsStore(_dir.Path);
        var id = Guid.NewGuid();
        File.WriteAllText(Path.Combine(_dir.Path, $"{id:N}.json"), "{ битый");

        Assert.Null(store.Load(id));
    }
}
