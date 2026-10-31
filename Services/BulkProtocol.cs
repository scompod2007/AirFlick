using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AirFlick.Protocol;

namespace AirFlick.Services;

public static class BulkProtocol
{
    public const int ChunkSize = 64 * 1024;

    public const string KindFile = "file";
    public const string KindSpeedRequest = "speed-request";
    public const string KindSpeedData = "speed-data";

    public static string BuildMetadata(IDictionary<string, object?> values)
    {
        return JsonSerializer.Serialize(values);
    }

    public static Dictionary<string, JsonElement> ParseMetadata(string metadataJson)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return result;
        }

        using var document = JsonDocument.Parse(metadataJson);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            result[property.Name] = property.Value.Clone();
        }

        return result;
    }

    public static string GetString(IReadOnlyDictionary<string, JsonElement> metadata, string key, string fallback = "")
    {
        return metadata.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    public static long GetInt64(IReadOnlyDictionary<string, JsonElement> metadata, string key, long fallback = 0)
    {
        return metadata.TryGetValue(key, out var value) && value.TryGetInt64(out var number)
            ? number
            : fallback;
    }

    /// <summary>Chunk metadata without the Dictionary/JsonSerializer cost; transferId is a GUID string.</summary>
    public static string BuildChunkMetadata(string transferId, long offset)
    {
        return "{\"kind\":\"" + KindFile + "\",\"transferId\":" + JsonSerializer.Serialize(transferId) +
               ",\"offset\":" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
    }

    /// <summary>
    /// Returns null when a file BulkStart is acceptable, otherwise the rejection reason.
    /// (SessionId is generated locally per peer, so it cannot be compared across the link.)
    /// </summary>
    public static string? ValidateStart(string transferId, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(transferId))
        {
            return "Missing transfer id";
        }

        if (sizeBytes < 0)
        {
            return "Missing or invalid file size";
        }

        return null;
    }

    /// <summary>Returns null when a file chunk is acceptable, otherwise the rejection reason.</summary>
    public static string? ValidateChunk(
        string expectedTransferId,
        string chunkTransferId,
        long expectedSize,
        long bytesReceived,
        long offset,
        int payloadLength)
    {
        if (!string.Equals(expectedTransferId, chunkTransferId, StringComparison.Ordinal))
        {
            return "Chunk belongs to a different transfer";
        }

        if (payloadLength <= 0)
        {
            return "Empty chunk";
        }

        if (offset != bytesReceived)
        {
            return $"Out-of-order chunk: expected offset {bytesReceived}, got {offset}";
        }

        if (expectedSize >= 0 && bytesReceived + payloadLength > expectedSize)
        {
            return "Chunk exceeds the declared file size";
        }

        return null;
    }

    /// <summary>
    /// Returns null when a completed file transfer may be finalized, otherwise the rejection reason.
    /// The caller must delete the .part file and report failure for any non-null result.
    /// </summary>
    public static string? ValidateCompletion(
        string expectedTransferId,
        string completeTransferId,
        long declaredSize,
        long bytesReceived,
        string? expectedSha256,
        string? actualSha256)
    {
        // An empty transfer id on the completion frame is tolerated for older peers, a wrong one is not.
        if (!string.IsNullOrEmpty(completeTransferId) &&
            !string.Equals(expectedTransferId, completeTransferId, StringComparison.Ordinal))
        {
            return "Completion frame belongs to a different transfer";
        }

        if (declaredSize < 0)
        {
            return "Missing or invalid file size";
        }

        if (bytesReceived != declaredSize)
        {
            return $"Size mismatch: expected {declaredSize} bytes, received {bytesReceived} bytes";
        }

        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return "Sender did not provide a checksum";
        }

        if (string.IsNullOrWhiteSpace(actualSha256) ||
            !expectedSha256.Equals(actualSha256, StringComparison.OrdinalIgnoreCase))
        {
            return "Checksum mismatch";
        }

        return null;
    }

    public static string SafeFileName(string fileName)
    {
        var input = string.IsNullOrWhiteSpace(fileName) ? "unknown_file" : fileName.Trim();
        var separatorIndex = input.LastIndexOfAny(['\\', '/']);
        var candidate = separatorIndex >= 0 ? input[(separatorIndex + 1)..] : input;

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            candidate = candidate.Replace(invalid, '_');
        }

        candidate = candidate.Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(candidate) || candidate is "." or "..")
        {
            candidate = "unknown_file";
        }

        var baseName = Path.GetFileNameWithoutExtension(candidate);
        if (IsReservedWindowsDeviceName(baseName))
        {
            candidate = $"_{candidate}";
        }

        return candidate;
    }

    public static string DuplicateSafePath(string directoryPath, string fileName)
    {
        Directory.CreateDirectory(directoryPath);

        var safeName = SafeFileName(fileName);
        var candidate = Path.Combine(directoryPath, safeName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        for (var index = 1; ; index++)
        {
            candidate = Path.Combine(directoryPath, $"{nameWithoutExtension} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    public static string Sha256Hex(byte[] hash)
    {
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }

    public static long NextStreamId()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToInt64(bytes);
        return value == long.MinValue ? 1 : Math.Abs(value);
    }

    public static Guid GuidFromStringOrNew(string value)
    {
        return Guid.TryParse(value, out var guid) ? guid : Guid.NewGuid();
    }

    public static double CalculateMbps(long bytes, TimeSpan duration)
    {
        bytes = Math.Max(bytes, 0);
        var seconds = Math.Max(duration.TotalSeconds, 0.001);
        return bytes * 8.0 / 1024 / 1024 / seconds;
    }

    private static bool IsReservedWindowsDeviceName(string value)
    {
        return value.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               IsReservedDeviceRange(value, "COM") ||
               IsReservedDeviceRange(value, "LPT");
    }

    private static bool IsReservedDeviceRange(string value, string prefix)
    {
        return value.Length == 4 &&
               value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               value[3] is >= '1' and <= '9';
    }
}
