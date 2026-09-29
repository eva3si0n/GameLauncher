namespace GameLauncher.Core.GameInfo;

/// <summary>Описание игры из Steam Store. Хранится отдельно от библиотеки: %LocalAppData%\GameLauncher\info\{id игры}.json.</summary>
public sealed class GameDetails
{
    public int SteamAppId { get; set; }

    /// <summary>Название в Steam — чтобы было видно, с какой игрой связано описание.</summary>
    public string SteamName { get; set; } = "";

    /// <summary>Краткое описание (обычный текст).</summary>
    public string ShortDescription { get; set; } = "";

    /// <summary>Подробное «Об игре» (обычный текст, HTML убран).</summary>
    public string About { get; set; } = "";

    public List<string> Genres { get; set; } = [];

    public List<string> Developers { get; set; } = [];

    public List<string> Publishers { get; set; } = [];

    /// <summary>Дата выхода в том виде, как её отдаёт Steam (строка, формат зависит от языка).</summary>
    public string ReleaseDate { get; set; } = "";

    public List<Screenshot> Screenshots { get; set; } = [];

    public DateTimeOffset FetchedAt { get; set; }
}

public sealed record Screenshot(Uri Thumbnail, Uri Full);
