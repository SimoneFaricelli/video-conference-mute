using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Vcm.Core;
using static Vcm.Core.CameraPipeProtocol;

namespace Vcm.App.Services;

/// <summary>
/// Pipe server the virtual camera media source connects to. The media source runs inside the Frame Server service
/// (session 0, LocalService), so the pipe ACL grants access to service accounts as well as to the current user.
/// Several instances can connect at once (one per activation of the virtual camera).
/// </summary>
internal sealed class CameraPipeServer : IDisposable
{
    private const int MaxInstances = 8;

    private readonly object _gate = new();
    private readonly List<Client> _clients = [];
    private readonly CancellationTokenSource _cts = new();
    private byte[] _stateMessage;
    private byte[] _overlayMessage;

    public CameraPipeServer(byte[] initialState, byte[] initialOverlay)
    {
        _stateMessage = initialState;
        _overlayMessage = initialOverlay;
    }

    /// <summary>Raised on a background thread when the aggregated "camera in use" flag changes.</summary>
    public event Action<bool>? InUseChanged;

    public bool InUse { get; private set; }

    public void Start()
    {
        _ = Task.Run(AcceptLoopAsync);
    }

    public void UpdateState(byte[] stateMessage) => Broadcast(() => _stateMessage = stateMessage, stateMessage);

    public void UpdateOverlay(byte[] overlayMessage) => Broadcast(() => _overlayMessage = overlayMessage, overlayMessage);

    private void Broadcast(Action store, byte[] message)
    {
        List<Client> clients;
        lock (_gate)
        {
            store();
            clients = [.. _clients];
        }

        foreach (var client in clients)
        {
            _ = client.SendAsync(message);
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous, 64 * 1024, 64 * 1024, CreateSecurity());
            }
            catch (IOException ex)
            {
                // All instances busy, or another process owns the name; retry later.
                Logger.Warn($"Camera pipe unavailable: {ex.Message}");
                await DelayAsync(TimeSpan.FromSeconds(2));
                continue;
            }
            catch (Exception ex)
            {
                Logger.Error("Camera pipe creation failed", ex);
                return;
            }

            try
            {
                await pipe.WaitForConnectionAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                return;
            }
            catch (IOException ex)
            {
                Logger.Warn($"Camera pipe connection failed: {ex.Message}");
                await pipe.DisposeAsync();
                continue;
            }

            var client = new Client(pipe);
            byte[] state, overlay;
            lock (_gate)
            {
                _clients.Add(client);
                state = _stateMessage;
                overlay = _overlayMessage;
            }

            Logger.Info("Virtual camera connected to the pipe");
            _ = client.SendAsync(state);
            _ = client.SendAsync(overlay);
            _ = Task.Run(() => ReadLoopAsync(client));
        }
    }

    private async Task ReadLoopAsync(Client client)
    {
        var header = new byte[HeaderSize];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await client.Pipe.ReadExactlyAsync(header, _cts.Token);
                var (type, length) = DecodeHeader(header);
                var payload = new byte[length];
                await client.Pipe.ReadExactlyAsync(payload, _cts.Token);

                switch (type)
                {
                    case MessageType.Hello:
                        Logger.Info($"Virtual camera hello: protocol {BitConverter.ToUInt32(payload, 0)}, pid {BitConverter.ToUInt32(payload, 4)}");
                        break;
                    case MessageType.InUse:
                        client.InUse = DecodeInUse(payload);
                        Logger.Info($"Virtual camera in use: {client.InUse}");
                        UpdateInUse();
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or OperationCanceledException or InvalidDataException or ObjectDisposedException)
        {
        }

        lock (_gate)
        {
            _clients.Remove(client);
        }
        client.Dispose();
        Logger.Info("Virtual camera disconnected from the pipe");
        UpdateInUse();
    }

    private void UpdateInUse()
    {
        bool inUse;
        lock (_gate)
        {
            inUse = _clients.Any(c => c.InUse);
            if (inUse == InUse) return;
            InUse = inUse;
        }
        InUseChanged?.Invoke(inUse);
    }

    private static PipeSecurity CreateSecurity()
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User!;
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        foreach (var sid in new[] { WellKnownSidType.LocalServiceSid, WellKnownSidType.LocalSystemSid, WellKnownSidType.NetworkServiceSid })
        {
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        }
        return security;
    }

    private async Task DelayAsync(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        lock (_gate)
        {
            foreach (var client in _clients) client.Dispose();
            _clients.Clear();
        }
    }

    private sealed class Client(NamedPipeServerStream pipe) : IDisposable
    {
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public NamedPipeServerStream Pipe { get; } = pipe;

        public bool InUse { get; set; }

        public async Task SendAsync(byte[] message)
        {
            await _writeLock.WaitAsync();
            try
            {
                await Pipe.WriteAsync(message);
                await Pipe.FlushAsync();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The read loop notices the broken connection and cleans up.
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Dispose()
        {
            try
            {
                Pipe.Dispose();
            }
            catch (IOException)
            {
            }
        }
    }
}
