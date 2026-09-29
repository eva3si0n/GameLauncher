namespace GameLauncher.Core.Library;

/// <summary>Поиск по библиотеке: без учёта регистра, «ё» = «е», все слова запроса должны встречаться в названии.</summary>
public static class LibrarySearch
{
    public static bool Matches(string name, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var normalizedName = Normalize(name);
        return query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(word => normalizedName.Contains(Normalize(word), StringComparison.Ordinal));
    }

    private static string Normalize(string text) =>
        text.ToLowerInvariant().Replace('ё', 'е');
}
