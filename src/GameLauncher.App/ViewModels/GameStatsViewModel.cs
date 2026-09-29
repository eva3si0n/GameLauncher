using GameLauncher.Core.PlayTime;

namespace GameLauncher.App.ViewModels;

/// <summary>Столбик графика по дням. Высота — в пикселях; пустой день — тонкая полупрозрачная черта.</summary>
public sealed record DayBar(double Height, double Opacity, string ToolTip);

/// <summary>Статистика игры на её странице: итоги, график за 30 дней, последние сессии.</summary>
public sealed class GameStatsViewModel
{
    public const int ChartDays = 30;
    public const int RecentCount = 10;
    private const double ChartHeight = 80;

    public GameStatsViewModel(IReadOnlyCollection<PlaySession> sessions, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var summary = PlayStats.Summarize(sessions, now, timeZone);
        Last7DaysText = PlayTimeFormat.FormatDuration(summary.Last7Days);
        Last30DaysText = PlayTimeFormat.FormatDuration(summary.Last30Days);
        SessionCountText = summary.SessionCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        AverageText = PlayTimeFormat.FormatDuration(summary.Average);
        LongestText = PlayTimeFormat.FormatDuration(summary.Longest);
        HasHistory = summary.SessionCount > 0;

        var days = PlayStats.Daily(sessions, now, timeZone, ChartDays);
        var max = days.Max(d => d.Total);
        Days = days.Select(d => d.Total > TimeSpan.Zero
                ? new DayBar(Math.Max(3, ChartHeight * (d.Total / max)), 1, $"{PlayTimeFormat.FormatDay(d.Day)}: {PlayTimeFormat.FormatDuration(d.Total)}")
                : new DayBar(2, 0.25, $"{PlayTimeFormat.FormatDay(d.Day)}: не играли"))
            .ToList();
        ChartCaption = $"{PlayTimeFormat.FormatDay(days[0].Day)} — {PlayTimeFormat.FormatDay(days[^1].Day)}";

        Recent = sessions
            .OrderByDescending(s => s.Start)
            .Take(RecentCount)
            .Select(s => PlayTimeFormat.FormatSession(s, timeZone))
            .ToList();
    }

    /// <summary>Есть хоть одна записанная сессия.</summary>
    public bool HasHistory { get; }

    public bool HasNoHistory => !HasHistory;

    public string Last7DaysText { get; }

    public string Last30DaysText { get; }

    public string SessionCountText { get; }

    public string AverageText { get; }

    public string LongestText { get; }

    public IReadOnlyList<DayBar> Days { get; }

    public string ChartCaption { get; }

    public IReadOnlyList<string> Recent { get; }
}
