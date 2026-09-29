using GameLauncher.Core.Artwork;

namespace GameLauncher.App.Services;

/// <summary>Диалоги и выбор файлов — отдельно от ViewModel, чтобы та не зависела от окна.</summary>
public interface IDialogService
{
    /// <summary>Выбор .exe игры. Null, если пользователь отменил.</summary>
    Task<string?> PickExeAsync();

    /// <summary>Запрос нового названия. Null, если пользователь отменил.</summary>
    Task<string?> PromptRenameAsync(string currentName);

    Task<bool> ConfirmDeleteAsync(string gameName);

    Task ShowMessageAsync(string title, string message);

    /// <summary>Ввод API-ключа SteamGridDB. <paramref name="validate"/> возвращает текст ошибки или null.</summary>
    Task<ApiKeyDialogResult> EditApiKeyAsync(bool hasKey, Func<string, Task<string?>> validate);

    /// <summary>Поиск игры в SteamGridDB. Null — отмена.</summary>
    Task<SteamGridDbGame?> PickSteamGridDbGameAsync(
        string initialQuery, Func<string, Task<IReadOnlyList<SteamGridDbGame>>> search);

    /// <summary>Выбор обложки из вариантов. Null — отмена.</summary>
    Task<SteamGridDbImage?> PickImageAsync(string title, IReadOnlyList<SteamGridDbImage> images);
}

public enum ApiKeyDialogAction
{
    Cancel,
    Save,
    Remove,
}

public sealed record ApiKeyDialogResult(ApiKeyDialogAction Action, string? Key = null);
