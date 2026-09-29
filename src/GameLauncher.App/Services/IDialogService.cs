using GameLauncher.Core.Artwork;
using GameLauncher.Core.Backup;

namespace GameLauncher.App.Services;

/// <summary>Диалоги и выбор файлов — отдельно от ViewModel, чтобы та не зависела от окна.</summary>
public interface IDialogService
{
    /// <summary>Выбор .exe игры. Null, если пользователь отменил.</summary>
    Task<string?> PickExeAsync();

    /// <summary>Запрос нового названия. Null, если пользователь отменил.</summary>
    Task<string?> PromptRenameAsync(string currentName);

    Task<bool> ConfirmDeleteAsync(string gameName);

    /// <summary>Куда сохранить резервную копию (.zip). Null — отмена.</summary>
    Task<string?> PickBackupSaveAsync(string suggestedFileName);

    /// <summary>Выбор резервной копии (.zip) для восстановления. Null — отмена.</summary>
    Task<string?> PickBackupOpenAsync();

    /// <summary>Подтверждение восстановления: текущие данные будут заменены.</summary>
    Task<bool> ConfirmRestoreAsync(BackupInfo backup);

    Task ShowMessageAsync(string title, string message);

    /// <summary>Ввод API-ключа SteamGridDB. <paramref name="validate"/> возвращает текст ошибки или null.</summary>
    Task<ApiKeyDialogResult> EditApiKeyAsync(bool hasKey, Func<string, Task<string?>> validate);

    /// <summary>
    /// Диалог поиска с выбором результата. Ошибки <paramref name="search"/>, для которых
    /// <paramref name="describeError"/> возвращает текст, показываются в диалоге. Null — отмена.
    /// </summary>
    Task<T?> PickFromSearchAsync<T>(
        string title,
        string initialQuery,
        Func<string, Task<IReadOnlyList<T>>> search,
        Func<T, string> display,
        Func<Exception, string?> describeError)
        where T : class;

    /// <summary>Просмотр скриншотов в полном размере, начиная с <paramref name="startIndex"/>.</summary>
    Task ShowScreenshotsAsync(IReadOnlyList<Uri> images, int startIndex);

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
