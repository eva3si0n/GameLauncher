using System.Text.Json;

namespace GameLauncher.Core.Library;

/// <summary>
/// Хранит библиотеку в JSON-файле. Запись атомарная: сначала во временный файл, затем замена,
/// чтобы падение посреди записи не оставило испорченный файл.
/// </summary>
public sealed class JsonLibraryStore(string filePath) : ILibraryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public string FilePath { get; } = filePath;

    /// <summary>
    /// Если при последней загрузке файл оказался нечитаемым, он был переименован —
    /// здесь путь к этой копии. Иначе null.
    /// </summary>
    public string? CorruptBackupPath { get; private set; }

    public LibraryData Load()
    {
        CorruptBackupPath = null;
        if (!File.Exists(FilePath))
        {
            return new LibraryData();
        }

        try
        {
            using var stream = File.OpenRead(FilePath);
            var data = JsonSerializer.Deserialize<LibraryData>(stream, JsonOptions)
                ?? throw new JsonException("Файл библиотеки пуст.");
            data.Games.RemoveAll(g => g is null || string.IsNullOrWhiteSpace(g.ExePath));
            return data;
        }
        catch (JsonException)
        {
            // Не теряем данные молча: откладываем испорченный файл в сторону и начинаем с пустой библиотеки.
            CorruptBackupPath = $"{FilePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(FilePath, CorruptBackupPath, overwrite: true);
            return new LibraryData();
        }
    }

    public void Save(LibraryData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        AtomicFile.Write(FilePath, stream => JsonSerializer.Serialize(stream, data, JsonOptions));
    }
}
