using GameLauncher.Core.Artwork;
using GameLauncher.Core.GameInfo;

namespace GameLauncher.Core.Library;

/// <summary>Удаление игры из библиотеки вместе с её картинками и описанием. Файлы самой игры не трогаются.</summary>
public sealed class GameRemover(GameLibrary library, ArtworkCache artwork, GameDetailsStore details)
{
    /// <summary>
    /// Удаляет игру. Ошибка сохранения библиотеки пробрасывается (игра остаётся). Картинки и описание удаляются
    /// по возможности: оставшийся в кэше файл работе не мешает.
    /// </summary>
    public void Remove(Guid gameId)
    {
        library.Remove(gameId);
        try
        {
            artwork.Delete(gameId);
            details.Delete(gameId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
