using System.IO;
using System.Runtime.InteropServices;
using Vcm.App.Native;

namespace Vcm.App.Services;

/// <summary>
/// Short confirmation sounds, synthesized once in memory: a falling two-tone for mute, a rising one for unmute.
/// </summary>
internal sealed class SoundService : IDisposable
{
    private readonly GCHandle _mute = GCHandle.Alloc(CreateTwoTone(880, 587), GCHandleType.Pinned);
    private readonly GCHandle _unmute = GCHandle.Alloc(CreateTwoTone(587, 880), GCHandleType.Pinned);

    public void Play(bool muted)
    {
        var sound = muted ? _mute : _unmute;
        NativeMethods.PlaySound(sound.AddrOfPinnedObject(), IntPtr.Zero,
            NativeMethods.SND_MEMORY | NativeMethods.SND_ASYNC | NativeMethods.SND_NODEFAULT);
    }

    public void Dispose()
    {
        NativeMethods.PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
        _mute.Free();
        _unmute.Free();
    }

    private static byte[] CreateTwoTone(double firstHz, double secondHz)
    {
        const int sampleRate = 44100;
        const double toneSeconds = 0.07;
        const double gap = 0.02;
        const double volume = 0.22;

        int toneSamples = (int)(sampleRate * toneSeconds);
        int gapSamples = (int)(sampleRate * gap);
        var samples = new short[toneSamples * 2 + gapSamples];

        void Tone(double hz, int offset)
        {
            for (int i = 0; i < toneSamples; i++)
            {
                // Short fade in/out avoids clicks.
                double envelope = Math.Min(1.0, Math.Min(i, toneSamples - i) / (sampleRate * 0.008));
                samples[offset + i] = (short)(Math.Sin(2 * Math.PI * hz * i / sampleRate) * envelope * volume * short.MaxValue);
            }
        }

        Tone(firstHz, 0);
        Tone(secondHz, toneSamples + gapSamples);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        int dataSize = samples.Length * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);          // PCM
        writer.Write((short)1);          // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);    // byte rate
        writer.Write((short)2);          // block align
        writer.Write((short)16);         // bits per sample
        writer.Write("data"u8);
        writer.Write(dataSize);
        foreach (short s in samples) writer.Write(s);
        writer.Flush();
        return stream.ToArray();
    }
}
