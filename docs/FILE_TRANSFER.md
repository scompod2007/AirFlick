# AirFlick File Transfer Protocol

AirFlick implements a dual-socket architecture for peer-to-peer file transfers over Wi-Fi Direct:
1. **Control Channel (Port 8987)**: JSON command framing and PIN authorization handshake.
2. **Bulk Data Channel (Port 8988)**: Raw chunked binary file stream with streaming SHA-256 validation.

---

## 1. Transfer Sequence

```text
  Sender                                                 Receiver
    |                                                       |
    | ----- [TCP Control Connect : Port 8987] ------------> |
    |                                                       |
    | <---- [AuthChallenge (Nonce, 120s TTL)] ------------- |
    |                                                       |
    | ----- [AuthProof (HMAC-SHA256 with 3-digit PIN)] ----> |
    |                                                       |
    | <---- [AuthSuccess & TransferTicket] ---------------- |
    |                                                       |
    | ===== [TCP Bulk Stream Connect : Port 8988] =======> |
    |                                                       |
    | ===== [Binary Header: FileSize (8B), NameLen (2B)] => |
    | ===== [Chunked Payload (64KB chunks)] ==============> |
    | ===== [Trailer: 32-byte SHA-256 Checksum] ==========> |
    |                                                       |
    | <---- [TransferStatus: Success (Control Socket)] ---- |
    |                                                       |
```

---

## 2. Chunking and Flow Control

- **Chunk Size**: 64 KB (65,536 bytes) buffer size for optimal network socket saturation.
- **Progress Tracking**: Both sender and receiver compute bytes transferred against total file length and report completion percentage to their respective ViewModels.
- **Integrity Check**: The sender computes the SHA-256 hash as bytes are read from disk. The receiver recomputes the SHA-256 hash incrementally as bytes arrive. Upon receiving the end-of-stream trailer, the receiver validates the digest using constant-time byte comparison.
- **Atomicity**: Files are written to a temporary staging path (`.part` extension) and only moved to the final destination upon successful SHA-256 verification.

