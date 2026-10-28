using System.Windows.Threading;
using Vcm.Core;

namespace Vcm.App.Toolbar;

/// <summary>
/// Shows the toolbar on the main monitor or on all monitors and applies the hide rules. Purely event driven:
/// it refreshes on state changes and once more when the 500 ms flash after a change expires.
/// </summary>
internal sealed class ToolbarManager : IDisposable
{
    private readonly VcmEngine _engine;
    private readonly Func<VcmSettings> _settings;
    private readonly DispatcherTimer _flashTimer;
    private readonly List<ToolbarWindow> _windows = [];
    private DateTime _lastChange = DateTime.MinValue;
    private bool _enabled;

    public ToolbarManager(VcmEngine engine, Func<VcmSettings> settings, Dispatcher dispatcher)
    {
        _engine = engine;
        _settings = settings;
        _flashTimer = new DispatcherTimer(ToolbarLogic.FlashDuration, DispatcherPriority.Normal, (_, _) =>
        {
            _flashTimer!.Stop();
            Refresh();
        }, dispatcher);
        _flashTimer.Stop();
    }

    public event Action<VcmDevice>? Clicked;

    public void Show()
    {
        _enabled = true;
        Rebuild();
    }

    public void Hide()
    {
        _enabled = false;
        CloseWindows();
    }

    /// <summary>Recreates the windows, e.g. after position, monitor or display configuration changes.</summary>
    public void Rebuild()
    {
        CloseWindows();
        if (!_enabled)
        {
            return;
        }

        var settings = _settings();
        var monitors = Monitors.GetAll();
        var targets = settings.ToolbarMonitor == ToolbarMonitors.All
            ? monitors
            : monitors.Where(m => m.IsPrimary).Take(1).ToList();

        foreach (var monitor in targets)
        {
            var window = new ToolbarWindow(monitor.Handle, settings.ToolbarPosition);
            window.Clicked += device => Clicked?.Invoke(device);
            _windows.Add(window);
        }
        Refresh();
    }

    /// <summary>Called after every mute change: keeps the toolbar visible for the flash duration.</summary>
    public void Flash()
    {
        _lastChange = DateTime.UtcNow;
        _flashTimer.Stop();
        _flashTimer.Start();
        Refresh();
    }

    public void Refresh()
    {
        if (_windows.Count == 0)
        {
            return;
        }

        bool flashActive = DateTime.UtcNow - _lastChange < ToolbarLogic.FlashDuration;
        bool micMuted = _engine.MicrophoneMuted;
        bool show = ToolbarLogic.ShouldShow(_settings().ToolbarHide, _engine.CameraInUse, _engine.CameraMuted, micMuted, flashActive);
        var visual = ToolbarLogic.GetVisual(micMuted, _engine.CameraInUse, _engine.CameraMuted);
        bool dark = Services.SystemTheme.AppsUseDarkTheme;

        foreach (var window in _windows)
        {
            window.Render(visual, dark);
            if (show && !window.IsVisible)
            {
                window.Show();
                window.Reposition();
            }
            else if (!show && window.IsVisible)
            {
                window.Hide();
            }
        }
    }

    private void CloseWindows()
    {
        foreach (var window in _windows) window.Close();
        _windows.Clear();
    }

    public void Dispose()
    {
        _flashTimer.Stop();
        CloseWindows();
    }
}
