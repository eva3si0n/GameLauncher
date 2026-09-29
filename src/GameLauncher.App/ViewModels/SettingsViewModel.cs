using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLauncher.App.Services;
using GameLauncher.Core;
using GameLauncher.Core.Artwork;
using GameLauncher.Core.Backup;
using GameLauncher.Core.Settings;
using GameLauncher.Core.Startup;

namespace GameLauncher.App.ViewModels;

/// <summary>Страница настроек: тема, трей и автозапуск, ключ SteamGridDB, витрина Steam, папка данных, резервные копии; а также размер окна.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly ISecretStore _keyStore;
    private readonly IDialogService _dialogs;
    private readonly HttpClient _http;
    private readonly Autostart _autostart;
    private readonly BackupService _backups;
    private readonly IAppLifecycle _lifecycle;

    public SettingsViewModel(
        SettingsStore store,
        ISecretStore keyStore,
        IDialogService dialogs,
        HttpClient http,
        Autostart autostart,
        BackupService backups,
        IAppLifecycle lifecycle)
    {
        _store = store;
        _settings = store.Load();
        _keyStore = keyStore;
        _dialogs = dialogs;
        _http = http;
        _autostart = autostart;
        _backups = backups;
        _lifecycle = lifecycle;
        ApiKey = keyStore.Load();

        EditApiKeyCommand = new AsyncRelayCommand(EditApiKeyAsync);
        OpenDataFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.DataDirectory));
        CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync);
        RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync);
        OpenBackupsFolderCommand = new RelayCommand(() => OpenFolder(_backups.AutoBackupDirectory));
    }

    /// <summary>Тема изменилась — окно применяет её.</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>Ключ SteamGridDB; null — не задан.</summary>
    public string? ApiKey { get; private set; }

    /// <summary>Основная витрина Steam для описаний.</summary>
    public string SteamRegion => _settings.SteamRegion;

    public AppTheme Theme => _settings.Theme;

    /// <summary>Индекс в списке «Как в системе / Светлая / Тёмная».</summary>
    public int ThemeIndex
    {
        get => (int)_settings.Theme;
        set
        {
            if (value < 0 || value == (int)_settings.Theme || !Enum.IsDefined((AppTheme)value))
            {
                return;
            }

            _settings.Theme = (AppTheme)value;
            Save();
            OnPropertyChanged();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Крестик прячет окно в трей; выключено — закрывает лаунчер.</summary>
    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set
        {
            if (value == _settings.CloseToTray)
            {
                return;
            }

            _settings.CloseToTray = value;
            Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Автозапуск вместе с Windows (сразу в трей). Состояние читается из реестра, а не из настроек.</summary>
    public bool StartWithWindows
    {
        get
        {
            try
            {
                return _autostart.IsEnabled;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return false;
            }
        }
        set
        {
            if (value == StartWithWindows)
            {
                return;
            }

            try
            {
                _autostart.SetEnabled(value);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                _ = _dialogs.ShowMessageAsync("Не удалось изменить автозапуск", ex.Message);
            }

            // Переключатель показывает то, что реально записалось.
            OnPropertyChanged();
        }
    }

    /// <summary>Подсказку «лаунчер в трее» показываем один раз — при первом сворачивании.</summary>
    public bool TryMarkTrayHintShown()
    {
        if (_settings.TrayHintShown)
        {
            return false;
        }

        _settings.TrayHintShown = true;
        Save();
        return true;
    }

    /// <summary>Витрины Steam для описаний: код страны и название.</summary>
    public static IReadOnlyList<(string Code, string Name)> SteamRegions { get; } =
    [
        ("tr", "Турция"),
        ("us", "США"),
        ("ru", "Россия"),
        ("kz", "Казахстан"),
        ("ua", "Украина"),
        ("de", "Германия"),
    ];

    public IReadOnlyList<string> SteamRegionNames { get; } = SteamRegions.Select(r => r.Name).ToList();

    public int SteamRegionIndex
    {
        get => Math.Max(0, SteamRegions.ToList().FindIndex(r => string.Equals(r.Code, _settings.SteamRegion, StringComparison.OrdinalIgnoreCase)));
        set
        {
            if (value < 0 || value >= SteamRegions.Count || SteamRegions[value].Code == _settings.SteamRegion)
            {
                return;
            }

            _settings.SteamRegion = SteamRegions[value].Code;
            Save();
            OnPropertyChanged();
        }
    }

    public string ApiKeyStatusText => ApiKey is null ? "Ключ не задан — поиск обложек недоступен." : "Ключ сохранён (зашифрован DPAPI).";

    public string DataDirectory => AppPaths.DataDirectory;

    public string AppVersion => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "—";

    public IAsyncRelayCommand EditApiKeyCommand { get; }

    public IRelayCommand OpenDataFolderCommand { get; }

    public IAsyncRelayCommand CreateBackupCommand { get; }

    public IAsyncRelayCommand RestoreBackupCommand { get; }

    public IRelayCommand OpenBackupsFolderCommand { get; }

    /// <summary>Размер и положение окна с прошлого запуска; null — первый запуск.</summary>
    public WindowPlacement? SavedWindowPlacement => _settings.Window;

    public void SaveWindowPlacement(WindowPlacement placement)
    {
        _settings.Window = placement;
        Save();
    }

    /// <summary>Диалог ключа SteamGridDB. Возвращает true, если после него ключ есть.</summary>
    public async Task<bool> EditApiKeyAsync()
    {
        var result = await _dialogs.EditApiKeyAsync(
            hasKey: ApiKey is not null,
            validate: async key =>
            {
                try
                {
                    // Любой поиск проверяет ключ: неверный отклоняется с 401.
                    await new SteamGridDbClient(_http, () => key).SearchGamesAsync("portal");
                    return null;
                }
                catch (SteamGridDbException ex)
                {
                    return ex.Message;
                }
            });

        try
        {
            switch (result.Action)
            {
                case ApiKeyDialogAction.Save:
                    _keyStore.Save(result.Key);
                    ApiKey = result.Key;
                    break;
                case ApiKeyDialogAction.Remove:
                    _keyStore.Save(null);
                    ApiKey = null;
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            await _dialogs.ShowMessageAsync("Не удалось сохранить ключ", ex.Message);
        }

        OnPropertyChanged(nameof(ApiKeyStatusText));
        return ApiKey is not null;
    }

    private void Save()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Настройка действует до перезапуска; сообщать о каждом сбое записи незачем.
        }
    }

    private async Task CreateBackupAsync()
    {
        var path = await _dialogs.PickBackupSaveAsync(_backups.SuggestedFileName);
        if (path is null)
        {
            return;
        }

        try
        {
            // Файлы читаются так, что запись библиотеки в это время не мешает, — можно в фоне.
            var info = await Task.Run(() => _backups.Create(path));
            await _dialogs.ShowMessageAsync(
                "Копия сохранена",
                $"{path}\n\nИгр: {info.GameCount}. Ключ SteamGridDB в копию не входит: после переустановки Windows его нужно ввести заново.");
        }
        catch (BackupException ex)
        {
            await _dialogs.ShowMessageAsync("Не удалось сохранить копию", ex.Message);
        }
    }

    private async Task RestoreBackupAsync()
    {
        if (await RefuseWhileGameRunsAsync())
        {
            return;
        }

        var path = await _dialogs.PickBackupOpenAsync();
        if (path is null)
        {
            return;
        }

        BackupInfo info;
        try
        {
            info = await Task.Run(() => _backups.Read(path));
        }
        catch (BackupException ex)
        {
            await _dialogs.ShowMessageAsync("Эту копию нельзя восстановить", ex.Message);
            return;
        }

        if (!await _dialogs.ConfirmRestoreAsync(info) || await RefuseWhileGameRunsAsync())
        {
            return;
        }

        try
        {
            // Синхронно в UI-потоке: все записи библиотеки идут отсюда же, и между восстановлением и перезапуском
            // ни одна запись из памяти не успеет затереть восстановленные файлы.
            _backups.Restore(path);
        }
        catch (BackupException ex)
        {
            await _dialogs.ShowMessageAsync(
                "Не удалось восстановить данные",
                $"{ex.Message}\n\nЕсли часть файлов успела замениться, прежнее состояние сохранено в папке автокопий (before-restore-…).");
            return;
        }

        _lifecycle.RestartWithoutSaving();
    }

    private async Task<bool> RefuseWhileGameRunsAsync()
    {
        if (!_lifecycle.HasRunningGames)
        {
            return false;
        }

        await _dialogs.ShowMessageAsync(
            "Сначала закройте игру",
            "Пока идёт учёт времени игры, восстановление недоступно: время записалось бы в заменяемую библиотеку.");
        return true;
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", $"\"{path}\"")?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
        }
    }
}
