using Vcm.Core;

namespace Vcm.Core.Tests;

internal sealed class FakeMicrophone : IMicrophoneController
{
    public bool IsMuted { get; set; }
    public int SetCalls { get; private set; }

    public void SetMuted(bool muted)
    {
        IsMuted = muted;
        SetCalls++;
    }
}

internal sealed class FakeCamera : ICameraController
{
    public bool IsInUse { get; set; }
    public bool IsMuted { get; set; }

    public void SetMuted(bool muted) => IsMuted = muted;
}

internal sealed class EngineFixture
{
    public FakeMicrophone Mic { get; } = new();
    public FakeCamera Camera { get; } = new();
    public VcmSettings Settings { get; } = new();
    public List<VcmStateChange> Changes { get; } = [];
    public VcmEngine Engine { get; }

    public EngineFixture()
    {
        Engine = new VcmEngine(Mic, Camera, () => Settings);
        Engine.StateChanged += Changes.Add;
    }
}
