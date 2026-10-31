# AirFlick Rebuild Audit — 02: Application Purpose and Features

## 1. What the Original Application (WDCableWUI) Actually Does

The inherited codebase is **WDCableWUI (Wi-Fi Direct Cable Windows UI)**. Its purpose was to act as a multi-functional wireless Swiss-Army knife connecting Windows and Android devices without an external Wi-Fi router.

### Legacy Features Inherited:
1. **Wi-Fi Direct Peer-to-Peer Pairing**: Automatic discovery and pairing using Windows WinRT APIs.
2. **Dual-Channel Multiplexed TCP Transport**:
   - Control Channel (TCP 8988) for heartbeats, metadata, and control messaging.
   - Bulk Channel (TCP 8989) for high-bandwidth data transfers.
3. **Full-Duplex Text Chat**: Real-time messaging between paired devices via `ChatService` and `ChatPage`.
4. **Network Speed Testing**: Benchmark tool sending dummy bulk packets over TCP 8989 to measure Mbps throughput via `SpeedTestService` and `SpeedTestPage`.
5. **Real-time Low-Latency Audio Streaming**: Capturing Windows system audio via WASAPI loopback, encoding into Opus via native libopus, and streaming over RTP/UDP via `AudioService` and `AudioPage`.
6. **Generic Drag-and-Drop File Transfer**: Sending and receiving arbitrary files with progress bars and transfer history via `FileTransferService` and `FileTransferPage`.
7. **Diagnostics & Adapter Management**: Running elevated PowerShell scripts to clean up orphaned Wi-Fi Direct virtual adapters via `WiFiDirectAdapterRemovalLauncher`.

---

## 2. What AirFlick Actually Requires (Backend Scope)

AirFlick has a focused and distinct mission: **High-speed, gesture-authorized file transfer between nearby Windows laptops.**

### In-Scope Backend Responsibilities:
1. **Nearby Peer Discovery**: Identifying other AirFlick laptops via Wi-Fi Direct or local discovery.
2. **Fast Connection Establishment**: Establishing an authenticated, reliable P2P session between Laptop A and Laptop B.
3. **Persistent Session Management**: Keeping the connection alive (via heartbeats) across multiple sequential file transfers without requiring reconnects.
4. **Proximity PIN Authorization Gate**: Enforcing a zero-knowledge, challenge-response HMAC-SHA256 proof exchange before any file is transmitted.
5. **Robust Chunked Streaming & Verification**: Streaming files in 64 KB chunks, verifying declared sizes, calculating streaming SHA-256 integrity hashes, and staging files in temporary `.part` containers before committing.
6. **Backend Command & State Machine**: Exposing an orchestration API (`AirFlickCommandService`) with well-defined states (`Waiting`, `FileGrabbed`, `PinRequired`, `PinVerified`, `Transferring`) to decouple backend logic from UI rendering and gesture recognition.
7. **Clean Integration Boundary for Frontend and Gestures**: Providing high-level asynchronous methods and events that the frontend and gesture teams can consume without touching raw sockets or byte streams.

---

## 3. Feature-by-Feature Classification

| Subsystem / Feature | Original Purpose | AirFlick Role | Classification | Justification / Action |
| :--- | :--- | :--- | :--- | :--- |
| **Wi-Fi Direct Services** (`WiFiDirectService`, `EndpointSelector`) | Discovers and pairs peers via WinRT | Laptop-to-laptop discovery and connection | **KEEP** | Core foundation for offline device-to-device transport without router infrastructure. |
| **Session Transport** (`SessionManager`, `ProtocolCodec`) | Dual-channel TCP sockets + heartbeats | Persistent connection for commands & data | **KEEP** | Highly reliable framing, correlation, and connection maintenance across transfers. |
| **Bulk File Transfer** (`FileTransferService`, `BulkProtocol`) | Streams files with SHA-256 and `.part` staging | Transmits files safely and verifies hashes | **KEEP** | Production-ready file transfer engine with path sanitization and integrity checks. |
| **AirFlick Engine** (`AirFlickCommandService`, `PinAuthorization`) | Orchestrates file grab, PIN, and transfer | Core AirFlick business logic | **KEEP** | The definitive backend orchestration layer for the product. |
| **Audio Streaming** (`AudioService`, `AudioProtocol`, `OpusSharp`) | Real-time system audio streaming | Unrelated to file transfer | **REMOVE CANDIDATE** | Adds 3,000+ lines of complex RTP/UDP code, native Opus C++ binaries, and audio dependencies. |
| **Network Speed Test** (`SpeedTestService`) | Measures network throughput | Unrelated to file transfer | **REMOVE CANDIDATE** | Clutters session multiplexer with dummy speed frames. |
| **Text Chat** (`ChatService`, `ChatProtocol`) | In-app text messaging | Unrelated to file transfer | **REMOVE CANDIDATE** | AirFlick is a file transfer application, not a chat messenger. |
| **App Settings & Persistence** (`DataManager`) | Persists theme, downloads folder, history | Persists user preferences | **KEEP AND REFACTOR** | Essential for remembering target download directory, but chat history code should be purged. |
| **Legacy UI Pages** (`ChatPage`, `AudioPage`, `SpeedTestPage`) | Views for legacy features | Unrelated to AirFlick | **REMOVE CANDIDATE** | Should be removed from navigation to eliminate clutter. |
| **AirFlick UI Shell** (`AirFlickPage`) | Prototype page for AirFlick | Frontend responsibility | **PRESERVE STUB / ADAPT** | Retain as reference or initial harness for frontend team. |

