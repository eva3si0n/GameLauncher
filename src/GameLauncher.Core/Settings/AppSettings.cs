namespace GameLauncher.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Настройки лаунчера: %LocalAppData%\GameLauncher\settings.json. Ключ SteamGridDB здесь не хранится — он в DPAPI.</summary>
public sealed class AppSettings
{
    public const string DefaultSteamRegion = "tr";

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Основная витрина Steam для описаний (код страны: tr, us, ru…).</summary>
    public string SteamRegion { get; set; } = DefaultSteamRegion;

    /// <summary>Размер и положение окна при последнем закрытии; null — ещё не сохранялись.</summary>
    public WindowPlacement? Window { get; set; }
}
