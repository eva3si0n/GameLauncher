using GameLauncher.Core.Startup;
using Microsoft.Win32;

namespace GameLauncher.App.Services;

/// <summary>
/// Автозапуск — значение в HKCU\Software\Microsoft\Windows\CurrentVersion\Run (прав администратора не нужно).
/// Отключение в «Параметры → Приложения → Автозагрузка» Windows хранит отдельно, эту запись оно не трогает.
/// </summary>
public sealed class RegistryAutostartStore : IAutostartStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GameLauncher";

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) as string;
    }

    public void Write(string? command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (command is null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
    }
}
