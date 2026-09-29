namespace GameLauncher.Core.Library;

/// <summary>Содержимое файла библиотеки.</summary>
public sealed class LibraryData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public List<Game> Games { get; set; } = [];
}
