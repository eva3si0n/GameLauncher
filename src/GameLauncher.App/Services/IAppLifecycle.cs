namespace GameLauncher.App.Services;

/// <summary>Состояние и перезапуск лаунчера — для восстановления из резервной копии.</summary>
public interface IAppLifecycle
{
    /// <summary>Идёт учёт времени какой-то игры.</summary>
    bool HasRunningGames { get; }

    /// <summary>
    /// Перезапустить лаунчер, ничего не сохранив из памяти: данные на диске уже заменены копией,
    /// и любая запись (библиотека, размер окна) затёрла бы их.
    /// </summary>
    void RestartWithoutSaving();
}
