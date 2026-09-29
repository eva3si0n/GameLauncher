namespace GameLauncher.Core.Artwork;

/// <summary>Игра в базе SteamGridDB.</summary>
public sealed record SteamGridDbGame(int Id, string Name, int? ReleaseYear, bool Verified)
{
    public string DisplayName => ReleaseYear is { } year ? $"{Name} ({year})" : Name;
}

/// <summary>Картинка SteamGridDB (обложка или баннер).</summary>
public sealed record SteamGridDbImage(int Id, Uri Url, Uri Thumb, int Width, int Height, int Score);

/// <summary>Ошибка ответа SteamGridDB.</summary>
public class SteamGridDbException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Ключ отклонён (401/403).</summary>
public sealed class SteamGridDbAuthException(string message) : SteamGridDbException(message);
