# AirFlick Rebuild Audit — 07: Security and Correctness Findings

This audit evaluates security boundaries, input validation, cryptographic guarantees, and concurrency safety across the existing backend code.

---

## 1. Summary of Identified Findings

| ID | Category | Severity | Component | Finding Summary |
| :--- | :--- | :--- | :--- | :--- |
| **SEC-01** | Cryptography | **MEDIUM** | `PinAuthorizationService.cs` | 3-digit PIN space is vulnerable to offline dictionary attack if packets are captured. |
| **SEC-02** | Access Control | **MEDIUM** | `FileTransferService.cs` | Receiver accepts `BulkStart` and stages `.part` files on disk without checking AirFlick PIN authorization. |
| **COR-01** | Architecture | **HIGH** | `AirFlickPage.xaml.cs` | UI creates its own `AirFlickStateMachine` and bypasses the global `AirFlickBackend.Command` orchestrator. |
| **COR-02** | Transport | **LOW** | `SessionManager.cs` | Heartbeat timeout (15s) can trigger abrupt session disconnect during high-concurrency network lag. |
| **COR-03** | Error Handling | **LOW** | `App.xaml.cs` | Unhandled exceptions write to `wdcable-crash.log` in local app data without rotation, risking unbounded growth. |

---

## 2. In-Depth Analysis of Findings

### SEC-01: Offline Brute-Force Vulnerability in PIN Proof
- **File**: `Services/PinAuthorizationService.cs` (lines 68–95, 230–255)
- **Mechanism**: 
  - Senders generate a 3-digit PIN ($000 \dots 999$).
  - Proof is computed as $\text{HMAC-SHA256}(\text{key} = \text{PIN}, \text{data} = \text{"airflick.pin.v1"} \,\|\, \text{AuthId} \,\|\, \text{Nonce})$.
  - Both `Nonce` and `proof` cross the network unencrypted on TCP port 8988.
- **Vulnerability**: While an online attacker guessing against Laptop A is locked out after 3 attempts, an active passive observer sniffing the Wi-Fi Direct network can iterate over all 1,000 PIN candidates in under 5 milliseconds on any modern CPU and recover the PIN.
- **Classification**: **KNOWN DESIGN LIMITATION (Proximity Barrier vs. Cryptographic Encryption)**.
- **Recommendation**: Document this explicitly as a human consent and proximity mechanism. For cryptographic security against network eavesdroppers, the TCP session should negotiate TLS or Diffie-Hellman ephemeral keys before PIN exchange.

### SEC-02: Receiver-Side Staging Pre-Authorization Gap
- **File**: `Services/FileTransferService.cs` (lines 350–410)
- **Mechanism**: When an incoming `BulkStart` frame arrives on Bulk TCP port 8989, `FileTransferService` immediately allocates an `IncomingFileStream` and calls `File.Create()` on `{downloadPath}/{safeFileName}.part`.
- **Vulnerability**: The PIN authorization check currently happens on the sender side (`AirFlickCommandService.SendPreparedFileAsync()`). The receiver side does not verify that a valid authorization ticket was granted for the incoming `transferId` before opening and writing the destination `.part` file. A rogue peer on the P2P network could flood the receiver's disk with staging files.
- **Recommendation**: Introduce an `IncomingAuthorizationRegistry` inside `AirFlickBackend` that must approve the `transferId` before `FileTransferService` opens any destination stream.

### COR-01: UI Bypass of Authoritative AirFlickBackend
- **File**: `UI/AirFlick/AirFlickPage.xaml.cs` (lines 27, 340–380)
- **Mechanism**: `AirFlickPage` instantiates a local `private readonly AirFlickStateMachine _stateMachine = new();` and directly calls `FileTransferService.SendFileAsync()`. It completely ignores `AirFlickBackend.Command`.
- **Impact**: The state shown on the AirFlick page is disconnected from the global command service. PIN authorization is never executed during UI-initiated transfers.
- **Recommendation**: Refactor `AirFlickPage.xaml.cs` to bind to `AirFlickBackend.Command.StateMachine` and invoke `AirFlickBackend.Command.PrepareFile()` / `SendPreparedFileAsync()`.

### COR-02: Path Traversal Protections (Verified Secure)
- **File**: `Services/BulkProtocol.cs` (lines 40–95)
- **Mechanism**: `BulkProtocol.SafeFileName` rigorously strips directory paths, replaces path separators, neutralizes traversal tokens (`..`, `.`), and prefixes Windows reserved device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1..9`, `LPT1..9`).
- **Verdict**: **SECURE**. Verified by unit tests in `BulkProtocolTests.cs`.

