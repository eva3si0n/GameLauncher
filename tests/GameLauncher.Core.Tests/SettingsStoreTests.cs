using GameLauncher.Core.Settings;

namespace GameLauncher.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private string SettingsPath => Path.Combine(_dir.Path, "settings.json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Equal("tr", settings.SteamRegion);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips_AndStoresEnumAsText()
    {
        var store = new SettingsStore(SettingsPath);

        store.Save(new AppSettings { Theme = AppTheme.Dark, SteamRegion = "us" });
        var loaded = store.Load();

        Assert.Equal(AppTheme.Dark, loaded.Theme);
        Assert.Equal("us", loaded.SteamRegion);
        Assert.Contains("\"Dark\"", File.ReadAllText(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Theory]
    [InlineData("{ битый")]
    [InlineData("""{"theme":"Rainbow"}""")]
    public void Load_BadFile_ReturnsDefaults(string json)
    {
        File.WriteAllText(SettingsPath, json);

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(AppTheme.System, settings.Theme);
    }

    [Fact]
    public void Load_EmptyRegion_FallsBackToDefault()
    {
        File.WriteAllText(SettingsPath, """{"theme":"Light","steamRegion":" "}""");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(AppTheme.Light, settings.Theme);
        Assert.Equal("tr", settings.SteamRegion);
    }

    [Fact]
    public void WindowPlacement_RoundTrips()
    {
        var store = new SettingsStore(SettingsPath);

        store.Save(new AppSettings { Window = new WindowPlacement(10, 20, 1280, 800, true) });

        Assert.Equal(new WindowPlacement(10, 20, 1280, 800, true), store.Load().Window);
    }

    [Fact]
    public void WindowPlacement_WithZeroSize_IsDropped()
    {
        File.WriteAllText(SettingsPath, """{"window":{"x":0,"y":0,"width":0,"height":600,"isMaximized":false}}""");

        Assert.Null(new SettingsStore(SettingsPath).Load().Window);
    }
}
