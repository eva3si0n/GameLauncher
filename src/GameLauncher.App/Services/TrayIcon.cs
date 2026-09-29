using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace GameLauncher.App.Services;

/// <summary>
/// Значок в области уведомлений (Shell_NotifyIcon) с меню «Открыть / Выход».
/// Сообщения значка приходят в скрытое окно, созданное в UI-потоке: его очередь прокачивает цикл сообщений WinUI.
/// Окно обычное верхнеуровневое (не message-only) — только такие получают TaskbarCreated (перезапуск
/// проводника, значок надо добавить заново) и WM_ENDSESSION (выход из системы).
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string WindowClassName = "GameLauncher.TrayWindow";
    private const int IconId = 1;
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const nuint CommandOpen = 1;
    private const nuint CommandExit = 2;

    private readonly DispatcherQueue _dispatcher;
    private readonly WndProc _wndProc; // держим делегат, иначе GC соберёт его, пока окно живо
    private readonly IntPtr _hwnd;
    private readonly IntPtr _icon;
    private readonly string _tooltip;
    private readonly uint _taskbarCreatedMessage;
    private bool _disposed;

    public TrayIcon(string iconPath, string tooltip, DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _tooltip = tooltip;
        _wndProc = OnMessage;

        var instance = GetModuleHandleW(null);
        var windowClass = new WNDCLASSEXW
        {
            cbSize = Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = WindowClassName,
        };
        if (RegisterClassExW(ref windowClass) == 0)
        {
            throw new InvalidOperationException($"RegisterClassEx: ошибка {Marshal.GetLastPInvokeError()}");
        }

        _hwnd = CreateWindowExW(0, WindowClassName, "GameLauncher", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            var error = Marshal.GetLastPInvokeError();
            UnregisterClassW(WindowClassName, instance);
            throw new InvalidOperationException($"CreateWindowEx: ошибка {error}");
        }

        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");

        // Маленькая иконка под текущий масштаб экрана.
        var size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());
        _icon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, size, size, LR_LOADFROMFILE);

        IsAdded = Add();
    }

    /// <summary>Клик по значку или «Открыть» в меню.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>«Выход» в меню.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>
    /// Windows завершает сеанс (выключение, перезагрузка, выход). Вызывается синхронно: после возврата процесс
    /// может быть убит, поэтому обработчик должен успеть сохранить данные.
    /// </summary>
    public event EventHandler? SessionEnding;

    /// <summary>Значок показан. False — проводник недоступен; прятать окно в трей тогда нельзя.</summary>
    public bool IsAdded { get; private set; }

    /// <summary>Уведомление от значка (в Windows 11 — обычное уведомление в углу экрана).</summary>
    public void ShowNotification(string title, string text)
    {
        if (!IsAdded)
        {
            return;
        }

        var data = CreateData(NIF_INFO);
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = NIIF_INFO;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var data = CreateData(0);
        Shell_NotifyIconW(NIM_DELETE, ref data);
        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
        }

        DestroyWindow(_hwnd);
        UnregisterClassW(WindowClassName, GetModuleHandleW(null));
    }

    private bool Add()
    {
        var data = CreateData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.szTip = _tooltip;
        return Shell_NotifyIconW(NIM_ADD, ref data);
    }

    private NOTIFYICONDATAW CreateData(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = IconId,
        uFlags = flags,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private IntPtr OnMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == CallbackMessage)
        {
            // Без NIM_SETVERSION в lParam приходит сообщение мыши. Реакцию откладываем в очередь диспетчера:
            // исключение внутри оконной процедуры уронило бы процесс мимо обработчиков WinUI.
            switch ((uint)(lParam.ToInt64() & 0xFFFF))
            {
                case WM_LBUTTONUP:
                    _dispatcher.TryEnqueue(() => OpenRequested?.Invoke(this, EventArgs.Empty));
                    break;
                case WM_RBUTTONUP:
                    _dispatcher.TryEnqueue(ShowMenu);
                    break;
            }

            return IntPtr.Zero;
        }

        if (message == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            IsAdded = Add(); // проводник перезапустился — значок пропал
            return IntPtr.Zero;
        }

        if (message == WM_ENDSESSION && wParam != IntPtr.Zero)
        {
            try
            {
                SessionEnding?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
            }

            return IntPtr.Zero;
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        if (_disposed)
        {
            return;
        }

        var menu = CreatePopupMenu();
        try
        {
            AppendMenuW(menu, MF_STRING, CommandOpen, "Открыть");
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, CommandExit, "Выход");
            SetMenuDefaultItem(menu, (uint)CommandOpen, 0);

            GetCursorPos(out var point);
            // Без этого меню не закрывается по клику мимо него (документированная особенность TrackPopupMenu).
            SetForegroundWindow(_hwnd);
            var command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON, point.X, point.Y, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch ((nuint)command)
            {
                case CommandOpen:
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CommandExit:
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    // --- Win32 ---

    private const uint WM_NULL = 0x0000;
    private const uint WM_ENDSESSION = 0x0016;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;

    private const int NIM_ADD = 0;
    private const int NIM_MODIFY = 1;
    private const int NIM_DELETE = 2;
    private const int NIF_MESSAGE = 0x01;
    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;
    private const int NIF_INFO = 0x10;
    private const int NIIF_INFO = 0x01;

    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_RETURNCMD = 0x0100;

    private const int SM_CXSMICON = 49;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(int message, ref NOTIFYICONDATAW data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
