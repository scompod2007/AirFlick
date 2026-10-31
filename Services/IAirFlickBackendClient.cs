using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AirFlick.Services;

/// <summary>
/// High-level backend entry point for AirFlick clients and frontends.
/// Abstracts Wi-Fi Direct, Session Management, PIN Authorization, and File Transfer.
/// </summary>
public interface IAirFlickBackendClient
{
    AirFlickState State { get; }
    SessionPhase SessionPhase { get; }
    bool IsConnected { get; }
    string? PeerName { get; }
    IReadOnlyList<string> DiscoveredDevices { get; }
    AirFlickPendingShareResult? CurrentPendingShare { get; }

    event EventHandler<AirFlickStateChangedEventArgs>? StateChanged;
    event EventHandler<AirFlickSessionChangedEventArgs>? SessionChanged;
    event EventHandler<string>? DeviceDiscovered;
    event EventHandler<AirFlickTransferProgressEventArgs>? TransferProgress;
    event EventHandler<AirFlickTransferResultEventArgs>? TransferCompleted;
    event EventHandler<AirFlickPinChallengeEventArgs>? PinAuthorizationRequested;
    event EventHandler<AirFlickPendingShareEventArgs>? PendingShareCreated;

    Task InitializeAsync();
    Task ShutdownAsync();

    Task StartDiscoveryAsync();
    Task StopDiscoveryAsync();
    Task<bool> ConnectAsync(string deviceName);
    Task DisconnectAsync();
    void CancelShare();

    Task<AirFlickTransferOutcome> SendFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<bool> SubmitPinAsync(string pin);

    /// <summary>
    /// Offline-first sender initiation: validates file and creates a pending share with a fresh 3-digit PIN
    /// before any receiver connects. Starts Wi-Fi Direct discoverability.
    /// </summary>
    Task<AirFlickPendingShareResult> InitiatePendingShareAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// End-to-end receiver orchestration: connects to sender, waits for session readiness, submits PIN proof,
    /// verifies SHA-256 integrity, and saves to Downloads.
    /// </summary>
    Task<AirFlickTransferOutcome> ReceiveFileFromSenderAsync(string senderDeviceName, string pin, CancellationToken cancellationToken = default);
}

public sealed record AirFlickPendingShareResult(
    bool Success,
    string? ErrorMessage,
    string? Pin,
    string? AuthorizationId,
    string? FileName,
    long SizeBytes);

public sealed class AirFlickPendingShareEventArgs : EventArgs
{
    public AirFlickPendingShareEventArgs(string pin, string authorizationId, string fileName, long sizeBytes)
    {
        Pin = pin;
        AuthorizationId = authorizationId;
        FileName = fileName;
        SizeBytes = sizeBytes;
    }

    public string Pin { get; }
    public string AuthorizationId { get; }
    public string FileName { get; }
    public long SizeBytes { get; }
}

public sealed class AirFlickTransferProgressEventArgs : EventArgs
{
    public AirFlickTransferProgressEventArgs(string fileName, long bytesTransferred, long totalBytes, int percentage)
    {
        FileName = fileName;
        BytesTransferred = bytesTransferred;
        TotalBytes = totalBytes;
        Percentage = percentage;
    }

    public string FileName { get; }
    public long BytesTransferred { get; }
    public long TotalBytes { get; }
    public int Percentage { get; }
}

public sealed class AirFlickTransferResultEventArgs : EventArgs
{
    public AirFlickTransferResultEventArgs(bool success, string fileName, string? errorMessage, string? destinationPath = null)
    {
        Success = success;
        FileName = fileName;
        ErrorMessage = errorMessage;
        DestinationPath = destinationPath;
    }

    public bool Success { get; }
    public string FileName { get; }
    public string? ErrorMessage { get; }
    public string? DestinationPath { get; }
}

public sealed class AirFlickPinChallengeEventArgs : EventArgs
{
    public AirFlickPinChallengeEventArgs(string authorizationId, string fileName, long sizeBytes)
    {
        AuthorizationId = authorizationId;
        FileName = fileName;
        SizeBytes = sizeBytes;
    }

    public string AuthorizationId { get; }
    public string FileName { get; }
    public long SizeBytes { get; }
}

