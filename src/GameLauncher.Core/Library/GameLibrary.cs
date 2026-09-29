namespace GameLauncher.Core.Library;

/// <summary>
/// Библиотека игр: список в памяти, каждое изменение сразу сохраняется.
/// Если сохранить не удалось, изменение откатывается и исключение пробрасывается —
/// список в памяти не расходится с файлом.
/// </summary>
public sealed class GameLibrary
{
    private readonly ILibraryStore _store;
    private readonly TimeProvider _time;
    private readonly LibraryData _data;

    public GameLibrary(ILibraryStore store, TimeProvider? time = null)
    {
        _store = store;
        _time = time ?? TimeProvider.System;
        _data = store.Load();
    }

    public IReadOnlyList<Game> Games => _data.Games;

    /// <summary>
    /// Добавляет игру по пути к .exe. Если такой exe уже есть в библиотеке, возвращает существующую игру
    /// и <paramref name="added"/> = false.
    /// </summary>
    public Game Add(string exePath, out bool added, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);

        var fullPath = Path.GetFullPath(exePath);
        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Нужен файл .exe.", nameof(exePath));
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Файл не найден.", fullPath);
        }

        // Пути в Windows нечувствительны к регистру.
        var existing = _data.Games.FirstOrDefault(
            g => string.Equals(g.ExePath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            added = false;
            return existing;
        }

        var game = new Game
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(fullPath) : name.Trim(),
            ExePath = fullPath,
            AddedAt = _time.GetUtcNow(),
        };
        _data.Games.Add(game);
        SaveOrRollback(() => _data.Games.Remove(game));
        added = true;
        return game;
    }

    public void Rename(Guid id, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var game = Find(id);
        var oldName = game.Name;
        game.Name = newName.Trim();
        SaveOrRollback(() => game.Name = oldName);
    }

    public void Remove(Guid id)
    {
        var game = Find(id);
        var index = _data.Games.IndexOf(game);
        _data.Games.RemoveAt(index);
        SaveOrRollback(() => _data.Games.Insert(index, game));
    }

    private void SaveOrRollback(Action rollback)
    {
        try
        {
            _store.Save(_data);
        }
        catch
        {
            rollback();
            throw;
        }
    }

    private Game Find(Guid id) =>
        _data.Games.FirstOrDefault(g => g.Id == id)
        ?? throw new KeyNotFoundException($"Игра {id} не найдена.");
}
