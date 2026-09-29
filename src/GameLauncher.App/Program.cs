using System.Diagnostics;
using System.Runtime.InteropServices;
using GameLauncher.Core.Startup;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace GameLauncher.App;

/// <summary>
/// Своя точка входа: лаунчер работает в одном экземпляре. Два экземпляра держали бы библиотеку в памяти
/// каждый своей и перезаписывали бы друг другу library.json. Повторный запуск передаёт активацию уже
/// открытому окну и завершается. Схема — из документации Windows App SDK (AppInstance + RedirectActivationToAsync).
/// </summary>
public static class Program
{
    private const string InstanceKey = "GameLauncher.Main";

    [STAThread]
    private static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Перезапуск самого себя (после восстановления из копии): ждём, пока старый процесс освободит ключ экземпляра.
        if (StartupOptions.Parse(Environment.GetCommandLineArgs().Skip(1)).WaitForProcessId is { } previous)
        {
            WaitForExit(previous);
        }

        if (RedirectToRunningInstance())
        {
            return 0;
        }

        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App(); // приложение живёт в Application.Current
        });
        return 0;
    }

    /// <summary>True — лаунчер уже запущен, активация передана ему, этот процесс завершается.</summary>
    private static bool RedirectToRunningInstance()
    {
        var mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (mainInstance.IsCurrent)
        {
            mainInstance.Activated += (_, _) => (Application.Current as App)?.BringToFront();
            return false;
        }

        // Ждём перенаправление, прокачивая COM-сообщения: простое блокирующее ожидание STA-потока может зависнуть.
        var redirected = CreateEvent(IntPtr.Zero, bManualReset: true, bInitialState: false, lpName: null);
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();
        // Право вывести окно на передний план есть у процесса, который запустил пользователь, — у этого.
        // Передаём его первому экземпляру: его окно может быть спрятано в трей, и тогда вывести его может только он сам.
        AllowSetForegroundWindow(mainInstance.ProcessId);
        Task.Run(() =>
        {
            mainInstance.RedirectActivationToAsync(args).AsTask().Wait();
            SetEvent(redirected);
        });
        _ = CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1, [redirected], out _);

        // Окно уже видно (не в трее) — выводим его и отсюда: так надёжнее, чем только из первого экземпляра.
        try
        {
            using var running = Process.GetProcessById((int)mainInstance.ProcessId);
            SetForegroundWindow(running.MainWindowHandle);
        }
        catch (ArgumentException)
        {
            // Первый экземпляр успел закрыться — ничего страшного.
        }

        return true;
    }

    private static void WaitForExit(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit(TimeSpan.FromSeconds(15));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Процесс уже завершился (или недоступен) — ждать нечего.
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, uint nHandles, IntPtr[] pHandles, out uint dwIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
