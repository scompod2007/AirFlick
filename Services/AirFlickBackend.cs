using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AirFlick.Protocol;

namespace AirFlick.Services;

/// <summary>
/// Windows-side wiring for <see cref="AirFlickCommandService"/>. Wraps the EXISTING WiFiDirectService,
/// SessionManager and FileTransferService; it adds no networking of its own. Owned by ServiceManager so the
/// command service and its state machine live as long as the services do, independent of any UI page.
/// </summary>
public static class AirFlickBackend
{
    private static readonly object Lock = new();
    private static AirFlickCommandService? _command;
    private static SessionManagerGateway? _sessionGateway;
    private static SessionAuthorizationChannel? _authorization;

    /// <summary>Null until ServiceManager has initialised the Wi-Fi Direct services.</summary>
    public static AirFlickCommandService? Command
    {
        get
        {
            lock (Lock)
            {
                return _command;
            }
        }
    }

    /// <summary>
    /// Receiver-side entry point for PIN authorization. A future UI/gesture layer subscribes to
    /// <see cref="SessionAuthorizationChannel.AuthorizationRequested"/> and answers with
    /// <see cref="SessionAuthorizationChannel.SubmitPinAsync"/>. Null until initialised.
    /// </summary>
    public static SessionAuthorizationChannel? Authorization
    {
        get
        {
            lock (Lock)
            {
                return _authorization;
            }
        }
    }

    public static void Initialize(WiFiDirectService wifiDirect, SessionManager session, FileTransferService fileTransfer)
    {
        ArgumentNullException.ThrowIfNull(wifiDirect);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(fileTransfer);

        lock (Lock)
        {
            DisposeCoreLocked();

            _authorization = new SessionAuthorizationChannel(session);
            _sessionGateway = new SessionManagerGateway(wifiDirect, session);
            _command = new AirFlickCommandService(
                _sessionGateway,
                new FileTransferGateway(fileTransfer),
                new PinAuthorizationService(),
                stateMachine: null,
                authorizationChannel: _authorization);
        }
    }

    public static void Reset()
    {
        lock (Lock)
        {
            DisposeCoreLocked();
        }
    }

    private static void DisposeCoreLocked()
    {
        _command?.Dispose();
        _command = null;

        // Unsubscribe explicitly so repeated Initialize/Reset cycles cannot multiply handlers on the
        // long-lived SessionManager/WiFiDirectService singletons.
        _sessionGateway?.Dispose();
        _sessionGateway = null;

        _authorization?.Dispose();
        _authorization = null;
    }

    private sealed class FileTransferGateway : IAirFlickTransferGateway
    {
        private readonly FileTransferService _fileTransfer;

        public FileTransferGateway(FileTransferService fileTransfer)
        {
            _fileTransfer = fileTransfer;
        }

        public async Task<AirFlickTransferOutcome> SendFileAsync(string filePath, CancellationToken cancellationToken)
        {
            var result = await _fileTransfer.SendFilePathAsync(filePath, cancellationToken).ConfigureAwait(false);
            return new AirFlickTransferOutcome(result.Success, result.Error, result.FileName, result.BytesSent);
        }
    }

    private sealed class SessionManagerGateway : IAirFlickSessionGateway, IDisposable
    {
        private readonly WiFiDirectService _wifiDirect;
        private readonly SessionManager _session;
        private bool _disposed;

        public SessionManagerGateway(WiFiDirectService wifiDirect, SessionManager session)
        {
            _wifiDirect = wifiDirect;
            _session = session;

            _session.StateChanged += OnStateChanged;
            _session.SessionReady += OnSessionReady;
            _session.SessionFailed += OnSessionFailed;
            _wifiDirect.DeviceDiscovered += OnDeviceDiscovered;
            _wifiDirect.ConnectionRequested += OnConnectionRequested;
        }

        public event EventHandler<AirFlickSessionChangedEventArgs>? PhaseChanged;

        public event EventHandler<string>? DeviceDiscovered;

        public SessionPhase Phase => _session.CurrentPhase;

        public bool IsReady => _session.IsReady;

        public string? PeerName => _session.PeerName ?? _wifiDirect.ConnectedDevice?.Name;

        public Task<bool> StartDiscoveryAsync()
        {
            return _wifiDirect.StartScanAsync("airflick_command");
        }

        public async Task<bool> ConnectToDeviceAsync(string deviceName)
        {
            var device = FindDiscoveredDevice(deviceName);
            return device != null && await _wifiDirect.ConnectToDeviceAsync(device).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _session.StateChanged -= OnStateChanged;
            _session.SessionReady -= OnSessionReady;
            _session.SessionFailed -= OnSessionFailed;
            _wifiDirect.DeviceDiscovered -= OnDeviceDiscovered;
            _wifiDirect.ConnectionRequested -= OnConnectionRequested;
        }

        private void OnConnectionRequested(object? sender, ConnectionRequestEventArgs e)
        {
            if (_disposed)
            {
                e.ResponseTask.TrySetResult(false);
                return;
            }

            // Accept only requests that can be handled safely under the existing connection and session model:
            // AirFlick operates on a single peer session at a time. Reject if already connected or connecting.
            if (_session.IsReady ||
                _wifiDirect.IsConnected ||
                _wifiDirect.CurrentState is WiFiDirectServiceState.ConnectingOutbound or WiFiDirectServiceState.ConnectingInbound or WiFiDirectServiceState.Connected)
            {
                e.ResponseTask.TrySetResult(false);
                return;
            }

            // Automatically accept the incoming Wi-Fi Direct connection request so that
            // the receiving laptop can establish transport without requiring manual pairing.
            // Final authorization is strictly gated by the 3-digit PIN proof over the control channel.
            e.ResponseTask.TrySetResult(true);
        }

        private void OnStateChanged(object? sender, SessionStateChangedEventArgs e)
        {
            PhaseChanged?.Invoke(this, new AirFlickSessionChangedEventArgs(e.Phase, e.PeerName));
        }

        private void OnSessionReady(object? sender, SessionReadyEventArgs e)
        {
            PhaseChanged?.Invoke(this, new AirFlickSessionChangedEventArgs(SessionPhase.Ready, e.PeerName));
        }

        private void OnSessionFailed(object? sender, SessionFailedEventArgs e)
        {
            PhaseChanged?.Invoke(this, new AirFlickSessionChangedEventArgs(SessionPhase.Failed, e.Message));
        }

        private void OnDeviceDiscovered(object? sender, WiFiDirectDevice device)
        {
            DeviceDiscovered?.Invoke(this, device.Name);
        }

        private WiFiDirectDevice? FindDiscoveredDevice(string deviceName)
        {
            // DiscoveredDevices is bound to UI and mutated on the UI thread; snapshot defensively.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    return _wifiDirect.DiscoveredDevices.ToArray().FirstOrDefault(d =>
                        string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
                }
                catch (InvalidOperationException)
                {
                }
            }

            return null;
        }
    }
}

public sealed class AirFlickAuthorizationRequestedEventArgs : EventArgs
{
    public AirFlickAuthorizationRequestedEventArgs(AirFlickAuthorizationChallenge challenge)
    {
        Challenge = challenge;
    }

    public AirFlickAuthorizationChallenge Challenge { get; }
}

public sealed class AirFlickAuthorizationResultEventArgs : EventArgs
{
    public AirFlickAuthorizationResultEventArgs(AirFlickAuthorizationResultMessage result)
    {
        Result = result;
    }

    public AirFlickAuthorizationResultMessage Result { get; }
}

/// <summary>
/// Carries the AirFlick PIN authorization exchange over the EXISTING control channel of an already Ready
/// session. It adds no frame types, no sockets and no session lifecycle: it writes
/// <see cref="ProtocolFrameType.ControlMessage"/> frames through <see cref="SessionManager"/> and reads the
/// peer's replies from <see cref="SessionManager.ControlFrameReceived"/>. Frames that are not AirFlick
/// authorization frames are left untouched for Chat and the other existing control consumers.
///
/// Sender side: <see cref="RequestProofAsync"/> (used by <see cref="AirFlickCommandService"/>).
/// Receiver side: <see cref="AuthorizationRequested"/> + <see cref="SubmitPinAsync"/>, which is the only
/// place a future UI/gesture layer needs to touch.
/// </summary>
public sealed class SessionAuthorizationChannel : IAirFlickAuthorizationChannel, IDisposable
{
    /// <summary>Matches the 120s (2-minute) PIN lifetime and the existing transfer-ack style timeouts.</summary>
    public static readonly TimeSpan DefaultProofTimeout = TimeSpan.FromSeconds(120);

    private readonly SessionManager _session;
    private readonly TimeSpan _proofTimeout;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingProofs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AirFlickAuthorizationChallenge> _incomingChallenges = new(StringComparer.Ordinal);
    private bool _disposed;

    public SessionAuthorizationChannel(SessionManager session, TimeSpan? proofTimeout = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _proofTimeout = proofTimeout ?? DefaultProofTimeout;

        _session.ControlFrameReceived += OnControlFrameReceived;
        _session.StateChanged += OnSessionStateChanged;
        _session.SessionFailed += OnSessionFailed;
    }

    /// <summary>Raised on the receiving side when the peer asks for PIN authorization.</summary>
    public event EventHandler<AirFlickAuthorizationRequestedEventArgs>? AuthorizationRequested;

    /// <summary>Raised on the receiving side with the sender's verdict for a submitted PIN.</summary>
    public event EventHandler<AirFlickAuthorizationResultEventArgs>? AuthorizationResult;

    /// <summary>Challenges received from the peer that have not yet been answered or invalidated.</summary>
    public IReadOnlyCollection<AirFlickAuthorizationChallenge> PendingChallenges => _incomingChallenges.Values.ToArray();

    // ---- sender side ----------------------------------------------------------------------------------

    public async Task<AirFlickAuthorizationResponse> RequestProofAsync(
        AirFlickAuthorizationChallenge challenge,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        if (!_session.IsReady)
        {
            return new AirFlickAuthorizationResponse(null, "Session is not ready");
        }

        var waiter = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingProofs[challenge.AuthorizationId] = waiter;

        try
        {
            await _session
                .SendControlFrameAsync(AirFlickAuthorizationProtocol.CreateChallengeFrame(challenge), cancellationToken)
                .ConfigureAwait(false);

            var proof = await waiter.Task.WaitAsync(_proofTimeout, cancellationToken).ConfigureAwait(false);
            return new AirFlickAuthorizationResponse(proof, null);
        }
        catch (TimeoutException)
        {
            return new AirFlickAuthorizationResponse(null, "Peer did not answer the authorization request in time");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AirFlickAuthorizationResponse(null, "Authorization was cancelled");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AirFlickAuthorizationResponse(null, ex.Message);
        }
        finally
        {
            _pendingProofs.TryRemove(challenge.AuthorizationId, out _);
        }
    }

    public async Task PublishResultAsync(string authorizationId, bool authorized, string? message)
    {
        if (string.IsNullOrWhiteSpace(authorizationId) || !_session.IsReady)
        {
            return;
        }

        try
        {
            await _session
                .SendControlFrameAsync(
                    AirFlickAuthorizationProtocol.CreateResultFrame(authorizationId, authorized, message))
                .ConfigureAwait(false);
        }
        catch
        {
            // Advisory only.
        }
    }

    // ---- receiver side --------------------------------------------------------------------------------

    /// <summary>
    /// Answers a pending challenge with the PIN the local user entered. The PIN never leaves this process:
    /// only the HMAC proof is transmitted. Returns false when the challenge is unknown, already answered,
    /// expired, the PIN is malformed, or the session is not Ready.
    /// </summary>
    public async Task<bool> SubmitPinAsync(string authorizationId, string pin, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authorizationId) ||
            !PinAuthorizationService.IsValidPinFormat(pin) ||
            !_session.IsReady ||
            !_incomingChallenges.TryRemove(authorizationId, out var challenge))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow >= challenge.ExpiresAtUtc)
        {
            return false;
        }

        try
        {
            var proof = PinAuthorizationService.ComputeProof(pin, challenge.AuthorizationId, challenge.Nonce);
            await _session
                .SendControlFrameAsync(
                    AirFlickAuthorizationProtocol.CreateProofFrame(challenge.AuthorizationId, proof),
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Drops a pending incoming challenge without answering it (user declined / timed out).</summary>
    public bool DeclineChallenge(string authorizationId)
    {
        return authorizationId != null && _incomingChallenges.TryRemove(authorizationId, out _);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.ControlFrameReceived -= OnControlFrameReceived;
        _session.StateChanged -= OnSessionStateChanged;
        _session.SessionFailed -= OnSessionFailed;
        FailPending("AirFlick authorization channel disposed");
        _incomingChallenges.Clear();
    }

    // ---- internals ------------------------------------------------------------------------------------

    private void OnControlFrameReceived(object? sender, ProtocolFrameReceivedEventArgs e)
    {
        if (_disposed || !AirFlickAuthorizationProtocol.IsAuthorizationFrame(e.Frame, out var operation))
        {
            return;
        }

        switch (operation)
        {
            case AirFlickAuthorizationProtocol.OpChallenge:
                if (AirFlickAuthorizationProtocol.TryParseChallenge(e.Frame, out var challenge) && challenge != null)
                {
                    _incomingChallenges[challenge.AuthorizationId] = challenge;
                    AuthorizationRequested?.Invoke(this, new AirFlickAuthorizationRequestedEventArgs(challenge));
                }

                break;

            case AirFlickAuthorizationProtocol.OpProof:
                if (AirFlickAuthorizationProtocol.TryParseProof(e.Frame, out var proof) &&
                    proof != null &&
                    _pendingProofs.TryRemove(proof.AuthorizationId, out var waiter))
                {
                    waiter.TrySetResult(proof.Proof);
                }

                break;

            case AirFlickAuthorizationProtocol.OpResult:
                if (AirFlickAuthorizationProtocol.TryParseResult(e.Frame, out var result) && result != null)
                {
                    _incomingChallenges.TryRemove(result.AuthorizationId, out _);
                    AuthorizationResult?.Invoke(this, new AirFlickAuthorizationResultEventArgs(result));
                }

                break;
        }
    }

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        if (e.Phase is SessionPhase.Disconnected or SessionPhase.Disconnecting or SessionPhase.Failed)
        {
            FailPending("Session ended before the peer authorized the transfer");
            _incomingChallenges.Clear();
        }
    }

    private void OnSessionFailed(object? sender, SessionFailedEventArgs e)
    {
        FailPending($"Session failed: {e.Message}");
        _incomingChallenges.Clear();
    }

    private void FailPending(string message)
    {
        foreach (var pair in _pendingProofs)
        {
            if (_pendingProofs.TryRemove(pair.Key, out var waiter))
            {
                waiter.TrySetException(new InvalidOperationException(message));
            }
        }
    }
}
