namespace GameLauncher.Core.PlayTime;

public enum PlaySessionState
{
    /// <summary>Игра не запущена из лаунчера.</summary>
    None,

    /// <summary>Нажали «Играть», процесса игры ещё не видно.</summary>
    Starting,

    /// <summary>Игра запущена, время идёт.</summary>
    Playing,
}
