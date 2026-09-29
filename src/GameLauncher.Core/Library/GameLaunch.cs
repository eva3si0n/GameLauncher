using System.Diagnostics;

namespace GameLauncher.Core.Library;

public static class GameLaunch
{
    /// <summary>
    /// Параметры запуска игры. Рабочая папка — папка exe: многие игры без этого не находят свои файлы и падают.
    /// UseShellExecute = true, чтобы игры, требующие прав администратора, показывали запрос UAC, а не падали с ошибкой 740.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (!File.Exists(game.ExePath))
        {
            throw new FileNotFoundException("Файл игры не найден.", game.ExePath);
        }

        return new ProcessStartInfo
        {
            FileName = game.ExePath,
            WorkingDirectory = Path.GetDirectoryName(game.ExePath)!,
            UseShellExecute = true,
        };
    }
}
