using System.Text.Json;

namespace GameLauncher.Core.GameInfo;

/// <summary>Описания игр: по файлу на игру, запись атомарная.</summary>
public sealed class GameDetailsStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Directory { get; } = directory;

    /// <summary>Описание игры; null — нет или файл не читается.</summary>
    public GameDetails? Load(Guid gameId)
    {
        var path = GetPath(gameId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<GameDetails>(stream, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Описание можно скачать заново — испорченный файл просто игнорируем.
            return null;
        }
    }

    public void Save(Guid gameId, GameDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        System.IO.Directory.CreateDirectory(Directory);
        var path = GetPath(gameId);
        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, details, JsonOptions);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    public void Delete(Guid gameId) => File.Delete(GetPath(gameId));

    private string GetPath(Guid gameId) => Path.Combine(Directory, $"{gameId:N}.json");
}
