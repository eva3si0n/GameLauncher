using GameLauncher.Core.Library;

namespace GameLauncher.Core.Tests;

public sealed class GameImporterTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly GameLibrary _library;
    private readonly FakeShortcuts _shortcuts = new();
    private readonly GameImporter _importer;

    public GameImporterTests()
    {
        _library = new GameLibrary(new JsonLibraryStore(Path.Combine(_dir.Path, "library.json")));
        _importer = new GameImporter(_library, _shortcuts);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Exe_IsAdded_WithExeName()
    {
        var exe = _dir.CreateFile(Path.Combine("Games", "Witcher", "witcher3.exe"));

        var result = Assert.Single(_importer.Import([exe]));

        Assert.Equal(ImportOutcome.Added, result.Outcome);
        Assert.Equal("witcher3", result.Game!.Name);
        Assert.Single(_library.Games);
        Assert.Null(GameImporter.Summarize([result])); // всё добавлено — сообщать нечего
    }

    [Fact]
    public void Shortcut_AddsTarget_WithShortcutName()
    {
        var exe = _dir.CreateFile(Path.Combine("Games", "Witcher", "witcher3.exe"));
        var lnk = _dir.CreateFile("Ведьмак 3.lnk");
        _shortcuts.Targets[lnk] = new ShortcutTarget(exe, "");

        var result = Assert.Single(_importer.Import([lnk]));

        Assert.Equal(ImportOutcome.Added, result.Outcome);
        Assert.Equal("Ведьмак 3", result.Game!.Name);
        Assert.Equal(exe, result.Game.ExePath);
    }

    [Fact]
    public void ShortcutWithArguments_AddedWithNote()
    {
        var exe = _dir.CreateFile(Path.Combine("Games", "Game", "game.exe"));
        var lnk = _dir.CreateFile("Game.lnk");
        _shortcuts.Targets[lnk] = new ShortcutTarget(exe, " -dx11 ");

        var results = _importer.Import([lnk]);

        Assert.Equal(ImportOutcome.Added, results[0].Outcome);
        Assert.Contains("-dx11", results[0].Note);
        Assert.Contains("аргументы ярлыка не сохранены: -dx11", GameImporter.Summarize(results));
    }

    [Fact]
    public void ShortcutToStoreLauncher_Skipped()
    {
        var steam = _dir.CreateFile(Path.Combine("Steam", "steam.exe"));
        var lnk = _dir.CreateFile("Portal 2.lnk");
        _shortcuts.Targets[lnk] = new ShortcutTarget(steam, "-applaunch 620");

        var result = Assert.Single(_importer.Import([lnk]));

        Assert.Equal(ImportOutcome.Skipped, result.Outcome);
        Assert.Contains("лаунчер магазина", result.Note);
        Assert.Empty(_library.Games);
    }

    [Fact]
    public void GameOwnLauncherExe_IsNotMistakenForStore()
    {
        var launcher = _dir.CreateFile(Path.Combine("Games", "Witcher", "launcher.exe"));
        var lnk = _dir.CreateFile("Witcher.lnk");
        _shortcuts.Targets[lnk] = new ShortcutTarget(launcher, "");

        Assert.Equal(ImportOutcome.Added, Assert.Single(_importer.Import([lnk])).Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ShortcutWithoutTarget_Skipped(string? target)
    {
        var lnk = _dir.CreateFile("Broken.lnk");
        _shortcuts.Targets[lnk] = target is null ? null : new ShortcutTarget(target, "");

        Assert.Equal(ImportOutcome.Skipped, Assert.Single(_importer.Import([lnk])).Outcome);
    }

    [Fact]
    public void ShortcutToNonExe_Skipped()
    {
        var doc = _dir.CreateFile("readme.txt");
        var lnk = _dir.CreateFile("Readme.lnk");
        _shortcuts.Targets[lnk] = new ShortcutTarget(doc, "");

        var result = Assert.Single(_importer.Import([lnk]));

        Assert.Equal(ImportOutcome.Skipped, result.Outcome);
        Assert.Contains("не на exe", result.Note);
    }

    [Fact]
    public void ResolverThrows_SkippedWithReason()
    {
        var lnk = _dir.CreateFile("Weird.lnk");
        _shortcuts.Throw = true;

        var result = Assert.Single(_importer.Import([lnk]));

        Assert.Equal(ImportOutcome.Skipped, result.Outcome);
        Assert.Contains("не удалось прочитать ярлык", result.Note);
    }

    [Fact]
    public void UrlFolderAndOtherFiles_Skipped()
    {
        var url = _dir.CreateFile("Portal 2.url");
        var folder = Path.Combine(_dir.Path, "Games");
        Directory.CreateDirectory(folder);
        var txt = _dir.CreateFile("notes.txt");

        var results = _importer.Import([url, folder, txt]);

        Assert.All(results, r => Assert.Equal(ImportOutcome.Skipped, r.Outcome));
        Assert.Contains("интернет-ярлык", results[0].Note);
        Assert.Contains("папка", results[1].Note);
        Assert.Contains("не exe", results[2].Note);
        Assert.Empty(_library.Games);
    }

    [Fact]
    public void MissingExe_Skipped()
    {
        var result = Assert.Single(_importer.Import([Path.Combine(_dir.Path, "nope.exe")]));

        Assert.Equal(ImportOutcome.Skipped, result.Outcome);
        Assert.Contains("не найден", result.Note);
    }

    [Fact]
    public void Duplicates_ReportedAsAlreadyInLibrary_EvenWithinOneDrop()
    {
        var exe = _dir.CreateFile(Path.Combine("Games", "A", "a.exe"));
        var lnk = _dir.CreateFile("A.lnk");
        _shortcuts.Targets[lnk] = new ShortcutTarget(exe, "");

        var results = _importer.Import([exe, lnk]);

        Assert.Equal(ImportOutcome.Added, results[0].Outcome);
        Assert.Equal(ImportOutcome.AlreadyInLibrary, results[1].Outcome);
        Assert.Single(_library.Games);
        Assert.Contains("уже в библиотеке", GameImporter.Summarize(results));
    }

    [Fact]
    public void Summarize_Mixed()
    {
        var exe = _dir.CreateFile(Path.Combine("Games", "A", "a.exe"));
        var url = _dir.CreateFile("B.url");

        var summary = GameImporter.Summarize(_importer.Import([exe, url]));

        Assert.StartsWith("Добавлено игр: 1.", summary);
        Assert.Contains("B.url — интернет-ярлык", summary);
    }

    private sealed class FakeShortcuts : IShortcutResolver
    {
        public Dictionary<string, ShortcutTarget?> Targets { get; } = [];

        public bool Throw { get; set; }

        public ShortcutTarget? Resolve(string shortcutPath) =>
            Throw ? throw new InvalidOperationException("COM недоступен") : Targets.GetValueOrDefault(shortcutPath);
    }
}
