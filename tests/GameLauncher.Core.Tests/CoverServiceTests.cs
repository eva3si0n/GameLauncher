using System.Net;
using GameLauncher.Core.Artwork;
using GameLauncher.Core.GameInfo;
using GameLauncher.Core.Library;

namespace GameLauncher.Core.Tests;

public sealed class CoverServiceTests : IDisposable
{
    private const int SgdbId = 5254;
    private static readonly SteamGridDbImage Grid = new(1, new Uri("https://cdn.test/grid/new.jpg"), new Uri("https://cdn.test/thumb.jpg"), 600, 900, 10);

    private readonly TempDirectory _dir = new();
    private readonly GameLibrary _library;
    private readonly ArtworkCache _artwork;
    private readonly Game _game;

    /// <summary>Ответы подставного HTTP по пути запроса; по умолчанию — всё успешно.</summary>
    private Func<HttpRequestMessage, HttpResponseMessage?> _override = _ => null;

    public CoverServiceTests()
    {
        _library = new GameLibrary(new JsonLibraryStore(Path.Combine(_dir.Path, "library.json")));
        _game = _library.Add(_dir.CreateFile("game.exe"), out _);
        _artwork = new ArtworkCache(Path.Combine(_dir.Path, "artwork"), Http());
    }

    public void Dispose() => _dir.Dispose();

    private HttpClient Http() => new(new FakeHttpHandler(request => _override(request) ?? Default(request)));

    private static HttpResponseMessage Default(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        return path switch
        {
            _ when path.StartsWith("/api/v2/heroes/") => FakeHttpHandler.Json("""
                {"success":true,"data":[{"id":7,"score":5,"url":"https://cdn.test/hero/h.png","thumb":"https://cdn.test/hero/t.png","width":1920,"height":620}]}
                """),
            _ when path.StartsWith("/api/v2/games/") => FakeHttpHandler.Json("""
                {"success":true,"data":{"id":5254,"external_platform_data":{"steam":[{"id":"292030"}]}}}
                """),
            "/api/appdetails" => FakeHttpHandler.Json("""{"292030":{"success":true,"data":{"name":"Ведьмак 3","short_description":"Описание"}}}"""),
            _ when request.RequestUri.Host == "cdn.test" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private CoverService CreateService()
    {
        var http = Http();
        var details = new DetailsService(_library, new GameDetailsStore(Path.Combine(_dir.Path, "info")), new SteamStoreClient(http));
        return new CoverService(_library, _artwork, new SteamGridDbClient(http, () => "key"), details);
    }

    private Game Reloaded() => Assert.Single(new GameLibrary(new JsonLibraryStore(Path.Combine(_dir.Path, "library.json"))).Games);

    [Fact]
    public async Task Apply_SavesGridHeroAndDetails()
    {
        var result = await CreateService().ApplyAsync(_game.Id, SgdbId, Grid, TestContext.Current.CancellationToken);

        var saved = Reloaded();
        Assert.Equal($"{_game.Id:N}-grid.jpg", saved.GridFile);
        Assert.Equal($"{_game.Id:N}-hero.png", saved.HeroFile);
        Assert.Equal(292030, saved.SteamAppId);
        Assert.True(result.HeroUpdated);
        Assert.Equal("Описание", result.Details?.ShortDescription);
        Assert.True(File.Exists(_artwork.GetPath(saved.GridFile!)));
    }

    [Fact]
    public async Task Apply_HeroFails_GridStillSavedAndOldHeroKept()
    {
        await CreateService().ApplyAsync(_game.Id, SgdbId, Grid, TestContext.Current.CancellationToken);
        var oldHero = Reloaded().HeroFile;
        _override = r => r.RequestUri!.AbsolutePath.StartsWith("/hero/") ? throw new HttpRequestException("обрыв сети") : null;
        var newGrid = Grid with { Url = new Uri("https://cdn.test/grid/other.png") };

        var result = await CreateService().ApplyAsync(_game.Id, SgdbId, newGrid, TestContext.Current.CancellationToken);

        var saved = Reloaded();
        Assert.False(result.HeroUpdated);
        Assert.Equal($"{_game.Id:N}-grid.png", saved.GridFile);
        Assert.True(File.Exists(_artwork.GetPath(saved.GridFile!)));
        Assert.False(File.Exists(_artwork.GetPath($"{_game.Id:N}-grid.jpg"))); // прежняя обложка другого формата удалена
        Assert.Equal(oldHero, saved.HeroFile);
        Assert.True(File.Exists(_artwork.GetPath(oldHero!)));
    }

    [Fact]
    public async Task Apply_NoHeroes_ClearsHero()
    {
        await CreateService().ApplyAsync(_game.Id, SgdbId, Grid, TestContext.Current.CancellationToken);
        _override = r => r.RequestUri!.AbsolutePath.StartsWith("/api/v2/heroes/") ? FakeHttpHandler.Json("""{"success":true,"data":[]}""") : null;

        await CreateService().ApplyAsync(_game.Id, SgdbId, Grid, TestContext.Current.CancellationToken);

        Assert.Null(Reloaded().HeroFile);
        Assert.Empty(Directory.GetFiles(_artwork.Directory, "*-hero.*"));
    }

    [Fact]
    public async Task Apply_GridDownloadFails_ThrowsAndLibraryUnchanged()
    {
        _override = r => r.RequestUri!.Host == "cdn.test" ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : null;

        await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateService().ApplyAsync(_game.Id, SgdbId, Grid, TestContext.Current.CancellationToken));

        Assert.Null(Reloaded().GridFile);
    }

    [Fact]
    public async Task Apply_DetailsAlreadyLinked_DoesNotReload()
    {
        _library.SetSteamAppId(_game.Id, 42);

        var result = await CreateService().ApplyAsync(_game.Id, SgdbId, Grid, TestContext.Current.CancellationToken);

        Assert.Null(result.Details);
        Assert.Equal(42, Reloaded().SteamAppId);
    }

    [Fact]
    public async Task Apply_SteamUnavailable_CoverStillApplied()
    {
        _override = r => r.RequestUri!.AbsolutePath == "/api/appdetails" ? throw new HttpRequestException("Steam недоступен") : null;

        var result = await CreateService().ApplyAsync(_game.Id, SgdbId, Grid, TestContext.Current.CancellationToken);

        Assert.Null(result.Details);
        Assert.NotNull(Reloaded().GridFile);
        Assert.Null(Reloaded().SteamAppId);
    }

    [Fact]
    public async Task Remove_ClearsLibraryAndCache()
    {
        var service = CreateService();
        await service.ApplyAsync(_game.Id, SgdbId, Grid, TestContext.Current.CancellationToken);

        service.Remove(_game.Id);

        Assert.Null(Reloaded().GridFile);
        Assert.Null(Reloaded().HeroFile);
        Assert.Empty(Directory.GetFiles(_artwork.Directory));
    }
}
