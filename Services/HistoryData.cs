using System;
using System.Collections.Generic;
using System.Linq;

namespace AirFlick.Services;

public sealed class FileTransferRecordData
{
    public string TransferId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public bool IsSender { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? ErrorMessage { get; set; }
}

public static class HistoryDataOperations
{
    public static List<FileTransferRecordData> UpsertTransferRecord(
        IEnumerable<FileTransferRecordData> existing,
        FileTransferRecordData record)
    {
        var records = new Dictionary<string, FileTransferRecordData>(StringComparer.Ordinal);
        foreach (var item in existing)
        {
            records[TransferKey(item)] = item;
        }
        records[TransferKey(record)] = record;

        return records.Values
            .OrderByDescending(item => item.Timestamp)
            .ThenBy(item => item.TransferId, StringComparer.Ordinal)
            .ToList();
    }

    public static string TransferKey(FileTransferRecordData record)
    {
        var id = string.IsNullOrWhiteSpace(record.TransferId)
            ? $"{record.FileName}:{record.Timestamp.Ticks}"
            : record.TransferId;
        return $"{(record.IsSender ? "send" : "receive")}:{id}";
    }
}
