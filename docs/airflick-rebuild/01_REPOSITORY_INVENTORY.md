# AirFlick Rebuild Audit — 01: Repository Inventory

## 1. Executive Summary & Repository Metadata
- **Repository Path**: `e:\PROJECT\AirFlick`
- **Application Type**: Windows App SDK (WinUI 3) Desktop Application (.NET 8.0)
- **Target Framework**: `net8.0-windows10.0.19041.0` (Min Target: `10.0.17763.0`)
- **Origin & Provenance**: The codebase originated as `WDCableWUI` (Wi-Fi Direct Cable Windows UI v2.0.1), which integrated multiple P2P utilities (Chat, File Transfer, Audio Streaming, Speed Test). The AirFlick core has been grafted on top of this codebase.
- **Audit Focus**: Dissecting backend components (networking, session management, file transfer, PIN authorization) from legacy features, UI artifacts, and unneeded assets.

---

## 2. Comprehensive Inventory of Source Files

### A. Project & Solution Configuration
| File Relative Path | Lines | Bytes | Role / Responsibility | Calls / Dependencies | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `WDCableWUI.csproj` | 97 | 4,791 | WinUI 3 project manifest. Specifies build properties, single-project MSIX, dependencies. | References `Microsoft.WindowsAppSDK` (1.7.250606001), `Microsoft.Windows.SDK.BuildTools`, `NAudio.Wasapi` (2.3.0), `OpusSharp` (1.6.8). | KEEP AND REFACTOR (Rename project & remove Audio packages) |
| `app.manifest` | 20 | 1,025 | Win32 application manifest declaring PerMonitorV2 DPI awareness and Windows 10/11 OS compatibility. | Required by Windows runtime during process startup. | KEEP |
| `Package.appxmanifest` | 60 | 2,256 | Packaging manifest for MSIX. Declares capabilities: `runFullTrust`, `proximity`, `wiFiControl`, `radios`, `privateNetworkClientServer`. Identity: `JINGCJIE.4084573DC88A9`. | Required for packaged build/deployment. | KEEP AND REFACTOR (Update identity & branding) |

### B. Startup & Shell UI (Frontend Integration Stubs)
| File Relative Path | Lines | Bytes | Role / Responsibility | Calls / Dependencies | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Program.cs` | 141 | 4,491 | WinMain application entry point. Implements single-instance redirection via `AppInstance.FindOrRegisterForKey("WDCable.MainInstance")`. | Calls `WinRT.ComWrappersSupport.InitializeComWrappers()` and `Application.Start()`. | KEEP AND REFACTOR (Update instance key) |
| `App.xaml` & `App.xaml.cs` | 17 / 246 | 667 / 9,868 | WinUI application object. Handles unhandled exceptions (writes `wdcable-crash.log`), initializes theme, localization, and calls `ServiceManager.Initialize()`. | Directly calls `ServiceManager.Initialize()`. Instantiates `MainWindow`. | KEEP AND REFACTOR |
| `MainWindow.xaml` & `.cs` | 180 / 532 | 8,417 / 20,448 | Main navigation window. Houses custom titlebar, Mica backdrop, NavigationView with pages (Connection, Chat, SpeedTest, FileTransfer, Audio, Settings, AirFlick). | Owns `ContentFrame`, checks Wi-Fi Direct readiness, manages navigation. | PRESERVE STUB / REF TO FRONTEND |

### C. Protocol Layer (`Protocol/`)
| File Relative Path | Lines | Bytes | Role / Responsibility | Calls / Dependencies | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Protocol/ProtocolConstants.cs` | 42 | 1,559 | Global protocol constants: Magic `0x57444342` ("WDCB"), Version `2`, HeaderSize `56`, Ports `8987` (Rendezvous), `8988` (Control), `8989` (Bulk), Capabilities. | Consumed by `ProtocolCodec`, `SessionManager`, `AudioProtocol`. | KEEP |
| `Protocol/ProtocolCodec.cs` | 298 | 10,617 | Big-Endian binary framing encoder and decoder for the 56-byte header and variable JSON/binary payloads. | Used across all transport channels (SessionManager, FileTransfer, Chat). | KEEP |
| `Protocol/ProtocolFrame.cs` | 43 | 944 | Immutable record representing a protocol frame (Type, Channel, Flags, StreamId, SequenceNumber, CorrelationId, MetadataJson, Payload). | Core data structure for all socket traffic. | KEEP |
| `Protocol/ProtocolFrameType.cs` | 64 | 2,246 | Frame type enum: `HandshakeHello`, `HandshakeAck`, `HeartbeatPing`, `ControlMessage`, `Ack`, `BulkStart`, `BulkChunk`, `BulkComplete`, `AudioFrame`, etc. | Protocol framing mapping. | KEEP |
| `Protocol/ProtocolChannel.cs` | 34 | 899 | Channel IDs: `Control = 1`, `Bulk = 2`, `Audio = 3`. | Protocol routing. | KEEP |
| `Protocol/ProtocolError.cs` | 16 | 271 | Protocol parsing error enum (`PartialRead`, `MalformedMagic`, `ProtocolMismatch`, etc.). | Used by `ProtocolCodec` and `ProtocolException`. | KEEP |
| `Protocol/ProtocolException.cs` | 15 | 272 | IOException subclass thrown on framing/codec failures. | Consumed by `ProtocolCodec` and `SessionManager`. | KEEP |

### D. AirFlick Core Services (`Services/`)
| File Relative Path | Lines | Bytes | Role / Responsibility | Calls / Dependencies | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Services/AirFlickCommandService.cs` | 629 | 22,016 | Hardware-independent orchestrator for the AirFlick workflow: `PrepareFile()`, `SelectTarget()`, `RequestAuthorization()`, `RequestRemoteAuthorizationAsync()`, `SendPreparedFileAsync()`. | Uses `IAirFlickSessionGateway`, `IAirFlickTransferGateway`, `IPinAuthorizationService`, `IAirFlickAuthorizationChannel`. | KEEP (Core AirFlick Backend) |
| `Services/AirFlickBackend.cs` | 456 | 16,710 | Gateway adapters and bridge connecting WinUI services (`WiFiDirectService`, `SessionManager`, `FileTransferService`) to `AirFlickCommandService`. Owns `SessionAuthorizationChannel`. | Instantiated in `ServiceManager`. Subscribes to `SessionManager.ControlFrameReceived`. | KEEP (Core AirFlick Backend) |
| `Services/AirFlickStateMachine.cs` | 219 | 6,912 | Thread-safe finite state machine tracking AirFlick workflow states (`Starting`, `Discovering`, `DeviceFound`, `Connecting`, `SessionReady`, `Waiting`, `FileGrabbed`, `PinRequired`, `PinVerified`, `Transferring`, `TransferComplete`, `Failed`). | Consumed by `AirFlickCommandService` and `AirFlickPage`. | KEEP (Core AirFlick Backend) |
| `Services/PinAuthorizationService.cs` | 296 | 9,707 | Proximity PIN authorization gate: generates 3-digit PIN, nonces, computes/verifies HMAC-SHA256 proofs (`ProofDomain = "airflick.pin.v1"`), tracks attempts (3 max) and expiry. | Consumed by `AirFlickCommandService`. | KEEP (Core AirFlick Backend) |
| `Services/AirFlickAuthorizationProtocol.cs` | 256 | 8,962 | Serialization and framing for authorization messages over TCP Control channel using `kind: "airflick.auth"` (`challenge`, `proof`, `result`). | Used by `SessionAuthorizationChannel` and `AirFlickCommandService`. | KEEP (Core AirFlick Backend) |

### E. Networking & Core Transport Services (`Services/`)
| File Relative Path | Lines | Bytes | Role / Responsibility | Calls / Dependencies | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Services/ServiceManager.cs` | 641 | 23,464 | Master singleton service coordinator. Validates Wi-Fi Direct driver capabilities via netsh, initializes singletons, and coordinates graceful teardown. | Owns `WiFiDirectService`, `SessionManager`, `DataManager`, feature services, and `AirFlickBackend`. | KEEP AND REFACTOR (Decouple non-AirFlick services) |
| `Services/WiFiDirectService.cs` | 1,530 | 62,127 | Manages WinRT `WiFiDirectAdvertisementPublisher`, `WiFiDirectWatcher`, incoming connection pairing prompts, and outbound connections. | Used by `ServiceManager` and `SessionManager`. | KEEP |
| `Services/SessionManager.cs` | 1,269 | 44,807 | Multiplexed transport manager. Handles UDP rendezvous (port 8987), dual TCP connections (Control 8988, Bulk 8989), capability negotiation, and 5s heartbeats. | Core connection manager for all features. | KEEP |
| `Services/FileTransferService.cs` | 1,068 | 42,233 | Bulk file transfer engine. Handles chunked streaming (64 KB), streaming SHA-256 calculation, `.part` staging files, and completion Acks. | Uses `SessionManager` on Bulk channel. | KEEP |
| `Services/BulkProtocol.cs` | 253 | 8,189 | Bulk framing protocol, metadata serialization, `SafeFileName` path traversal defense, and `DuplicateSafePath` collision avoidance. | Used by `FileTransferService`. | KEEP |
| `Services/DataManager.cs` | 692 | 22,409 | Manages local application settings, theme, download folder location, and JSON-based history. | Used by `ServiceManager`, `MainWindow`, `FileTransferPage`. | KEEP AND REFACTOR |
| `Services/WiFiDirectCapabilityReport.cs` | 118 | 3,596 | Parses output of `netsh wlan show wirelesscapabilities` to verify P2P Device Discovery and Wi-Fi Direct GO/Client support. | Used by `ServiceManager.ValidateWiFiDirectSupport()`. | KEEP |
| `Services/WiFiDirectEndpointSelector.cs` | 107 | 3,365 | Resolves IP address pairs from Wi-Fi Direct link and assigns Group Owner / Client roles based on byte comparison. | Used by `SessionManager`. | KEEP |
| `Services/SessionStateMachine.cs` | 87 | 2,605 | Validates state transitions for transport session (`WifiDirectConnected` -> `ConnectingTransport` -> `Handshaking` -> `Ready`). | Used by `SessionManager`. | KEEP |
| `Services/SessionHandshakeMetadata.cs` | 155 | 5,093 | Constructs and validates `HandshakeHello` / `HandshakeAck` frames. | Used by `SessionManager`. | KEEP |
| `Services/SessionRendezvousPayload.cs` | 34 | 1,093 | Constructs JSON payload for UDP port 8987 rendezvous broadcast. | Used by `SessionManager`. | KEEP |
| `Services/SessionPhase.cs` | 32 | 878 | Enum for session transport phases (`Ready`, `Degraded`, `Disconnected`, etc.). | Consumed across `SessionManager`, `AirFlickCommandService`. | KEEP |
| `Services/SessionRole.cs` | 30 | 722 | Enum for Wi-Fi Direct roles (`GroupOwner`, `Client`). | Consumed across `SessionManager`. | KEEP |
| `Services/SessionTransportRole.cs` | 40 | 1,180 | Enum for transport roles (`Listener`, `Connector`). | Consumed across `SessionManager`. | KEEP |
| `Services/WiFiDirectCleanupCoordinator.cs` | 162 | 5,234 | Cleans up stale paired Wi-Fi Direct device entries in Windows device store. | Called during device reset. | KEEP |
| `Services/WiFiDirectAdapterRemovalLauncher.cs` | 151 | 5,667 | Spawns elevated PowerShell process to clean ghost Wi-Fi Direct virtual miniport adapters. | Diagnostic utility. | KEEP / ISOLATE |
| `Services/HistoryData.cs` | 82 | 2,429 | Data contracts for persisted chat and file transfer history. | Used by `DataManager`. | KEEP (File transfer history only) |

### F. Unrelated Legacy Features (`Services/` & `UI/`)
| File Relative Path | Lines | Bytes | Role / Responsibility | Calls / Dependencies | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Services/AudioService.cs` | 1,875 | 63,200 | Real-time audio streaming engine using WASAPI loopback capture, Opus encoding, and RTP/UDP transmission. | References `NAudio.Wasapi`, `OpusSharp`, `JitterBuffer`, `RtcpProtocol`. | REMOVE CANDIDATE |
| `Services/AudioProtocol.cs` | 672 | 22,539 | Audio session negotiation (SDP-like offers/accepts, bitrate quality, payload types). | Used by `AudioService`. | REMOVE CANDIDATE |
| `Services/JitterBuffer.cs` | 239 | 7,095 | Playout delay buffer and packet loss concealment for incoming audio. | Used by `AudioService`. | REMOVE CANDIDATE |
| `Services/LibOpusAudioCodec.cs` | 125 | 3,117 | Native P/Invoke wrapper around libopus. | Used by `AudioService`. | REMOVE CANDIDATE |
| `Services/RtcpProtocol.cs` | 148 | 4,960 | Real-time Transport Control Protocol encoder/decoder for audio jitter/loss feedback. | Used by `AudioService`. | REMOVE CANDIDATE |
| `Services/RtpPacket.cs` | 87 | 2,757 | RTP packet binary header encoder/decoder. | Used by `AudioService`. | REMOVE CANDIDATE |
| `Services/ChatService.cs` | 225 | 7,374 | Text messaging service over TCP Control channel. | Uses `SessionManager`. | ISOLATE / REMOVE CANDIDATE |
| `Services/ChatProtocol.cs` | 121 | 3,650 | Chat JSON frame builder and parser (`kind: "chat"`). | Used by `ChatService`. | ISOLATE / REMOVE CANDIDATE |
| `Services/SpeedTestService.cs` | 729 | 26,868 | TCP throughput testing service (measures upload/download Mbps). | Uses `SessionManager` on Bulk channel. | ISOLATE / REMOVE CANDIDATE |
| `UI/Audio/AudioPage.xaml` & `.cs` | ~150 / 600 | ~6K / 18,547 | UI for audio streaming. | Depends on `AudioService`. | REMOVE CANDIDATE (Unrelated UI) |
| `UI/Chat/ChatPage.xaml` & `.cs` | ~120 / 512 | ~5K / 17,657 | UI for text chat. | Depends on `ChatService`. | REMOVE CANDIDATE (Unrelated UI) |
| `UI/SpeedTest/SpeedTestPage.xaml` & `.cs`| ~140 / 583 | ~6K / 23,358 | UI for speed benchmark. | Depends on `SpeedTestService`. | REMOVE CANDIDATE (Unrelated UI) |
| `UI/FileTransfer/FileTransferPage.xaml` & `.cs`| ~180 / 742 | ~8K / 26,480 | Generic drag-and-drop file transfer UI from WDCable. | Redundant with `AirFlickPage`. | ISOLATE / FRONTEND REFERENCE |
| `UI/Connection/ConnectionPage.xaml` & `.cs` | 254 / 426 | 15,675 / 15,071 | Manual Wi-Fi Direct pairing UI. | Useful reference for frontend team. | PRESERVE STUB / FRONTEND REFERENCE |
| `UI/Settings/SettingsPage.xaml` & `.cs` | ~150 / 602 | ~6K / 23,258 | Settings page for theme, downloads folder, language, adapter reset. | Essential app settings. | PRESERVE STUB / FRONTEND REFERENCE |
| `UI/AirFlick/AirFlickPage.xaml` & `.cs` | 188 / 415 | 10,358 / 15,222 | Dedicated AirFlick page (Phase 2 UI shell). | Frontend team ownership. | PRESERVE STUB / FRONTEND REFERENCE |

### G. Test Suite (`WDCableWUI.Tests/`)
| File Relative Path | Lines | Bytes | Role / Responsibility | Tested Component | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `WDCableWUI.Tests/WDCableWUI.Tests.csproj` | 42 | 2,879 | MSTest .NET 8 test project. Links to Services source files. | Test harness. | KEEP AND REFACTOR |
| `WDCableWUI.Tests/AirFlickCommandServiceTests.cs` | 387 | 14,125 | Unit tests for AirFlick grab/auth/transfer workflow using fakes. | `AirFlickCommandService` | KEEP |
| `WDCableWUI.Tests/AirFlickStateMachineTests.cs` | 160 | 5,497 | Unit tests for AirFlick state machine transitions. | `AirFlickStateMachine` | KEEP |
| `WDCableWUI.Tests/PinAuthorizationServiceTests.cs`| 171 | 6,129 | Unit tests for 3-digit PIN format, HMAC proofs, attempt lockout. | `PinAuthorizationService` | KEEP |
| `WDCableWUI.Tests/AirFlickAuthorizationProtocolTests.cs`| 127 | 4,974 | Unit tests for JSON challenge/proof/result control frames. | `AirFlickAuthorizationProtocol` | KEEP |
| `WDCableWUI.Tests/ProtocolCodecTests.cs` | 329 | 12,439 | Unit tests for 56-byte header codec, Big-Endian encoding, corrupted frames. | `ProtocolCodec` | KEEP |
| `WDCableWUI.Tests/BulkProtocolTests.cs` | 90 | 2,876 | Unit tests for filename sanitization and path traversal defense. | `BulkProtocol` | KEEP |
| `WDCableWUI.Tests/SessionStateMachineTests.cs` | 53 | 1,611 | Unit tests for transport session phase jumps. | `SessionStateMachine` | KEEP |
| `WDCableWUI.Tests/WiFiDirectCapabilityReportTests.cs` | 66 | 2,327 | Unit tests for wireless capability report parser. | `WiFiDirectCapabilityReport` | KEEP |
| `WDCableWUI.Tests/ChatProtocolTests.cs` | 81 | 3,112 | Unit tests for chat control frames. | `ChatProtocol` | REMOVE CANDIDATE WITH CHAT |
| `WDCableWUI.Tests/AudioProtocolTests.cs` | 416 | 18,456 | Unit tests for audio SDP offers and parameters. | `AudioProtocol` | REMOVE CANDIDATE WITH AUDIO |
| `WDCableWUI.Tests/JitterBufferTests.cs` | 167 | 5,460 | Unit tests for audio jitter buffer playout. | `JitterBuffer` | REMOVE CANDIDATE WITH AUDIO |

