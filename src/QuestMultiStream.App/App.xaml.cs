using System.Windows;
using System.Windows.Threading;

namespace QuestMultiStream.App;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstanceGuard;

    protected override void OnStartup(StartupEventArgs e)
    {
        DesktopAppLog.Initialize();
        DesktopAppLog.Info("Application startup requested.");

        if (!SingleInstanceGuard.TryAcquire(out _singleInstanceGuard))
        {
            DesktopAppLog.Info("Another instance is already running. Activating the existing window and exiting.");
            SingleInstanceGuard.TryActivateExistingInstance();
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DesktopAppLog.Info($"Application exit code {e.ApplicationExitCode}.");

        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        SingleInstanceGuard.ReleaseCurrent();
        _singleInstanceGuard = null;

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DesktopAppLog.Error("Unhandled dispatcher exception.", e.Exception);
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        DesktopAppLog.Error(
            $"Unhandled AppDomain exception. Terminating={e.IsTerminating}.",
            e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        DesktopAppLog.Error("Unobserved task exception.", e.Exception);
    }
}
