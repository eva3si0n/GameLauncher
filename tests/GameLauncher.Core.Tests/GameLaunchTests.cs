using GameLauncher.Core.Library;

namespace GameLauncher.Core.Tests;

public sealed class GameLaunchTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void CreateStartInfo_WorkingDirectoryIsExeFolder()
    {
        var exe = _dir.CreateFile(Path.Combine("Games", "Doom", "bin", "doom.exe"));
        var game = new Game { Id = Guid.NewGuid(), Name = "Doom", ExePath = exe };

        var info = GameLaunch.CreateStartInfo(game);

        Assert.Equal(exe, info.FileName);
        Assert.Equal(Path.GetDirectoryName(exe), info.WorkingDirectory);
        Assert.True(info.UseShellExecute);
    }

    [Fact]
    public void CreateStartInfo_MissingExe_Throws()
    {
        var game = new Game { Id = Guid.NewGuid(), Name = "Нет", ExePath = Path.Combine(_dir.Path, "missing.exe") };

        Assert.Throws<FileNotFoundException>(() => GameLaunch.CreateStartInfo(game));
    }
}
