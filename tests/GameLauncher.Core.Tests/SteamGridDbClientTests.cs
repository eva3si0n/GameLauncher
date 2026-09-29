using System.Net;
using GameLauncher.Core.Artwork;

namespace GameLauncher.Core.Tests;

public sealed class SteamGridDbClientTests
{
    private static (SteamGridDbClient Client, FakeHttpHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond, string? key = "test-key")
    {
        var handler = new FakeHttpHandler(respond);
        return (new SteamGridDbClient(new HttpClient(handler), () => key), handler);
    }

    [Fact]
    public async Task Search_SendsBearerKeyAndParsesGames()
    {
        var (client, handler) = Create(_ => FakeHttpHandler.Json("""
            {"success":true,"data":[
              {"id":5254,"name":"The Witcher 3: Wild Hunt","types":["steam"],"verified":true,"release_date":1431993600},
              {"id":1,"name":"Без даты","types":[],"verified":false,"release_date":null}
            ]}
            """));

        var games = await client.SearchGamesAsync("  witcher 3 ", TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("test-key", request.Headers.Authorization.Parameter);
        Assert.Equal("https://www.steamgriddb.com/api/v2/search/autocomplete/witcher%203", request.RequestUri!.AbsoluteUri);
        Assert.Equal(2, games.Count);
        Assert.Equal(new SteamGridDbGame(5254, "The Witcher 3: Wild Hunt", 2015, true), games[0]);
        Assert.Equal("The Witcher 3: Wild Hunt (2015)", games[0].DisplayName);
        Assert.Null(games[1].ReleaseYear);
        Assert.Equal("Без даты", games[1].DisplayName);
    }

    [Fact]
    public async Task GetGrids_RequestsVerticalSizesAndSortsByScore()
    {
        var (client, handler) = Create(_ => FakeHttpHandler.Json("""
            {"success":true,"data":[
              {"id":1,"score":2,"url":"https://cdn2.steamgriddb.com/grid/a.png","thumb":"https://cdn2.steamgriddb.com/thumb/a.png","width":600,"height":900},
              {"id":2,"score":10,"url":"https://cdn2.steamgriddb.com/grid/b.jpg","thumb":"https://cdn2.steamgriddb.com/thumb/b.jpg","width":600,"height":900},
              {"id":3,"score":50,"url":null,"thumb":null,"width":600,"height":900}
            ]}
            """));

        var grids = await client.GetGridsAsync(5254, TestContext.Current.CancellationToken);

        var uri = Assert.Single(handler.Requests).RequestUri!;
        Assert.Equal("/api/v2/grids/game/5254", uri.AbsolutePath);
        Assert.Contains("dimensions=600x900", uri.Query);
        Assert.Contains("nsfw=false", uri.Query);
        Assert.Equal([2, 1], grids.Select(g => g.Id));
    }

    [Fact]
    public async Task GetHeroes_UsesHeroesEndpoint()
    {
        var (client, handler) = Create(_ => FakeHttpHandler.Json("""{"success":true,"data":[]}"""));

        Assert.Empty(await client.GetHeroesAsync(7, TestContext.Current.CancellationToken));
        Assert.Equal("/api/v2/heroes/game/7", Assert.Single(handler.Requests).RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RejectedKey_ThrowsAuthException(HttpStatusCode status)
    {
        var (client, _) = Create(_ => FakeHttpHandler.Json("""{"success":false,"errors":["Unauthorized"]}""", status));

        await Assert.ThrowsAsync<SteamGridDbAuthException>(() => client.SearchGamesAsync("x", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingKey_ThrowsAuthExceptionWithoutRequest()
    {
        var (client, handler) = Create(_ => throw new InvalidOperationException(), key: " ");

        await Assert.ThrowsAsync<SteamGridDbAuthException>(() => client.SearchGamesAsync("x", TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NotFound_ReturnsEmpty()
    {
        var (client, _) = Create(_ => FakeHttpHandler.Json("""{"success":false,"errors":["Game not found"]}""", HttpStatusCode.NotFound));

        Assert.Empty(await client.GetGridsAsync(1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ServerError_ThrowsWithApiErrors()
    {
        var (client, _) = Create(_ => FakeHttpHandler.Json("""{"success":false,"errors":["Something broke"]}""", HttpStatusCode.InternalServerError));

        var ex = await Assert.ThrowsAsync<SteamGridDbException>(() => client.SearchGamesAsync("x", TestContext.Current.CancellationToken));
        Assert.Contains("Something broke", ex.Message);
    }

    [Fact]
    public async Task NonJsonResponse_ThrowsSteamGridDbException()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>") });

        await Assert.ThrowsAsync<SteamGridDbException>(() => client.SearchGamesAsync("x", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NetworkFailure_ThrowsSteamGridDbException()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("no network"));

        await Assert.ThrowsAsync<SteamGridDbException>(() => client.SearchGamesAsync("x", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("""{"success":true,"data":{"id":5254,"name":"W3","external_platform_data":{"steam":[{"id":"292030","name":"W3"}]}}}""", 292030)]
    [InlineData("""{"success":true,"data":{"id":5254,"name":"W3","external_platform_data":{"steam":[{"id":292030}]}}}""", 292030)]
    [InlineData("""{"success":true,"data":{"id":5254,"name":"W3","external_platform_data":{"gog":[{"id":"1"}]}}}""", null)]
    [InlineData("""{"success":true,"data":{"id":5254,"name":"W3"}}""", null)]
    public async Task GetSteamAppId_ReadsPlatformData(string json, int? expected)
    {
        var (client, handler) = Create(_ => FakeHttpHandler.Json(json));

        var appId = await client.GetSteamAppIdAsync(5254, TestContext.Current.CancellationToken);

        var uri = Assert.Single(handler.Requests).RequestUri!;
        Assert.Equal("/api/v2/games/id/5254", uri.AbsolutePath);
        Assert.Equal("?platformdata=steam", uri.Query);
        Assert.Equal(expected, appId);
    }
}
