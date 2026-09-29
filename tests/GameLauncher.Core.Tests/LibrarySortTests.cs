using GameLauncher.Core.Library;

namespace GameLauncher.Core.Tests;

public sealed class LibrarySortTests
{
    private static readonly DateTimeOffset Day = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static Game G(string name, int? playedDaysAgo = null, int minutes = 0) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ExePath = $@"C:\Games\{name}.exe",
        LastPlayedAt = playedDaysAgo is { } d ? Day.AddDays(-d) : null,
        TotalPlayTime = TimeSpan.FromMinutes(minutes),
    };

    private static List<string> Names(IEnumerable<Game> games, LibrarySortMode mode) =>
        LibrarySort.Sort(games, g => g, mode).Select(g => g.Name).ToList();

    [Fact]
    public void LastPlayed_RecentFirst_NeverPlayedLastByName()
    {
        var games = new[] { G("Яблоко"), G("Старая", 30), G("Альфа"), G("Свежая", 1) };

        Assert.Equal(["Свежая", "Старая", "Альфа", "Яблоко"], Names(games, LibrarySortMode.LastPlayed));
    }

    [Fact]
    public void PlayTime_MostPlayedFirst_TiesByName()
    {
        var games = new[] { G("Б", minutes: 10), G("А", minutes: 10), G("В", minutes: 500), G("Г") };

        Assert.Equal(["В", "А", "Б", "Г"], Names(games, LibrarySortMode.PlayTime));
    }

    [Fact]
    public void Name_RussianRules()
    {
        var games = new[] { G("ёжик"), G("Жук"), G("еда"), G("Ели"), G("abc"), G("Ёлка"), G("2077") };

        // Правила ru-RU (ICU, как и в Windows 11): цифры, кириллица, латиница; регистр не важен;
        // «ё» сравнивается как «е», поэтому «ёжик» раньше «Ели».
        Assert.Equal(["2077", "еда", "ёжик", "Ели", "Ёлка", "Жук", "abc"], Names(games, LibrarySortMode.Name));
    }

    [Fact]
    public void Added_KeepsLibraryOrder()
    {
        var games = new[] { G("Второй", 1), G("Первый"), G("Третий", minutes: 5) };

        Assert.Equal(["Второй", "Первый", "Третий"], Names(games, LibrarySortMode.Added));
    }
}
