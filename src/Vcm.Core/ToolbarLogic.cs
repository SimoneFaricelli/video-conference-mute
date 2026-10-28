namespace Vcm.Core;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public readonly record struct PixelPoint(int X, int Y);

public enum CameraVisualState
{
    On,
    Off,
    NotInUse,
}

public readonly record struct ToolbarVisual(bool MicrophoneOn, CameraVisualState Camera);

/// <summary>Toolbar rules of the PowerToys <c>Toolbar.cpp</c>.</summary>
public static class ToolbarLogic
{
    /// <summary>Toolbar size in device-independent pixels (the original 322×44 design).</summary>
    public const int Width = 322;
    public const int Height = 44;

    /// <summary>How long the toolbar stays visible after any mute change (OVERLAY_SHOW_TIME).</summary>
    public static readonly TimeSpan FlashDuration = TimeSpan.FromMilliseconds(500);

    private const int BorderOffset = 12;
    private const int TopRightBorderOffset = 40;

    /// <summary>
    /// Top-left corner of the toolbar inside a monitor work area. Offsets are scaled with the monitor DPI
    /// (the original used fixed pixels, which at 100% gives the same result).
    /// </summary>
    public static PixelPoint CalculatePosition(string position, PixelRect workArea, int width, int height, double scale)
    {
        int offset = (int)Math.Round(BorderOffset * scale);
        int topRightOffset = (int)Math.Round(TopRightBorderOffset * scale);
        int midX = workArea.Left + workArea.Width / 2;

        return position switch
        {
            ToolbarPositions.TopLeft => new(workArea.Left + offset, workArea.Top + offset),
            ToolbarPositions.TopCenter => new(midX - width / 2, workArea.Top + offset),
            ToolbarPositions.BottomLeft => new(workArea.Left + offset, workArea.Bottom - height - offset),
            ToolbarPositions.BottomCenter => new(midX - width / 2, workArea.Bottom - height - offset),
            ToolbarPositions.BottomRight => new(workArea.Right - width - offset, workArea.Bottom - height - offset),
            _ => new(workArea.Right - width - offset, workArea.Top + topRightOffset),
        };
    }

    /// <param name="hideMode">Value of <c>toolbar_hide</c>.</param>
    /// <param name="cameraInUse">An application is streaming from the virtual camera.</param>
    /// <param name="cameraMuted">Raw camera mute flag.</param>
    /// <param name="microphoneMuted">Microphone state.</param>
    /// <param name="flashActive">Less than <see cref="FlashDuration"/> has passed since the last mute change.</param>
    public static bool ShouldShow(string hideMode, bool cameraInUse, bool cameraMuted, bool microphoneMuted, bool flashActive)
    {
        bool show = hideMode switch
        {
            ToolbarHideModes.Never => true,
            ToolbarHideModes.WhenBothMuted => (!cameraMuted && cameraInUse) || !microphoneMuted,
            ToolbarHideModes.WhenBothUnmuted => (cameraMuted && cameraInUse) || microphoneMuted,
            // "After timeout": visible only for a short time after each change.
            _ => false,
        };
        return show || flashActive;
    }

    public static ToolbarVisual GetVisual(bool microphoneMuted, bool cameraInUse, bool cameraMuted) =>
        new(!microphoneMuted, !cameraInUse ? CameraVisualState.NotInUse : cameraMuted ? CameraVisualState.Off : CameraVisualState.On);

    /// <summary>Clicks on the left half toggle the microphone, on the right half the camera.</summary>
    public static VcmDevice HitTest(double x, double toolbarWidth) => x < toolbarWidth / 2 ? VcmDevice.Microphone : VcmDevice.Camera;
}
