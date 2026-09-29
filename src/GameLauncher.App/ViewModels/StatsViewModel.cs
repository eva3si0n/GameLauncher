using CommunityToolkit.Mvvm.ComponentModel;
using GameLauncher.Core.PlayTime;

namespace GameLauncher.App.ViewModels;

/// <summary>Строка «Больше всего играли»: игра, время за период и полоска относительно лидера.</summary>
public sealed record TopGameItem(GameItemViewModel Game, string TimeText, double BarWidth)
{
    public string Name => Game.Name;
}

/// <summary>Экран «Статистика»: итоги по всем играм за неделю, месяц или всё время, график по дням, самые наигранные игры.</summary>
public sealed class StatsViewModel : ObservableObject
{
    private const double TopBarMaxWidth = 240;
    private const int TopCount = 10;

    private readonly PlayHistory _history;
    private readonly Func<IReadOnlyCollection<GameItemViewModel>> _games;
    private int _periodIndex;

    public StatsViewModel(PlayHistory history, Func<IReadOnlyCollection<GameItemViewModel>> games)
    {
        _history = history;
        _games = games;
        Refresh();
    }

    /// <summary>«За неделю / За месяц / За всё время».</summary>
    public int PeriodIndex
    {
        get => _periodIndex;
        set
        {
            if (value is < 0 or > 2 || !SetProperty(ref _periodIndex, value))
            {
                return;
            }

            Refresh();
        }
    }

    public bool HasHistory { get; private set; }

    public bool HasNoHistory => !HasHistory;

    public string TotalText { get; private set; } = "";

    public string SessionCountText { get; private set; } = "";

    public string GameCountText { get; private set; } = "";

    /// <summary>Сумма «Сыграно» по библиотеке — с временем, наигранным до появления истории.</summary>
    public string LibraryTotalText { get; private set; } = "";

    public string ChartTitle { get; private set; } = "";

    public IReadOnlyList<DayBar> Days { get; private set; } = [];

    public string ChartCaption { get; private set; } = "";

    public IReadOnlyList<TopGameItem> TopGames { get; private set; } = [];

    /// <summary>Пересчитать (открытие экрана, смена периода, новая запись в истории).</summary>
    public void Refresh()
    {
        var now = DateTimeOffset.Now;
        var zone = TimeZoneInfo.Local;
        var sessions = _history.Sessions;
        var games = _games();

        var (from, chartDays, chartTitle) = _periodIndex switch
        {
            0 => (PlayStats.PeriodStart(now, zone, 7), 7, "По дням за неделю"),
            1 => (PlayStats.PeriodStart(now, zone, 30), 30, "По дням за месяц"),
            _ => (DateTimeOffset.MinValue, 30, "По дням за последние 30 дней"),
        };

        var summary = PlayStats.SummarizePeriod(sessions, from, now);
        HasHistory = sessions.Count > 0;
        TotalText = PlayTimeFormat.FormatDuration(summary.Total);
        SessionCountText = summary.SessionCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        GameCountText = summary.GameCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        LibraryTotalText = PlayTimeFormat.FormatDuration(games.Aggregate(TimeSpan.Zero, (sum, g) => sum + g.Game.TotalPlayTime));

        var days = PlayStats.Daily(sessions, now, zone, chartDays);
        ChartTitle = chartTitle;
        Days = DayBar.Build(days);
        ChartCaption = DayBar.Caption(days);

        var byId = games.ToDictionary(g => g.Id);
        var top = PlayStats.ByGame(sessions, from, now)
            .Where(t => byId.ContainsKey(t.GameId))
            .Take(TopCount)
            .ToList();
        var max = top.Count == 0 ? TimeSpan.Zero : top[0].Total;
        TopGames = top
            .Select(t => new TopGameItem(byId[t.GameId], PlayTimeFormat.FormatDuration(t.Total), Math.Max(4, TopBarMaxWidth * (t.Total / max))))
            .ToList();

        foreach (var name in new[]
        {
            nameof(HasHistory), nameof(HasNoHistory), nameof(TotalText), nameof(SessionCountText), nameof(GameCountText),
            nameof(LibraryTotalText), nameof(ChartTitle), nameof(Days), nameof(ChartCaption), nameof(TopGames),
        })
        {
            OnPropertyChanged(name);
        }
    }
}
