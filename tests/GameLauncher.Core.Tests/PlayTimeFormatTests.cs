using GameLauncher.Core.PlayTime;

namespace GameLauncher.Core.Tests;

public sealed class PlayTimeFormatTests
{
    [Theory]
    [InlineData(0, "Не запускалась")]
    [InlineData(30, "Меньше минуты")]
    [InlineData(59, "Меньше минуты")]
    [InlineData(60, "1 мин")]
    [InlineData(45 * 60 + 59, "45 мин")]
    [InlineData(3600, "1 ч")]
    [InlineData(3 * 3600 + 5 * 60, "3 ч 5 мин")]
    [InlineData(99 * 3600 + 59 * 60, "99 ч 59 мин")]
    [InlineData(120 * 3600 + 30 * 60, "120 ч")]
    public void Format(int seconds, string expected)
    {
        Assert.Equal(expected, PlayTimeFormat.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void FormatDate_UsesRussianGenitiveMonthAndTimeZone()
    {
        var utcPlus3 = TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");
        var date = new DateTimeOffset(2026, 9, 29, 11, 5, 0, TimeSpan.Zero);

        Assert.Equal("29 сентября 2026, 14:05", PlayTimeFormat.FormatDate(date, utcPlus3));
    }

    [Fact]
    public void FormatDate_Null_ReturnsPlaceholder()
    {
        Assert.Equal("Ещё не запускалась", PlayTimeFormat.FormatDate(null, TimeZoneInfo.Utc));
        Assert.Equal("—", PlayTimeFormat.FormatDate(null, TimeZoneInfo.Utc, "—"));
    }
}
