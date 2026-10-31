using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AirFlick.Services;

// ---- Gateways: the only things the command layer knows about the outside world -------------------------
// Real implementations live in AirFlickBackend.cs (they wrap WiFiDirectService / SessionManager /
// FileTransferService). Keeping this file free of Windows types lets it be unit-tested without hardware.

public sealed class AirFlickSessionChangedEventArgs : EventArgs
{
    public AirFlickSessionChangedEventArgs(SessionPhase phase, string? peerName)
    {
        Phase = phase;
        PeerName = peerName;
    }

    public SessionPhase Phase { get; }

    public string? PeerName { get; }
}

public interface IAirFlickSessionGateway
{
    SessionPhase Phase { get; }

    bool IsReady { get; }

    string? PeerName { get; }

    event EventHandler<AirFlickSessionChangedEventArgs>? PhaseChanged;

    event EventHandler<string>? DeviceDiscovered;

    Task<bool> StartDiscoveryAsync();

    Task<bool> ConnectToDeviceAsync(string deviceName);
}

public sealed record AirFlickTransferOutcome(bool Success, string? Error, string FileName, long BytesSent, string? DestinationPath = null);

public interface IAirFlickTransferGateway
{
    /// <summary>Streams the file; Success means the receiver verified and finalized it.</summary>
    Task<AirFlickTransferOutcome> SendFileAsync(string filePath, CancellationToken cancellationToken);
}

/// <summary>Proof returned by the peer, or the reason none arrived.</summary>
public sealed record AirFlickAuthorizationResponse(byte[]? Proof, string? Error);

/// <summary>
/// The single protocol integration point for PIN authorization. The command layer only knows this
/// interface; the wire format lives in <see cref="AirFlickAuthorizationProtocol"/> and the transport
/// wiring in AirFlickBackend, so no PIN logic is scattered through the networking stack.
/// </summary>
public interface IAirFlickAuthorizationChannel
{
    /// <summary>Publishes the challenge to the peer and waits for the peer's HMAC proof.</summary>
    Task<AirFlickAuthorizationResponse> RequestProofAsync(
        AirFlickAuthorizationChallenge challenge,
        CancellationToken cancellationToken);

    /// <summary>Tells the peer whether its proof was accepted. Best effort; never throws.</summary>
    Task PublishResultAsync(string authorizationId, bool authorized, string? message);
}

// ---- Results ------------------------------------------------------------------------------------------

public enum AirFlickCommandStatus
{
    Ok,
    NotReady,
    NoFilePrepared,
    FileNotFound,
    TargetMismatch,
    NotAuthorized,
    AuthorizationFailed,
    Busy,
    InvalidState,
    Cancelled,
    TransferFailed
}

public sealed record AirFlickCommandResult(
    AirFlickCommandStatus Status,
    string? Message = null,
    PinAuthorizationTicket? Ticket = null)
{
    public bool Success => Status == AirFlickCommandStatus.Ok;
}

/// <summary>
/// Backend command layer for AirFlick. Future callers (gesture process, CLI, UI, tests) use this and never
/// touch Wi-Fi Direct, sockets or the bulk protocol. It contains no networking code: it orchestrates the
/// session gateway, the PIN service and the transfer gateway, and publishes <see cref="AirFlickStateMachine"/>.
///
/// Typical flow (session is already Ready, no reconnect per file):
///   PrepareFile -> (SelectTarget) -> RequestAuthorization -> VerifyPin / SubmitPinProof -> SendPreparedFileAsync
/// After a transfer (success or failure) the backend returns to Waiting while the session is still healthy.
/// </summary>
public sealed class AirFlickCommandService : IDisposable
{
    private readonly IAirFlickSessionGateway _session;
    private readonly IAirFlickTransferGateway _transfer;
    private readonly IPinAuthorizationService _pin;
    private readonly IAirFlickAuthorizationChannel? _authorizationChannel;
    private readonly AirFlickStateMachine _state;
    private readonly object _lock = new();

    private string? _preparedFilePath;
    private string? _selectedTarget;
    private PinAuthorizationTicket? _ticket;
    private bool _isPendingShare;
    private bool _isSending;
    private CancellationTokenSource? _sendCts;
    private bool _disposed;

    public AirFlickCommandService(
        IAirFlickSessionGateway session,
        IAirFlickTransferGateway transfer,
        IPinAuthorizationService pin,
        AirFlickStateMachine? stateMachine = null,
        IAirFlickAuthorizationChannel? authorizationChannel = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _transfer = transfer ?? throw new ArgumentNullException(nameof(transfer));
        _pin = pin ?? throw new ArgumentNullException(nameof(pin));
        _authorizationChannel = authorizationChannel;
        _state = stateMachine ?? new AirFlickStateMachine();

        _state.StateChanged += OnStateMachineChanged;
        _session.PhaseChanged += OnPhaseChanged;
        _session.DeviceDiscovered += OnDeviceDiscovered;

        // Seed from the real session so the backend is correct even if a session is already Ready
        // (no UI page needs to be open).
        _state.ReportSessionPhase(_session.Phase, _session.PeerName);
    }

    public event EventHandler<AirFlickStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Raised by <see cref="RequestRemoteAuthorizationAsync"/> as soon as the ticket exists and before the
    /// peer's answer is awaited, so the local side can display the PIN for the user to read out.
    /// </summary>
    public event EventHandler<PinAuthorizationTicket>? AuthorizationRequested;

    public AirFlickStateMachine StateMachine => _state;

    public AirFlickState State => _state.Current;

    public bool IsSessionReady => _session.IsReady;

    public string? PreparedFilePath
    {
        get
        {
            lock (_lock)
            {
                return _preparedFilePath;
            }
        }
    }

    public string? SelectedTarget
    {
        get
        {
            lock (_lock)
            {
                return _selectedTarget;
            }
        }
    }

    public PinAuthorizationTicket? CurrentTicket
    {
        get
        {
            lock (_lock)
            {
                return _ticket;
            }
        }
    }

    public bool HasPendingShare
    {
        get
        {
            lock (_lock)
            {
                return _isPendingShare && _ticket != null && !_pin.IsAuthorized(_ticket.AuthorizationId);
            }
        }
    }

    // ---- Discovery / connection (orchestration only; WiFiDirectService does the work) ----------------

    public Task<bool> StartDiscoveryAsync()
    {
        _state.ReportDiscovering();
        return _session.StartDiscoveryAsync();
    }

    public async Task<AirFlickCommandResult> ConnectAsync(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return new AirFlickCommandResult(AirFlickCommandStatus.InvalidState, "Device name is required");
        }

        if (_session.IsReady)
        {
            return new AirFlickCommandResult(AirFlickCommandStatus.Ok, "Session already ready");
        }

        _state.ReportDeviceConnecting(deviceName);
        var connected = await _session.ConnectToDeviceAsync(deviceName).ConfigureAwait(false);
        if (!connected)
        {
            _state.ReportSessionPhase(_session.Phase, _session.PeerName);
            return new AirFlickCommandResult(AirFlickCommandStatus.NotReady, $"Could not connect to {deviceName}");
        }

        return new AirFlickCommandResult(AirFlickCommandStatus.Ok, "Connecting");
    }

    // ---- Transfer workflow ----------------------------------------------------------------------------

    /// <summary>
    /// Offline-first file share initiation: prepares the file and creates a fresh 3-digit PIN authorization ticket
    /// before any peer has connected. Associations of file, ticket and expiry are established.
    /// </summary>
    public AirFlickCommandResult InitiatePendingShare(string filePath)
    {
        lock (_lock)
        {
            if (_isSending)
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.Busy, "A transfer is in progress");
            }

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.FileNotFound, "File does not exist");
            }

            ClearAuthorizationLocked();
            if (_state.Current == AirFlickState.Failed)
            {
                _state.Recover("prepare_new_share");
            }
            _state.ReportFileReleased();

            if (!_state.ReportPendingShare(Path.GetFileName(filePath)))
            {
                _preparedFilePath = null;
                _isPendingShare = false;
                return new AirFlickCommandResult(AirFlickCommandStatus.InvalidState, $"Cannot prepare a file while {_state.Current}");
            }

            _preparedFilePath = filePath;
            var ticket = _pin.CreateAuthorization();
            _ticket = ticket;
            _isPendingShare = true;
            _state.ReportPinRequired();

            AuthorizationRequested?.Invoke(this, ticket);
            return new AirFlickCommandResult(AirFlickCommandStatus.Ok, Ticket: ticket);
        }
    }

    /// <summary>"Grab": remember which file will be sent. Requires a Ready session and an idle backend.</summary>
    public AirFlickCommandResult PrepareFile(string filePath)
    {
        lock (_lock)
        {
            if (_isSending)
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.Busy, "A transfer is in progress");
            }

            if (!_session.IsReady)
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.NotReady, "Session is not ready");
            }

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.FileNotFound, "File does not exist");
            }

            // Replacing an earlier prepared file drops its authorization.
            ClearAuthorizationLocked();
            if (_state.Current == AirFlickState.Failed)
            {
                _state.Recover("prepare_new_share");
            }
            _state.ReportFileReleased();

            if (!_state.ReportFileGrabbed(Path.GetFileName(filePath)))
            {
                _preparedFilePath = null;
                _isPendingShare = false;
                return new AirFlickCommandResult(AirFlickCommandStatus.InvalidState, $"Cannot prepare a file while {_state.Current}");
            }

            _preparedFilePath = filePath;
            _isPendingShare = false;
            return new AirFlickCommandResult(AirFlickCommandStatus.Ok);
        }
    }

    /// <summary>
    /// Choose the receiving peer. AirFlick currently has one session at a time, so the target must be the
    /// connected peer; this exists so callers have a stable place for target selection.
    /// </summary>
    public AirFlickCommandResult SelectTarget(string peerName)
    {
        lock (_lock)
        {
            if (!_session.IsReady)
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.NotReady, "Session is not ready");
            }

            if (!string.Equals(peerName, _session.PeerName, StringComparison.OrdinalIgnoreCase))
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.TargetMismatch, $"{peerName} is not the connected peer");
            }

            _selectedTarget = peerName;

            // Only meaningful once a file is prepared; otherwise the backend stays in Waiting.
            _state.ReportTargetSelected(peerName);
            return new AirFlickCommandResult(AirFlickCommandStatus.Ok);
        }
    }

    /// <summary>Starts PIN authorization for the prepared file. The returned ticket carries the PIN + challenge.</summary>
    public AirFlickCommandResult RequestAuthorization()
    {
        lock (_lock)
        {
            if (_isSending)
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.Busy, "A transfer is in progress");
            }

            if (!_session.IsReady)
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.NotReady, "Session is not ready");
            }

            if (_preparedFilePath == null)
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.NoFilePrepared, "No file prepared");
            }

            if (_selectedTarget != null &&
                !string.Equals(_selectedTarget, _session.PeerName, StringComparison.OrdinalIgnoreCase))
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.TargetMismatch, "Selected target is no longer connected");
            }

            ClearAuthorizationLocked();
            var ticket = _pin.CreateAuthorization();
            _ticket = ticket;
            _state.ReportPinRequired();
            return new AirFlickCommandResult(AirFlickCommandStatus.Ok, Ticket: ticket);
        }
    }

    /// <summary>Verifies a typed PIN (programmatic/test path; a receiver would normally send a proof).</summary>
    public AirFlickCommandResult VerifyPin(string pin)
    {
        lock (_lock)
        {
            if (_ticket == null)
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.NotAuthorized, "No authorization requested");
            }

            return HandleVerificationLocked(_pin.VerifyPin(_ticket.AuthorizationId, pin));
        }
    }

    /// <summary>Verifies a receiver-computed HMAC proof (see PinAuthorizationService.ComputeProof).</summary>
    public AirFlickCommandResult SubmitPinProof(byte[] proof)
    {
        lock (_lock)
        {
            if (_ticket == null)
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.NotAuthorized, "No authorization requested");
            }

            return HandleVerificationLocked(_pin.VerifyProof(_ticket.AuthorizationId, proof));
        }
    }

    /// <summary>
    /// Full authorization round trip over the existing Ready session: creates the ticket, publishes the
    /// challenge to the peer through <see cref="IAirFlickAuthorizationChannel"/>, waits for the peer's
    /// proof and verifies it. The returned ticket carries the PIN that the local user reads out; the PIN
    /// itself never leaves this process. Requires an authorization channel to have been supplied.
    /// </summary>
    public async Task<AirFlickCommandResult> RequestRemoteAuthorizationAsync(CancellationToken cancellationToken = default)
    {
        var channel = _authorizationChannel;
        if (channel == null)
        {
            return new AirFlickCommandResult(
                AirFlickCommandStatus.InvalidState,
                "No authorization channel is configured");
        }

        PinAuthorizationTicket ticket;
        lock (_lock)
        {
            if (_preparedFilePath == null)
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.NoFilePrepared, "No file prepared");
            }

            // Reuse pending ticket if still unexpired and valid
            if (_ticket != null && DateTimeOffset.UtcNow < _ticket.ExpiresAtUtc)
            {
                ticket = _ticket;
            }
            else
            {
                var request = RequestAuthorization();
                if (!request.Success || request.Ticket is not { } t)
                {
                    return request;
                }
                ticket = t;
            }
        }

        string? fileName;
        lock (_lock)
        {
            fileName = _preparedFilePath == null ? null : Path.GetFileName(_preparedFilePath);
        }

        long sizeBytes = -1;
        try
        {
            var path = PreparedFilePath;
            if (path != null && File.Exists(path))
            {
                sizeBytes = new FileInfo(path).Length;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var challenge = new AirFlickAuthorizationChallenge(
            ticket.AuthorizationId,
            ticket.Nonce,
            fileName ?? string.Empty,
            sizeBytes,
            ticket.ExpiresAtUtc);

        AuthorizationRequested?.Invoke(this, ticket);

        AirFlickAuthorizationResponse response;
        try
        {
            response = await channel.RequestProofAsync(challenge, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Cancel();
            return new AirFlickCommandResult(AirFlickCommandStatus.Cancelled, "Authorization cancelled");
        }
        catch (Exception ex)
        {
            response = new AirFlickAuthorizationResponse(null, ex.Message);
        }

        if (response.Proof == null)
        {
            lock (_lock)
            {
                ClearAuthorizationLocked();
            }

            _state.ReportPinFailed(response.Error ?? "No authorization response");
            return new AirFlickCommandResult(
                AirFlickCommandStatus.AuthorizationFailed,
                response.Error ?? "Peer did not authorize the transfer");
        }

        var verification = SubmitPinProof(response.Proof);
        await SafePublishResultAsync(channel, ticket.AuthorizationId, verification).ConfigureAwait(false);
        return verification.Success
            ? new AirFlickCommandResult(AirFlickCommandStatus.Ok, Ticket: ticket)
            : verification;
    }

    /// <summary>Sends the prepared file immediately over the existing session. Requires a verified PIN.</summary>
    public async Task<AirFlickCommandResult> SendPreparedFileAsync()
    {
        string path;
        CancellationToken token;

        lock (_lock)
        {
            if (_isSending)
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.Busy, "A transfer is in progress");
            }

            if (!_session.IsReady)
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.NotReady, "Session is not ready");
            }

            if (_preparedFilePath == null)
            {
                return new AirFlickCommandResult(AirFlickCommandStatus.NoFilePrepared, "No file prepared");
            }

            // Authorization is single-use: TryConsume succeeds exactly once for a verified, unexpired ticket.
            if (_ticket == null || !_pin.TryConsume(_ticket.AuthorizationId))
            {
                if (_ticket != null)
                {
                    ClearAuthorizationLocked();
                    _state.ReportPinFailed("Authorization missing, expired or already used");
                }

                return new AirFlickCommandResult(AirFlickCommandStatus.NotAuthorized, "Transfer is not authorized");
            }

            _ticket = null;
            _isSending = true;
            _sendCts = new CancellationTokenSource();
            token = _sendCts.Token;
            path = _preparedFilePath;
        }

        var fileName = Path.GetFileName(path);
        _state.ReportTransferStarted(fileName);

        AirFlickTransferOutcome outcome;
        try
        {
            outcome = await _transfer.SendFileAsync(path, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            outcome = new AirFlickTransferOutcome(false, "Cancelled", fileName, 0);
        }
        catch (Exception ex)
        {
            outcome = new AirFlickTransferOutcome(false, ex.Message, fileName, 0);
        }
        finally
        {
            lock (_lock)
            {
                _isSending = false;
                _sendCts?.Dispose();
                _sendCts = null;
                _preparedFilePath = null;
                _isPendingShare = false;
            }
        }

        if (outcome.Success)
        {
            // Back to Waiting; the session was never touched, so the next file needs no reconnect.
            _state.ReportTransferComplete(fileName);
            return new AirFlickCommandResult(AirFlickCommandStatus.Ok);
        }

        var cancelled = token.IsCancellationRequested;
        _state.ReportTransferFailed(outcome.Error ?? "Transfer failed");
        if (_session.IsReady)
        {
            // A failed/cancelled transfer must not lock the backend while the connection is healthy.
            _state.Recover(outcome.Error);
        }
        else
        {
            // The connection itself is gone: reflect the real session phase instead of a stuck Failed.
            _state.ReportSessionPhase(_session.Phase, _session.PeerName);
        }

        return new AirFlickCommandResult(
            cancelled ? AirFlickCommandStatus.Cancelled : AirFlickCommandStatus.TransferFailed,
            outcome.Error);
    }

    /// <summary>Cancels the running transfer, or drops the prepared file and any pending authorization.</summary>
    public AirFlickCommandResult Cancel()
    {
        lock (_lock)
        {
            if (_isSending)
            {
                _sendCts?.Cancel();
                return new AirFlickCommandResult(AirFlickCommandStatus.Ok, "Cancelling transfer");
            }

            ClearAuthorizationLocked();
            _preparedFilePath = null;
            _selectedTarget = null;
            _isPendingShare = false;
            if (_state.Current == AirFlickState.Failed)
            {
                _state.Recover("user_cancelled");
            }
            _state.ReportFileReleased();
            return new AirFlickCommandResult(AirFlickCommandStatus.Ok);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ClearAuthorizationLocked();
            _sendCts?.Cancel();
        }

        _session.PhaseChanged -= OnPhaseChanged;
        _session.DeviceDiscovered -= OnDeviceDiscovered;
        _state.StateChanged -= OnStateMachineChanged;
    }

    // ---- internals ------------------------------------------------------------------------------------

    private AirFlickCommandResult HandleVerificationLocked(PinAuthorizationResult result)
    {
        switch (result.Status)
        {
            case PinAuthorizationStatus.Authorized:
                _state.ReportPinVerified();
                return new AirFlickCommandResult(AirFlickCommandStatus.Ok);

            case PinAuthorizationStatus.WrongPin:
                // Still PinRequired; the caller may retry until attempts run out.
                return new AirFlickCommandResult(
                    AirFlickCommandStatus.AuthorizationFailed,
                    $"Wrong PIN, {result.AttemptsRemaining} attempt(s) remaining");

            default:
                // TooManyAttempts / Expired / Cancelled / AlreadyUsed / Unknown: the ticket is dead.
                ClearAuthorizationLocked();
                _state.ReportPinFailed(result.Status.ToString());
                return new AirFlickCommandResult(AirFlickCommandStatus.AuthorizationFailed, result.Status.ToString());
        }
    }

    private static async Task SafePublishResultAsync(
        IAirFlickAuthorizationChannel channel,
        string authorizationId,
        AirFlickCommandResult verification)
    {
        try
        {
            await channel
                .PublishResultAsync(authorizationId, verification.Success, verification.Message)
                .ConfigureAwait(false);
        }
        catch
        {
            // The verdict is advisory for the peer's UI; failing to deliver it must not fail the flow.
        }
    }

    private void ClearAuthorizationLocked()
    {
        _isPendingShare = false;
        if (_ticket != null)
        {
            _pin.Cancel(_ticket.AuthorizationId);
            _ticket = null;
        }
    }

    private void OnPhaseChanged(object? sender, AirFlickSessionChangedEventArgs e)
    {
        lock (_lock)
        {
            if (e.Phase != SessionPhase.Ready && !_isSending)
            {
                // Only clear if this was a session-bound file (not an offline pending share waiting for connection)
                if (!_isPendingShare)
                {
                    ClearAuthorizationLocked();
                    _preparedFilePath = null;
                    _selectedTarget = null;
                }
            }
        }

        _state.ReportSessionPhase(e.Phase, e.PeerName);
    }

    private void OnDeviceDiscovered(object? sender, string deviceName)
    {
        _state.ReportDeviceFound(deviceName);
    }

    private void OnStateMachineChanged(object? sender, AirFlickStateChangedEventArgs e)
    {
        StateChanged?.Invoke(this, e);
    }
}
