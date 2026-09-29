using System.Globalization;

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

    /// <summary>Длительность для статистики: «0 мин», «меньше минуты», дальше — как <see cref="Format"/>.</summary>
    public static string FormatDuration(TimeSpan time) => time <= TimeSpan.Zero ? "0 мин" : Format(time);

    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>«29 сентября 2026, 14:05» в заданном часовом поясе; null — «Ещё не запускалась».</summary>
    public static string FormatDate(DateTimeOffset? value, TimeZoneInfo timeZone, string whenNull = "Ещё не запускалась")
    {
        if (value is not { } date)
        {
            return whenNull;
        }

        var local = TimeZoneInfo.ConvertTime(date, timeZone);
        return local.ToString("d MMMM yyyy, HH:mm", Russian);
    }

    /// <summary>
    /// Строка сессии: «29 сентября, 18:05–19:40 · 1 ч 35 мин»; через полночь —
    /// «28 сентября, 23:10 – 29 сентября, 01:05 · 1 ч 55 мин».
    /// </summary>
    public static string FormatSession(PlaySession session, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(session);
        var start = TimeZoneInfo.ConvertTime(session.Start, timeZone);
        var end = TimeZoneInfo.ConvertTime(session.End, timeZone);
        var range = start.Date == end.Date
            ? $"{start.ToString("d MMMM, HH:mm", Russian)}–{end.ToString("HH:mm", Russian)}"
            : $"{start.ToString("d MMMM, HH:mm", Russian)} – {end.ToString("d MMMM, HH:mm", Russian)}";
        return $"{range} · {FormatDuration(session.Duration)}";
    }

    /// <summary>«29 сентября» — подпись дня в статистике.</summary>
    public static string FormatDay(DateOnly day) => day.ToString("d MMMM", Russian);
}
