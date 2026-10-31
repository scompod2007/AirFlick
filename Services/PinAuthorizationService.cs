using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace AirFlick.Services;

public enum PinAuthorizationStatus
{
    Authorized,
    WrongPin,
    Expired,
    TooManyAttempts,
    Cancelled,
    AlreadyUsed,
    Unknown
}

public sealed record PinAuthorizationResult(PinAuthorizationStatus Status, int AttemptsRemaining)
{
    public bool IsAuthorized => Status == PinAuthorizationStatus.Authorized;
}

/// <summary>
/// What the sender holds after starting an authorization. <see cref="Pin"/> is shown to / entered by the
/// human; <see cref="AuthorizationId"/> and <see cref="Nonce"/> are the (non-secret) challenge that would be
/// sent to the receiver. The PIN itself is never part of the challenge or the proof.
/// </summary>
public sealed record PinAuthorizationTicket(
    string AuthorizationId,
    string Pin,
    byte[] Nonce,
    DateTimeOffset ExpiresAtUtc,
    int MaxAttempts);

public interface IPinAuthorizationService
{
    PinAuthorizationTicket CreateAuthorization();

    /// <summary>Verifies a receiver-computed proof (see <see cref="PinAuthorizationService.ComputeProof"/>).</summary>
    PinAuthorizationResult VerifyProof(string authorizationId, byte[] proof);

    /// <summary>Convenience for local/programmatic/test use: derives the proof from a typed PIN and verifies it.</summary>
    PinAuthorizationResult VerifyPin(string authorizationId, string pin);

    bool IsAuthorized(string authorizationId);

    /// <summary>Spends an authorization; returns true exactly once for an authorized, unexpired ticket.</summary>
    bool TryConsume(string authorizationId);

    void Cancel(string authorizationId);
}

/// <summary>
/// PIN authorization gate. It is independent of Wi-Fi Direct, SessionManager, FileTransferService and
/// BulkProtocol: it only decides whether a transfer request has been authorized.
///
/// Scheme: the sender creates a random 3-digit PIN and a random 32-byte nonce. The receiver, knowing the
/// PIN typed by the user, answers with proof = HMAC-SHA256(key = PIN, data = domain | id | nonce). The sender
/// compares proofs in constant time. Tickets expire, allow a limited number of attempts, and are single-use.
///
/// Limitation: a 3-digit PIN has only 1000 values, so an attacker who captures nonce + proof can brute force
/// the PIN offline instantly. Attempt limits only stop online guessing. Treat this as a proximity/consent
/// gate, not strong cryptographic authentication (see the project notes for stronger options).
/// </summary>
public sealed class PinAuthorizationService : IPinAuthorizationService
{
    public const int PinLength = 3;
    public const int NonceSize = 32;
    public const int DefaultMaxAttempts = 3;
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(120);

    private const string ProofDomain = "airflick.pin.v1";

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _lifetime;
    private readonly int _maxAttempts;

    public PinAuthorizationService(
        TimeSpan? lifetime = null,
        int maxAttempts = DefaultMaxAttempts,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        _lifetime = lifetime ?? DefaultLifetime;
        _maxAttempts = maxAttempts;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Uniformly random PIN in 111-555 where every digit is restricted to 1-5, always exactly three digits.</summary>
    public static string GenerateThreeDigitPin()
    {
        Span<char> pinChars = stackalloc char[PinLength];
        for (int i = 0; i < PinLength; i++)
        {
            pinChars[i] = (char)('0' + RandomNumberGenerator.GetInt32(1, 6));
        }
        return new string(pinChars);
    }

    public static bool IsValidPinFormat(string? pin)
    {
        if (pin is not { Length: PinLength })
        {
            return false;
        }

        foreach (var c in pin)
        {
            if (c is < '1' or > '5')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Receiver-side proof: HMAC-SHA256 keyed by the PIN over (domain | authorizationId | nonce).</summary>
    public static byte[] ComputeProof(string pin, string authorizationId, byte[] nonce)
    {
        ArgumentNullException.ThrowIfNull(pin);
        ArgumentNullException.ThrowIfNull(authorizationId);
        ArgumentNullException.ThrowIfNull(nonce);

        var idBytes = Encoding.UTF8.GetBytes(authorizationId);
        var data = new byte[ProofDomain.Length + 1 + idBytes.Length + 1 + nonce.Length];
        var offset = Encoding.UTF8.GetBytes(ProofDomain, 0, ProofDomain.Length, data, 0);
        data[offset++] = 0;
        idBytes.CopyTo(data, offset);
        offset += idBytes.Length;
        data[offset++] = 0;
        nonce.CopyTo(data, offset);

        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(pin), data);
    }

    public PinAuthorizationTicket CreateAuthorization()
    {
        var ticket = new PinAuthorizationTicket(
            Guid.NewGuid().ToString("N"),
            GenerateThreeDigitPin(),
            RandomNumberGenerator.GetBytes(NonceSize),
            _clock() + _lifetime,
            _maxAttempts);

        lock (_lock)
        {
            PruneLocked();
            _entries[ticket.AuthorizationId] = new Entry(ticket);
        }

        return ticket;
    }

    public PinAuthorizationResult VerifyPin(string authorizationId, string pin)
    {
        // A malformed PIN is still a wrong attempt: it must burn an attempt like any other guess.
        lock (_lock)
        {
            if (!_entries.TryGetValue(authorizationId, out var entry))
            {
                return new PinAuthorizationResult(PinAuthorizationStatus.Unknown, 0);
            }

            var proof = IsValidPinFormat(pin)
                ? ComputeProof(pin, authorizationId, entry.Ticket.Nonce)
                : Array.Empty<byte>();
            return VerifyLocked(entry, proof);
        }
    }

    public PinAuthorizationResult VerifyProof(string authorizationId, byte[] proof)
    {
        ArgumentNullException.ThrowIfNull(proof);

        lock (_lock)
        {
            return _entries.TryGetValue(authorizationId, out var entry)
                ? VerifyLocked(entry, proof)
                : new PinAuthorizationResult(PinAuthorizationStatus.Unknown, 0);
        }
    }

    public bool IsAuthorized(string authorizationId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(authorizationId, out var entry) &&
                   entry.Authorized &&
                   !entry.Consumed &&
                   !entry.Cancelled &&
                   !IsExpired(entry);
        }
    }

    public bool TryConsume(string authorizationId)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(authorizationId, out var entry) ||
                !entry.Authorized || entry.Consumed || entry.Cancelled || IsExpired(entry))
            {
                return false;
            }

            entry.Consumed = true;
            return true;
        }
    }

    public void Cancel(string authorizationId)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(authorizationId, out var entry))
            {
                entry.Cancelled = true;
            }
        }
    }

    private PinAuthorizationResult VerifyLocked(Entry entry, byte[] proof)
    {
        if (entry.Cancelled)
        {
            return new PinAuthorizationResult(PinAuthorizationStatus.Cancelled, 0);
        }

        if (entry.Consumed)
        {
            return new PinAuthorizationResult(PinAuthorizationStatus.AlreadyUsed, 0);
        }

        if (IsExpired(entry))
        {
            return new PinAuthorizationResult(PinAuthorizationStatus.Expired, 0);
        }

        if (entry.Attempts >= entry.Ticket.MaxAttempts)
        {
            // Locked out: even the correct PIN no longer works.
            return new PinAuthorizationResult(PinAuthorizationStatus.TooManyAttempts, 0);
        }

        entry.Attempts++;
        var expected = ComputeProof(entry.Ticket.Pin, entry.Ticket.AuthorizationId, entry.Ticket.Nonce);
        if (proof.Length == expected.Length && CryptographicOperations.FixedTimeEquals(proof, expected))
        {
            entry.Authorized = true;
            return new PinAuthorizationResult(PinAuthorizationStatus.Authorized, entry.Ticket.MaxAttempts - entry.Attempts);
        }

        var remaining = entry.Ticket.MaxAttempts - entry.Attempts;
        return new PinAuthorizationResult(
            remaining <= 0 ? PinAuthorizationStatus.TooManyAttempts : PinAuthorizationStatus.WrongPin,
            remaining);
    }

    private bool IsExpired(Entry entry)
    {
        return _clock() >= entry.Ticket.ExpiresAtUtc;
    }

    private void PruneLocked()
    {
        List<string>? stale = null;
        foreach (var pair in _entries)
        {
            if (IsExpired(pair.Value) || pair.Value.Consumed || pair.Value.Cancelled)
            {
                (stale ??= []).Add(pair.Key);
            }
        }

        if (stale != null)
        {
            foreach (var key in stale)
            {
                _entries.Remove(key);
            }
        }
    }

    private sealed class Entry
    {
        public Entry(PinAuthorizationTicket ticket)
        {
            Ticket = ticket;
        }

        public PinAuthorizationTicket Ticket { get; }
        public int Attempts { get; set; }
        public bool Authorized { get; set; }
        public bool Consumed { get; set; }
        public bool Cancelled { get; set; }
    }
}
