using Vcm.Core;
using static Vcm.Core.CameraPipeProtocol;

namespace Vcm.Core.Tests;

public class CameraPipeProtocolTests
{
    [Fact]
    public void State_RoundTrip()
    {
        var state = new CameraState(true, false, 42, @"\\?\USB#VID_046D&PID_085E#{e5323777-f976-4f5b-9b55-b94699c46e44}\GLOBAL");
        byte[] msg = EncodeState(state);

        var (type, length) = DecodeHeader(msg);
        Assert.Equal(MessageType.State, type);
        Assert.Equal(msg.Length - HeaderSize, length);
        Assert.Equal(state, DecodeState(msg.AsSpan(HeaderSize)));
    }

    [Fact]
    public void State_LayoutIsStable()
    {
        // The C++ media source parses this layout byte by byte.
        byte[] msg = EncodeState(new CameraState(true, true, 0x01020304, "AB"));
        Assert.Equal(
            new byte[] { 2, 0, 0, 0, 16, 0, 0, 0, 1, 1, 0, 0, 4, 3, 2, 1, 2, 0, 0, 0, (byte)'A', 0, (byte)'B', 0 },
            msg);
    }

    [Fact]
    public void Overlay_And_InUse()
    {
        byte[] overlay = EncodeOverlay(7, [0xFF, 0xD8, 0xFF]);
        Assert.Equal(new byte[] { 3, 0, 0, 0, 7, 0, 0, 0, 7, 0, 0, 0, 0xFF, 0xD8, 0xFF }, overlay);

        byte[] inUse = EncodeInUse(true);
        Assert.Equal((MessageType.InUse, 1), DecodeHeader(inUse));
        Assert.True(DecodeInUse(inUse.AsSpan(HeaderSize)));
        Assert.False(DecodeInUse(EncodeInUse(false).AsSpan(HeaderSize)));
    }

    [Fact]
    public void Header_RejectsOversizedPayload()
    {
        byte[] header = [1, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x7F];
        Assert.Throws<InvalidDataException>(() => DecodeHeader(header));
    }

    [Fact]
    public void State_RejectsTruncatedPayload()
    {
        byte[] msg = EncodeState(new CameraState(false, false, 1, "camera"));
        Assert.Throws<InvalidDataException>(() => DecodeState(msg.AsSpan(HeaderSize, msg.Length - HeaderSize - 2)));
    }
}
