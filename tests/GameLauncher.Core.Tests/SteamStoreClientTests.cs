using System.Net;
using GameLauncher.Core.GameInfo;

namespace GameLauncher.Core.Tests;

public sealed class SteamStoreClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static (SteamStoreClient Client, FakeHttpHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHttpHandler(respond);
        return (new SteamStoreClient(new HttpClient(handler), new FixedTime(Now)), handler);
    }

    [Fact]
    public async Task GetDetails_ParsesRussianDetailsAndScreenshots()
    {
        var (client, handler) = Create(_ => FakeHttpHandler.Json("""
            {"292030":{"success":true,"data":{
              "type":"game","name":"Ведьмак 3: Дикая Охота","steam_appid":292030,
              "short_description":"Вы — Геральт из Ривии, &quot;ведьмак&quot;.",
              "about_the_game":"<h1>Об игре</h1><p>Первая строка<br>Вторая</p><ul><li>Раз</li><li>Два</li></ul>",
              "developers":["CD PROJEKT RED"],"publishers":["CD PROJEKT RED",""],
              "genres":[{"id":"3","description":"Ролевые игры"}],
              "screenshots":[{"id":0,"path_thumbnail":"https://cdn.steam/ss1.600x338.jpg","path_full":"https://cdn.steam/ss1.1920x1080.jpg"}],
              "release_date":{"coming_soon":false,"date":"18 мая. 2015 г."}
            }}}
            """));

        var details = await client.GetDetailsAsync(292030, TestContext.Current.CancellationToken);

        var uri = Assert.Single(handler.Requests).RequestUri!;
        Assert.Equal("/api/appdetails", uri.AbsolutePath);
        Assert.Contains("appids=292030", uri.Query);
        Assert.Contains("l=russian", uri.Query);
        Assert.Contains("cc=tr", uri.Query);
        Assert.NotNull(details);
        Assert.Equal(292030, details.SteamAppId);
        Assert.Equal("Ведьмак 3: Дикая Охота", details.SteamName);
        Assert.Equal("Вы — Геральт из Ривии, \"ведьмак\".", details.ShortDescription);
        Assert.Equal("Об игре\nПервая строка\nВторая\n\n• Раз\n• Два", details.About);
        Assert.Equal(["Ролевые игры"], details.Genres);
        Assert.Equal(["CD PROJEKT RED"], details.Developers);
        Assert.Equal(["CD PROJEKT RED"], details.Publishers);
        Assert.Equal("18 мая. 2015 г.", details.ReleaseDate);
        Assert.Equal(new Screenshot(new Uri("https://cdn.steam/ss1.600x338.jpg"), new Uri("https://cdn.steam/ss1.1920x1080.jpg")), Assert.Single(details.Screenshots));
        Assert.Equal(Now, details.FetchedAt);
    }

    [Fact]
    public async Task GetDetails_NotSoldInTurkey_FallsBackToUsThenRussia()
    {
        static string Region(HttpRequestMessage r) => System.Text.RegularExpressions.Regex.Match(r.RequestUri!.Query, "cc=([a-z]+)").Groups[1].Value;
        var (client, handler) = Create(request => Region(request) == "ru"
            ? FakeHttpHandler.Json("""{"5":{"success":true,"data":{"name":"Только в РФ","short_description":"Описание"}}}""")
            : FakeHttpHandler.Json("""{"5":{"success":false}}"""));

        var details = await client.GetDetailsAsync(5, TestContext.Current.CancellationToken);

        Assert.Equal("Только в РФ", details?.SteamName);
        Assert.Equal(["tr", "us", "ru"], handler.Requests.Select(Region));
    }

    [Fact]
    public async Task GetDetails_UsesConfiguredPrimaryRegionWithoutDuplicates()
    {
        static string Region(HttpRequestMessage r) => System.Text.RegularExpressions.Regex.Match(r.RequestUri!.Query, "cc=([a-z]+)").Groups[1].Value;
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"5":{"success":false}}"""));
        var client = new SteamStoreClient(new HttpClient(handler), primaryRegion: () => "US");

        Assert.Null(await client.GetDetailsAsync(5, TestContext.Current.CancellationToken));
        Assert.Equal(["us", "ru"], handler.Requests.Select(Region));

        await client.SearchAsync("x", TestContext.Current.CancellationToken);
        Assert.Equal("us", Region(handler.Requests[^1]));
    }

    [Fact]
    public async Task GetDetails_FoundInTurkey_DoesNotQueryOtherRegions()
    {
        var (client, handler) = Create(_ => FakeHttpHandler.Json("""{"5":{"success":true,"data":{"name":"Игра"}}}"""));

        Assert.NotNull(await client.GetDetailsAsync(5, TestContext.Current.CancellationToken));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("""{"1":{"success":false}}""")]
    [InlineData("null")]
    [InlineData("""{"2":{"success":true,"data":{}}}""")]
    public async Task GetDetails_UnknownApp_ReturnsNull(string json)
    {
        var (client, _) = Create(_ => FakeHttpHandler.Json(json));

        Assert.Null(await client.GetDetailsAsync(1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Search_ReturnsOnlyApps()
    {
        var (client, handler) = Create(_ => FakeHttpHandler.Json("""
            {"total":2,"items":[
              {"type":"app","name":"Portal 2","id":620},
              {"type":"sub","name":"Portal Bundle","id":7932},
              {"name":"Без типа","id":1}
            ]}
            """));

        var apps = await client.SearchAsync(" portal 2 ", TestContext.Current.CancellationToken);

        var query = Assert.Single(handler.Requests).RequestUri!.Query;
        Assert.Contains("term=portal%202", query);
        Assert.Contains("cc=tr", query);
        Assert.Equal([new SteamStoreApp(620, "Portal 2"), new SteamStoreApp(1, "Без типа")], apps);
    }

    [Fact]
    public async Task TooManyRequests_ThrowsFriendlyError()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var ex = await Assert.ThrowsAsync<SteamStoreException>(() => client.GetDetailsAsync(1, TestContext.Current.CancellationToken));
        Assert.Contains("ограничил", ex.Message);
    }

    [Fact]
    public async Task NetworkFailureAndBadJson_ThrowSteamStoreException()
    {
        var (offline, _) = Create(_ => throw new HttpRequestException("no network"));
        var (broken, _) = Create(_ => FakeHttpHandler.Json("<html>"));

        await Assert.ThrowsAsync<SteamStoreException>(() => offline.SearchAsync("x", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<SteamStoreException>(() => broken.GetDetailsAsync(1, TestContext.Current.CancellationToken));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
