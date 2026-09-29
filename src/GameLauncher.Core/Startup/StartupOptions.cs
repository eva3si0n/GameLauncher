using System.Globalization;

namespace GameLauncher.Core.Startup;

/// <summary>Аргументы командной строки лаунчера.</summary>
/// <param name="StartInTray">Запуск без окна, сразу в трей.</param>
/// <param name="WaitForProcessId">
/// Перед стартом дождаться завершения процесса с этим PID — так лаунчер перезапускает сам себя
/// (после восстановления из копии): иначе новый экземпляр застал бы старый и передал активацию ему.
/// </param>
public sealed record StartupOptions(bool StartInTray, int? WaitForProcessId = null)
{
    /// <summary>Запуск без окна, сразу в трей (так лаунчер стартует вместе с Windows).</summary>
    public const string TrayArgument = "--tray";

    /// <summary>Префикс аргумента ожидания: --wait-pid=1234.</summary>
    public const string WaitForProcessPrefix = "--wait-pid=";

    /// <summary>Разбор аргументов без пути к exe. Незнакомые и некорректные аргументы игнорируются.</summary>
    public static StartupOptions Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var tray = false;
        int? waitFor = null;
        foreach (var arg in args)
        {
            if (string.Equals(arg, TrayArgument, StringComparison.OrdinalIgnoreCase))
            {
                tray = true;
            }
            else if (arg.StartsWith(WaitForProcessPrefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(arg.AsSpan(WaitForProcessPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                && pid > 0)
            {
                waitFor = pid;
            }
        }

        return new StartupOptions(tray, waitFor);
    }
}
