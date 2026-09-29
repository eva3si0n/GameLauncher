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
}
