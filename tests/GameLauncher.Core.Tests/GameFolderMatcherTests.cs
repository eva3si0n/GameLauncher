using GameLauncher.Core.PlayTime;

namespace GameLauncher.Core.Tests;

public sealed class GameFolderMatcherTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "gl-matcher");

    private static string P(params string[] parts) => Path.Combine([Root, .. parts]);

    [Fact]
    public void MatchesExeInSameFolderAndSubfolders()
    {
        var matcher = new GameFolderMatcher(P("Games", "Doom", "doom.exe"));

        Assert.True(matcher.Matches(P("Games", "Doom", "doom.exe")));
        Assert.True(matcher.Matches(P("Games", "Doom", "bin", "doom_x64.exe")));
    }

    [Fact]
    public void DoesNotMatchSiblingFolderWithSamePrefix()
    {
        var matcher = new GameFolderMatcher(P("Games", "Doom", "doom.exe"));

        Assert.False(matcher.Matches(P("Games", "Doom Eternal", "doom.exe")));
        Assert.False(matcher.Matches(P("Games", "doom.exe")));
    }

    [Fact]
    public void MatchesIgnoringCase()
    {
        var matcher = new GameFolderMatcher(P("Games", "Doom", "doom.exe"));

        Assert.True(matcher.Matches(P("GAMES", "DOOM", "BIN", "DOOM.EXE")));
    }

    [Fact]
    public void ExeInDriveRoot_MatchesOnlyItself()
    {
        var rootExe = Path.Combine(Path.GetPathRoot(Root)!, "game.exe");
        var matcher = new GameFolderMatcher(rootExe);

        Assert.True(matcher.Matches(rootExe));
        Assert.False(matcher.Matches(Path.Combine(Path.GetPathRoot(Root)!, "Windows", "explorer.exe")));
    }
}
