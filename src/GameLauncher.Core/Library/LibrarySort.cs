using System.Globalization;

namespace GameLauncher.Core.Library;

/// <summary>Порядок карточек в библиотеке. Значения — индексы в списке сортировки на экране, не менять порядок.</summary>
public enum LibrarySortMode
{
    /// <summary>Недавно запущенные сверху; ни разу не запускавшиеся — в конце по названию.</summary>
    LastPlayed,

    /// <summary>Самые наигранные сверху.</summary>
    PlayTime,

    /// <summary>По алфавиту, без учёта регистра.</summary>
    Name,

    /// <summary>В порядке добавления.</summary>
    Added,
}

public static class LibrarySort
{
    // Русские правила: «ё» рядом с «е», латиница и кириллица — каждая по своему алфавиту.
    private static readonly StringComparer NameComparer = StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), ignoreCase: true);

    /// <summary>Упорядочить элементы (карточки, игры) по игре, которую возвращает <paramref name="game"/>. Сортировка устойчивая.</summary>
    public static IEnumerable<T> Sort<T>(IEnumerable<T> items, Func<T, Game> game, LibrarySortMode mode)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(game);

        return mode switch
        {
            // null меньше любой даты — при сортировке по убыванию незапускавшиеся уходят в конец.
            LibrarySortMode.LastPlayed => items.OrderByDescending(i => game(i).LastPlayedAt).ThenBy(i => game(i).Name, NameComparer),
            LibrarySortMode.PlayTime => items.OrderByDescending(i => game(i).TotalPlayTime).ThenBy(i => game(i).Name, NameComparer),
            LibrarySortMode.Name => items.OrderBy(i => game(i).Name, NameComparer),
            _ => items, // Added: библиотека хранит игры в порядке добавления
        };
    }
}
