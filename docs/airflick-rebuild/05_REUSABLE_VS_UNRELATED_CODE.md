# AirFlick Rebuild Audit — 05: Reusable vs. Unrelated Code

## 1. Summary of Code Classification

The repository currently contains approximately **13,500 lines of C# source code** (excluding tests and generated files). 

| Category | Lines of Code | % of Codebase | Action |
| :--- | :--- | :--- | :--- |
| **AirFlick Backend & Authorization** | ~1,850 lines | 14% | **PRESERVE & EXTEND** |
| **Core Networking & File Transfer** | ~5,600 lines | 41% | **PRESERVE WITH CARE** |
| **Shell UI & Settings (Frontend Hand-off)**| ~2,300 lines | 17% | **PRESERVE AS STUBS / ADAPT** |
| **Unrelated Features (Audio, Chat, Speed)** | ~3,750 lines | 28% | **STAGE FOR REMOVAL / ISOLATION** |

---

## 2. Reusable Backend Code (Must Preserve)

### A. Core Wi-Fi Direct and Transport (Foundation)
1. `Services/WiFiDirectService.cs`: WinRT device watcher, advertisement publisher, and connection listener.
2. `Services/WiFiDirectCapabilityReport.cs`: Probe for wireless adapter capabilities via `netsh`.
3. `Services/WiFiDirectEndpointSelector.cs`: IP pair and Group Owner / Client resolution.
4. `Services/SessionManager.cs`: Full multiplexed TCP session controller with heartbeats and recovery.
5. `Services/SessionStateMachine.cs`, `SessionPhase.cs`, `SessionRole.cs`, `SessionTransportRole.cs`: State transitions for the transport layer.
6. `Protocol/` (All 7 files): Binary 56-byte header codec, frame definitions, error handling, and constants.

### B. File Transfer and Bulk Streaming Engine
1. `Services/FileTransferService.cs`: High-performance 64 KB chunk streaming, streaming SHA-256 calculation, `.part` staging, and completion Acks.
2. `Services/BulkProtocol.cs`: Chunk metadata serialization, path traversal security (`SafeFileName`), and duplicate name resolution (`DuplicateSafePath`).

### C. AirFlick Orchestration & Authorization
1. `Services/AirFlickCommandService.cs`: Orchestrates file preparation, target verification, remote PIN proof requests, and file streaming.
2. `Services/AirFlickBackend.cs`: Bridges `SessionManager` and `FileTransferService` to `AirFlickCommandService`.
3. `Services/AirFlickStateMachine.cs`: Thread-safe application state machine.
4. `Services/PinAuthorizationService.cs`: Proximity PIN generation, 32-byte nonces, and HMAC-SHA256 proof engine.
5. `Services/AirFlickAuthorizationProtocol.cs`: Serialization of PIN challenge, proof, and result control frames.

---

## 3. Unrelated Code (Removal Candidates)

### A. Real-time Audio Streaming (3,059 lines)
- **Files**:
  - `Services/AudioService.cs` (1,875 lines)
  - `Services/AudioProtocol.cs` (672 lines)
  - `Services/JitterBuffer.cs` (239 lines)
  - `Services/RtcpProtocol.cs` (148 lines)
  - `Services/LibOpusAudioCodec.cs` (125 lines)
  - `UI/Audio/AudioPage.xaml` & `AudioPage.xaml.cs` (~750 lines)
  - `WDCableWUI.Tests/AudioProtocolTests.cs` (416 lines)
  - `WDCableWUI.Tests/JitterBufferTests.cs` (167 lines)
- **Third-Party Dependencies to Drop**:
  - `NAudio.Wasapi` (2.3.0)
  - `OpusSharp` (1.6.8)
- **Reason for Removal**: Audio capture, Opus compression, and RTP/UDP streaming have no relationship to transferring files between laptops. Dropping this eliminates third-party native C++ binaries (`opus.dll`) and over 3,000 lines of code.

### B. Network Speed Benchmark (1,312 lines)
- **Files**:
  - `Services/SpeedTestService.cs` (729 lines)
  - `UI/SpeedTest/SpeedTestPage.xaml` & `SpeedTestPage.xaml.cs` (~583 lines)
- **Reason for Removal**: A benchmark tool transmitting throwaway bytes over the bulk socket is unneeded in a dedicated file-sharing app.

### C. In-App Text Chat (858 lines)
- **Files**:
  - `Services/ChatService.cs` (225 lines)
  - `Services/ChatProtocol.cs` (121 lines)
  - `UI/Chat/ChatPage.xaml` & `ChatPage.xaml.cs` (~512 lines)
  - `WDCableWUI.Tests/ChatProtocolTests.cs` (81 lines)
- **Reason for Removal**: AirFlick is designed for file transfer. Maintaining a text messenger adds dead UI and extra control-frame routing.

---

## 4. UI Code Handoff Boundaries

Because your friends are responsible for the frontend and gesture system:
- **`UI/AirFlick/`**: Leave as the primary stub/reference for your frontend colleagues.
- **`UI/Connection/`**: Retain as a diagnostic/debug screen for pairing before gesture automation is completed.
- **`UI/Settings/`**: Retain for configuring default download directories and theme settings.
- **`MainWindow.xaml`**: Prune navigation entries for removed features (Audio, Chat, SpeedTest) so the UI presents only AirFlick, Connection, and Settings.

