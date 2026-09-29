using GameLauncher.Core.Artwork;
using GameLauncher.Core.GameInfo;
using GameLauncher.Core.PlayTime;

namespace GameLauncher.Core.Library;

/// <summary>Удаление игры из библиотеки вместе с её картинками, описанием и историей сессий. Файлы самой игры не трогаются.</summary>
public sealed class GameRemover(GameLibrary library, ArtworkCache artwork, GameDetailsStore details, PlayHistory? history = null)
{
    /// <summary>
    /// Удаляет игру. Ошибка сохранения библиотеки пробрасывается (игра остаётся). Картинки, описание и история
    /// удаляются по возможности: оставшиеся данные работе не мешают.
    /// </summary>
    public void Remove(Guid gameId)
    {
        library.Remove(gameId);

        // Шаги независимы: сбой одного (например, папки описаний ещё нет) не должен оставить остальные данные.
        TryCleanup(() => artwork.Delete(gameId));
        TryCleanup(() => details.Delete(gameId));
        TryCleanup(() => history?.RemoveGame(gameId));
    }

    private static void TryCleanup(Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
