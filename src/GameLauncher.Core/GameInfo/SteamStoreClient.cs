using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameLauncher.Core.GameInfo;

/// <summary>Игра в результатах поиска Steam Store.</summary>
public sealed record SteamStoreApp(int AppId, string Name);

public sealed class SteamStoreException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Неофициальный, но общедоступный API магазина Steam (ключ не нужен):
/// appdetails — описание, жанры, скриншоты; storesearch — поиск по названию.
/// Лимит — порядка 200 запросов за 5 минут, для лаунчера этого с запасом.
/// </summary>
public sealed class SteamStoreClient(HttpClient http, TimeProvider? time = null)
{
    public static readonly Uri BaseUri = new("https://store.steampowered.com/api/");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Описание игры на русском. Null — Steam не знает такой AppID или не отдаёт по нему данных.</summary>
    public async Task<GameDetails?> GetDetailsAsync(int appId, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<Dictionary<string, AppDetailsEnvelope>>(
            $"appdetails?appids={appId}&l=russian&cc=ru", cancellationToken);
        if (response is null
            || !response.TryGetValue(appId.ToString(System.Globalization.CultureInfo.InvariantCulture), out var envelope)
            || !envelope.Success
            || envelope.Data is not { } data)
        {
            return null;
        }

        return new GameDetails
        {
            SteamAppId = appId,
            SteamName = data.Name ?? "",
            ShortDescription = HtmlText.ToPlainText(data.ShortDescription),
            About = HtmlText.ToPlainText(data.AboutTheGame ?? data.DetailedDescription),
            Genres = data.Genres?.Select(g => g.Description).OfType<string>().ToList() ?? [],
            Developers = data.Developers ?? [],
            Publishers = data.Publishers?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? [],
            ReleaseDate = data.ReleaseDate?.Date ?? "",
            Screenshots = data.Screenshots?
                .Where(s => s.PathThumbnail is not null && s.PathFull is not null)
                .Select(s => new Screenshot(s.PathThumbnail!, s.PathFull!))
                .ToList() ?? [],
            FetchedAt = _time.GetUtcNow(),
        };
    }

    /// <summary>Поиск игр в магазине по названию.</summary>
    public async Task<IReadOnlyList<SteamStoreApp>> SearchAsync(string term, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);

        var response = await GetAsync<SearchResponse>(
            $"storesearch/?term={Uri.EscapeDataString(term.Trim())}&l=russian&cc=ru", cancellationToken);
        return response?.Items?
            .Where(i => i.Type is null or "app")
            .Select(i => new SteamStoreApp(i.Id, i.Name ?? ""))
            .ToList() ?? [];
    }

    private async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(new Uri(BaseUri, relativePath), cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new SteamStoreException("Steam временно ограничил запросы. Попробуйте через несколько минут.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new SteamStoreException($"Steam ответил ошибкой (HTTP {(int)response.StatusCode}).");
            }

            // На неизвестный AppID Steam иногда отвечает телом "null".
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new SteamStoreException("Не удалось связаться с Steam. Проверьте подключение к интернету.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SteamStoreException("Steam не ответил вовремя.", ex);
        }
        catch (JsonException ex)
        {
            throw new SteamStoreException("Неожиданный ответ Steam.", ex);
        }
    }

    private sealed class AppDetailsEnvelope
    {
        public bool Success { get; set; }

        public AppData? Data { get; set; }
    }

    private sealed class AppData
    {
        public string? Name { get; set; }

        [JsonPropertyName("short_description")]
        public string? ShortDescription { get; set; }

        [JsonPropertyName("about_the_game")]
        public string? AboutTheGame { get; set; }

        [JsonPropertyName("detailed_description")]
        public string? DetailedDescription { get; set; }

        public List<string>? Developers { get; set; }

        public List<string>? Publishers { get; set; }

        public List<Genre>? Genres { get; set; }

        public List<ScreenshotDto>? Screenshots { get; set; }

        [JsonPropertyName("release_date")]
        public ReleaseDateDto? ReleaseDate { get; set; }
    }

    private sealed class Genre
    {
        public string? Description { get; set; }
    }

    private sealed class ScreenshotDto
    {
        [JsonPropertyName("path_thumbnail")]
        public Uri? PathThumbnail { get; set; }

        [JsonPropertyName("path_full")]
        public Uri? PathFull { get; set; }
    }

    private sealed class ReleaseDateDto
    {
        public string? Date { get; set; }
    }

    private sealed class SearchResponse
    {
        public List<SearchItem>? Items { get; set; }
    }

    private sealed class SearchItem
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Type { get; set; }
    }
}
