namespace GameLauncher.Core.Artwork;

/// <summary>Хранилище API-ключа. Реализация в App шифрует его через DPAPI (CurrentUser).</summary>
public interface ISecretStore
{
    string? Load();

    /// <summary>Сохраняет ключ; null или пустая строка — удалить ключ.</summary>
    void Save(string? secret);
}
