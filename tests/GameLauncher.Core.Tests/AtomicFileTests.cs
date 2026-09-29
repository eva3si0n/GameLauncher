namespace GameLauncher.Core.Tests;

public sealed class AtomicFileTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Write_CreatesDirectoryAndLeavesNoTemp()
    {
        var path = Path.Combine(_dir.Path, "a", "b", "file.bin");

        AtomicFile.WriteAllBytes(path, [1, 2, 3]);

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Write_FailureInTheMiddle_KeepsOriginalAndRemovesTemp()
    {
        var path = Path.Combine(_dir.Path, "file.bin");
        AtomicFile.WriteAllBytes(path, [1]);

        Assert.Throws<InvalidOperationException>(() => AtomicFile.Write(path, stream =>
        {
            stream.Write([9, 9, 9]);
            throw new InvalidOperationException("сбой посреди записи");
        }));

        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task WriteAsync_ReplacesFile()
    {
        var path = Path.Combine(_dir.Path, "file.bin");
        AtomicFile.WriteAllBytes(path, [1]);

        await AtomicFile.WriteAsync(path, (s, ct) => s.WriteAsync(new byte[] { 7, 7 }, ct).AsTask(), TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 7, 7 }, File.ReadAllBytes(path));
    }
}
