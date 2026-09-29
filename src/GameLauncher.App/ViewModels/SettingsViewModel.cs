using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameLauncher.App.Services;
using GameLauncher.Core;
using GameLauncher.Core.Artwork;
using GameLauncher.Core.Settings;

namespace GameLauncher.App.ViewModels;

/// <summary>Страница настроек: тема, ключ SteamGridDB, витрина Steam, папка данных; а также размер окна.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly ISecretStore _keyStore;
    private readonly IDialogService _dialogs;
    private readonly HttpClient _http;

    public SettingsViewModel(SettingsStore store, ISecretStore keyStore, IDialogService dialogs, HttpClient http)
    {
        _store = store;
        _settings = store.Load();
        _keyStore = keyStore;
        _dialogs = dialogs;
        _http = http;
        ApiKey = keyStore.Load();

        EditApiKeyCommand = new AsyncRelayCommand(EditApiKeyAsync);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
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

    private static void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            Process.Start("explorer.exe", $"\"{AppPaths.DataDirectory}\"")?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
        }
    }
}
