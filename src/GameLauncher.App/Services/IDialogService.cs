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
}
