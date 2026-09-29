using GameLauncher.Core.Library;

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

    /// <summary>Порядок карточек в библиотеке.</summary>
    public LibrarySortMode LibrarySort { get; set; } = LibrarySortMode.LastPlayed;

    /// <summary>Крестик прячет окно в трей (время игр продолжает считаться); false — закрывает лаунчер.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Подсказка «лаунчер работает в трее» уже показывалась.</summary>
    public bool TrayHintShown { get; set; }

    /// <summary>Размер и положение окна при последнем закрытии; null — ещё не сохранялись.</summary>
    public WindowPlacement? Window { get; set; }
}
