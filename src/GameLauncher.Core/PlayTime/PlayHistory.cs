using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameLauncher.Core.PlayTime;

/// <summary>Одна игровая сессия: от появления процесса игры до его завершения (или до последнего сохранения).</summary>
public sealed record PlaySession(Guid Id, Guid GameId, DateTimeOffset Start, DateTimeOffset End)
{
    [JsonIgnore]
    public TimeSpan Duration => End - Start;
}

/// <summary>
/// История сессий: %LocalAppData%\GameLauncher\sessions.json. Дополняет общее время игры в библиотеке —
/// время, наигранное до появления истории, есть только в общей сумме.
/// Идущая сессия переписывается при каждом сохранении времени (раз в минуту), так что падение
/// лаунчера теряет не больше минуты. Не потокобезопасна: вызывать из одного потока.
/// </summary>
public sealed class PlayHistory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly List<PlaySession> _sessions;

    public PlayHistory(string filePath)
    {
        FilePath = filePath;
        _sessions = Load();
    }

    public string FilePath { get; }

    /// <summary>Нечитаемый файл при загрузке был отложен сюда; иначе null.</summary>
    public string? CorruptBackupPath { get; private set; }

    public IReadOnlyList<PlaySession> Sessions => _sessions;

    /// <summary>История изменилась (записана или удалена сессия).</summary>
    public event EventHandler? Changed;

    public IEnumerable<PlaySession> ForGame(Guid gameId) => _sessions.Where(s => s.GameId == gameId);

    /// <summary>Записать сессию или обновить уже записанную (по <see cref="PlaySession.Id"/>). Ошибка записи пробрасывается, память откатывается.</summary>
    public void Record(PlaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.End < session.Start)
        {
            throw new ArgumentException("Конец сессии раньше начала.", nameof(session));
        }

        var index = _sessions.FindIndex(s => s.Id == session.Id);
        var previous = index >= 0 ? _sessions[index] : null;
        if (index >= 0)
        {
            _sessions[index] = session;
        }
        else
        {
            _sessions.Add(session);
        }

        try
        {
            Save();
        }
        catch
        {
            if (previous is not null)
            {
                _sessions[index] = previous;
            }
            else
            {
                _sessions.RemoveAt(_sessions.Count - 1);
            }

            throw;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Удалить все сессии игры (игру убрали из библиотеки).</summary>
    public void RemoveGame(Guid gameId)
    {
        var removed = _sessions.Where(s => s.GameId == gameId).ToList();
        if (removed.Count == 0)
        {
            return;
        }

        _sessions.RemoveAll(s => s.GameId == gameId);
        try
        {
            Save();
        }
        catch
        {
            _sessions.AddRange(removed);
            throw;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<PlaySession> Load()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(FilePath);
            var data = JsonSerializer.Deserialize<HistoryData>(stream, JsonOptions);
            return data?.Sessions?.Where(s => s is not null && s.End >= s.Start).ToList() ?? [];
        }
        catch (JsonException)
        {
            // Как с библиотекой: испорченный файл — в сторону, начинаем заново, но ничего не теряем молча.
            CorruptBackupPath = $"{FilePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(FilePath, CorruptBackupPath, overwrite: true);
            return [];
        }
    }

    private void Save() =>
        AtomicFile.Write(FilePath, stream => JsonSerializer.Serialize(stream, new HistoryData(HistoryData.CurrentVersion, _sessions), JsonOptions));

    private sealed record HistoryData(int Version, List<PlaySession>? Sessions)
    {
        public const int CurrentVersion = 1;
    }
}
