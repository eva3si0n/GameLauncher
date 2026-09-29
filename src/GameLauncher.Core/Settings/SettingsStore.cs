using System.Text.Json;
using GameLauncher.Core.Library;
using System.Text.Json.Serialization;

namespace GameLauncher.Core.Settings;

/// <summary>Хранит настройки в JSON; запись атомарная. Нечитаемый файл — настройки по умолчанию.</summary>
public sealed class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new AppSettings();
            }

            using var stream = File.OpenRead(filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions) ?? new AppSettings();
            if (!Enum.IsDefined(settings.Theme))
            {
                settings.Theme = AppTheme.System;
            }

            if (!Enum.IsDefined(settings.LibrarySort))
            {
                settings.LibrarySort = LibrarySortMode.LastPlayed;
            }

            if (settings.Window is { Width: <= 0 } or { Height: <= 0 })
            {
                settings.Window = null;
            }

            if (string.IsNullOrWhiteSpace(settings.SteamRegion))
            {
                settings.SteamRegion = AppSettings.DefaultSteamRegion;
            }

            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AtomicFile.Write(filePath, stream => JsonSerializer.Serialize(stream, settings, JsonOptions));
    }
}
