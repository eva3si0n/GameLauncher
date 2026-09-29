using GameLauncher.Core;

namespace GameLauncher.App.Services;

/// <summary>Пишет необработанные исключения в %LocalAppData%\GameLauncher\crash.log.</summary>
internal static class CrashLog
{
    public static void Write(Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.AppendAllText(
                Path.Combine(AppPaths.DataDirectory, "crash.log"),
                $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Лог падения не должен сам ронять приложение.
        }
    }
}
