# AirFlick Architecture Audit — Repository Map & File Inventory

## 1. Source Directory Tree

```
e:\PROJECT\AirFlick
├── Assets/                                 [Application icon and MSIX packaging assets]
│   ├── SplashScreen.scale-200.png
│   ├── LockScreenLogo.scale-200.png
│   ├── Square150x150Logo.scale-200.png
│   ├── Square44x44Logo.scale-200.png
│   ├── Square44x44Logo.targetsize-24_altform-unplated.png
│   ├── Wide310x150Logo.scale-200.png
│   └── WDCable.ico
├── Protocol/                               [Binary framing & protocol definitions]
│   ├── ProtocolChannel.cs                  (34 lines - Control, Bulk, Audio channels)
│   ├── ProtocolCodec.cs                    (298 lines - 56-byte binary frame encoder/decoder)
│   ├── ProtocolConstants.cs                (42 lines - Magic 0x57444342, ports 8987-8989, limits)
│   ├── ProtocolError.cs                    (16 lines - Protocol framing error enum)
│   ├── ProtocolException.cs                (15 lines - Protocol parsing exception)
│   ├── ProtocolFrame.cs                    (43 lines - Frame data model)
│   └── ProtocolFrameType.cs                (64 lines - Frame type enum & wire mapping)
├── Services/                               [Core networking, AirFlick engine, and services]
│   ├── AirFlickAuthorizationProtocol.cs    (256 lines - PIN challenge/proof wire format)
│   ├── AirFlickBackend.cs                  (456 lines - Service gateways and session auth channel)
│   ├── AirFlickCommandService.cs           (629 lines - High-level AirFlick workflow orchestrator)
│   ├── AirFlickStateMachine.cs             (219 lines - AirFlick finite state machine)
│   ├── AudioProtocol.cs                    (672 lines - Real-time audio negotiation & framing)
│   ├── AudioService.cs                     (1,875 lines - WASAPI loopback, Opus, RTP/UDP)
│   ├── BulkProtocol.cs                     (253 lines - File transfer metadata & path security)
│   ├── ChatProtocol.cs                     (121 lines - Chat frame serialization)
│   ├── ChatService.cs                      (225 lines - Chat message exchange service)
│   ├── DataManager.cs                      (692 lines - Settings & history persistence)
│   ├── FileTransferService.cs              (1,068 lines - Chunked file sender & receiver with SHA-256)
│   ├── HistoryData.cs                      (82 lines - Chat and transfer history models)
│   ├── JitterBuffer.cs                     (239 lines - Audio jitter playout buffer)
│   ├── LibOpusAudioCodec.cs                (125 lines - Opus native encoder/decoder wrapper)
│   ├── PinAuthorizationService.cs          (296 lines - PIN ticket, HMAC-SHA256 proof engine)
│   ├── RtcpProtocol.cs                     (148 lines - RTCP Sender/Receiver reports)
│   ├── RtpPacket.cs                        (87 lines - RTP packet serialization)
│   ├── ServiceManager.cs                   (641 lines - Central singleton service lifecycle)
│   ├── SessionHandshakeMetadata.cs         (155 lines - Handshake HELLO/ACK parser)
│   ├── SessionManager.cs                   (1,269 lines - Multiplexed TCP session controller)
│   ├── SessionPhase.cs                     (32 lines - Session phase enum)
│   ├── SessionRendezvousPayload.cs         (34 lines - UDP rendezvous payload model)
│   ├── SessionRole.cs                      (30 lines - GroupOwner / Client roles)
│   ├── SessionStateMachine.cs              (87 lines - Session phase transition validation)
│   ├── SessionTransportRole.cs             (40 lines - Listener / Connector roles)
│   ├── SpeedTestService.cs                 (729 lines - Network throughput benchmark service)
│   ├── WiFiDirectAdapterRemovalLauncher.cs (151 lines - Elevated adapter cleanup runner)
│   ├── WiFiDirectCapabilityReport.cs       (118 lines - Netsh wireless capability parser)
│   ├── WiFiDirectCleanupCoordinator.cs     (162 lines - Stale paired device cleanup)
│   ├── WiFiDirectEndpointSelector.cs       (107 lines - IP selection & role negotiation)
│   └── WiFiDirectService.cs                (1,530 lines - WinRT Wi-Fi Direct discovery & pairing)
├── Strings/                                [Localization resources]
│   ├── en/Resources.resw                   (623 lines - English UI text)
│   └── zh-CN/Resources.resw                (623 lines - Chinese UI text)
├── UI/                                     [WinUI 3 XAML Pages & Code-behind]
│   ├── AirFlick/
│   │   ├── AirFlickPage.xaml               (188 lines - AirFlick dedicated UI view)
│   │   └── AirFlickPage.xaml.cs            (415 lines - AirFlick page event wiring)
│   ├── Audio/
│   │   ├── AudioPage.xaml                  (XAML layout for audio streaming)
│   │   └── AudioPage.xaml.cs               (600 lines - Audio UI code-behind)
│   ├── Chat/
│   │   ├── ChatPage.xaml                   (XAML layout for text chat)
│   │   └── ChatPage.xaml.cs                (512 lines - Chat UI code-behind)
│   ├── Connection/
│   │   ├── ConnectionPage.xaml             (254 lines - Wi-Fi Direct connection controls)
│   │   └── ConnectionPage.xaml.cs          (426 lines - Connection UI code-behind)
│   ├── FileTransfer/
│   │   ├── FileTransferPage.xaml           (XAML layout for standard file transfers)
│   │   └── FileTransferPage.xaml.cs        (742 lines - File transfer UI code-behind)
│   ├── Settings/
│   │   ├── SettingsPage.xaml               (XAML layout for settings)
│   │   └── SettingsPage.xaml.cs            (602 lines - Settings UI code-behind)
│   └── SpeedTest/
│       ├── SpeedTestPage.xaml              (XAML layout for speed testing)
│       └── SpeedTestPage.xaml.cs           (583 lines - Speed test UI code-behind)
├── WDCableWUI.Tests/                       [Unit test suite (.NET 8 MSTest)]
│   ├── AirFlickAuthorizationProtocolTests.cs (127 lines - Tests PIN control frame serialization)
│   ├── AirFlickCommandServiceTests.cs      (387 lines - Tests AirFlick grab/auth/transfer workflow)
│   ├── AirFlickStateMachineTests.cs        (160 lines - Tests AirFlick state transitions)
│   ├── AudioProtocolTests.cs               (416 lines - Tests audio negotiation & offers)
│   ├── BulkProtocolTests.cs                (90 lines - Tests file metadata & path sanitization)
│   ├── ChatProtocolTests.cs                (81 lines - Tests chat frame parsing)
│   ├── JitterBufferTests.cs                (167 lines - Tests playout buffering & PLC)
│   ├── PinAuthorizationServiceTests.cs     (171 lines - Tests 3-digit PIN & HMAC proofs)
│   ├── ProtocolCodecTests.cs               (329 lines - Tests 56-byte header codec & roundtrips)
│   ├── SessionStateMachineTests.cs         (53 lines - Tests transport session state flow)
│   ├── WDCableWUI.Tests.csproj             (42 lines - Test project configuration)
│   └── WiFiDirectCapabilityReportTests.cs  (66 lines - Tests netsh capability parsing)
├── App.xaml                                (17 lines - WinUI Application XAML)
├── App.xaml.cs                             (246 lines - WinUI App startup & unhandled error trapping)
├── app.manifest                            (20 lines - Windows application manifest)
├── LICENSE                                 (116 lines - Proprietary software license)
├── MainWindow.xaml                         (180 lines - Main shell window with navigation pane)
├── MainWindow.xaml.cs                      (532 lines - Main shell window navigation & events)
├── Package.appxmanifest                    (60 lines - MSIX application package manifest)
├── Program.cs                              (141 lines - WinMain entry point with single-instance lock)
├── README.md                               (104 lines - AirFlick concept documentation)
└── WDCableWUI.csproj                       (97 lines - Main C# project file)
```

### Excluded Generated / Build Artifact Directories
- `bin/` (Compilation outputs)
- `obj/` (Intermediate build files and XAML code-generators)
- `AppPackages/` (MSIX packaging bundles)
- `.vs/`, `.git/` (IDE and version control metadata)

---

## 2. File-by-File Classification & Assessment

### A. AirFlick Core Files
| File Path | Classification | Responsibility | Calls / Dependencies |
| :--- | :--- | :--- | :--- |
| `Services/AirFlickStateMachine.cs` | **PRESENT AND REFERENCED** | State machine for AirFlick states (`Waiting`, `FileGrabbed`, `PinRequired`, etc.) | Used by `AirFlickCommandService`, `AirFlickPage` |
| `Services/PinAuthorizationService.cs` | **PRESENT AND REFERENCED** | Generates 3-digit PIN, nonces, calculates and verifies HMAC-SHA256 proofs | Used by `AirFlickCommandService`, `AirFlickBackend` |
| `Services/AirFlickAuthorizationProtocol.cs` | **PRESENT AND REFERENCED** | Encodes/decodes `airflick.auth` control frames | Used by `AirFlickBackend`, `AirFlickCommandService` |
| `Services/AirFlickCommandService.cs` | **PRESENT AND REFERENCED** | High-level orchestrator for file preparation, target selection, PIN verification, and transfer | Initialized in `AirFlickBackend.Initialize()` |
| `Services/AirFlickBackend.cs` | **PRESENT AND REFERENCED** | Adapts concrete WinUI services (`WiFiDirectService`, `SessionManager`, `FileTransferService`) to AirFlick gateways | Called by `ServiceManager.InitializeAdditionalServices()` |
| `UI/AirFlick/AirFlickPage.xaml` | **PRESENT AND REFERENCED** | WinUI 3 page UI for AirFlick | Registered in `MainWindow.xaml.cs` (`_pageTypes["AirFlick"]`) |
| `UI/AirFlick/AirFlickPage.xaml.cs` | **PRESENT AND REFERENCED** | Code-behind handling UI events, observing services | Subscribes to `WiFiDirectService`, `SessionManager`, `FileTransferService` |

### B. Missing Files Identified in Concept / Previous Tasks
| File Name | Classification | Evidence & Impact |
| :--- | :--- | :--- |
| `gesture.py` / `main.py` / `*.py` | **MISSING REQUIRED COMPONENT** | No Python gesture recognition scripts exist in the repository. |
| `IGestureProcessManager.cs` | **MISSING REQUIRED COMPONENT** | No interface exists for spawning or managing gesture detection subprocesses. |
| `PythonGestureProcessManager.cs` | **MISSING REQUIRED COMPONENT** | No process lifecycle management for Python or MediaPipe. |
| `GestureCommand.cs` | **MISSING REQUIRED COMPONENT** | No structured gesture event data contract exists. |
| `GestureCommandParser.cs` | **MISSING REQUIRED COMPONENT** | No JSON/line parser for gesture bridge stdout. |
| `GestureCommandRouter.cs` | **MISSING REQUIRED COMPONENT** | No router dispatching gesture commands to `AirFlickCommandService`. |
| `AirFlickIncomingAuthorizationRegistry.cs` | **DUPLICATE / INLINE ALTERNATIVE** | Functionality implemented inline in `AirFlickBackend.cs` (`_incomingChallenges`). |

