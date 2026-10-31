using System;
using System.Text.Json;
using AirFlick.Protocol;

namespace AirFlick.Services;

/// <summary>
/// The challenge the sender publishes to the receiver. It carries no secret: the PIN never crosses the
/// wire, only the authorization id and the nonce the receiver needs to compute
/// <see cref="PinAuthorizationService.ComputeProof"/>.
/// </summary>
public sealed record AirFlickAuthorizationChallenge(
    string AuthorizationId,
    byte[] Nonce,
    string FileName,
    long SizeBytes,
    DateTimeOffset ExpiresAtUtc);

public sealed record AirFlickAuthorizationProofMessage(string AuthorizationId, byte[] Proof);

public sealed record AirFlickAuthorizationResultMessage(string AuthorizationId, bool Authorized, string? Message);

/// <summary>
/// The single place where AirFlick PIN authorization meets the WDCable wire format. It deliberately
/// introduces NO new <see cref="ProtocolFrameType"/> values: it reuses
/// <see cref="ProtocolFrameType.ControlMessage"/> on <see cref="ProtocolChannel.Control"/> and
/// discriminates on a metadata "kind", exactly like <see cref="ChatProtocol"/>. Older WDCable clients
/// parse the frame, see an unknown kind and ignore it, so backward compatibility is preserved.
///
/// Keeping the codec here means no PIN logic leaks into SessionManager, SessionTransport,
/// ProtocolCodec, FileTransferService or BulkProtocol. The transport-side wiring lives in
/// AirFlickBackend.SessionAuthorizationChannel; everything else only sees
/// <see cref="IAirFlickAuthorizationChannel"/>.
/// </summary>
public static class AirFlickAuthorizationProtocol
{
    public const string Kind = "airflick.auth";

    public const string OpChallenge = "challenge";
    public const string OpProof = "proof";
    public const string OpResult = "result";

    public static ProtocolFrame CreateChallengeFrame(AirFlickAuthorizationChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        var metadata = JsonSerializer.Serialize(new
        {
            kind = Kind,
            op = OpChallenge,
            authorizationId = challenge.AuthorizationId,
            fileName = challenge.FileName,
            sizeBytes = challenge.SizeBytes,
            expiresAt = challenge.ExpiresAtUtc.ToUnixTimeMilliseconds()
        });

        return new ProtocolFrame(
            ProtocolFrameType.ControlMessage,
            ProtocolChannel.Control,
            correlationId: CorrelationIdFor(challenge.AuthorizationId),
            metadataJson: metadata,
            payload: (byte[])challenge.Nonce.Clone());
    }

    public static ProtocolFrame CreateProofFrame(string authorizationId, byte[] proof)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationId);
        ArgumentNullException.ThrowIfNull(proof);

        var metadata = JsonSerializer.Serialize(new
        {
            kind = Kind,
            op = OpProof,
            authorizationId
        });

        return new ProtocolFrame(
            ProtocolFrameType.ControlMessage,
            ProtocolChannel.Control,
            correlationId: CorrelationIdFor(authorizationId),
            metadataJson: metadata,
            payload: (byte[])proof.Clone());
    }

    public static ProtocolFrame CreateResultFrame(string authorizationId, bool authorized, string? message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationId);

        var metadata = JsonSerializer.Serialize(new
        {
            kind = Kind,
            op = OpResult,
            authorizationId,
            authorized,
            message
        });

        return new ProtocolFrame(
            ProtocolFrameType.ControlMessage,
            ProtocolChannel.Control,
            correlationId: CorrelationIdFor(authorizationId),
            metadataJson: metadata);
    }

    /// <summary>True when the frame is an AirFlick authorization frame of any operation.</summary>
    public static bool IsAuthorizationFrame(ProtocolFrame frame, out string operation)
    {
        operation = string.Empty;
        if (frame == null ||
            frame.Type != ProtocolFrameType.ControlMessage ||
            frame.Channel != ProtocolChannel.Control)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(frame.MetadataJson) ? "{}" : frame.MetadataJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("kind", out var kind) ||
                kind.ValueKind != JsonValueKind.String ||
                kind.GetString() != Kind)
            {
                return false;
            }

            operation = GetString(root, "op");
            return operation.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryParseChallenge(ProtocolFrame frame, out AirFlickAuthorizationChallenge? challenge)
    {
        challenge = null;
        if (!IsAuthorizationFrame(frame, out var operation) || operation != OpChallenge)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(frame.MetadataJson);
            var root = document.RootElement;
            var authorizationId = GetString(root, "authorizationId");
            if (authorizationId.Length == 0 || frame.Payload.Length == 0)
            {
                return false;
            }

            challenge = new AirFlickAuthorizationChallenge(
                authorizationId,
                (byte[])frame.Payload.Clone(),
                GetString(root, "fileName"),
                GetInt64(root, "sizeBytes", -1),
                DateTimeOffset.FromUnixTimeMilliseconds(
                    GetInt64(root, "expiresAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public static bool TryParseProof(ProtocolFrame frame, out AirFlickAuthorizationProofMessage? proof)
    {
        proof = null;
        if (!IsAuthorizationFrame(frame, out var operation) || operation != OpProof)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(frame.MetadataJson);
            var authorizationId = GetString(document.RootElement, "authorizationId");
            if (authorizationId.Length == 0 || frame.Payload.Length == 0)
            {
                return false;
            }

            proof = new AirFlickAuthorizationProofMessage(authorizationId, (byte[])frame.Payload.Clone());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryParseResult(ProtocolFrame frame, out AirFlickAuthorizationResultMessage? result)
    {
        result = null;
        if (!IsAuthorizationFrame(frame, out var operation) || operation != OpResult)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(frame.MetadataJson);
            var root = document.RootElement;
            var authorizationId = GetString(root, "authorizationId");
            if (authorizationId.Length == 0)
            {
                return false;
            }

            var authorized = root.TryGetProperty("authorized", out var flag) && flag.ValueKind == JsonValueKind.True;
            var message = root.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString()
                : null;

            result = new AirFlickAuthorizationResultMessage(authorizationId, authorized, message);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Authorization ids are created as GUID "N" strings, so they map onto the frame correlation id and
    /// peers can correlate without parsing metadata. Anything else gets a stable derived id.
    /// </summary>
    public static Guid CorrelationIdFor(string authorizationId)
    {
        return Guid.TryParse(authorizationId, out var guid) ? guid : Guid.NewGuid();
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static long GetInt64(JsonElement root, string propertyName, long fallback)
    {
        return root.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value)
            ? value
            : fallback;
    }
}
