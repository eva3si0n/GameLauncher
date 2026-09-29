namespace GameLauncher.Core.PlayTime;

/// <summary>
/// Процесс относится к игре, если его exe лежит в папке игры (или её подпапках).
/// Так учитываются игры со стартером: стартер завершается, а игра, запущенная им, продолжает сессию.
/// </summary>
public sealed class GameFolderMatcher
{
    private readonly string _exePath;
    private readonly string? _folderPrefix;

    public GameFolderMatcher(string gameExePath)
    {
        _exePath = Path.GetFullPath(gameExePath);
        var folder = Path.GetDirectoryName(_exePath);

        // Если exe лежит в корне диска, «папка игры» — весь диск. Тогда засчитываем только сам exe.
        if (!string.IsNullOrEmpty(folder) && Path.GetPathRoot(folder) != folder)
        {
            _folderPrefix = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        }
    }

    public bool Matches(string processExePath) =>
        _folderPrefix is not null
            ? processExePath.StartsWith(_folderPrefix, StringComparison.OrdinalIgnoreCase)
            : string.Equals(processExePath, _exePath, StringComparison.OrdinalIgnoreCase);
}
