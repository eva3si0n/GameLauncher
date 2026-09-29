namespace GameLauncher.Core.PlayTime;

/// <summary>Итоги по сессиям: периоды — от начала местного дня (N−1) дней назад до «сейчас».</summary>
public sealed record PlaySummary(TimeSpan Last7Days, TimeSpan Last30Days, int SessionCount, TimeSpan Average, TimeSpan Longest);

/// <summary>Итоги по всем играм за период: время, число сессий и разных игр, задевших период.</summary>
public sealed record PeriodSummary(TimeSpan Total, int SessionCount, int GameCount);

/// <summary>Время за один местный день.</summary>
public sealed record DayTotal(DateOnly Day, TimeSpan Total);

/// <summary>Время игры за период.</summary>
public sealed record GameTotal(Guid GameId, TimeSpan Total);

/// <summary>
/// Расчёты по истории сессий. Сессия, перешедшая через полночь, делится между днями;
/// в периоды попадает только часть сессии внутри периода.
/// </summary>
public static class PlayStats
{
    public static PlaySummary Summarize(IEnumerable<PlaySession> sessions, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var list = sessions.ToList();
        var total = list.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);
        return new PlaySummary(
            Total(list, PeriodStart(now, timeZone, 7), now),
            Total(list, PeriodStart(now, timeZone, 30), now),
            list.Count,
            list.Count == 0 ? TimeSpan.Zero : total / list.Count,
            list.Count == 0 ? TimeSpan.Zero : list.Max(s => s.Duration));
    }

    /// <summary>Время по дням за последние <paramref name="days"/> местных дней, включая сегодня; от старых к новым.</summary>
    public static IReadOnlyList<DayTotal> Daily(IEnumerable<PlaySession> sessions, DateTimeOffset now, TimeZoneInfo timeZone, int days)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        var list = sessions.ToList();
        var today = LocalDate(now, timeZone);
        var result = new List<DayTotal>(days);
        for (var i = days - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            result.Add(new DayTotal(day, Total(list, LocalMidnight(day, timeZone), LocalMidnight(day.AddDays(1), timeZone))));
        }

        return result;
    }

    /// <summary>Итоги за период [from, to): сессия считается, если хоть часть её попала в период.</summary>
    public static PeriodSummary SummarizePeriod(IEnumerable<PlaySession> sessions, DateTimeOffset from, DateTimeOffset to)
    {
        var inPeriod = sessions.Where(s => Overlap(s, from, to) > TimeSpan.Zero).ToList();
        return new PeriodSummary(Total(inPeriod, from, to), inPeriod.Count, inPeriod.Select(s => s.GameId).Distinct().Count());
    }

    /// <summary>Время по играм за период, от большего к меньшему; игры без времени в периоде не попадают.</summary>
    public static IReadOnlyList<GameTotal> ByGame(IEnumerable<PlaySession> sessions, DateTimeOffset from, DateTimeOffset to) =>
        sessions
            .GroupBy(s => s.GameId)
            .Select(g => new GameTotal(g.Key, Total(g, from, to)))
            .Where(g => g.Total > TimeSpan.Zero)
            .OrderByDescending(g => g.Total)
            .ToList();

    /// <summary>Начало периода «последние N дней»: полночь местного дня N−1 дней назад.</summary>
    public static DateTimeOffset PeriodStart(DateTimeOffset now, TimeZoneInfo timeZone, int days) =>
        LocalMidnight(LocalDate(now, timeZone).AddDays(-(days - 1)), timeZone);

    /// <summary>Сколько времени сессий приходится на [from, to).</summary>
    public static TimeSpan Total(IEnumerable<PlaySession> sessions, DateTimeOffset from, DateTimeOffset to) =>
        sessions.Aggregate(TimeSpan.Zero, (sum, s) => sum + Overlap(s, from, to));

    private static TimeSpan Overlap(PlaySession session, DateTimeOffset from, DateTimeOffset to)
    {
        var start = session.Start > from ? session.Start : from;
        var end = session.End < to ? session.End : to;
        return end > start ? end - start : TimeSpan.Zero;
    }

    private static DateOnly LocalDate(DateTimeOffset time, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, timeZone).DateTime);

    private static DateTimeOffset LocalMidnight(DateOnly day, TimeZoneInfo timeZone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }
}
