using System.Windows;
using System.Windows.Threading;
using Vcm.App.Services;
using Wpf.Ui.Appearance;

namespace Vcm.App;

public partial class App : Application
{
    private const string MutexName = @"Local\VCM.SingleInstance";
    private const string ShowSettingsEventName = @"Local\VCM.ShowSettings";

    private Mutex? _mutex;
    private EventWaitHandle? _showSettingsEvent;
    private RegisteredWaitHandle? _showSettingsWait;
    private AppController? _controller;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out bool firstInstance);
        if (!firstInstance)
        {
            // Another instance is running: ask it to open its settings window and quit.
            if (EventWaitHandle.TryOpenExisting(ShowSettingsEventName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        ApplicationThemeManager.ApplySystemTheme();

        _showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        _showSettingsWait = ThreadPool.RegisterWaitForSingleObject(_showSettingsEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _controller?.ShowSettings()), null, Timeout.Infinite, executeOnlyOnce: false);

        bool firstRun = SettingsStore.IsFirstRun;
        _controller = new AppController(Dispatcher);
        await _controller.StartAsync();

        bool background = e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        if (!background || firstRun)
        {
            _controller.ShowSettings();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showSettingsWait?.Unregister(null);
        _controller?.Dispose();
        _showSettingsEvent?.Dispose();
        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned (second instance).
            }
            _mutex.Dispose();
        }
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Unhandled exception", e.Exception);
        // Keep running: a broken settings page must not leave the microphone muted without the app.
        e.Handled = true;
    }
}
