using System.Diagnostics;
using System.Runtime.InteropServices;
using GameLauncher.Core.PlayTime;
using Microsoft.Win32.SafeHandles;

namespace GameLauncher.App.Services;

/// <summary>
/// Пути exe процессов через QueryFullProcessImageName с правом PROCESS_QUERY_LIMITED_INFORMATION.
/// Process.MainModule для этого не годится: на процессах с повышенными правами и защищённых он бросает «Access denied».
/// </summary>
public sealed class WindowsRunningProcesses : IRunningProcesses
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public IReadOnlyCollection<string> GetExePaths()
    {
        var paths = new List<string>();
        var buffer = new char[1024];
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (TryGetExePath(process.Id, buffer) is { } path)
                {
                    paths.Add(path);
                }
            }
        }

        return paths;
    }

    private static string? TryGetExePath(int processId, char[] buffer)
    {
        using var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle.IsInvalid)
        {
            return null; // системные процессы и т. п. — пропускаем
        }

        var size = buffer.Length;
        return QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, [Out] char[] exeName, ref int size);
}
