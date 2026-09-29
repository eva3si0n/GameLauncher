using GameLauncher.Core.Library;
using GameLauncher.Core.PlayTime;
using Microsoft.UI.Dispatching;

namespace GameLauncher.App.Services;

/// <summary>
/// Раз в 5 секунд опрашивает процессы, пока есть запущенные из лаунчера игры.
/// Список процессов собирается в фоне, трекер обновляется в UI-потоке.
/// </summary>
public sealed class PlayTimeMonitor
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly PlaySessionTracker _tracker;
    private readonly IRunningProcesses _processes;
    private readonly TimeProvider _time;
    private readonly DispatcherQueueTimer _timer;
    private bool _polling;

    public PlayTimeMonitor(PlaySessionTracker tracker, IRunningProcesses processes, DispatcherQueue dispatcher, TimeProvider? time = null)
    {
        _tracker = tracker;
        _processes = processes;
        _time = time ?? TimeProvider.System;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = PollInterval;
        _timer.IsRepeating = true;
        _timer.Tick += async (_, _) => await PollAsync();
    }

    /// <summary>Состояние сессий или сохранённое время изменились.</summary>
    public event EventHandler? Changed;

    public PlaySessionState GetState(Guid gameId) => _tracker.GetState(gameId);

    public void OnLaunched(Game game)
    {
        _tracker.Launch(game, _time.GetUtcNow());
        Changed?.Invoke(this, EventArgs.Empty);
        if (!_timer.IsRunning)
        {
            _timer.Start();
        }
    }

    /// <summary>При закрытии лаунчера: сохранить время идущих сессий.</summary>
    public void Stop()
    {
        _timer.Stop();
        _tracker.FlushAll(_time.GetUtcNow());
    }

    private async Task PollAsync()
    {
        if (_polling)
        {
            return; // предыдущий опрос ещё идёт
        }

        _polling = true;
        try
        {
            var paths = await Task.Run(_processes.GetExePaths);
            if (_tracker.Tick(_time.GetUtcNow(), paths))
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }

            if (!_tracker.HasSessions)
            {
                _timer.Stop();
            }
        }
        finally
        {
            _polling = false;
        }
    }
}
