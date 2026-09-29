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
        var tempPath = path + ".tmp";

        try
        {
            using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(target, cancellationToken);
            }

            // Другие виды картинки этой игры (с иным расширением) больше не нужны.
            Delete(gameId, kind);
            File.Move(tempPath, path, overwrite: true);
            return fileName;
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    /// <summary>Удаляет картинки игры: одного вида или все.</summary>
    public void Delete(Guid gameId, ArtworkKind? kind = null)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return;
        }

        var pattern = kind is { } k ? $"{gameId:N}-{k.ToString().ToLowerInvariant()}.*" : $"{gameId:N}-*";
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, pattern))
        {
            if (!file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
            }
        }
    }
}
