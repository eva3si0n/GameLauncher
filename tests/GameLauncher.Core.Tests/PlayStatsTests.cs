using GameLauncher.Core.PlayTime;

namespace GameLauncher.Core.Tests;

public sealed class PlayStatsTests
{
    // Москва: UTC+3 без перехода на летнее время — «местный день» предсказуем.
    private static readonly TimeZoneInfo Msk = TimeZoneInfo.CreateCustomTimeZone("test+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");

    // «Сейчас»: 29 сентября 2026, 15:00 по Москве.
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 15, 0, 0, TimeSpan.FromHours(3));
    private static readonly Guid GameA = Guid.NewGuid();
    private static readonly Guid GameB = Guid.NewGuid();

    /// <summary>Сессия по московскому времени: день сентября, час начала, длительность в часах.</summary>
    private static PlaySession S(int day, double hour, double hours, Guid? game = null)
    {
        // Через целые минуты: AddHours(18 + 5 / 60.0) из-за округления double даёт 18:04:59.999.
        var start = new DateTimeOffset(2026, 9, day, 0, 0, 0, TimeSpan.FromHours(3)).AddMinutes(Math.Round(hour * 60));
        return new PlaySession(Guid.NewGuid(), game ?? GameA, start, start.AddMinutes(Math.Round(hours * 60)));
    }

    [Fact]
    public void Summarize_Empty_AllZero()
    {
        var summary = PlayStats.Summarize([], Now, Msk);

        Assert.Equal(new PlaySummary(TimeSpan.Zero, TimeSpan.Zero, 0, TimeSpan.Zero, TimeSpan.Zero), summary);
    }

    [Fact]
    public void Summarize_PeriodsStartAtLocalMidnight()
    {
        var sessions = new[]
        {
            S(29, 10, 2),   // сегодня
            S(23, 20, 1),   // 7-й день назад включительно (23-е — начало периода «7 дней»)
            S(22, 20, 1),   // 8 дней назад — только в «30 днях»
            S(1, 12, 3),    // 1 сентября — 28 дней назад, в «30 днях» (они начинаются 31 августа)
        };

        var summary = PlayStats.Summarize(sessions, Now, Msk);

        Assert.Equal(TimeSpan.FromHours(3), summary.Last7Days);
        Assert.Equal(TimeSpan.FromHours(7), summary.Last30Days);
        Assert.Equal(4, summary.SessionCount);
        Assert.Equal(TimeSpan.FromHours(7.0 / 4), summary.Average);
        Assert.Equal(TimeSpan.FromHours(3), summary.Longest);
    }

    [Fact]
    public void Summarize_SessionCrossingPeriodStart_CountsOnlyPartInside()
    {
        // 22-е 23:00 → 23-е 01:00: в «7 дней» (с полуночи 23-го) попадает только час.
        var summary = PlayStats.Summarize([S(22, 23, 2)], Now, Msk);

        Assert.Equal(TimeSpan.FromHours(1), summary.Last7Days);
        Assert.Equal(TimeSpan.FromHours(2), summary.Last30Days);
    }

    [Fact]
    public void Daily_SplitsAcrossMidnight_OldestFirst_IncludesToday()
    {
        var days = PlayStats.Daily([S(27, 22, 3), S(29, 9, 1)], Now, Msk, days: 3);

        Assert.Equal(
            [
                new DayTotal(new DateOnly(2026, 9, 27), TimeSpan.FromHours(2)),
                new DayTotal(new DateOnly(2026, 9, 28), TimeSpan.FromHours(1)),
                new DayTotal(new DateOnly(2026, 9, 29), TimeSpan.FromHours(1)),
            ],
            days);
    }

    [Fact]
    public void Daily_UsesLocalDate_NotUtc()
    {
        // 29-е 01:00 по Москве = 28-е 22:00 UTC — это всё равно 29-е.
        var days = PlayStats.Daily([S(29, 1, 1)], Now, Msk, days: 2);

        Assert.Equal(TimeSpan.Zero, days[0].Total);
        Assert.Equal(TimeSpan.FromHours(1), days[1].Total);
    }

    [Fact]
    public void ByGame_SortedDescending_SkipsGamesOutsidePeriod()
    {
        var gameC = Guid.NewGuid();
        var sessions = new[] { S(29, 10, 1, GameA), S(28, 10, 3, GameB), S(29, 12, 1, GameA), S(1, 10, 5, gameC) };

        var totals = PlayStats.ByGame(sessions, PlayStats.PeriodStart(Now, Msk, 7), Now);

        Assert.Equal([new GameTotal(GameB, TimeSpan.FromHours(3)), new GameTotal(GameA, TimeSpan.FromHours(2))], totals);
    }

    [Theory]
    [InlineData(0, "0 мин")]
    [InlineData(30, "Меньше минуты")]
    [InlineData(3900, "1 ч 5 мин")]
    public void FormatDuration(int seconds, string expected)
    {
        Assert.Equal(expected, PlayTimeFormat.FormatDuration(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void FormatSession_SameDay()
    {
        Assert.Equal("29 сентября, 18:05–19:40 · 1 ч 35 мин", PlayTimeFormat.FormatSession(S(29, 18 + 5 / 60.0, 95 / 60.0), Msk));
    }

    [Fact]
    public void FormatSession_AcrossMidnight_ShowsBothDates()
    {
        Assert.Equal(
            "28 сентября, 23:10 – 29 сентября, 01:05 · 1 ч 55 мин",
            PlayTimeFormat.FormatSession(S(28, 23 + 10 / 60.0, 115 / 60.0), Msk));
    }

    [Fact]
    public void FormatDay_Genitive()
    {
        Assert.Equal("1 октября", PlayTimeFormat.FormatDay(new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void SummarizePeriod_CountsOverlappingSessionsAndDistinctGames()
    {
        var sessions = new[]
        {
            S(29, 10, 1, GameA),
            S(28, 10, 2, GameA),
            S(22, 23, 2, GameB), // заходит в период на час (с полуночи 23-го)
            S(10, 10, 5, GameB), // вне периода
        };

        var summary = PlayStats.SummarizePeriod(sessions, PlayStats.PeriodStart(Now, Msk, 7), Now);

        Assert.Equal(new PeriodSummary(TimeSpan.FromHours(4), 3, 2), summary);
    }

    [Fact]
    public void SummarizePeriod_AllTime()
    {
        var summary = PlayStats.SummarizePeriod([S(1, 10, 5, GameA), S(29, 10, 1, GameB)], DateTimeOffset.MinValue, Now);

        Assert.Equal(new PeriodSummary(TimeSpan.FromHours(6), 2, 2), summary);
    }
}
