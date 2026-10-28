using Vcm.Core;

namespace Vcm.Core.Tests;

public class ToolbarLogicTests
{
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1040);

    [Theory]
    [InlineData(ToolbarPositions.TopLeft, 12, 12)]
    [InlineData(ToolbarPositions.TopCenter, 960 - 161, 12)]
    [InlineData(ToolbarPositions.TopRight, 1920 - 322 - 12, 40)]
    [InlineData(ToolbarPositions.BottomLeft, 12, 1040 - 44 - 12)]
    [InlineData(ToolbarPositions.BottomCenter, 960 - 161, 1040 - 44 - 12)]
    [InlineData(ToolbarPositions.BottomRight, 1920 - 322 - 12, 1040 - 44 - 12)]
    [InlineData("unknown", 1920 - 322 - 12, 40)]
    public void Position_At100Percent_MatchesOriginal(string position, int x, int y)
    {
        var p = ToolbarLogic.CalculatePosition(position, WorkArea, 322, 44, 1.0);
        Assert.Equal(new PixelPoint(x, y), p);
    }

    [Fact]
    public void Position_At150Percent_ScalesOffsetsAndRespectsWorkAreaOrigin()
    {
        var secondary = new PixelRect(1920, 100, 1920 + 2560, 100 + 1400);
        int w = (int)(322 * 1.5), h = (int)(44 * 1.5);

        Assert.Equal(new PixelPoint(1920 + 2560 - w - 18, 100 + 60),
            ToolbarLogic.CalculatePosition(ToolbarPositions.TopRight, secondary, w, h, 1.5));
        Assert.Equal(new PixelPoint(1920 + 18, 100 + 1400 - h - 18),
            ToolbarLogic.CalculatePosition(ToolbarPositions.BottomLeft, secondary, w, h, 1.5));
    }

    [Theory]
    // hide mode, camera in use, camera muted, mic muted, expected
    [InlineData(ToolbarHideModes.Never, false, false, false, true)]
    [InlineData(ToolbarHideModes.WhenBothUnmuted, true, false, false, false)]
    [InlineData(ToolbarHideModes.WhenBothUnmuted, false, false, true, true)]
    [InlineData(ToolbarHideModes.WhenBothUnmuted, true, true, false, true)]
    [InlineData(ToolbarHideModes.WhenBothUnmuted, false, true, false, false)] // idle camera mute does not count
    [InlineData(ToolbarHideModes.WhenBothMuted, true, true, true, false)]
    [InlineData(ToolbarHideModes.WhenBothMuted, false, false, true, false)]  // idle camera counts as muted
    [InlineData(ToolbarHideModes.WhenBothMuted, true, false, true, true)]
    [InlineData(ToolbarHideModes.WhenBothMuted, false, true, false, true)]
    [InlineData(ToolbarHideModes.AfterTimeout, true, true, true, false)]
    public void Visibility(string mode, bool camInUse, bool camMuted, bool micMuted, bool expected)
    {
        Assert.Equal(expected, ToolbarLogic.ShouldShow(mode, camInUse, camMuted, micMuted, flashActive: false));
    }

    [Theory]
    [InlineData(ToolbarHideModes.AfterTimeout)]
    [InlineData(ToolbarHideModes.WhenBothUnmuted)]
    [InlineData(ToolbarHideModes.WhenBothMuted)]
    public void Visibility_FlashAlwaysShows(string mode)
    {
        Assert.True(ToolbarLogic.ShouldShow(mode, true, true, true, flashActive: true));
        Assert.True(ToolbarLogic.ShouldShow(mode, true, false, false, flashActive: true));
    }

    [Theory]
    [InlineData(false, true, false, true, CameraVisualState.On)]
    [InlineData(false, true, true, true, CameraVisualState.Off)]
    [InlineData(true, true, true, false, CameraVisualState.Off)]
    [InlineData(true, false, true, false, CameraVisualState.NotInUse)]
    [InlineData(false, false, false, true, CameraVisualState.NotInUse)]
    public void Visual(bool micMuted, bool camInUse, bool camMuted, bool expectedMicOn, CameraVisualState expectedCam)
    {
        Assert.Equal(new ToolbarVisual(expectedMicOn, expectedCam), ToolbarLogic.GetVisual(micMuted, camInUse, camMuted));
    }

    [Fact]
    public void HitTest_SplitsInHalves()
    {
        Assert.Equal(VcmDevice.Microphone, ToolbarLogic.HitTest(10, 322));
        Assert.Equal(VcmDevice.Microphone, ToolbarLogic.HitTest(160.9, 322));
        Assert.Equal(VcmDevice.Camera, ToolbarLogic.HitTest(161, 322));
        Assert.Equal(VcmDevice.Camera, ToolbarLogic.HitTest(300, 322));
    }
}
