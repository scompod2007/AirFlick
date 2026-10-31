using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using AirFlick.Protocol;
using Windows.Storage;

namespace AirFlick.Services
{
    public class FileTransferService : IDisposable
    {
        private static FileTransferService? _instance;
        private static readonly object Lock = new();

        private readonly DispatcherQueue? _dispatcherQueue;
        private readonly ConcurrentDictionary<long, IncomingFileStream> _incomingFiles = new();
        private readonly ConcurrentDictionary<string, bool> _activeSendIds = new();
        private readonly ConcurrentDictionary<long, TaskCompletionSource<IReadOnlyDictionary<string, JsonElement>>> _pendingAcks = new();
        private static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(30);
        private const int ProgressIntervalMs = 100;
        private readonly Channel<ProtocolFrame> _bulkFrames;
        private readonly CancellationTokenSource _bulkFrameCancellationTokenSource = new();
        private SessionManager? _sessionManager;
        private string _downloadPath;
        private bool _isDisposed;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<FileTransferEventArgs>? FileReceived;
        public event EventHandler<FileTransferEventArgs>? FileSent;
        public event EventHandler<FileTransferProgressEventArgs>? TransferProgress;
        public event EventHandler<FileReceiveStartedEventArgs>? FileReceiveStarted;
        public event EventHandler<FileTransferFailedEventArgs>? TransferFailed;

        public static FileTransferService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (Lock)
                    {
                        _instance ??= new FileTransferService();
                    }
                }

                return _instance;
            }
        }

        public bool IsConnected => _sessionManager?.IsReady ?? false;

        public string DownloadPath => _downloadPath;

        private FileTransferService()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _bulkFrames = Channel.CreateUnbounded<ProtocolFrame>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            _ = Task.Run(() => ProcessBulkFramesAsync(_bulkFrameCancellationTokenSource.Token));
            _downloadPath = DefaultDownloadPath();
            try
            {
                _downloadPath = ServiceManager.DataManager?.GetDownloadPath() ?? _downloadPath;
            }
            catch
            {
            }

            _sessionManager = ServiceManager.SessionManager;
            if (_sessionManager != null)
            {
                SubscribeToSession(_sessionManager);
            }

            OnStatusChanged("FileTransferService initialized");
        }

        public void SetDownloadPath(string path)
        {
            try
            {
                _downloadPath = string.IsNullOrWhiteSpace(path) ? DefaultDownloadPath() : path;
                ServiceManager.DataManager?.SetDownloadPath(_downloadPath);
                OnStatusChanged($"Download path updated: {_downloadPath}");
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to update download path: {ex.Message}");
            }
        }

        public async Task SendFileAsync(StorageFile file)
        {
            if (file == null)
            {
                OnErrorOccurred("Cannot send null file");
                return;
            }

            try
            {
                var properties = await file.GetBasicPropertiesAsync();
                await using var input = await file.OpenStreamForReadAsync();
                await SendStreamAsync(input, file.Name, (long)properties.Size, file.Path).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to send file: {ex.Message}");
            }
        }

        /// <summary>
        /// Path-based entry point used by the AirFlick backend command layer (no StorageFile needed).
        /// Streams the file, and only reports success after the receiver has verified and finalized it.
        /// Cancellation is honoured between chunks only, so a frame is never half-written.
        /// </summary>
        public async Task<FileTransferResult> SendFilePathAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var displayName = Path.GetFileName(filePath ?? string.Empty);
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    const string missing = "File does not exist";
                    OnErrorOccurred(missing);
                    return new FileTransferResult(false, displayName, 0, missing);
                }

                var info = new FileInfo(filePath);
                await using var input = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                return await SendStreamAsync(input, info.Name, info.Length, filePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to send file: {ex.Message}");
                return new FileTransferResult(false, displayName, 0, ex.Message);
            }
        }

        private async Task<FileTransferResult> SendStreamAsync(
            Stream input,
            string fileName,
            long sizeBytes,
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            var safeName = BulkProtocol.SafeFileName(fileName);
            if (_sessionManager?.IsReady != true || string.IsNullOrWhiteSpace(_sessionManager.CurrentSessionId))
            {
                const string notReady = "WDCable session is not ready for file transfer";
                OnErrorOccurred(notReady);
                return new FileTransferResult(false, safeName, 0, notReady);
            }

            var transferId = Guid.NewGuid().ToString();
            var streamId = BulkProtocol.NextStreamId();
            var startedAt = DateTimeOffset.UtcNow;
            long bytesSent = 0;
            _activeSendIds[transferId] = true;

            // Register for the receiver's verdict BEFORE sending anything.
            var ackWaiter = new TaskCompletionSource<IReadOnlyDictionary<string, JsonElement>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingAcks[streamId] = ackWaiter;

            try
            {
                TransferDiagnostics.Log("FileSendStart", "Sender", transferId: transferId, message: $"Sending {safeName} ({sizeBytes} bytes)");
                OnStatusChanged($"Sending file: {safeName} ({sizeBytes} bytes)");
                OnTransferProgress(safeName, 0, sizeBytes, transferId, isSender: true);

                await _sessionManager.SendBulkFrameAsync(
                    ProtocolFrameType.BulkStart,
                    BulkProtocol.BuildMetadata(new Dictionary<string, object?>
                    {
                        ["kind"] = BulkProtocol.KindFile,
                        ["transferId"] = transferId,
                        ["sessionId"] = _sessionManager.CurrentSessionId,
                        ["fileName"] = safeName,
                        ["sizeBytes"] = sizeBytes,
                        ["timestamp"] = startedAt.ToUnixTimeMilliseconds()
                    }),
                    streamId: streamId,
                    correlationId: Guid.Parse(transferId)).ConfigureAwait(false);

                using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[BulkProtocol.ChunkSize];
                var correlationId = Guid.Parse(transferId);
                var progressClock = System.Diagnostics.Stopwatch.StartNew();

                while (true)
                {
                    // Cancel only between frames: cancelling inside a socket write would corrupt framing.
                    cancellationToken.ThrowIfCancellationRequested();

                    var read = await ReadFullAsync(input, buffer).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    sha256.AppendData(buffer, 0, read);

                    // The frame is fully encoded before WriteFrameAsync returns, so the read buffer
                    // can be passed as-is (and reused) whenever the chunk is full-size.
                    byte[] payload = buffer;
                    if (read != buffer.Length)
                    {
                        payload = new byte[read];
                        Buffer.BlockCopy(buffer, 0, payload, 0, read);
                    }

                    await _sessionManager.SendBulkFrameAsync(
                        ProtocolFrameType.BulkChunk,
                        BulkProtocol.BuildChunkMetadata(transferId, bytesSent),
                        payload,
                        streamId,
                        correlationId).ConfigureAwait(false);

                    bytesSent += read;
                    if (progressClock.ElapsedMilliseconds >= ProgressIntervalMs)
                    {
                        progressClock.Restart();
                        OnTransferProgress(safeName, bytesSent, sizeBytes, transferId, isSender: true);
                        TransferDiagnostics.Log("FileSendProgress", "Sender", transferId: transferId, bytesTransferred: bytesSent, totalBytes: sizeBytes);
                    }
                }

                var hash = BulkProtocol.Sha256Hex(sha256.GetHashAndReset());

                await _sessionManager.SendBulkFrameAsync(
                    ProtocolFrameType.BulkComplete,
                    BulkProtocol.BuildMetadata(new Dictionary<string, object?>
                    {
                        ["kind"] = BulkProtocol.KindFile,
                        ["transferId"] = transferId,
                        ["sizeBytes"] = bytesSent,
                        ["sha256"] = hash,
                        ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }),
                    streamId: streamId,
                    correlationId: correlationId).ConfigureAwait(false);

                OnTransferProgress(safeName, bytesSent, sizeBytes, transferId, isSender: true);
                OnStatusChanged($"Waiting for receiver to verify: {safeName}");
                TransferDiagnostics.Log("FileSendAwaitingAck", "Sender", transferId: transferId, message: $"Sent {bytesSent} bytes, hash: {hash}, waiting for receiver verification");

                // Success is decided by the receiver: it has written, hashed, verified and finalized the file.
                var ack = await ackWaiter.Task.WaitAsync(AckTimeout).ConfigureAwait(false);
                var ackedHash = BulkProtocol.GetString(ack, "sha256");
                var ackedSize = BulkProtocol.GetInt64(ack, "sizeBytes", -1);
                if (!string.IsNullOrEmpty(ackedHash) && !ackedHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Receiver acknowledged a different checksum than was sent");
                }

                if (ackedSize >= 0 && ackedSize != bytesSent)
                {
                    throw new IOException("Receiver acknowledged a different size than was sent");
                }

                TransferDiagnostics.Log("FileSendComplete", "Sender", transferId: transferId, message: $"Verified and acknowledged by receiver: {safeName}");
                OnStatusChanged($"File sent and verified: {safeName}");
                OnFileSent(safeName, sourcePath, bytesSent, transferId);
                return new FileTransferResult(true, safeName, bytesSent, null);
            }
            catch (Exception ex)
            {
                var message = ex is TimeoutException
                    ? "Receiver did not confirm the file in time"
                    : ex.Message;
                TransferDiagnostics.Log("FileSendError", "Sender", transferId: transferId, message: message, ex: ex);
                await TryCancelBulkStreamAsync(streamId, transferId, "file_send_failed", message).ConfigureAwait(false);
                OnTransferFailed(safeName, sourcePath, bytesSent, transferId, isSender: true, errorMessage: message);
                OnErrorOccurred($"Failed to send file: {message}");
                return new FileTransferResult(false, safeName, bytesSent, message);
            }
            finally
            {
                _pendingAcks.TryRemove(streamId, out _);
                _activeSendIds.TryRemove(transferId, out _);
            }
        }

        private static async Task<int> ReadFullAsync(Stream input, byte[] buffer)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await input.ReadAsync(buffer.AsMemory(total, buffer.Length - total)).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }

        private void FailPendingAcks(string message)
        {
            foreach (var pair in _pendingAcks)
            {
                if (_pendingAcks.TryRemove(pair.Key, out var waiter))
                {
                    waiter.TrySetException(new IOException(message));
                }
            }
        }

        public void StartListening()
        {
            OnStatusChanged(IsConnected ? "File transfer bulk channel is ready" : "Waiting for WDCable session readiness");
        }

        public void StopListening()
        {
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            FailPendingAcks("FileTransferService disposed");
            _bulkFrameCancellationTokenSource.Cancel();
            _bulkFrames.Writer.TryComplete();
            if (_sessionManager != null)
            {
                UnsubscribeFromSession(_sessionManager);
                _sessionManager = null;
            }

            CleanupIncoming(deletePartialFiles: true);
            _bulkFrameCancellationTokenSource.Dispose();
            OnStatusChanged("FileTransferService disposing");
        }

        public static void ResetInstance()
        {
            lock (Lock)
            {
                _instance?.Dispose();
                _instance = null;
            }
        }

        private void SubscribeToSession(SessionManager sessionManager)
        {
            sessionManager.SessionReady += OnSessionReady;
            sessionManager.StateChanged += OnSessionStateChanged;
            sessionManager.SessionFailed += OnSessionFailed;
            sessionManager.BulkFrameReceived += OnBulkFrameReceived;
            sessionManager.ControlFrameReceived += OnControlFrameReceived;
        }

        private void UnsubscribeFromSession(SessionManager sessionManager)
        {
            sessionManager.SessionReady -= OnSessionReady;
            sessionManager.StateChanged -= OnSessionStateChanged;
            sessionManager.SessionFailed -= OnSessionFailed;
            sessionManager.BulkFrameReceived -= OnBulkFrameReceived;
            sessionManager.ControlFrameReceived -= OnControlFrameReceived;
        }

        private void OnSessionReady(object? sender, SessionReadyEventArgs e)
        {
            OnStatusChanged("WDCable session ready - File transfer is available");
        }

        private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs e)
        {
            if (e.Phase is SessionPhase.Disconnected or SessionPhase.Failed or SessionPhase.Disconnecting)
            {
                FailPendingAcks("Session ended before the receiver confirmed the file");
                CleanupIncoming(deletePartialFiles: true);
            }
        }

        private void OnSessionFailed(object? sender, SessionFailedEventArgs e)
        {
            FailPendingAcks($"Session failed: {e.Message}");
            CleanupIncoming(deletePartialFiles: true);
            OnErrorOccurred($"WDCable session failed: {e.Message}");
        }

        private void OnControlFrameReceived(object? sender, ProtocolFrameReceivedEventArgs e)
        {
            if (e.Frame.Type is not (ProtocolFrameType.Error or ProtocolFrameType.Ack))
            {
                return;
            }

            try
            {
                var metadata = BulkProtocol.ParseMetadata(e.Frame.MetadataJson);

                if (e.Frame.Type == ProtocolFrameType.Ack)
                {
                    // Receiver's verified-and-finalized confirmation for a file we sent.
                    if (BulkProtocol.GetString(metadata, "ackFor") == "bulk.complete" &&
                        _pendingAcks.TryRemove(e.Frame.StreamId, out var ackWaiter))
                    {
                        ackWaiter.TrySetResult(metadata);
                    }

                    return;
                }

                var code = BulkProtocol.GetString(metadata, "code");
                if (code.StartsWith("file_", StringComparison.Ordinal) || code.StartsWith("bulk_", StringComparison.Ordinal))
                {
                    var message = BulkProtocol.GetString(metadata, "message", "Peer reported file transfer error");
                    if (_pendingAcks.TryRemove(e.Frame.StreamId, out var errorWaiter))
                    {
                        errorWaiter.TrySetException(new IOException($"Receiver rejected the file: {message}"));
                    }

                    OnErrorOccurred(message);
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Invalid peer file error metadata: {ex.Message}");
            }
        }

        private void OnBulkFrameReceived(object? sender, ProtocolFrameReceivedEventArgs e)
        {
            if (!_isDisposed)
            {
                _bulkFrames.Writer.TryWrite(e.Frame);
            }
        }

        private async Task ProcessBulkFramesAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var frame in _bulkFrames.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    await HandleBulkFrameAsync(frame).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task HandleBulkFrameAsync(ProtocolFrame frame)
        {
            try
            {
                var metadata = BulkProtocol.ParseMetadata(frame.MetadataJson);
                var kind = BulkProtocol.GetString(metadata, "kind");
                if (kind != BulkProtocol.KindFile && !_incomingFiles.ContainsKey(frame.StreamId))
                {
                    return;
                }

                switch (frame.Type)
                {
                    case ProtocolFrameType.BulkStart:
                        if (kind == BulkProtocol.KindFile)
                        {
                            await StartIncomingFileAsync(frame, metadata).ConfigureAwait(false);
                        }
                        break;
                    case ProtocolFrameType.BulkChunk:
                        await HandleIncomingFileChunkAsync(frame, metadata).ConfigureAwait(false);
                        break;
                    case ProtocolFrameType.BulkComplete:
                        await CompleteIncomingFileAsync(frame, metadata).ConfigureAwait(false);
                        break;
                    case ProtocolFrameType.BulkCancel:
                        CancelIncomingFile(frame, metadata);
                        break;
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"File transfer frame handling failed: {ex.Message}");
            }
        }

        private async Task StartIncomingFileAsync(ProtocolFrame frame, IReadOnlyDictionary<string, JsonElement> metadata)
        {
            var transferId = BulkProtocol.GetString(metadata, "transferId", Guid.NewGuid().ToString());
            var originalName = BulkProtocol.GetString(metadata, "fileName", "unknown_file");
            var expectedSize = BulkProtocol.GetInt64(metadata, "sizeBytes", -1);

            var startError = BulkProtocol.ValidateStart(
                BulkProtocol.GetString(metadata, "transferId"),
                expectedSize);
            if (startError != null)
            {
                OnTransferFailed(originalName, string.Empty, 0, transferId, isSender: false, errorMessage: startError);
                OnErrorOccurred($"Rejected incoming file: {startError}");
                await SendBulkErrorAsync(frame.StreamId, "file_receive_rejected", startError).ConfigureAwait(false);
                return;
            }

            if (_incomingFiles.TryRemove(frame.StreamId, out var existing))
            {
                existing.Dispose();
                TryDelete(existing.WorkingPath);
                OnTransferFailed(
                    existing.SafeFileName,
                    existing.TargetPath,
                    existing.BytesReceived,
                    existing.TransferId,
                    isSender: false,
                    errorMessage: "Duplicate file transfer start received for the same stream.");
            }

            string targetPath;
            string workingPath;
            string safeName;
            IncomingFileStream incoming;
            try
            {
                var targetDirectory = ResolveDownloadDirectory();
                targetPath = BulkProtocol.DuplicateSafePath(targetDirectory, originalName);
                workingPath = CreatePartialPath(targetPath);
                safeName = Path.GetFileName(targetPath);

                incoming = new IncomingFileStream(
                    transferId,
                    originalName,
                    safeName,
                    expectedSize,
                    targetPath,
                    workingPath,
                    File.Create(workingPath),
                    SHA256.Create());
            }
            catch (Exception ex)
            {
                // The destination could not be opened at all: tell the sender now instead of letting it
                // wait for the acknowledgement to time out.
                var reason = $"Receiver could not open the destination file: {ex.Message}";
                OnTransferFailed(originalName, string.Empty, 0, transferId, isSender: false, errorMessage: reason);
                OnErrorOccurred(reason);
                await SendBulkErrorAsync(frame.StreamId, "file_receive_rejected", reason).ConfigureAwait(false);
                return;
            }

            _incomingFiles[frame.StreamId] = incoming;
            OnStatusChanged($"Receiving file: {safeName} ({expectedSize} bytes)");
            OnFileReceiveStarted(safeName, expectedSize, transferId);
            OnTransferProgress(safeName, 0, expectedSize, transferId, isSender: false);
        }

        private async Task HandleIncomingFileChunkAsync(ProtocolFrame frame, IReadOnlyDictionary<string, JsonElement> metadata)
        {
            if (!_incomingFiles.TryGetValue(frame.StreamId, out var incoming))
            {
                // A file chunk for a transfer that was never started (or was already aborted) is rejected
                // rather than silently dropped, so the sender fails fast and nothing is ever written.
                if (BulkProtocol.GetString(metadata, "kind") == BulkProtocol.KindFile)
                {
                    const string unknown = "Chunk received for an unknown or already-closed transfer";
                    OnErrorOccurred($"File receive rejected: {unknown}");
                    await SendBulkErrorAsync(frame.StreamId, "file_receive_failed", unknown).ConfigureAwait(false);
                }

                return;
            }

            var chunkError = BulkProtocol.ValidateChunk(
                incoming.TransferId,
                BulkProtocol.GetString(metadata, "transferId"),
                incoming.ExpectedSize,
                incoming.BytesReceived,
                BulkProtocol.GetInt64(metadata, "offset", -1),
                frame.Payload.Length);
            if (chunkError != null)
            {
                await AbortIncomingFileAsync(frame.StreamId, incoming, chunkError).ConfigureAwait(false);
                return;
            }

            try
            {
                incoming.OutputStream.Write(frame.Payload, 0, frame.Payload.Length);
                incoming.Digest.TransformBlock(frame.Payload, 0, frame.Payload.Length, null, 0);
            }
            catch (Exception ex)
            {
                // Disk full / file lost: abort, delete the .part file and tell the sender.
                await AbortIncomingFileAsync(frame.StreamId, incoming, $"Failed to write received data: {ex.Message}")
                    .ConfigureAwait(false);
                return;
            }

            incoming.BytesReceived += frame.Payload.Length;

            var now = Environment.TickCount64;
            if (now - incoming.LastProgressTick >= ProgressIntervalMs)
            {
                incoming.LastProgressTick = now;
                OnTransferProgress(incoming.SafeFileName, incoming.BytesReceived, incoming.ExpectedSize, incoming.TransferId, isSender: false);
            }
        }

        private async Task AbortIncomingFileAsync(long streamId, IncomingFileStream incoming, string reason)
        {
            _incomingFiles.TryRemove(streamId, out _);
            incoming.Dispose();
            TryDelete(incoming.WorkingPath);
            OnTransferFailed(incoming.SafeFileName, incoming.TargetPath, incoming.BytesReceived, incoming.TransferId, isSender: false, errorMessage: reason);
            OnErrorOccurred($"File receive aborted: {reason}");
            await SendBulkErrorAsync(streamId, "file_receive_failed", reason).ConfigureAwait(false);
        }

        private async Task CompleteIncomingFileAsync(ProtocolFrame frame, IReadOnlyDictionary<string, JsonElement> metadata)
        {
            if (!_incomingFiles.TryRemove(frame.StreamId, out var incoming))
            {
                if (BulkProtocol.GetString(metadata, "kind") == BulkProtocol.KindFile)
                {
                    const string unknown = "Completion received for an unknown or already-closed transfer";
                    OnErrorOccurred($"File receive rejected: {unknown}");
                    await SendBulkErrorAsync(frame.StreamId, "file_receive_failed", unknown).ConfigureAwait(false);
                }

                return;
            }

            try
            {
                incoming.Digest.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                incoming.OutputStream.Flush();
                incoming.OutputStream.Dispose();
                var actualHash = BulkProtocol.Sha256Hex(incoming.Digest.Hash ?? Array.Empty<byte>());

                // Single place for transfer-id / size / checksum validation, shared with the unit tests.
                var completionError = BulkProtocol.ValidateCompletion(
                    incoming.TransferId,
                    BulkProtocol.GetString(metadata, "transferId"),
                    BulkProtocol.GetInt64(metadata, "sizeBytes", incoming.ExpectedSize),
                    incoming.BytesReceived,
                    BulkProtocol.GetString(metadata, "sha256"),
                    actualHash);
                if (completionError != null)
                {
                    throw new IOException($"{completionError} for {incoming.SafeFileName}");
                }

                var finalPath = MoveCompletedFile(incoming);
                var finalName = Path.GetFileName(finalPath);
                TransferDiagnostics.Log("FileReceiveComplete", "Receiver", transferId: incoming.TransferId, message: $"Successfully finalized and verified: {finalName} at {finalPath}");
                OnTransferProgress(finalName, incoming.BytesReceived, incoming.ExpectedSize, incoming.TransferId, isSender: false);
                OnStatusChanged($"File received successfully: {finalName}");
                OnFileReceived(finalName, finalPath, incoming.BytesReceived, incoming.TransferId);
                await SendAckAsync(frame, "bulk.complete", actualHash, incoming.BytesReceived).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TransferDiagnostics.Log("FileReceiveCompleteError", "Receiver", transferId: incoming.TransferId, message: ex.Message, ex: ex);
                TryDelete(incoming.WorkingPath);
                await SendBulkErrorAsync(frame.StreamId, "file_receive_failed", ex.Message).ConfigureAwait(false);
                OnTransferFailed(incoming.SafeFileName, incoming.TargetPath, incoming.BytesReceived, incoming.TransferId, isSender: false, errorMessage: ex.Message);
                OnErrorOccurred($"File receive failed: {ex.Message}");
            }
            finally
            {
                incoming.Dispose();
            }
        }

        private void CancelIncomingFile(ProtocolFrame frame, IReadOnlyDictionary<string, JsonElement> metadata)
        {
            if (!_incomingFiles.TryRemove(frame.StreamId, out var incoming))
            {
                return;
            }

            incoming.Dispose();
            TryDelete(incoming.WorkingPath);
            var reason = BulkProtocol.GetString(metadata, "reason", "cancelled");
            OnTransferFailed(incoming.SafeFileName, incoming.TargetPath, incoming.BytesReceived, incoming.TransferId, isSender: false, errorMessage: reason);
            OnErrorOccurred($"File transfer cancelled: {incoming.SafeFileName} ({reason})");
        }

        private async Task SendAckAsync(ProtocolFrame frame, string ackFor, string? sha256 = null, long sizeBytes = -1)
        {
            if (_sessionManager?.IsReady != true)
            {
                return;
            }

            await _sessionManager.SendControlFrameAsync(
                ProtocolFrameType.Ack,
                BulkProtocol.BuildMetadata(new Dictionary<string, object?>
                {
                    ["ackFor"] = ackFor,
                    ["streamId"] = frame.StreamId,
                    ["status"] = "ok",
                    ["sha256"] = sha256,
                    ["sizeBytes"] = sizeBytes,
                    ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }),
                streamId: frame.StreamId,
                correlationId: frame.CorrelationId).ConfigureAwait(false);
        }

        private async Task TryCancelBulkStreamAsync(long streamId, string transferId, string reason, string message)
        {
            if (_sessionManager?.IsReady != true)
            {
                return;
            }

            try
            {
                await _sessionManager.SendBulkFrameAsync(
                    ProtocolFrameType.BulkCancel,
                    BulkProtocol.BuildMetadata(new Dictionary<string, object?>
                    {
                        ["kind"] = BulkProtocol.KindFile,
                        ["transferId"] = transferId,
                        ["reason"] = reason,
                        ["message"] = message
                    }),
                    streamId: streamId,
                    correlationId: BulkProtocol.GuidFromStringOrNew(transferId)).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private async Task SendBulkErrorAsync(long streamId, string code, string message)
        {
            if (_sessionManager?.IsReady != true)
            {
                return;
            }

            await _sessionManager.SendControlFrameAsync(
                ProtocolFrameType.Error,
                BulkProtocol.BuildMetadata(new Dictionary<string, object?>
                {
                    ["code"] = code,
                    ["message"] = message,
                    ["streamId"] = streamId
                }),
                streamId: streamId).ConfigureAwait(false);
        }

        private string ResolveDownloadDirectory()
        {
            var fallback = DefaultDownloadPath();
            var requested = string.IsNullOrWhiteSpace(_downloadPath) ? fallback : _downloadPath;
            try
            {
                Directory.CreateDirectory(requested);
                return requested;
            }
            catch
            {
                Directory.CreateDirectory(fallback);
                OnStatusChanged($"Download path unavailable, using fallback: {fallback}");
                return fallback;
            }
        }

        private static string CreatePartialPath(string targetPath)
        {
            var directory = Path.GetDirectoryName(targetPath) ?? DefaultDownloadPath();
            var fileName = Path.GetFileName(targetPath);
            return Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.wdcable-part");
        }

        private static string MoveCompletedFile(IncomingFileStream incoming)
        {
            var targetPath = incoming.TargetPath;
            if (File.Exists(targetPath))
            {
                var directory = Path.GetDirectoryName(targetPath) ?? DefaultDownloadPath();
                targetPath = BulkProtocol.DuplicateSafePath(directory, Path.GetFileName(targetPath));
            }

            File.Move(incoming.WorkingPath, targetPath);
            return targetPath;
        }

        private void CleanupIncoming(bool deletePartialFiles)
        {
            foreach (var pair in _incomingFiles)
            {
                if (_incomingFiles.TryRemove(pair.Key, out var incoming))
                {
                    incoming.Dispose();
                    if (deletePartialFiles)
                    {
                        TryDelete(incoming.WorkingPath);
                    }
                }
            }
        }

        private static string DefaultDownloadPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private void OnStatusChanged(string status)
        {
            RaiseOnDispatcher(() => StatusChanged?.Invoke(this, status));
        }

        private void OnErrorOccurred(string error)
        {
            RaiseOnDispatcher(() => ErrorOccurred?.Invoke(this, error));
        }

        private void OnFileReceived(string fileName, string filePath, long fileSize, string transferId)
        {
            PersistTerminalRecord(
                transferId,
                fileName,
                filePath,
                fileSize,
                isSender: false,
                status: "Received");
            RaiseOnDispatcher(() => FileReceived?.Invoke(this, new FileTransferEventArgs(fileName, filePath, fileSize, transferId)));
        }

        private void OnFileSent(string fileName, string filePath, long fileSize, string transferId)
        {
            PersistTerminalRecord(
                transferId,
                fileName,
                filePath,
                fileSize,
                isSender: true,
                status: "Sent");
            RaiseOnDispatcher(() => FileSent?.Invoke(this, new FileTransferEventArgs(fileName, filePath, fileSize, transferId)));
        }

        private void OnTransferProgress(string fileName, long bytesTransferred, long totalBytes, string transferId, bool isSender)
        {
            RaiseOnDispatcher(() => TransferProgress?.Invoke(this, new FileTransferProgressEventArgs(fileName, bytesTransferred, totalBytes, transferId, isSender)));
        }

        private void OnFileReceiveStarted(string fileName, long fileSize, string transferId)
        {
            RaiseOnDispatcher(() => FileReceiveStarted?.Invoke(this, new FileReceiveStartedEventArgs(fileName, fileSize, transferId)));
        }

        private void OnTransferFailed(string fileName, string filePath, long fileSize, string transferId, bool isSender, string errorMessage)
        {
            PersistTerminalRecord(
                transferId,
                fileName,
                filePath,
                fileSize,
                isSender,
                status: "Failed",
                errorMessage);
            RaiseOnDispatcher(() => TransferFailed?.Invoke(this, new FileTransferFailedEventArgs(fileName, filePath, fileSize, transferId, isSender, errorMessage)));
        }

        private static void PersistTerminalRecord(
            string transferId,
            string fileName,
            string filePath,
            long fileSize,
            bool isSender,
            string status,
            string? errorMessage = null)
        {
            ServiceManager.DataManager?.UpsertFileTransferRecord(new FileTransferRecordData
            {
                TransferId = transferId,
                FileName = fileName,
                FilePath = filePath,
                FileSize = fileSize,
                IsSender = isSender,
                Status = status,
                Timestamp = DateTime.Now,
                ErrorMessage = errorMessage
            });
        }

        private void RaiseOnDispatcher(Action action)
        {
            if (_dispatcherQueue != null)
            {
                _dispatcherQueue.TryEnqueue(() => action());
            }
            else
            {
                action();
            }
        }

        private sealed class IncomingFileStream : IDisposable
        {
            public IncomingFileStream(
                string transferId,
                string fileName,
                string safeFileName,
                long expectedSize,
                string targetPath,
                string workingPath,
                FileStream outputStream,
                SHA256 digest)
            {
                TransferId = transferId;
                FileName = fileName;
                SafeFileName = safeFileName;
                ExpectedSize = expectedSize;
                TargetPath = targetPath;
                WorkingPath = workingPath;
                OutputStream = outputStream;
                Digest = digest;
            }

            public string TransferId { get; }
            public string FileName { get; }
            public string SafeFileName { get; }
            public long ExpectedSize { get; }
            public string TargetPath { get; }
            public string WorkingPath { get; }
            public FileStream OutputStream { get; }
            public SHA256 Digest { get; }
            public long BytesReceived { get; set; }
            public long LastProgressTick { get; set; }

            public void Dispose()
            {
                OutputStream.Dispose();
                Digest.Dispose();
            }
        }
    }

    /// <summary>Final sender-side result; Success means the receiver verified and finalized the file.</summary>
    public sealed record FileTransferResult(bool Success, string FileName, long BytesSent, string? Error);

    public class FileTransferEventArgs : EventArgs
    {
        public string FileName { get; }
        public string FilePath { get; }
        public long FileSize { get; }
        public string TransferId { get; }

        public FileTransferEventArgs(string fileName, string filePath, long fileSize, string transferId = "")
        {
            FileName = fileName;
            FilePath = filePath;
            FileSize = fileSize;
            TransferId = transferId;
        }
    }

    public class FileTransferProgressEventArgs : EventArgs
    {
        public string FileName { get; }
        public long BytesTransferred { get; }
        public long TotalBytes { get; }
        public string TransferId { get; }
        public bool IsSender { get; }
        public double ProgressPercentage
        {
            get
            {
                if (TotalBytes < 0)
                {
                    return 0;
                }

                if (TotalBytes == 0)
                {
                    return 100;
                }

                return Math.Min(100, Math.Max(0, (double)BytesTransferred / TotalBytes * 100));
            }
        }

        public FileTransferProgressEventArgs(
            string fileName,
            long bytesTransferred,
            long totalBytes,
            string transferId = "",
            bool isSender = false)
        {
            FileName = fileName;
            BytesTransferred = bytesTransferred;
            TotalBytes = totalBytes;
            TransferId = transferId;
            IsSender = isSender;
        }
    }

    public class FileReceiveStartedEventArgs : EventArgs
    {
        public string FileName { get; }
        public long FileSize { get; }
        public string TransferId { get; }

        public FileReceiveStartedEventArgs(string fileName, long fileSize, string transferId = "")
        {
            FileName = fileName;
            FileSize = fileSize;
            TransferId = transferId;
        }
    }

    public class FileTransferFailedEventArgs : EventArgs
    {
        public string FileName { get; }
        public string FilePath { get; }
        public long FileSize { get; }
        public string TransferId { get; }
        public bool IsSender { get; }
        public string ErrorMessage { get; }

        public FileTransferFailedEventArgs(
            string fileName,
            string filePath,
            long fileSize,
            string transferId,
            bool isSender,
            string errorMessage)
        {
            FileName = fileName;
            FilePath = filePath;
            FileSize = fileSize;
            TransferId = transferId;
            IsSender = isSender;
            ErrorMessage = errorMessage;
        }
    }
}
