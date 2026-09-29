namespace GameLauncher.Core.Startup;

/// <summary>Аргументы командной строки лаунчера.</summary>
public sealed record StartupOptions(bool StartInTray)
{
    /// <summary>Запуск без окна, сразу в трей (так лаунчер стартует вместе с Windows).</summary>
    public const string TrayArgument = "--tray";

    /// <summary>Разбор аргументов без пути к exe. Незнакомые аргументы игнорируются.</summary>
    public static StartupOptions Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return new StartupOptions(args.Any(a => string.Equals(a, TrayArgument, StringComparison.OrdinalIgnoreCase)));
    }
}
