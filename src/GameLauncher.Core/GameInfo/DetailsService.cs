using GameLauncher.Core.Library;

namespace GameLauncher.Core.GameInfo;

/// <summary>Описания игр из Steam: загрузка, сохранение и привязка AppID к игре.</summary>
public sealed class DetailsService(GameLibrary library, GameDetailsStore store, SteamStoreClient steam)
{
    /// <summary>Сохранённое описание; null — не загружалось.</summary>
    public GameDetails? Load(Guid gameId) => store.Load(gameId);

    public Task<IReadOnlyList<SteamStoreApp>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        steam.SearchAsync(query, cancellationToken);

    /// <summary>
    /// Скачивает описание по AppID, сохраняет его и запоминает AppID у игры.
    /// Null — Steam не отдал данных (ничего не сохраняется). Ошибки сети — <see cref="SteamStoreException"/>.
    /// </summary>
    public async Task<GameDetails?> DownloadAsync(Guid gameId, int appId, CancellationToken cancellationToken = default)
    {
        var details = await steam.GetDetailsAsync(appId, cancellationToken);
        if (details is null)
        {
            return null;
        }

        store.Save(gameId, details);
        library.SetSteamAppId(gameId, appId);
        return details;
    }
}
