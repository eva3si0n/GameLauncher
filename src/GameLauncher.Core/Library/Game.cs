namespace GameLauncher.Core.Library;

/// <summary>Игра в библиотеке.</summary>
public sealed class Game
{
    public required Guid Id { get; init; }

    public required string Name { get; set; }

    /// <summary>Полный путь к .exe игры.</summary>
    public required string ExePath { get; init; }

    public DateTimeOffset AddedAt { get; init; }

    /// <summary>Суммарное время в игре.</summary>
    public TimeSpan TotalPlayTime { get; set; }

    /// <summary>Когда игру видели запущенной в последний раз.</summary>
    public DateTimeOffset? LastPlayedAt { get; set; }
}
