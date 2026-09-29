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

    /// <summary>Каталог данных текущего пользователя.</summary>
    public static string DataDirectory =>
        GetDataDirectory(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
}
