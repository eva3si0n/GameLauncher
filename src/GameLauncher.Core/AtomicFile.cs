namespace GameLauncher.Core;

/// <summary>
/// Атомарная запись файла: данные пишутся во временный файл рядом, сбрасываются на диск и только потом
/// заменяют целевой. Падение посреди записи оставляет прежний файл целым.
/// </summary>
public static class AtomicFile
{
    public static void Write(string path, Action<Stream> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var tempPath = PrepareTemp(path);
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath); // после успешной замены файла уже нет — ничего не делает
        }
    }

    public static void WriteAllBytes(string path, byte[] bytes) => Write(path, stream => stream.Write(bytes));

    public static async Task WriteAsync(string path, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);

        var tempPath = PrepareTemp(path);
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await write(stream, cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static string PrepareTemp(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return path + ".tmp";
    }
}
