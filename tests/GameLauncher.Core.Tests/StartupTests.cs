using GameLauncher.Core.Settings;
using GameLauncher.Core.Startup;

namespace GameLauncher.Core.Tests;

public sealed class StartupTests : IDisposable
{
    private const string Exe = @"C:\Games\Game Launcher\GameLauncher.exe";

    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--tray" }, true)]
    [InlineData(new[] { "--TRAY" }, true)]
    [InlineData(new[] { "--other", "--tray" }, true)]
    [InlineData(new[] { "tray" }, false)]
    public void Parse_DetectsTrayArgument(string[] args, bool expected)
    {
        Assert.Equal(expected, StartupOptions.Parse(args).StartInTray);
    }

    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "--wait-pid=1234" }, 1234)]
    [InlineData(new[] { "--tray", "--WAIT-PID=42" }, 42)]
    [InlineData(new[] { "--wait-pid=" }, null)]
    [InlineData(new[] { "--wait-pid=abc" }, null)]
    [InlineData(new[] { "--wait-pid=-5" }, null)]
    [InlineData(new[] { "--wait-pid=0" }, null)]
    public void Parse_WaitForProcessId(string[] args, int? expected)
    {
        Assert.Equal(expected, StartupOptions.Parse(args).WaitForProcessId);
    }

    [Fact]
    public void Command_QuotesPathAndAddsTrayArgument()
    {
        var autostart = new Autostart(new FakeStore(), Exe);

        Assert.Equal("\"C:\\Games\\Game Launcher\\GameLauncher.exe\" --tray", autostart.Command);
    }

    [Fact]
    public void SetEnabled_WritesAndRemovesCommand()
    {
        var store = new FakeStore();
        var autostart = new Autostart(store, Exe);
        Assert.False(autostart.IsEnabled);

        autostart.SetEnabled(true);
        Assert.True(autostart.IsEnabled);
        Assert.Equal(autostart.Command, store.Value);

        autostart.SetEnabled(false);
        Assert.False(autostart.IsEnabled);
        Assert.Null(store.Value);
    }

    [Fact]
    public void RepairIfMoved_RewritesStalePath()
    {
        var store = new FakeStore { Value = "\"D:\\Old\\GameLauncher.exe\" --tray" };
        var autostart = new Autostart(store, Exe);

        Assert.True(autostart.RepairIfMoved());
        Assert.Equal(autostart.Command, store.Value);
        Assert.Equal(1, store.Writes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"C:\\Games\\Game Launcher\\GameLauncher.exe\" --tray")]
    [InlineData("\"c:\\games\\game launcher\\gamelauncher.exe\" --TRAY")]
    public void RepairIfMoved_DisabledOrCurrent_DoesNotWrite(string? value)
    {
        var store = new FakeStore { Value = value };

        Assert.False(new Autostart(store, Exe).RepairIfMoved());
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void Settings_TrayDefaults_AndRoundTrip()
    {
        var path = Path.Combine(_dir.Path, "settings.json");
        var store = new SettingsStore(path);

        var defaults = store.Load();
        Assert.True(defaults.CloseToTray);
        Assert.False(defaults.TrayHintShown);

        store.Save(new AppSettings { CloseToTray = false, TrayHintShown = true });
        var loaded = store.Load();
        Assert.False(loaded.CloseToTray);
        Assert.True(loaded.TrayHintShown);
    }

    [Fact]
    public void Settings_OldFileWithoutTrayFields_KeepsDefaults()
    {
        var path = Path.Combine(_dir.Path, "settings.json");
        File.WriteAllText(path, """{"theme":"Dark","steamRegion":"us"}""");

        var loaded = new SettingsStore(path).Load();

        Assert.True(loaded.CloseToTray);
        Assert.False(loaded.TrayHintShown);
    }

    private sealed class FakeStore : IAutostartStore
    {
        public string? Value { get; set; }

        public int Writes { get; private set; }

        public string? Read() => Value;

        public void Write(string? command)
        {
            Value = command;
            Writes++;
        }
    }
}
