using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameLauncher.Core.Artwork;

/// <summary>
/// Клиент SteamGridDB API v2 (https://www.steamgriddb.com/api/v2).
/// Ответы приходят в обёртке { success, data, errors }.
/// </summary>
public sealed class SteamGridDbClient(HttpClient http, Func<string?> apiKey)
{
    public static readonly Uri BaseUri = new("https://www.steamgriddb.com/api/v2/");

    /// <summary>Вертикальные обложки: 600x900 — основной размер, 342x482 и 660x930 — старые форматы Steam.</summary>
    private const string GridQuery = "dimensions=600x900,342x482,660x930&mimes=image/png,image/jpeg&nsfw=false&humor=false";

    private const string HeroQuery = "mimes=image/png,image/jpeg&nsfw=false&humor=false";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<SteamGridDbGame>> SearchGamesAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var games = await GetAsync<List<GameDto>>(
            $"search/autocomplete/{Uri.EscapeDataString(query.Trim())}", cancellationToken);
        return games
            .Select(g => new SteamGridDbGame(
                g.Id,
                g.Name,
                g.ReleaseDate is > 0 ? DateTimeOffset.FromUnixTimeSeconds(g.ReleaseDate.Value).Year : null,
                g.Verified))
            .ToList();
    }

    /// <summary>
    /// Steam AppID игры из SteamGridDB (параметр platformdata=steam). Null — у игры нет привязки к Steam.
    /// </summary>
    public async Task<int?> GetSteamAppIdAsync(int gameId, CancellationToken cancellationToken = default)
    {
        var game = await GetAsync<GameWithPlatformsDto>($"games/id/{gameId}?platformdata=steam", cancellationToken);
        var id = game.ExternalPlatformData?.GetValueOrDefault("steam")?.FirstOrDefault()?.Id;

        // SteamGridDB отдаёт id строкой, но принимаем и число.
        return id switch
        {
            { ValueKind: JsonValueKind.Number } n when n.TryGetInt32(out var number) && number > 0 => number,
            { ValueKind: JsonValueKind.String } s when int.TryParse(
                s.GetString(),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) && parsed > 0 => parsed,
            _ => null,
        };
    }

    /// <summary>Вертикальные обложки игры, лучшие (по рейтингу) — первыми.</summary>
    public Task<IReadOnlyList<SteamGridDbImage>> GetGridsAsync(int gameId, CancellationToken cancellationToken = default) =>
        GetImagesAsync($"grids/game/{gameId}?{GridQuery}", cancellationToken);

    /// <summary>Широкие баннеры (hero) игры, лучшие — первыми.</summary>
    public Task<IReadOnlyList<SteamGridDbImage>> GetHeroesAsync(int gameId, CancellationToken cancellationToken = default) =>
        GetImagesAsync($"heroes/game/{gameId}?{HeroQuery}", cancellationToken);

    private async Task<IReadOnlyList<SteamGridDbImage>> GetImagesAsync(string path, CancellationToken cancellationToken)
    {
        var images = await GetAsync<List<ImageDto>>(path, cancellationToken);
        return images
            .Where(i => i.Url is not null)
            .OrderByDescending(i => i.Score)
            .Select(i => new SteamGridDbImage(i.Id, i.Url!, i.Thumb ?? i.Url!, i.Width, i.Height, i.Score))
            .ToList();
    }

    private async Task<T> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
        where T : new()
    {
        var key = apiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new SteamGridDbAuthException("Не задан API-ключ SteamGridDB.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUri, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new SteamGridDbException("Не удалось связаться с SteamGridDB. Проверьте подключение к интернету.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SteamGridDbException("SteamGridDB не ответил вовремя.", ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new SteamGridDbAuthException("SteamGridDB отклонил API-ключ. Проверьте ключ в настройках.");
            }

            // 404 у SteamGridDB — «ничего не найдено».
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new T();
            }

            Envelope<T>? envelope;
            try
            {
                envelope = await response.Content.ReadFromJsonAsync<Envelope<T>>(JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new SteamGridDbException($"Неожиданный ответ SteamGridDB (HTTP {(int)response.StatusCode}).", ex);
            }

            if (!response.IsSuccessStatusCode || envelope is not { Success: true })
            {
                var details = envelope?.Errors is { Count: > 0 } errors ? string.Join(", ", errors) : $"HTTP {(int)response.StatusCode}";
                throw new SteamGridDbException($"Ошибка SteamGridDB: {details}.");
            }

            return envelope.Data ?? new T();
        }
    }

    private sealed class Envelope<T>
    {
        public bool Success { get; set; }

        public T? Data { get; set; }

        public List<string>? Errors { get; set; }
    }

    private sealed class GameDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public bool Verified { get; set; }

        [JsonPropertyName("release_date")]
        public long? ReleaseDate { get; set; }
    }

    private sealed class GameWithPlatformsDto
    {
        [JsonPropertyName("external_platform_data")]
        public Dictionary<string, List<PlatformEntryDto>>? ExternalPlatformData { get; set; }
    }

    private sealed class PlatformEntryDto
    {
        public JsonElement? Id { get; set; }
    }

    private sealed class ImageDto
    {
        public int Id { get; set; }

        public int Score { get; set; }

        public Uri? Url { get; set; }

        public Uri? Thumb { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }
    }
}
