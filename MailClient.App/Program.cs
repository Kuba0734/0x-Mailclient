using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MailClient.App;

/// <summary>
/// Custom entry point: ensures only one instance runs. A second launch
/// (start menu, autostart, notification click) is redirected to the
/// running instance, which then brings its window to the front.
/// </summary>
public static class Program
{
    private const string InstanceKey = "MailClient.App.Main";

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (TryRedirectToRunningInstance())
            return 0;

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }

    private static bool TryRedirectToRunningInstance()
    {
        AppInstance keyInstance;
        try
        {
            keyInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
        }
        catch
        {
            return false;
        }

        if (keyInstance.IsCurrent)
        {
            keyInstance.Activated += (_, _) => App.Instance?.ShowMainWindow();
            return false;
        }

        var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        RedirectActivationTo(activationArgs, keyInstance);
        return true;
    }

    // See https://learn.microsoft.com/windows/apps/windows-app-sdk/applifecycle/applifecycle-single-instance
    private static void RedirectActivationTo(AppActivationArguments args, AppInstance keyInstance)
    {
        // Let the running instance bring its window to the foreground.
        try { AllowSetForegroundWindow(keyInstance.ProcessId); } catch { }

        var redirectEventHandle = CreateEvent(IntPtr.Zero, true, false, null);
        Task.Run(() =>
        {
            try { keyInstance.RedirectActivationToAsync(args).AsTask().Wait(); }
            catch { /* the other instance went away */ }
            SetEvent(redirectEventHandle);
        });

        const uint CWMO_DEFAULT = 0;
        const uint INFINITE = 0xFFFFFFFF;
        _ = CoWaitForMultipleObjects(CWMO_DEFAULT, INFINITE, 1, [redirectEventHandle], out _);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll")]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, ulong nHandles, IntPtr[] pHandles, out uint dwIndex);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);
}
