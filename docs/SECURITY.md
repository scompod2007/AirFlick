# Security and Authorization Model

AirFlick is designed for peer-to-peer wireless file transfers in public and semi-public environments (offices, coffee shops, classrooms).

---

## 1. Threat Model & Mitigations

### 1.1 Unauthorized Transfer Initiation
- **Risk**: A rogue device on the local Wi-Fi initiates unauthorized file downloads or uploads.
- **Mitigation**: AirFlick requires an explicit **3-digit PIN authentication handshake** before transferring any file payload. The receiver displays the PIN, and the sender must prove knowledge of the PIN before the receiver opens the bulk transfer port.

### 1.2 Brute-Force Attacks on PIN
- **Risk**: Since the PIN is 3 digits chosen from `[1, 5]`, the total keyspace is 5³ = 125 possible combinations.
- **Mitigation**:
  - The receiver limits PIN verification attempts to **a maximum of 3**.
  - Upon 3 failed attempts, the current authorization session is immediately terminated, and the temporary transfer ticket is invalidated.
  - A strict **120-second monotonic expiry** ensures tickets cannot be replayed or probed indefinitely.

### 1.3 Side-Channel Timing Attacks
- **Risk**: An attacker measures microsecond variations in response times to deduce matching PIN digits.
- **Mitigation**: Verification uses `CryptographicOperations.FixedTimeEquals` for constant-time comparison of challenge tokens and HMAC digests.

### 1.4 Payload Tampering & Incomplete Transfers
- **Risk**: Packets dropped or altered in transit over direct wireless frames.
- **Mitigation**: The transfer stream is verified via end-to-end SHA-256 calculation. Any hash mismatch causes immediate deletion of the staged `.part` file.

