using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using GameLauncher.Core.Library;

namespace GameLauncher.Core.Backup;

/// <summary>Сведения о резервной копии — для подтверждения восстановления.</summary>
public sealed record BackupInfo(DateTimeOffset CreatedAt, int GameCount, bool IncludesArtwork);

/// <summary>Копия не создана или не может быть восстановлена; текст — для пользователя.</summary>
public sealed class BackupException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>
/// Резервные копии данных лаунчера — zip с файлами:
/// <c>backup.json</c> (описание копии), <c>library.json</c>, <c>settings.json</c>, <c>sessions.json</c>, <c>info\*</c>, <c>artwork\*</c>.
/// Ключ SteamGridDB не копируется: он зашифрован DPAPI для текущего пользователя Windows и в другом месте бесполезен.
/// <para>
/// Файлы данных читаются с FileShare.ReadWrite | Delete: запись библиотеки (временный файл + замена) не упадёт,
/// даже если совпадёт с копированием, а копия получит либо старую, либо новую версию файла целиком.
/// </para>
/// </summary>
public sealed class BackupService(string dataDirectory, TimeProvider? time = null)
{
    public const string ManifestName = "backup.json";
    public const int FormatVersion = 1;

    /// <summary>Сколько ежедневных автокопий хранить.</summary>
    public const int AutoBackupsToKeep = 7;

    /// <summary>Сколько копий «перед восстановлением» хранить.</summary>
    public const int SafetyBackupsToKeep = 3;

    private const string LibraryName = "library.json";
    private const string SettingsName = "settings.json";
    private const string SessionsName = "sessions.json";

    // Необязательные файлы верхнего уровня: если их нет в копии, при восстановлении остаются текущие.
    private static readonly string[] OptionalFiles = [SettingsName, SessionsName];
    private const string StagingName = ".restore";
    private static readonly string[] Folders = ["info", "artwork"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    // Автокопия идёт в фоне — не даём ей пересечься с ручной копией или восстановлением.
    private readonly object _sync = new();

    /// <summary>Папка автоматических копий и копий перед восстановлением.</summary>
    public string AutoBackupDirectory => Path.Combine(dataDirectory, "backups");

    /// <summary>Имя файла для ручной копии: GameLauncher-backup-2026-09-29.zip.</summary>
    public string SuggestedFileName => $"GameLauncher-backup-{FormatDate(_time.GetLocalNow())}.zip";

    /// <summary>
    /// Создать копию. <paramref name="includeFolders"/> — класть ли обложки и описания (ежедневные автокопии — без них).
    /// </summary>
    public BackupInfo Create(string zipPath, bool includeFolders = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        lock (_sync)
        {
            return CreateCore(zipPath, includeFolders);
        }
    }

    private BackupInfo CreateCore(string zipPath, bool includeFolders)
    {

        var library = ReadShared(Path.Combine(dataDirectory, LibraryName))
            ?? throw new BackupException("Библиотека ещё пуста — копировать нечего.");
        var optional = OptionalFiles
            .Select(name => (Name: name, Bytes: ReadShared(Path.Combine(dataDirectory, name))))
            .Where(f => f.Bytes is not null)
            .ToList();
        var info = new BackupInfo(_time.GetLocalNow(), ParseLibrary(library, "Библиотека").Games.Count, includeFolders);

        try
        {
            AtomicFile.Write(zipPath, stream =>
            {
                using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                AddEntry(zip, ManifestName, JsonSerializer.SerializeToUtf8Bytes(
                    new Manifest(FormatVersion, info.CreatedAt, info.GameCount, info.IncludesArtwork), JsonOptions));
                AddEntry(zip, LibraryName, library);
                foreach (var (name, bytes) in optional)
                {
                    AddEntry(zip, name, bytes!);
                }

                if (includeFolders)
                {
                    foreach (var folder in Folders)
                    {
                        AddFolder(zip, folder);
                    }
                }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BackupException($"Не удалось записать копию: {ex.Message}", ex);
        }

        return info;
    }

    /// <summary>Проверить копию и прочитать сведения о ней. Ничего не меняет.</summary>
    public BackupInfo Read(string zipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var manifestEntry = zip.GetEntry(ManifestName)
                ?? throw new BackupException("Это не резервная копия GameLauncher.");
            var manifest = JsonSerializer.Deserialize<Manifest>(ReadEntry(manifestEntry), JsonOptions)
                ?? throw new BackupException("Описание копии пустое.");
            if (manifest.Format > FormatVersion)
            {
                throw new BackupException("Копия сделана более новой версией лаунчера — обновите лаунчер.");
            }

            var libraryEntry = zip.GetEntry(LibraryName)
                ?? throw new BackupException("В копии нет библиотеки.");
            var library = ParseLibrary(ReadEntry(libraryEntry), "Библиотека в копии");
            var includesArtwork = zip.Entries.Any(e => MapEntry(e.FullName) is { } path && Path.GetDirectoryName(path) is { Length: > 0 });
            return new BackupInfo(manifest.CreatedAt, library.Games.Count, includesArtwork);
        }
        catch (InvalidDataException ex)
        {
            throw new BackupException("Файл повреждён или это не zip-архив.", ex);
        }
        catch (JsonException ex)
        {
            throw new BackupException("Копия повреждена: не удалось прочитать данные.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BackupException($"Не удалось открыть копию: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Восстановить данные из копии. Текущие данные сначала сохраняются в копию «перед восстановлением»
    /// (<see cref="AutoBackupDirectory"/>). Обложки и описания заменяются, только если они есть в копии.
    /// После восстановления лаунчер нужно перезапустить, не сохраняя ничего из памяти.
    /// </summary>
    public BackupInfo Restore(string zipPath)
    {
        lock (_sync)
        {
            return RestoreCore(zipPath);
        }
    }

    private BackupInfo RestoreCore(string zipPath)
    {
        var info = Read(zipPath);
        var staging = Path.Combine(dataDirectory, StagingName);
        try
        {
            DeleteDirectory(staging);
            Directory.CreateDirectory(staging);
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (MapEntry(entry.FullName) is not { } relative)
                    {
                        continue; // описание копии и всё незнакомое (в том числе пути с «..») не распаковываем
                    }

                    var target = Path.Combine(staging, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            }

            if (File.Exists(Path.Combine(dataDirectory, LibraryName)))
            {
                var safety = Path.Combine(AutoBackupDirectory, $"before-restore-{FormatStamp(_time.GetLocalNow())}.zip");
                CreateCore(safety, includeFolders: true);
                Prune("before-restore-*.zip", SafetyBackupsToKeep);
            }

            File.Move(Path.Combine(staging, LibraryName), Path.Combine(dataDirectory, LibraryName), overwrite: true);
            foreach (var name in OptionalFiles)
            {
                if (File.Exists(Path.Combine(staging, name)))
                {
                    File.Move(Path.Combine(staging, name), Path.Combine(dataDirectory, name), overwrite: true);
                }
            }

            foreach (var folder in Folders)
            {
                var restored = Path.Combine(staging, folder);
                if (Directory.Exists(restored))
                {
                    var current = Path.Combine(dataDirectory, folder);
                    DeleteDirectory(current);
                    Directory.Move(restored, current);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new BackupException($"Не удалось восстановить данные: {ex.Message}", ex);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }

        return info;
    }

    /// <summary>
    /// Ежедневная автокопия библиотеки, настроек и истории сессий (без обложек): одна за день, хранятся последние
    /// <see cref="AutoBackupsToKeep"/>. Возвращает путь созданной копии или null, если сегодняшняя уже есть.
    /// </summary>
    public string? EnsureDailyBackup()
    {
        lock (_sync)
        {
            return EnsureDailyBackupCore();
        }
    }

    private string? EnsureDailyBackupCore()
    {
        var path = Path.Combine(AutoBackupDirectory, $"auto-{FormatDate(_time.GetLocalNow())}.zip");
        if (File.Exists(path) || !File.Exists(Path.Combine(dataDirectory, LibraryName)))
        {
            return null;
        }

        CreateCore(path, includeFolders: false);
        Prune("auto-*.zip", AutoBackupsToKeep);
        return path;
    }

    /// <summary>
    /// Путь внутри копии → путь относительно папки данных; null — запись не восстанавливается.
    /// Допускаются только library.json, settings.json и файлы прямо в info/ и artwork/ — так архив
    /// не может записать что-либо за пределы папки данных.
    /// </summary>
    private static string? MapEntry(string fullName)
    {
        var name = fullName.Replace('\\', '/');
        if (name == LibraryName || OptionalFiles.Contains(name))
        {
            return name;
        }

        var slash = name.IndexOf('/');
        if (slash <= 0)
        {
            return null;
        }

        var folder = name[..slash];
        var file = name[(slash + 1)..];
        if (!Folders.Contains(folder)
            || file.Length == 0
            || file is "." or ".."
            || file.Contains('/')
            || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || file.IndexOfAny(['\\', ':', '*', '?', '"', '<', '>', '|']) >= 0)
        {
            return null;
        }

        return Path.Combine(folder, file);
    }

    /// <param name="what">Чья библиотека — для текста ошибки: «Библиотека» или «Библиотека в копии».</param>
    private static LibraryData ParseLibrary(byte[] json, string what)
    {
        LibraryData? data;
        try
        {
            data = JsonSerializer.Deserialize<LibraryData>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new BackupException($"{what} повреждена.", ex);
        }

        if (data is null)
        {
            throw new BackupException($"{what} повреждена.");
        }

        if (data.Version > LibraryData.CurrentVersion)
        {
            throw new BackupException($"{what} сохранена более новой версией лаунчера — обновите лаунчер.");
        }

        return data;
    }

    private void AddFolder(ZipArchive zip, string folder)
    {
        var directory = Path.Combine(dataDirectory, folder);
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || ReadShared(file) is not { } bytes)
            {
                continue; // недописанный файл или удалён между перечислением и чтением
            }

            // Картинки уже сжаты — второй раз не жмём.
            AddEntry(zip, $"{folder}/{name}", bytes, folder == "artwork" ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
        }
    }

    private static void AddEntry(ZipArchive zip, string name, byte[] bytes, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var entry = zip.CreateEntry(name, level).Open();
        entry.Write(bytes);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Прочитать файл, не мешая его атомарной замене; null — файла нет.</summary>
    private static byte[]? ReadShared(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private void Prune(string pattern, int keep)
    {
        if (!Directory.Exists(AutoBackupDirectory))
        {
            return;
        }

        // Дата в имени сортируется как строка: новые — в конце.
        foreach (var old in Directory.EnumerateFiles(AutoBackupDirectory, pattern).Order(StringComparer.Ordinal).SkipLast(keep))
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Удалим в следующий раз.
            }
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            DeleteDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string FormatDate(DateTimeOffset time) => time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatStamp(DateTimeOffset time) => time.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);

    private sealed record Manifest(int Format, DateTimeOffset CreatedAt, int Games, bool IncludesArtwork);
}
