using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AirFlick.Services;

/// <summary>
/// Standard implementation of <see cref="IAirFlickBackendClient"/> that wires into
/// ServiceManager, AirFlickBackend, SessionManager, and FileTransferService.
/// </summary>
public sealed class AirFlickBackendClient : IAirFlickBackendClient
{
    private readonly List<string> _discoveredDevices = [];
    private readonly object _lock = new();
    private string? _activeAuthorizationId;
    private AirFlickPendingShareResult? _currentPendingShare;

    public AirFlickState State => AirFlickBackend.Command?.State ?? AirFlickState.Starting;
    public SessionPhase SessionPhase => ServiceManager.SessionManager?.CurrentPhase ?? SessionPhase.Disconnected;
    public bool IsConnected => ServiceManager.SessionManager?.IsReady ?? false;
    public string? PeerName => ServiceManager.SessionManager?.PeerName;
    public AirFlickPendingShareResult? CurrentPendingShare
    {
        get
        {
            lock (_lock)
            {
                return _currentPendingShare;
            }
        }
    }

    public IReadOnlyList<string> DiscoveredDevices
    {
        get
        {
            lock (_lock)
            {
                return _discoveredDevices.ToArray();
            }
        }
    }

    public event EventHandler<AirFlickStateChangedEventArgs>? StateChanged;
    public event EventHandler<AirFlickSessionChangedEventArgs>? SessionChanged;
    public event EventHandler<string>? DeviceDiscovered;
    public event EventHandler<AirFlickTransferProgressEventArgs>? TransferProgress;
    public event EventHandler<AirFlickTransferResultEventArgs>? TransferCompleted;
    public event EventHandler<AirFlickPinChallengeEventArgs>? PinAuthorizationRequested;
    public event EventHandler<AirFlickPendingShareEventArgs>? PendingShareCreated;

    public async Task InitializeAsync()
    {
        await ServiceManager.InitializeAsync().ConfigureAwait(false);

        if (AirFlickBackend.Command != null)
        {
            AirFlickBackend.Command.StateChanged += (s, e) => StateChanged?.Invoke(this, e);
        }

        if (ServiceManager.WiFiDirectService != null)
        {
            ServiceManager.WiFiDirectService.DeviceDiscovered += (s, device) => OnDeviceDiscovered(device.Name);
        }

        if (ServiceManager.SessionManager != null)
        {
            ServiceManager.SessionManager.StateChanged += (s, e) =>
            {
                SessionChanged?.Invoke(this, new AirFlickSessionChangedEventArgs(e.Phase, e.PeerName));
            };
        }

        if (AirFlickBackend.Authorization != null)
        {
            AirFlickBackend.Authorization.AuthorizationRequested += OnAuthorizationRequested;
        }

        if (ServiceManager.FileTransferService != null)
        {
            ServiceManager.FileTransferService.TransferProgress += (s, e) =>
            {
                TransferProgress?.Invoke(this, new AirFlickTransferProgressEventArgs(
                    e.FileName,
                    e.BytesTransferred,
                    e.TotalBytes,
                    (int)Math.Round(e.ProgressPercentage)));
            };

            ServiceManager.FileTransferService.FileReceived += (s, e) =>
            {
                TransferCompleted?.Invoke(this, new AirFlickTransferResultEventArgs(true, e.FileName, null, e.FilePath));
            };

            ServiceManager.FileTransferService.FileSent += (s, e) =>
            {
                TransferCompleted?.Invoke(this, new AirFlickTransferResultEventArgs(true, e.FileName, null, e.FilePath));
            };

            ServiceManager.FileTransferService.TransferFailed += (s, e) =>
            {
                TransferCompleted?.Invoke(this, new AirFlickTransferResultEventArgs(false, e.FileName, e.ErrorMessage, null));
            };
        }
    }

    public async Task ShutdownAsync()
    {
        await ServiceManager.ShutdownAsync().ConfigureAwait(false);
    }

    public async Task StartDiscoveryAsync()
    {
        if (AirFlickBackend.Command != null)
        {
            await AirFlickBackend.Command.StartDiscoveryAsync().ConfigureAwait(false);
        }
    }

    public async Task StopDiscoveryAsync()
    {
        if (ServiceManager.WiFiDirectService != null)
        {
            await ServiceManager.WiFiDirectService.StopScanAsync("client_requested").ConfigureAwait(false);
        }
    }

    public async Task<bool> ConnectAsync(string deviceName)
    {
        if (AirFlickBackend.Command == null) return false;
        var result = await AirFlickBackend.Command.ConnectAsync(deviceName).ConfigureAwait(false);
        return result.Success;
    }

    public async Task DisconnectAsync()
    {
        if (ServiceManager.WiFiDirectService != null)
        {
            await ServiceManager.WiFiDirectService.DisconnectAsync("user_requested").ConfigureAwait(false);
        }
    }

    public void CancelShare()
    {
        lock (_lock)
        {
            _currentPendingShare = null;
        }

        if (AirFlickBackend.Command != null)
        {
            AirFlickBackend.Command.Cancel();
        }
    }

    public async Task<AirFlickTransferOutcome> SendFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (AirFlickBackend.Command == null)
        {
            return new AirFlickTransferOutcome(false, "Backend not initialized", Path.GetFileName(filePath), 0);
        }

        var prep = AirFlickBackend.Command.PrepareFile(filePath);
        if (!prep.Success)
        {
            return new AirFlickTransferOutcome(false, prep.Message ?? "Failed to prepare file", Path.GetFileName(filePath), 0);
        }

        var auth = await AirFlickBackend.Command.RequestRemoteAuthorizationAsync(cancellationToken).ConfigureAwait(false);
        if (!auth.Success)
        {
            return new AirFlickTransferOutcome(false, auth.Message ?? "Authorization failed", Path.GetFileName(filePath), 0);
        }

        var transferResult = await AirFlickBackend.Command.SendPreparedFileAsync().ConfigureAwait(false);
        return new AirFlickTransferOutcome(transferResult.Success, transferResult.Message, Path.GetFileName(filePath), 0);
    }

    public async Task<bool> SubmitPinAsync(string pin)
    {
        if (AirFlickBackend.Authorization == null || string.IsNullOrEmpty(_activeAuthorizationId))
        {
            return false;
        }

        return await AirFlickBackend.Authorization.SubmitPinAsync(_activeAuthorizationId, pin).ConfigureAwait(false);
    }

    public async Task<AirFlickPendingShareResult> InitiatePendingShareAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (AirFlickBackend.Command == null)
        {
            return new AirFlickPendingShareResult(false, "Backend command service not initialized", null, null, Path.GetFileName(filePath), 0);
        }

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new AirFlickPendingShareResult(false, "File does not exist: " + filePath, null, null, Path.GetFileName(filePath), 0);
        }

        var result = AirFlickBackend.Command.InitiatePendingShare(filePath);
        if (!result.Success || result.Ticket is not { } ticket)
        {
            return new AirFlickPendingShareResult(false, result.Message ?? "Failed to initiate share", null, null, Path.GetFileName(filePath), 0);
        }

        long sizeBytes = 0;
        try
        {
            sizeBytes = new FileInfo(filePath).Length;
        }
        catch
        {
        }

        var shareResult = new AirFlickPendingShareResult(
            true,
            null,
            ticket.Pin,
            ticket.AuthorizationId,
            Path.GetFileName(filePath),
            sizeBytes);

        lock (_lock)
        {
            _currentPendingShare = shareResult;
        }

        PendingShareCreated?.Invoke(this, new AirFlickPendingShareEventArgs(
            ticket.Pin,
            ticket.AuthorizationId,
            Path.GetFileName(filePath),
            sizeBytes));

        // Start advertising on Wi-Fi Direct so nearby receivers can discover this sender
        if (ServiceManager.WiFiDirectService != null)
        {
            await ServiceManager.WiFiDirectService.EnsureDiscoverableAsync("pending_share").ConfigureAwait(false);
        }

        // Start background watcher that waits for a receiver to connect and completes the transfer
        _ = Task.Run(() => AwaitReceiverAndTransferAsync(cancellationToken), CancellationToken.None);

        return shareResult;
    }

    public async Task<AirFlickTransferOutcome> ReceiveFileFromSenderAsync(string senderDeviceName, string pin, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(senderDeviceName))
        {
            return new AirFlickTransferOutcome(false, "Sender device name is required", "", 0);
        }

        if (!PinAuthorizationService.IsValidPinFormat(pin))
        {
            return new AirFlickTransferOutcome(false, "PIN must be exactly 3 digits", "", 0);
        }

        var challengeTcs = new TaskCompletionSource<AirFlickAuthorizationChallenge>(TaskCreationOptions.RunContinuationsAsynchronously);
        var authResultTcs = new TaskCompletionSource<AirFlickAuthorizationResultMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transferTcs = new TaskCompletionSource<AirFlickTransferResultEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<AirFlickAuthorizationRequestedEventArgs>? challengeHandler = null;
        EventHandler<AirFlickAuthorizationResultEventArgs>? resultHandler = null;
        EventHandler<AirFlickTransferResultEventArgs>? transferHandler = null;

        challengeHandler = (s, e) => challengeTcs.TrySetResult(e.Challenge);
        if (AirFlickBackend.Authorization != null)
        {
            AirFlickBackend.Authorization.AuthorizationRequested += challengeHandler;
            resultHandler = (s, e) => authResultTcs.TrySetResult(e.Result);
            AirFlickBackend.Authorization.AuthorizationResult += resultHandler;
        }

        transferHandler = (s, e) => transferTcs.TrySetResult(e);
        TransferCompleted += transferHandler;

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(PinAuthorizationService.DefaultLifetime);
            var token = linkedCts.Token;

            // Connect to sender via Wi-Fi Direct if not already connected
            if (!IsConnected || !string.Equals(PeerName, senderDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                var connected = await ConnectAsync(senderDeviceName).ConfigureAwait(false);
                if (!connected)
                {
                    return new AirFlickTransferOutcome(false, $"Failed to connect to sender '{senderDeviceName}'", "", 0);
                }
            }

            // Await session readiness
            var readinessTimeout = DateTimeOffset.UtcNow.AddSeconds(30);
            while (!IsConnected && DateTimeOffset.UtcNow < readinessTimeout)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(100, token).ConfigureAwait(false);
            }

            if (!IsConnected)
            {
                return new AirFlickTransferOutcome(false, "Session readiness timed out", "", 0);
            }

            // Await authorization challenge from sender
            var challenge = await challengeTcs.Task.WaitAsync(TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
            if (challenge == null)
            {
                return new AirFlickTransferOutcome(false, "No authorization challenge received from sender", "", 0);
            }

            // Submit entered PIN proof for this challenge
            if (AirFlickBackend.Authorization == null)
            {
                return new AirFlickTransferOutcome(false, "Authorization service unavailable", challenge.FileName, 0);
            }

            var submitted = await AirFlickBackend.Authorization.SubmitPinAsync(challenge.AuthorizationId, pin, token).ConfigureAwait(false);
            if (!submitted)
            {
                return new AirFlickTransferOutcome(false, "Failed to submit PIN proof to sender", challenge.FileName, 0);
            }

            // Await authorization verdict from sender
            var authVerdict = await authResultTcs.Task.WaitAsync(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
            if (!authVerdict.Authorized)
            {
                return new AirFlickTransferOutcome(false, authVerdict.Message ?? "Authorization rejected: incorrect PIN", challenge.FileName, 0);
            }

            // Await file transfer completion
            var transferOutcome = await transferTcs.Task.WaitAsync(token).ConfigureAwait(false);
            return new AirFlickTransferOutcome(
                transferOutcome.Success,
                transferOutcome.ErrorMessage,
                transferOutcome.FileName,
                challenge.SizeBytes,
                transferOutcome.DestinationPath);
        }
        catch (TimeoutException)
        {
            return new AirFlickTransferOutcome(false, "Transfer workflow timed out", "", 0);
        }
        catch (OperationCanceledException)
        {
            return new AirFlickTransferOutcome(false, "Transfer workflow cancelled", "", 0);
        }
        catch (Exception ex)
        {
            return new AirFlickTransferOutcome(false, ex.Message, "", 0);
        }
        finally
        {
            if (AirFlickBackend.Authorization != null)
            {
                if (challengeHandler != null) AirFlickBackend.Authorization.AuthorizationRequested -= challengeHandler;
                if (resultHandler != null) AirFlickBackend.Authorization.AuthorizationResult -= resultHandler;
            }
            if (transferHandler != null)
            {
                TransferCompleted -= transferHandler;
            }
        }
    }

    private async Task AwaitReceiverAndTransferAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 1. Wait for incoming connection to reach SessionPhase.Ready
            while (!cancellationToken.IsCancellationRequested)
            {
                if (IsConnected && SessionPhase == SessionPhase.Ready)
                {
                    break;
                }

                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested || !IsConnected || AirFlickBackend.Command == null)
            {
                return;
            }

            // 2. Request remote authorization over the now-connected control channel
            var auth = await AirFlickBackend.Command.RequestRemoteAuthorizationAsync(cancellationToken).ConfigureAwait(false);
            if (!auth.Success)
            {
                return;
            }

            // 3. Authorization verified! Stream the prepared file over bulk transport
            await AirFlickBackend.Command.SendPreparedFileAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Pending share sender transfer error: {ex.Message}");
        }
        finally
        {
            lock (_lock)
            {
                _currentPendingShare = null;
            }
        }
    }

    private void OnDeviceDiscovered(string deviceName)
    {
        lock (_lock)
        {
            if (!_discoveredDevices.Contains(deviceName))
            {
                _discoveredDevices.Add(deviceName);
            }
        }
        DeviceDiscovered?.Invoke(this, deviceName);
    }

    private void OnAuthorizationRequested(object? sender, AirFlickAuthorizationRequestedEventArgs e)
    {
        _activeAuthorizationId = e.Challenge.AuthorizationId;
        PinAuthorizationRequested?.Invoke(this, new AirFlickPinChallengeEventArgs(
            e.Challenge.AuthorizationId,
            e.Challenge.FileName,
            e.Challenge.SizeBytes));
    }
}
