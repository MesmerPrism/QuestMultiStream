using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace QuestMultiStream.App;

internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\QuestMultiStream.App.SingleInstance";
    private const int SwRestore = 9;
    private const int SwShow = 5;
    private static SingleInstanceGuard? _current;

    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;

    private SingleInstanceGuard(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    public static bool TryAcquire(out SingleInstanceGuard? guard)
    {
        guard = null;

        try
        {
            var mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var createdNew);
            guard = new SingleInstanceGuard(mutex, ownsMutex: createdNew);
            if (createdNew)
            {
                _current = guard;
                return true;
            }

            guard.Dispose();
            guard = null;
            return false;
        }
        catch (AbandonedMutexException exception)
        {
            var mutex = exception.Mutex
                ?? throw new InvalidOperationException("Abandoned mutex did not expose the underlying handle.");
            guard = new SingleInstanceGuard(mutex, ownsMutex: true);
            _current = guard;
            return true;
        }
    }

    public static void ReleaseCurrent()
    {
        _current?.Dispose();
        _current = null;
    }

    public static bool TryActivateExistingInstance()
    {
        var currentProcess = Process.GetCurrentProcess();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var existingProcess = Process.GetProcessesByName(currentProcess.ProcessName)
                .Where(process => process.Id != currentProcess.Id)
                .OrderBy(process => SafeGetStartTimeUtc(process))
                .FirstOrDefault();

            if (existingProcess is not null)
            {
                existingProcess.Refresh();
                var windowHandle = existingProcess.MainWindowHandle;
                if (windowHandle != IntPtr.Zero)
                {
                    if (NativeMethods.IsIconic(windowHandle))
                    {
                        NativeMethods.ShowWindow(windowHandle, SwRestore);
                    }
                    else
                    {
                        NativeMethods.ShowWindow(windowHandle, SwShow);
                    }

                    NativeMethods.BringWindowToTop(windowHandle);
                    return NativeMethods.SetForegroundWindow(windowHandle);
                }
            }

            Thread.Sleep(150);
        }

        return false;
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex.Dispose();
    }

    private static DateTime SafeGetStartTimeUtc(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return DateTime.MaxValue;
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr windowHandle);
    }
}
