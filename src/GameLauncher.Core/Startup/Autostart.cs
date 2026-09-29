namespace GameLauncher.Core.Startup;

/// <summary>Где хранится команда автозапуска (в App — значение в HKCU\...\Run).</summary>
public interface IAutostartStore
{
    /// <summary>Записанная команда; null — автозапуска нет.</summary>
    string? Read();

    /// <summary>Записать команду; null — убрать автозапуск.</summary>
    void Write(string? command);
}

/// <summary>
/// Автозапуск вместе с Windows: лаунчер стартует сразу в трей. Включённость определяется самой записью,
/// а не настройками, — чтобы не расходиться с тем, что реально стоит в системе.
/// </summary>
public sealed class Autostart(IAutostartStore store, string exePath)
{
    /// <summary>Команда для текущего exe: путь в кавычках (в нём бывают пробелы) и <see cref="StartupOptions.TrayArgument"/>.</summary>
    public string Command { get; } = $"\"{exePath}\" {StartupOptions.TrayArgument}";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(store.Read());

    public void SetEnabled(bool enabled) => store.Write(enabled ? Command : null);

    /// <summary>
    /// Папку с лаунчером перенесли, а автозапуск указывает на старый exe — переписать на текущий.
    /// Возвращает true, если запись изменена.
    /// </summary>
    public bool RepairIfMoved()
    {
        var current = store.Read();
        if (string.IsNullOrWhiteSpace(current) || string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        store.Write(Command);
        return true;
    }
}
