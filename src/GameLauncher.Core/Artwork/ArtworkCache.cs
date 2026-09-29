namespace GameLauncher.Core.Artwork;

public enum ArtworkKind
{
    /// <summary>Вертикальная обложка.</summary>
    Grid,

    /// <summary>Широкий баннер.</summary>
    Hero,
}

/// <summary>
/// Локальный кэш картинок: %LocalAppData%\GameLauncher\artwork\{id игры}-{вид}{расширение}.
/// Запись атомарная, чтобы оборванная загрузка не оставила битый файл.
/// </summary>
public sealed class ArtworkCache(string directory, HttpClient http)
{
    public string Directory { get; } = directory;

    /// <summary>Полный путь к файлу из кэша по имени, записанному в библиотеке.</summary>
    public string GetPath(string fileName) => Path.Combine(Directory, Path.GetFileName(fileName));

    /// <summary>Скачивает картинку и возвращает имя файла в кэше.</summary>
    public async Task<string> DownloadAsync(Guid gameId, ArtworkKind kind, Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        System.IO.Directory.CreateDirectory(Directory);
        var extension = Path.GetExtension(url.AbsolutePath).ToLowerInvariant() switch
        {
            ".png" => ".png",
            ".jpg" or ".jpeg" => ".jpg",
            ".webp" => ".webp",
            _ => ".img",
        };
        var fileName = $"{gameId:N}-{kind.ToString().ToLowerInvariant()}{extension}";
        var path = Path.Combine(Directory, fileName);

        using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await AtomicFile.WriteAsync(
                path,
                async (target, ct) =>
                {
                    await using var source = await response.Content.ReadAsStreamAsync(ct);
                    await source.CopyToAsync(target, ct);
                },
                cancellationToken);
        }

        // Картинка того же вида с другим расширением (прежняя) больше не нужна.
        foreach (var old in EnumerateFiles(gameId, kind).Where(f => !string.Equals(f, path, StringComparison.OrdinalIgnoreCase)))
        {
            File.Delete(old);
        }

        return fileName;
    }

    /// <summary>Удаляет картинки игры: одного вида или все.</summary>
    public void Delete(Guid gameId, ArtworkKind? kind = null)
    {
        foreach (var file in EnumerateFiles(gameId, kind).ToList())
        {
            File.Delete(file);
        }
    }

    private IEnumerable<string> EnumerateFiles(Guid gameId, ArtworkKind? kind)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        var pattern = kind is { } k ? $"{gameId:N}-{k.ToString().ToLowerInvariant()}.*" : $"{gameId:N}-*";
        return System.IO.Directory.EnumerateFiles(Directory, pattern)
            .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }
}
