using GameLauncher.Core.GameInfo;
using GameLauncher.Core.Library;

namespace GameLauncher.Core.Artwork;

/// <summary>Что получилось сделать после выбора обложки, кроме неё самой.</summary>
public sealed record CoverResult(bool HeroUpdated, GameDetails? Details);

/// <summary>Обложки и баннеры игры: скачивание в кэш и запись в библиотеку.</summary>
public sealed class CoverService(GameLibrary library, ArtworkCache artwork, SteamGridDbClient steamGridDb, DetailsService details)
{
    /// <summary>
    /// Применяет выбранную обложку игры из SteamGridDB.
    /// <list type="number">
    /// <item>Обложка скачивается и сразу записывается в библиотеку. Её ошибка пробрасывается — библиотека не меняется.</item>
    /// <item>Баннер (лучший по рейтингу) — по возможности: при ошибке остаётся прежний.</item>
    /// <item>Если у игры ещё нет описания и SteamGridDB знает её Steam AppID — загружается описание (тоже по возможности).</item>
    /// </list>
    /// </summary>
    public async Task<CoverResult> ApplyAsync(Guid gameId, int steamGridDbGameId, SteamGridDbImage grid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var game = library.Get(gameId);
        var gridFile = await artwork.DownloadAsync(gameId, ArtworkKind.Grid, grid.Url, cancellationToken);
        library.SetArtwork(gameId, gridFile, game.HeroFile);

        var heroUpdated = await TryUpdateHeroAsync(gameId, steamGridDbGameId, gridFile, cancellationToken);
        var loadedDetails = game.SteamAppId is null
            ? await TryLoadDetailsAsync(gameId, steamGridDbGameId, cancellationToken)
            : null;
        return new CoverResult(heroUpdated, loadedDetails);
    }

    /// <summary>Убирает обложку и баннер игры: из библиотеки и из кэша.</summary>
    public void Remove(Guid gameId)
    {
        library.SetArtwork(gameId, null, null);
        artwork.Delete(gameId);
    }

    private async Task<bool> TryUpdateHeroAsync(Guid gameId, int steamGridDbGameId, string gridFile, CancellationToken cancellationToken)
    {
        try
        {
            var heroes = await steamGridDb.GetHeroesAsync(steamGridDbGameId, cancellationToken);
            if (heroes.Count == 0)
            {
                library.SetArtwork(gameId, gridFile, null);
                artwork.Delete(gameId, ArtworkKind.Hero);
                return true;
            }

            var heroFile = await artwork.DownloadAsync(gameId, ArtworkKind.Hero, heroes[0].Url, cancellationToken);
            library.SetArtwork(gameId, gridFile, heroFile);
            return true;
        }
        catch (Exception ex) when (IsRecoverable(ex, cancellationToken))
        {
            return false;
        }
    }

    private async Task<GameDetails?> TryLoadDetailsAsync(Guid gameId, int steamGridDbGameId, CancellationToken cancellationToken)
    {
        try
        {
            return await steamGridDb.GetSteamAppIdAsync(steamGridDbGameId, cancellationToken) is { } appId
                ? await details.DownloadAsync(gameId, appId, cancellationToken)
                : null;
        }
        catch (Exception ex) when (IsRecoverable(ex, cancellationToken) || ex is SteamStoreException)
        {
            return null;
        }
    }

    private static bool IsRecoverable(Exception ex, CancellationToken cancellationToken) =>
        ex is SteamGridDbException or HttpRequestException or IOException or UnauthorizedAccessException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);
}
