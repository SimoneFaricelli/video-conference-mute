using System.Buffers.Binary;
using System.Text;

namespace Vcm.Core;

/// <summary>
/// Messages exchanged on <c>\\.\pipe\VCM.VirtualCamera</c> between the app (pipe server) and the virtual camera
/// media source running inside the Windows Frame Server (pipe client). Must match <c>PipeProtocol.h</c>.
/// Each message is an 8-byte header (type, payload length; little-endian uint32) followed by the payload.
/// </summary>
public static class CameraPipeProtocol
{
    public const string PipeName = "VCM.VirtualCamera";
    public const uint ProtocolVersion = 1;
    public const int HeaderSize = 8;
    public const int MaxPayloadSize = 32 * 1024 * 1024;

    public enum MessageType : uint
    {
        /// <summary>Source → app: uint32 protocol version, uint32 process id.</summary>
        Hello = 1,

        /// <summary>App → source: see <see cref="CameraState"/>.</summary>
        State = 2,

        /// <summary>App → source: uint32 overlay version, then the encoded image file (empty = black).</summary>
        Overlay = 3,

        /// <summary>Source → app: uint8 1 while streaming, 0 when stopped.</summary>
        InUse = 4,
    }

    public readonly record struct CameraState(bool Muted, bool PowerOffWhenMuted, uint OverlayVersion, string SourceSymbolicLink);

    public static byte[] EncodeState(CameraState state)
    {
        byte[] link = Encoding.Unicode.GetBytes(state.SourceSymbolicLink ?? string.Empty);
        var payload = new byte[12 + link.Length];
        payload[0] = state.Muted ? (byte)1 : (byte)0;
        payload[1] = state.PowerOffWhenMuted ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), state.OverlayVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), (uint)(link.Length / 2));
        link.CopyTo(payload, 12);
        return Frame(MessageType.State, payload);
    }

    public static CameraState DecodeState(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 12) throw new InvalidDataException("State message too short");
        uint chars = BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]);
        if (12 + chars * 2 > payload.Length) throw new InvalidDataException("Invalid symbolic link length");
        string link = Encoding.Unicode.GetString(payload.Slice(12, (int)chars * 2));
        return new CameraState(payload[0] != 0, payload[1] != 0, BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]), link);
    }

    public static byte[] EncodeOverlay(uint version, ReadOnlySpan<byte> image)
    {
        var payload = new byte[4 + image.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, version);
        image.CopyTo(payload.AsSpan(4));
        return Frame(MessageType.Overlay, payload);
    }

    public static byte[] EncodeHello(uint processId)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, ProtocolVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), processId);
        return Frame(MessageType.Hello, payload);
    }

    public static byte[] EncodeInUse(bool inUse) => Frame(MessageType.InUse, [inUse ? (byte)1 : (byte)0]);

    public static bool DecodeInUse(ReadOnlySpan<byte> payload) => payload.Length >= 1 && payload[0] != 0;

    public static (MessageType Type, int Length) DecodeHeader(ReadOnlySpan<byte> header)
    {
        var type = (MessageType)BinaryPrimitives.ReadUInt32LittleEndian(header);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        if (length > MaxPayloadSize) throw new InvalidDataException($"Message too large: {length}");
        return (type, (int)length);
    }

    private static byte[] Frame(MessageType type, byte[] payload)
    {
        var message = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(message, (uint)type);
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(message, HeaderSize);
        return message;
    }
}
