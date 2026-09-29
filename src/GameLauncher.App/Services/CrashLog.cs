using GameLauncher.Core;

namespace GameLauncher.App.Services;

/// <summary>
/// Пишет необработанные исключения в %LocalAppData%\GameLauncher\crash.log.
/// Файл ограничен ~1 МБ: при переполнении он переименовывается в crash.old.log (предыдущая копия удаляется).
/// </summary>
internal static class CrashLog
{
    private const long MaxSizeBytes = 1024 * 1024;
    private static readonly object Sync = new();

    private static string LogPath => Path.Combine(AppPaths.DataDirectory, "crash.log");

    /// <summary>Подключает запись всех необработанных исключений: UI-поток, фоновые потоки, забытые задачи.</summary>
    public static void Install(Microsoft.UI.Xaml.Application app)
    {
        app.UnhandledException += (_, e) => Write(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write(e.Exception);
            e.SetObserved(); // ошибка забытой задачи не должна ронять приложение
        };
    }

    public static void Write(Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                var path = LogPath;
                if (File.Exists(path) && new FileInfo(path).Length > MaxSizeBytes)
                {
                    File.Move(path, Path.Combine(AppPaths.DataDirectory, "crash.old.log"), overwrite: true);
                }

                File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Лог падения не должен сам ронять приложение.
        }
    }
}
