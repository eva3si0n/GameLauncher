namespace GameLauncher.Core.PlayTime;

/// <summary>Пути к exe запущенных процессов. Реализация — в App (Windows API).</summary>
public interface IRunningProcesses
{
    /// <summary>Полные пути exe всех процессов, которые удалось прочитать. Недоступные пропускаются.</summary>
    IReadOnlyCollection<string> GetExePaths();
}
