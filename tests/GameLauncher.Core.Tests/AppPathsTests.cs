using GameLauncher.Core;

namespace GameLauncher.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void GetDataDirectory_AppendsAppFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "root");

        Assert.Equal(Path.Combine(root, "GameLauncher"), AppPaths.GetDataDirectory(root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetDataDirectory_RejectsBlankRoot(string root)
    {
        Assert.Throws<ArgumentException>(() => AppPaths.GetDataDirectory(root));
    }
}
