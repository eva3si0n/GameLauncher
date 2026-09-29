namespace GameLauncher.Core;

/// <summary>
/// Пути к данным лаунчера. Все данные живут в %LocalAppData%\GameLauncher.
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "GameLauncher";

    /// <summary>Каталог данных внутри заданного корня (корень передаётся явно — удобно для тестов).</summary>
    public static string GetDataDirectory(string localAppDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataRoot);
        return Path.Combine(localAppDataRoot, AppFolderName);
    }

    /// <summary>Файл библиотеки игр текущего пользователя.</summary>
    public static string LibraryFilePath => Path.Combine(DataDirectory, "library.json");

    /// <summary>Кэш обложек и баннеров.</summary>
    public static string ArtworkDirectory => Path.Combine(DataDirectory, "artwork");

    /// <summary>Описания игр из Steam.</summary>
    public static string GameDetailsDirectory => Path.Combine(DataDirectory, "info");

    /// <summary>Зашифрованный (DPAPI) API-ключ SteamGridDB.</summary>
    public static string SteamGridDbKeyPath => Path.Combine(DataDirectory, "steamgriddb.key");

    /// <summary>Каталог данных текущего пользователя.</summary>
    public static string DataDirectory =>
        GetDataDirectory(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
}
