using System;

namespace AirFlick.Services;

/// <summary>
/// Backend/application state for the AirFlick workflow. This is deliberately separate from
/// <see cref="SessionStateMachine"/> and <see cref="SessionPhase"/>: it never drives the WDCable
/// session, it only observes it (via <see cref="ReportSessionPhase"/>) and layers the transfer
/// workflow (file prepared, PIN, transferring) on top. It has no gesture, camera or UI states.
///
/// A single shared instance is owned by <see cref="AirFlickCommandService"/> so the state does
/// not depend on any page being open. All members are thread-safe; StateChanged is raised outside
/// the internal lock.
/// </summary>
public enum AirFlickState
{
    Starting,
    Discovering,
    DeviceFound,
    Connecting,
    SessionReady,
    Waiting,
    FileGrabbed,   // a file is prepared for sending ("FileReady")
    TargetSelected,
    PinRequired,
    PinVerified,
    Transferring,
    TransferComplete,
    Failed
}

public sealed class AirFlickStateChangedEventArgs : EventArgs
{
    public AirFlickStateChangedEventArgs(AirFlickState state, string? detail = null)
    {
        State = state;
        Detail = detail;
    }

    public AirFlickState State { get; }

    public string? Detail { get; }
}

public sealed class AirFlickStateMachine
{
    private readonly object _lock = new();
    private AirFlickState _current = AirFlickState.Starting;

    public AirFlickState Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public event EventHandler<AirFlickStateChangedEventArgs>? StateChanged;

    /// <summary>True while a file/PIN/transfer workflow is in progress.</summary>
    public bool IsTransferFlowActive
    {
        get
        {
            var state = Current;
            return state is AirFlickState.FileGrabbed
                or AirFlickState.TargetSelected
                or AirFlickState.PinRequired
                or AirFlickState.PinVerified
                or AirFlickState.Transferring;
        }
    }

    public void ReportStarting()
    {
        Apply(_ => AirFlickState.Starting, null);
    }

    public void ReportDiscovering()
    {
        // Never override a further-along state just because scanning is still running.
        Apply(c => c is AirFlickState.Starting or AirFlickState.Discovering or AirFlickState.Failed
            ? AirFlickState.Discovering
            : null, null);
    }

    public void ReportDeviceFound(string? deviceName)
    {
        Apply(c => c is AirFlickState.Starting or AirFlickState.Discovering
            ? AirFlickState.DeviceFound
            : null, deviceName);
    }

    public void ReportDeviceConnecting(string? deviceName)
    {
        Apply(_ => AirFlickState.Connecting, deviceName);
    }

    public void ReportSessionPhase(SessionPhase phase, string? detail = null)
    {
        switch (phase)
        {
            case SessionPhase.Ready:
                // A Degraded->Ready flip must not wipe an in-progress file/PIN/transfer.
                if (!IsTransferFlowActive)
                {
                    Apply(_ => AirFlickState.SessionReady, detail);
                    Apply(_ => AirFlickState.Waiting, detail);
                }
                break;
            case SessionPhase.WifiDirectConnected:
            case SessionPhase.ConnectingTransport:
            case SessionPhase.Handshaking:
                // Do not overwrite an active file/PIN/transfer workflow: the connection
                // handshake is infrastructure; the pending-share state must be preserved.
                if (!IsTransferFlowActive)
                {
                    Apply(_ => AirFlickState.Connecting, detail);
                }
                break;
            case SessionPhase.Failed:
                Apply(_ => AirFlickState.Failed, detail);
                break;
            case SessionPhase.Disconnected:
            case SessionPhase.Disconnecting:
            case SessionPhase.Degraded:
                Apply(_ => AirFlickState.Discovering, detail);
                break;
        }
    }

    /// <summary>A file was prepared. Only accepted while the session is Ready and idle (Waiting).</summary>
    public bool ReportFileGrabbed(string fileName)
    {
        return Apply(c => c == AirFlickState.Waiting ? AirFlickState.FileGrabbed : null, fileName);
    }

    /// <summary>
    /// A pending share was prepared offline (before or during discovery, or recovering from a previous failure).
    /// Transitions to FileGrabbed.
    /// </summary>
    public bool ReportPendingShare(string fileName)
    {
        return Apply(c => c is AirFlickState.Starting
            or AirFlickState.Discovering
            or AirFlickState.DeviceFound
            or AirFlickState.Waiting
            or AirFlickState.FileGrabbed
            or AirFlickState.Failed
            ? AirFlickState.FileGrabbed
            : null, fileName);
    }

    /// <summary>The receiving peer was chosen for the prepared file. Ignored when no file is prepared.</summary>
    public bool ReportTargetSelected(string? peerName)
    {
        return Apply(c => c is AirFlickState.FileGrabbed or AirFlickState.TargetSelected
            ? AirFlickState.TargetSelected
            : null, peerName);
    }

    /// <summary>The prepared file was dropped/cancelled before it was sent, or failure was cleared.</summary>
    public bool ReportFileReleased()
    {
        return Apply(c => c is AirFlickState.FileGrabbed
                or AirFlickState.TargetSelected
                or AirFlickState.PinRequired
                or AirFlickState.PinVerified
                or AirFlickState.Failed
            ? AirFlickState.Waiting
            : null, null);
    }

    public bool ReportPinRequired()
    {
        return Apply(c => c is AirFlickState.FileGrabbed or AirFlickState.TargetSelected or AirFlickState.PinRequired
            ? AirFlickState.PinRequired
            : null, null);
    }

    public bool ReportPinVerified()
    {
        return Apply(c => c == AirFlickState.PinRequired ? AirFlickState.PinVerified : null, null);
    }

    /// <summary>Authorization failed/expired/cancelled: the file stays prepared, a new PIN is needed.</summary>
    public bool ReportPinFailed(string? reason)
    {
        return Apply(c => c is AirFlickState.PinRequired or AirFlickState.PinVerified
            ? AirFlickState.FileGrabbed
            : null, reason);
    }

    public void ReportTransferStarted(string fileName)
    {
        Apply(_ => AirFlickState.Transferring, fileName);
    }

    public void ReportTransferComplete(string fileName)
    {
        Apply(_ => AirFlickState.TransferComplete, fileName);
        Apply(_ => AirFlickState.Waiting, null);
    }

    public void ReportTransferFailed(string reason)
    {
        Apply(_ => AirFlickState.Failed, reason);
    }

    /// <summary>
    /// Leaves Failed after a transfer/authorization failure when the network session is still healthy.
    /// Connection failures are recovered by the next session phase report instead.
    /// </summary>
    public bool Recover(string? detail = null)
    {
        return Apply(c => c == AirFlickState.Failed ? AirFlickState.Waiting : null, detail);
    }

    private bool Apply(Func<AirFlickState, AirFlickState?> transition, string? detail)
    {
        AirFlickState next;
        lock (_lock)
        {
            var result = transition(_current);
            if (result is not AirFlickState target)
            {
                return false;
            }

            next = target;
            _current = next;
        }

        StateChanged?.Invoke(this, new AirFlickStateChangedEventArgs(next, detail));
        return true;
    }
}
