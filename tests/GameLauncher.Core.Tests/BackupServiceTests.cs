using System.IO.Compression;
using System.Text;
using GameLauncher.Core.Backup;

namespace GameLauncher.Core.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private const string LibraryJson = """{"version":1,"games":[{"id":"8c1f7b1e-0000-0000-0000-000000000001","name":"Игра","exePath":"C:\\Games\\a.exe"}]}""";

    private readonly TempDirectory _dir = new();
    private readonly LocalTime _time = new(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero));

    public BackupServiceTests()
    {
        Directory.CreateDirectory(DataDir);
    }

    public void Dispose() => _dir.Dispose();

    private string DataDir => Path.Combine(_dir.Path, "data");

    private string Data(string relative) => Path.Combine(DataDir, relative);

    private string ZipPath => Path.Combine(_dir.Path, "backup.zip");

    private BackupService CreateService() => new(DataDir, _time);

    private void WriteData(string relative, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Data(relative))!);
        File.WriteAllText(Data(relative), content);
    }

    private void SeedData()
    {
        WriteData("library.json", LibraryJson);
        WriteData("settings.json", """{"theme":"Dark"}""");
        WriteData("steamgriddb.key", "secret");
        WriteData("crash.log", "boom");
        WriteData(Path.Combine("artwork", "a-grid.png"), "png");
        WriteData(Path.Combine("artwork", "b-grid.png.tmp"), "half");
        WriteData(Path.Combine("info", "a.json"), "{}");
        WriteData(Path.Combine("backups", "auto-2026-09-28.zip"), "old");
    }

    private static List<string> EntryNames(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void Create_IncludesDataAndArtwork_ButNotKeyLogsTempOrBackups()
    {
        SeedData();

        var info = CreateService().Create(ZipPath);

        Assert.Equal(1, info.GameCount);
        Assert.True(info.IncludesArtwork);
        Assert.Equal(
            ["artwork/a-grid.png", "backup.json", "info/a.json", "library.json", "settings.json"],
            EntryNames(ZipPath));
        Assert.False(File.Exists(ZipPath + ".tmp"));
    }

    [Fact]
    public void Create_WithoutLibrary_Throws()
    {
        var ex = Assert.Throws<BackupException>(() => CreateService().Create(ZipPath));

        Assert.Contains("пуста", ex.Message);
        Assert.False(File.Exists(ZipPath));
    }

    [Fact]
    public void Create_WorksWhileLibraryIsOpenForWriting()
    {
        SeedData();
        using var writer = new FileStream(Data("library.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        CreateService().Create(ZipPath);

        Assert.Contains("library.json", EntryNames(ZipPath));
    }

    [Fact]
    public void Read_ReturnsInfo()
    {
        SeedData();
        CreateService().Create(ZipPath);

        var info = CreateService().Read(ZipPath);

        Assert.Equal(1, info.GameCount);
        Assert.True(info.IncludesArtwork);
        Assert.Equal(_time.GetLocalNow(), info.CreatedAt);
    }

    [Fact]
    public void Read_NotZip_Throws()
    {
        File.WriteAllText(ZipPath, "это не архив");

        var ex = Assert.Throws<BackupException>(() => CreateService().Read(ZipPath));

        Assert.Contains("zip", ex.Message);
    }

    [Fact]
    public void Read_ZipWithoutManifest_Throws()
    {
        MakeZip(("library.json", LibraryJson));

        var ex = Assert.Throws<BackupException>(() => CreateService().Read(ZipPath));

        Assert.Contains("не резервная копия", ex.Message);
    }

    [Fact]
    public void Read_NewerFormat_Throws()
    {
        MakeZip(("backup.json", """{"format":2,"createdAt":"2026-09-29T12:00:00+00:00"}"""), ("library.json", LibraryJson));

        var ex = Assert.Throws<BackupException>(() => CreateService().Read(ZipPath));

        Assert.Contains("более новой", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{ битый")]
    public void Read_MissingOrCorruptLibrary_Throws(string? library)
    {
        var entries = new List<(string, string)> { ("backup.json", """{"format":1,"createdAt":"2026-09-29T12:00:00+00:00"}""") };
        if (library is not null)
        {
            entries.Add(("library.json", library));
        }

        MakeZip([.. entries]);

        Assert.Throws<BackupException>(() => CreateService().Read(ZipPath));
    }

    [Fact]
    public void Restore_ReplacesData_KeepsKey_AndMakesSafetyCopy()
    {
        SeedData();
        CreateService().Create(ZipPath);

        // Данные изменились после копии.
        WriteData("library.json", """{"version":1,"games":[]}""");
        WriteData("settings.json", """{"theme":"Light"}""");
        File.Delete(Data(Path.Combine("artwork", "a-grid.png")));
        WriteData(Path.Combine("artwork", "new.png"), "new");

        var info = CreateService().Restore(ZipPath);

        Assert.Equal(1, info.GameCount);
        Assert.Equal(LibraryJson, File.ReadAllText(Data("library.json")));
        Assert.Contains("Dark", File.ReadAllText(Data("settings.json")));
        Assert.True(File.Exists(Data(Path.Combine("artwork", "a-grid.png"))));
        Assert.False(File.Exists(Data(Path.Combine("artwork", "new.png"))));
        Assert.Equal("secret", File.ReadAllText(Data("steamgriddb.key")));
        Assert.False(Directory.Exists(Data(".restore")));

        // Состояние до восстановления сохранено.
        var safety = Assert.Single(Directory.GetFiles(Data("backups"), "before-restore-*.zip"));
        Assert.Contains("artwork/new.png", EntryNames(safety));
    }

    [Fact]
    public void Restore_BackupWithoutArtwork_KeepsCurrentArtwork()
    {
        SeedData();
        CreateService().Create(ZipPath, includeFolders: false);
        WriteData(Path.Combine("artwork", "new.png"), "new");

        var info = CreateService().Restore(ZipPath);

        Assert.False(info.IncludesArtwork);
        Assert.True(File.Exists(Data(Path.Combine("artwork", "new.png"))));
        Assert.True(File.Exists(Data(Path.Combine("info", "a.json"))));
    }

    [Fact]
    public void Restore_IgnoresEntriesOutsideDataFolders()
    {
        MakeZip(
            ("backup.json", """{"format":1,"createdAt":"2026-09-29T12:00:00+00:00"}"""),
            ("library.json", LibraryJson),
            ("../evil.txt", "x"),
            ("artwork/../../evil2.txt", "x"),
            ("artwork/sub/deep.png", "x"),
            ("steamgriddb.key", "stolen"),
            ("artwork/ok.png", "png"));

        CreateService().Restore(ZipPath);

        Assert.False(File.Exists(Path.Combine(_dir.Path, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(_dir.Path, "evil2.txt")));
        Assert.False(File.Exists(Path.Combine(DataDir, "evil.txt")));
        Assert.False(Directory.Exists(Data(Path.Combine("artwork", "sub"))));
        Assert.False(File.Exists(Data("steamgriddb.key")));
        Assert.True(File.Exists(Data(Path.Combine("artwork", "ok.png"))));
    }

    [Fact]
    public void Restore_InvalidBackup_LeavesDataUntouched()
    {
        SeedData();
        File.WriteAllText(ZipPath, "мусор");

        Assert.Throws<BackupException>(() => CreateService().Restore(ZipPath));

        Assert.Equal(LibraryJson, File.ReadAllText(Data("library.json")));
        Assert.False(Directory.Exists(Data(".restore")));
    }

    [Fact]
    public void EnsureDailyBackup_OncePerDay_WithoutArtwork()
    {
        SeedData();
        var service = CreateService();

        var first = service.EnsureDailyBackup();
        var second = service.EnsureDailyBackup();

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal("auto-2026-09-29.zip", Path.GetFileName(first));
        Assert.Equal(["backup.json", "library.json", "settings.json"], EntryNames(first!));
    }

    [Fact]
    public void EnsureDailyBackup_NoLibrary_DoesNothing()
    {
        Assert.Null(CreateService().EnsureDailyBackup());
        Assert.False(Directory.Exists(Data("backups")));
    }

    [Fact]
    public void EnsureDailyBackup_KeepsLastSeven()
    {
        SeedData();
        for (var day = 1; day <= 8; day++)
        {
            WriteData(Path.Combine("backups", $"auto-2026-09-{day:00}.zip"), "old");
        }

        CreateService().EnsureDailyBackup();

        var left = Directory.GetFiles(Data("backups"), "auto-*.zip").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(BackupService.AutoBackupsToKeep, left.Count);
        Assert.Equal("auto-2026-09-04.zip", left[0]);
        Assert.Equal("auto-2026-09-29.zip", left[^1]);
    }

    [Fact]
    public void SuggestedFileName_UsesLocalDate()
    {
        Assert.Equal("GameLauncher-backup-2026-09-29.zip", CreateService().SuggestedFileName);
    }

    private void MakeZip(params (string Name, string Content)[] entries)
    {
        using var zip = ZipFile.Open(ZipPath, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }
    }

    /// <summary>Фиксированное время в UTC как «местное» — чтобы имена файлов не зависели от часового пояса машины.</summary>
    private sealed class LocalTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
