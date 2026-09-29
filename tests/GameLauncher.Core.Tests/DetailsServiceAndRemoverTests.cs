using System.Net;
using GameLauncher.Core.Artwork;
using GameLauncher.Core.GameInfo;
using GameLauncher.Core.Library;

namespace GameLauncher.Core.Tests;

public sealed class DetailsServiceAndRemoverTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly GameLibrary _library;
    private readonly GameDetailsStore _store;
    private readonly Game _game;

    public DetailsServiceAndRemoverTests()
    {
        _library = new GameLibrary(new JsonLibraryStore(Path.Combine(_dir.Path, "library.json")));
        _store = new GameDetailsStore(Path.Combine(_dir.Path, "info"));
        _game = _library.Add(_dir.CreateFile("game.exe"), out _);
    }

    public void Dispose() => _dir.Dispose();

    private DetailsService Service(string appDetailsJson) =>
        new(_library, _store, new SteamStoreClient(new HttpClient(new FakeHttpHandler(_ => FakeHttpHandler.Json(appDetailsJson)))));

    [Fact]
    public async Task Download_SavesDetailsAndLinksAppId()
    {
        var service = Service("""{"620":{"success":true,"data":{"name":"Portal 2","short_description":"Кооператив"}}}""");

        var details = await service.DownloadAsync(_game.Id, 620, TestContext.Current.CancellationToken);

        Assert.Equal("Portal 2", details?.SteamName);
        Assert.Equal("Кооператив", service.Load(_game.Id)?.ShortDescription);
        Assert.Equal(620, _library.Get(_game.Id).SteamAppId);
    }

    [Fact]
    public async Task Download_NoData_SavesNothing()
    {
        var service = Service("""{"620":{"success":false}}""");

        Assert.Null(await service.DownloadAsync(_game.Id, 620, TestContext.Current.CancellationToken));
        Assert.Null(service.Load(_game.Id));
        Assert.Null(_library.Get(_game.Id).SteamAppId);
    }

    [Fact]
    public async Task Remover_DeletesGameArtworkAndDetails_ButNotOtherGames()
    {
        var other = _library.Add(_dir.CreateFile("other.exe"), out _);
        var http = new HttpClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) }));
        var artwork = new ArtworkCache(Path.Combine(_dir.Path, "artwork"), http);
        await artwork.DownloadAsync(_game.Id, ArtworkKind.Grid, new Uri("https://cdn.test/a.png"), TestContext.Current.CancellationToken);
        var otherGrid = await artwork.DownloadAsync(other.Id, ArtworkKind.Grid, new Uri("https://cdn.test/b.png"), TestContext.Current.CancellationToken);
        _store.Save(_game.Id, new GameDetails { SteamAppId = 1 });
        _store.Save(other.Id, new GameDetails { SteamAppId = 2 });

        new GameRemover(_library, artwork, _store).Remove(_game.Id);

        Assert.Equal(other.Id, Assert.Single(_library.Games).Id);
        Assert.Equal([artwork.GetPath(otherGrid)], Directory.GetFiles(artwork.Directory));
        Assert.Null(_store.Load(_game.Id));
        Assert.NotNull(_store.Load(other.Id));
    }
}
