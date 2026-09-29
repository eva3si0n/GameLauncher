using System.Net;
using GameLauncher.Core.Artwork;

namespace GameLauncher.Core.Tests;

public sealed class ArtworkCacheTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly Guid _gameId = Guid.NewGuid();

    public void Dispose() => _dir.Dispose();

    private ArtworkCache Create(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(Path.Combine(_dir.Path, "artwork"), new HttpClient(new FakeHttpHandler(respond)));

    private static HttpResponseMessage Bytes(params byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    [Fact]
    public async Task Download_SavesFileWithExtensionAndReturnsName()
    {
        var cache = Create(_ => Bytes(1, 2, 3));

        var name = await cache.DownloadAsync(_gameId, ArtworkKind.Grid, new Uri("https://cdn.example/grid/abc.PNG"), TestContext.Current.CancellationToken);

        Assert.Equal($"{_gameId:N}-grid.png", name);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(cache.GetPath(name)));
        Assert.Empty(Directory.GetFiles(cache.Directory, "*.tmp"));
    }

    [Fact]
    public async Task Download_ReplacesPreviousFileOfSameKindWithOtherExtension()
    {
        var cache = Create(_ => Bytes(9));
        var png = await cache.DownloadAsync(_gameId, ArtworkKind.Grid, new Uri("https://cdn.example/a.png"), TestContext.Current.CancellationToken);
        var hero = await cache.DownloadAsync(_gameId, ArtworkKind.Hero, new Uri("https://cdn.example/h.png"), TestContext.Current.CancellationToken);

        var jpg = await cache.DownloadAsync(_gameId, ArtworkKind.Grid, new Uri("https://cdn.example/b.jpeg"), TestContext.Current.CancellationToken);

        Assert.Equal($"{_gameId:N}-grid.jpg", jpg);
        Assert.False(File.Exists(cache.GetPath(png)));
        Assert.True(File.Exists(cache.GetPath(hero)));
    }

    [Fact]
    public async Task FailedDownload_KeepsOldFileAndLeavesNoTemp()
    {
        var fail = false;
        var cache = Create(_ => fail ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Bytes(1));
        var name = await cache.DownloadAsync(_gameId, ArtworkKind.Grid, new Uri("https://cdn.example/a.png"), TestContext.Current.CancellationToken);
        fail = true;

        await Assert.ThrowsAsync<HttpRequestException>(
            () => cache.DownloadAsync(_gameId, ArtworkKind.Grid, new Uri("https://cdn.example/b.png"), TestContext.Current.CancellationToken));

        Assert.True(File.Exists(cache.GetPath(name)));
        Assert.Empty(Directory.GetFiles(cache.Directory, "*.tmp"));
    }

    [Fact]
    public async Task Delete_RemovesOnlyThisGamesFiles()
    {
        var cache = Create(_ => Bytes(1));
        var other = Guid.NewGuid();
        await cache.DownloadAsync(_gameId, ArtworkKind.Grid, new Uri("https://cdn.example/a.png"), TestContext.Current.CancellationToken);
        await cache.DownloadAsync(_gameId, ArtworkKind.Hero, new Uri("https://cdn.example/b.png"), TestContext.Current.CancellationToken);
        var otherName = await cache.DownloadAsync(other, ArtworkKind.Grid, new Uri("https://cdn.example/c.png"), TestContext.Current.CancellationToken);

        cache.Delete(_gameId);

        Assert.Equal([cache.GetPath(otherName)], Directory.GetFiles(cache.Directory));
    }

    [Fact]
    public void GetPath_IgnoresDirectoryPartsInName()
    {
        var cache = Create(_ => Bytes());

        Assert.Equal(Path.Combine(cache.Directory, "x.png"), cache.GetPath(Path.Combine("..", "..", "x.png")));
    }
}
