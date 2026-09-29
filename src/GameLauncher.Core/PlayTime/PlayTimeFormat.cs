namespace GameLauncher.Core.PlayTime;

public static class PlayTimeFormat
{
    /// <summary>«Не запускалась», «меньше минуты», «45 мин», «3 ч 5 мин», «120 ч».</summary>
    public static string Format(TimeSpan time)
    {
        if (time <= TimeSpan.Zero)
        {
            return "Не запускалась";
        }

        if (time < TimeSpan.FromMinutes(1))
        {
            return "Меньше минуты";
        }

        var hours = (long)time.TotalHours;
        var minutes = time.Minutes;
        return hours switch
        {
            0 => $"{minutes} мин",
            >= 100 => $"{hours} ч",
            _ when minutes == 0 => $"{hours} ч",
            _ => $"{hours} ч {minutes} мин",
        };
    }
}
