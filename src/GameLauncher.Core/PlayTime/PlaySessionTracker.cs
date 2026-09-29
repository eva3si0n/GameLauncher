using GameLauncher.Core.Library;

namespace GameLauncher.Core.PlayTime;

/// <summary>
/// Учёт времени игр, запущенных из лаунчера. Вызывающий код периодически (раз в ~5 с) передаёт в
/// <see cref="Tick"/> текущее время и пути exe запущенных процессов.
/// <list type="bullet">
/// <item>Сессия начинается, когда появился процесс из папки игры, и идёт, пока жив хоть один такой процесс.</item>
/// <item>Если процесс не появился за <see cref="StartTimeout"/>, запуск считается несостоявшимся.</item>
/// <item>Сессия заканчивается, если процессов нет дольше <see cref="EndGrace"/> — чтобы пережить паузу
/// между завершением стартера и появлением игры.</item>
/// <item>Накопленное время сохраняется раз в <see cref="FlushInterval"/>, чтобы падение лаунчера не съело сессию.</item>
/// <item>Вместе со временем в <paramref name="history"/> записывается сама сессия (начало и конец).</item>
/// </list>
/// Не потокобезопасен: вызывать из одного потока.
/// </summary>
public sealed class PlaySessionTracker(GameLibrary library, PlayHistory? history = null)
{
    public static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan EndGrace = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(1);

    private readonly Dictionary<Guid, Session> _sessions = [];

    public bool HasSessions => _sessions.Count > 0;

    public PlaySessionState GetState(Guid gameId) =>
        !_sessions.TryGetValue(gameId, out var session) ? PlaySessionState.None
        : session.FirstSeen is null ? PlaySessionState.Starting
        : PlaySessionState.Playing;

    /// <summary>Отметить, что игру только что запустили. Повторный запуск идущей сессии ничего не меняет.</summary>
    public void Launch(Game game, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(game);
        _sessions.TryAdd(game.Id, new Session(game.Id, new GameFolderMatcher(game.ExePath), now));
    }

    /// <summary>
    /// Обновить сессии. Возвращает true, если у какой-то игры изменилось состояние или сохранённое время.
    /// </summary>
    public bool Tick(DateTimeOffset now, IReadOnlyCollection<string> runningExePaths)
    {
        ArgumentNullException.ThrowIfNull(runningExePaths);

        var changed = false;
        foreach (var session in _sessions.Values.ToList())
        {
            if (runningExePaths.Any(session.Matcher.Matches))
            {
                if (session.FirstSeen is null)
                {
                    session.FirstSeen = session.CountedUntil = session.LastFlush = now;
                    changed = true;
                }

                session.LastSeen = now;
                session.MissingSince = null;

                if (now - session.LastFlush >= FlushInterval)
                {
                    changed |= Flush(session, now);
                }

                continue;
            }

            if (session.FirstSeen is null)
            {
                if (now - session.LaunchedAt > StartTimeout)
                {
                    _sessions.Remove(session.GameId);
                    changed = true;
                }

                continue;
            }

            session.MissingSince ??= now;
            if (now - session.MissingSince >= EndGrace)
            {
                Flush(session, now);
                _sessions.Remove(session.GameId);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Сохранить накопленное время всех сессий — при закрытии лаунчера.</summary>
    public void FlushAll(DateTimeOffset now)
    {
        foreach (var session in _sessions.Values.ToList())
        {
            Flush(session, now);
        }
    }

    private bool Flush(Session session, DateTimeOffset now)
    {
        session.LastFlush = now;
        if (session.LastSeen is not { } lastSeen || session.CountedUntil is not { } countedUntil)
        {
            return false;
        }

        var delta = lastSeen - countedUntil;
        if (delta <= TimeSpan.Zero)
        {
            return false;
        }

        try
        {
            library.AddPlayTime(session.GameId, delta, lastSeen);
            session.CountedUntil = lastSeen;
            RecordHistory(session, lastSeen);
            return true;
        }
        catch (KeyNotFoundException)
        {
            // Игру удалили из библиотеки во время сессии.
            _sessions.Remove(session.GameId);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Не удалось сохранить — время не теряется, попробуем при следующем сохранении.
            return false;
        }
    }

    private void RecordHistory(Session session, DateTimeOffset end)
    {
        if (history is null || session.FirstSeen is not { } start)
        {
            return;
        }

        try
        {
            history.Record(new PlaySession(session.HistoryId, session.GameId, start, end));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // История — дополнение: общее время уже сохранено. Сессия допишется при следующем сохранении.
        }
    }

    private sealed class Session(Guid gameId, GameFolderMatcher matcher, DateTimeOffset launchedAt)
    {
        public Guid GameId { get; } = gameId;

        /// <summary>Id записи в истории: одна и та же запись обновляется при каждом сохранении.</summary>
        public Guid HistoryId { get; } = Guid.NewGuid();

        public GameFolderMatcher Matcher { get; } = matcher;

        public DateTimeOffset LaunchedAt { get; } = launchedAt;

        public DateTimeOffset? FirstSeen { get; set; }

        public DateTimeOffset? LastSeen { get; set; }

        /// <summary>До какого момента время уже записано в библиотеку.</summary>
        public DateTimeOffset? CountedUntil { get; set; }

        public DateTimeOffset LastFlush { get; set; }

        public DateTimeOffset? MissingSince { get; set; }
    }
}
