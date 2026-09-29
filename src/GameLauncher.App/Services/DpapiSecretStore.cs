using System.Security.Cryptography;
using System.Text;
using GameLauncher.Core.Artwork;

namespace GameLauncher.App.Services;

/// <summary>
/// Хранит секрет в файле, зашифрованным DPAPI с областью CurrentUser:
/// расшифровать может только этот пользователь Windows на этом компьютере.
/// </summary>
public sealed class DpapiSecretStore(string filePath) : ISecretStore
{
    private static readonly byte[] Entropy = "GameLauncher.SteamGridDB.v1"u8.ToArray();

    public string? Load()
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(filePath), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            // Файл от другого пользователя/компьютера или повреждён — считаем, что ключа нет.
            return null;
        }
    }

    public void Save(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            File.Delete(filePath);
            return;
        }

        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret.Trim()), Entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var tempPath = filePath + ".tmp";
        File.WriteAllBytes(tempPath, encrypted);
        File.Move(tempPath, filePath, overwrite: true);
    }
}
