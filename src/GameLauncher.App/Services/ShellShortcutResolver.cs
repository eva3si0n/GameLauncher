using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using GameLauncher.Core.Library;

namespace GameLauncher.App.Services;

/// <summary>
/// Цель ярлыка .lnk через IShellLink (ShellLink из shell32). Вызывать из STA-потока — UI-поток подходит.
/// Ошибки COM пробрасываются: GameImporter покажет их как «не удалось прочитать ярлык».
/// </summary>
public sealed class ShellShortcutResolver : IShortcutResolver
{
    private const int MaxPath = 32768;

    public ShortcutTarget? Resolve(string shortcutPath)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(shortcutPath, 0); // STGM_READ

            var path = new StringBuilder(MaxPath);
            link.GetPath(path, path.Capacity, IntPtr.Zero, 0);
            if (path.Length == 0)
            {
                return null; // ярлык на объект оболочки без пути (например, «Этот компьютер»)
            }

            var arguments = new StringBuilder(MaxPath);
            link.GetArguments(arguments, arguments.Capacity);
            return new ShortcutTarget(Environment.ExpandEnvironmentVariables(path.ToString()), arguments.ToString());
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    /// <summary>IShellLinkW: порядок методов — порядок vtable, менять нельзя.</summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
