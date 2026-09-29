namespace GameLauncher.Core.Library;

/// <summary>Куда ведёт ярлык .lnk: путь к файлу и аргументы.</summary>
public sealed record ShortcutTarget(string Path, string Arguments);

/// <summary>Чтение ярлыков .lnk (в App — через IShellLink Windows).</summary>
public interface IShortcutResolver
{
    /// <summary>Цель ярлыка; null — ярлык не читается или ни на что не указывает.</summary>
    ShortcutTarget? Resolve(string shortcutPath);
}

public enum ImportOutcome
{
    Added,
    AlreadyInLibrary,
    Skipped,
}

/// <summary>Что стало с одним перетащенным файлом. <paramref name="Note"/> — причина пропуска или оговорка.</summary>
public sealed record ImportResult(string Source, ImportOutcome Outcome, Game? Game = null, string? Note = null);

/// <summary>
/// Добавление игр из перетащенных файлов: .exe — как есть, ярлык .lnk — по его цели (название — имя ярлыка).
/// Ярлыки на лаунчеры магазинов (Steam, Epic…) и интернет-ярлыки .url не добавляются: exe игры в них нет.
/// </summary>
public sealed class GameImporter(GameLibrary library, IShortcutResolver shortcuts)
{
    /// <summary>Лаунчеры магазинов: ярлык на них запускает игру через магазин, сам exe игрой не является.</summary>
    private static readonly HashSet<string> StoreLaunchers = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam.exe", "epicgameslauncher.exe", "battle.net.exe", "battle.net launcher.exe", "galaxyclient.exe",
        "upc.exe", "ubisoftconnect.exe", "eadesktop.exe", "origin.exe", "rockstarlauncher.exe",
        // Не добавлять сюда общие имена вроде launcher.exe: так называются стартеры многих игр.
    };

    public IReadOnlyList<ImportResult> Import(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.Select(ImportOne).ToList();
    }

    private ImportResult ImportOne(string path)
    {
        if (Directory.Exists(path))
        {
            return new ImportResult(path, ImportOutcome.Skipped, Note: "это папка — перетащите exe игры из неё");
        }

        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".exe":
                return AddExe(path, path, name: null, note: null);

            case ".lnk":
                ShortcutTarget? target;
                try
                {
                    target = shortcuts.Resolve(path);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return new ImportResult(path, ImportOutcome.Skipped, Note: $"не удалось прочитать ярлык: {ex.Message}");
                }

                if (target is null || string.IsNullOrWhiteSpace(target.Path))
                {
                    return new ImportResult(path, ImportOutcome.Skipped, Note: "ярлык ни на что не указывает");
                }

                if (!string.Equals(Path.GetExtension(target.Path), ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return new ImportResult(path, ImportOutcome.Skipped, Note: $"ярлык ведёт не на exe: {target.Path}");
                }

                if (StoreLaunchers.Contains(Path.GetFileName(target.Path)))
                {
                    return new ImportResult(path, ImportOutcome.Skipped,
                        Note: $"ярлык запускает лаунчер магазина ({Path.GetFileName(target.Path)}), а не игру — перетащите exe из папки игры");
                }

                var note = string.IsNullOrWhiteSpace(target.Arguments)
                    ? null
                    : $"аргументы ярлыка не сохранены: {target.Arguments.Trim()}";
                return AddExe(path, target.Path, Path.GetFileNameWithoutExtension(path), note);

            case ".url":
                return new ImportResult(path, ImportOutcome.Skipped,
                    Note: "интернет-ярлык (например, Steam или Epic) не указывает на exe — перетащите exe из папки игры");

            default:
                return new ImportResult(path, ImportOutcome.Skipped, Note: "не exe и не ярлык");
        }
    }

    private ImportResult AddExe(string source, string exePath, string? name, string? note)
    {
        try
        {
            var game = library.Add(exePath, out var added, name);
            return new ImportResult(source, added ? ImportOutcome.Added : ImportOutcome.AlreadyInLibrary, game, note);
        }
        catch (FileNotFoundException)
        {
            return new ImportResult(source, ImportOutcome.Skipped, Note: $"файл не найден: {exePath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new ImportResult(source, ImportOutcome.Skipped, Note: ex.Message);
        }
    }

    /// <summary>
    /// Текст итога для пользователя; null — всё добавлено без оговорок, сообщать нечего.
    /// </summary>
    public static string? Summarize(IReadOnlyList<ImportResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.All(r => r.Outcome == ImportOutcome.Added && r.Note is null))
        {
            return null;
        }

        var lines = new List<string>();
        var added = results.Count(r => r.Outcome == ImportOutcome.Added);
        if (added > 0)
        {
            lines.Add($"Добавлено игр: {added}.");
        }

        foreach (var r in results.Where(r => r.Outcome == ImportOutcome.Added && r.Note is not null))
        {
            lines.Add($"• {r.Game!.Name}: {r.Note}.");
        }

        foreach (var r in results.Where(r => r.Outcome == ImportOutcome.AlreadyInLibrary))
        {
            lines.Add($"• {r.Game!.Name} — уже в библиотеке.");
        }

        foreach (var r in results.Where(r => r.Outcome == ImportOutcome.Skipped))
        {
            lines.Add($"• {Path.GetFileName(r.Source)} — {r.Note}.");
        }

        return string.Join("\n", lines);
    }
}
