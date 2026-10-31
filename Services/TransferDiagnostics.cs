using System;
using System.IO;
using System.Net.Sockets;

namespace AirFlick.Services;

public static class TransferDiagnostics
{
    private static readonly object FileLock = new();
    private static readonly string LogFilePath;

    static TransferDiagnostics()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDir = Path.Combine(localAppData, "AirFlick", "logs");
            Directory.CreateDirectory(logDir);
            LogFilePath = Path.Combine(logDir, "airflick_transfer_diagnostics.log");
        }
        catch
        {
            LogFilePath = "airflick_transfer_diagnostics.log";
        }
    }

    public static void Log(
        string stage,
        string role,
        string? transferId = null,
        string? connectionState = null,
        string? message = null,
        Exception? ex = null,
        int? retryCount = null,
        long? bytesTransferred = null,
        long? totalBytes = null)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("o");
            var tid = string.IsNullOrWhiteSpace(transferId) ? "-" : transferId;
            var cstate = string.IsNullOrWhiteSpace(connectionState) ? "-" : connectionState;
            var retries = retryCount.HasValue ? retryCount.Value.ToString() : "-";
            var progress = (bytesTransferred.HasValue && totalBytes.HasValue) 
                ? $"{bytesTransferred.Value}/{totalBytes.Value}" 
                : "-";

            string socketError = "-";
            if (ex is SocketException se)
            {
                socketError = $"{se.SocketErrorCode}({(int)se.SocketErrorCode})";
            }
            else if (ex?.InnerException is SocketException innerSe)
            {
                socketError = $"{innerSe.SocketErrorCode}({(int)innerSe.SocketErrorCode})";
            }

            var exInfo = ex == null ? "-" : $"{ex.GetType().Name}: {ex.Message}";

            // Format structured log entry (never logging PINs or proofs):
            // [TIMESTAMP] [STAGE] [ROLE] [TID] [STATE] [SOCKET_ERR] [RETRY] [PROGRESS] [MSG] [EX]
            var logLine = $"[{timestamp}] [{stage}] [{role}] [{tid}] [{cstate}] [SockErr:{socketError}] [Retry:{retries}] [Progress:{progress}] {message} {exInfo}".Trim();

            System.Diagnostics.Debug.WriteLine($"[AirFlickDiag] {logLine}");

            lock (FileLock)
            {
                File.AppendAllText(LogFilePath, logLine + Environment.NewLine);
            }
        }
        catch
        {
            // Logging should never throw or disrupt transfer flow
        }
    }
}

