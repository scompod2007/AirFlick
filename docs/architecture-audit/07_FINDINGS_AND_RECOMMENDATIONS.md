# AirFlick Architecture Audit — Findings, Technical Debt, and Defects

## 1. Prioritized Classification of Findings

### [CRITICAL] Core Gesture Subsystem Completely Missing
- **Defect**: The product identity is "Gesture-Controlled Laptop-to-Laptop File Transfer", but no Python scripts, OpenCV/MediaPipe tracking, or gesture models exist in the repository.
- **Impact**: Without gestures, the application is strictly a conventional manual Wi-Fi Direct file sharing tool.
- **Affected Components**: Python layer, UI preview area (`CameraPreviewPlaceholder`).

### [HIGH] Lack of Python-to-C# Interprocess Bridge
- **Defect**: No IPC bridge (`Process.Start`, stdin/stdout streamer, or socket server) exists to feed machine-readable gesture triggers into C#.
- **Impact**: Even if a Python script is created, C# has no mechanism to spawn it, monitor its health, or parse its events into commands.

### [HIGH] Disconnection Between AirFlickPage and AirFlickBackend
- **Defect**: `AirFlickBackend.Initialize()` instantiates a global `AirFlickCommandService` and `SessionAuthorizationChannel` inside `ServiceManager`. However, `AirFlickPage.xaml.cs` creates a separate, local `AirFlickStateMachine` instance and interacts directly with `FileTransferService.SendFileAsync()`.
- **Impact**: The UI does not drive or reflect the authoritative `AirFlickCommandService` or its remote PIN authorization channel.

### [MEDIUM] Offline Brute-Force Vulnerability in PIN Proof
- **Defect**: The 3-digit PIN space ($1,000$ values) combined with transmitting the challenge nonce and HMAC-SHA256 proof over plaintext TCP allows an eavesdropper to determine the PIN offline.
- **Impact**: While online guessing is stopped after 3 attempts, the PIN provides proximity consent rather than cryptographic authentication against network attackers.

### [MEDIUM] Receiver-Side Staging Pre-Authorization Gap
- **Defect**: In `FileTransferService.cs`, an incoming `BulkStart` immediately begins creating `.part` staging files on the destination drive before checking if an AirFlick authorization ticket has been verified.
- **Impact**: An attacker who connects to port 8989 could initiate arbitrary bulk transfers that consume disk space without triggering the PIN gate.

### [LOW] Legacy Features & Navigation Clutter
- **Finding**: WDCable features (Audio streaming, SpeedTest, Chat) remain compiled and present in the navigation bar alongside AirFlick.
- **Impact**: Increases binary size and UI complexity, though useful as standalone diagnostics.

---

## 2. Technical Debt and Dead Code Candidates

| File / Component | Status | Analysis & Recommendation |
| :--- | :--- | :--- |
| `Services/AudioService.cs` (1,875 lines) & `AudioProtocol.cs` (672 lines) | Unrelated to AirFlick | Mature, high-quality audio streaming. Keep if multi-feature app is intended; isolate or decouple if AirFlick becomes a dedicated single-purpose app. |
| `Services/SpeedTestService.cs` (729 lines) | Diagnostic tool | Useful for testing Wi-Fi Direct throughput; can be kept in a "Diagnostics" or "Tools" menu. |
| `Services/ChatService.cs` (225 lines) | Feature module | Simple text chat over control channel. Harmless to retain. |
| `UI/AirFlick/AirFlickPage.xaml.cs` (standalone state machine) | Architectural debt | Refactor to consume `AirFlickBackend.Command` rather than running its own state machine. |

