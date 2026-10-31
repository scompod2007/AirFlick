# AirFlick Architecture Audit — File Evidence Index

This index maps every major architectural conclusion, design claim, and defect identified in this audit directly to verified source code line numbers in the repository.

---

## 1. Application Lifecycle & Startup Evidence

| Topic / Conclusion | File Path | Line Range | Evidence Description |
| :--- | :--- | :--- | :--- |
| Single Instance Enforcement | `Program.cs` | L38–L50 | `AppInstance.FindOrRegisterForKey("WDCable.MainInstance")` |
| Application Startup & Error Log | `App.xaml.cs` | L30–L65 | Initializes crash trapping, theme, localization, calls `ServiceManager.Initialize()` |
| Wireless Driver Capability Probe | `Services/ServiceManager.cs` | L214–L275 | Runs `netsh.exe wlan show wirelesscapabilities` and checks P2P Discovery |
| AirFlick Backend Initializer | `Services/ServiceManager.cs` | L380–L385 | Calls `AirFlickBackend.Initialize(_wifiDirectService, _sessionManager, _fileTransferService)` |
| AirFlick Page Registration | `MainWindow.xaml.cs` | L50–L70 | Registers `"AirFlick" -> typeof(AirFlickPage)` in `_pageTypes` navigation map |

---

## 2. Networking & Protocol Evidence

| Topic / Conclusion | File Path | Line Range | Evidence Description |
| :--- | :--- | :--- | :--- |
| Magic Number & Default Ports | `Protocol/ProtocolConstants.cs` | L5–L16 | Magic `0x57444342`, Ports `8987` (Rendezvous), `8988` (Control), `8989` (Bulk) |
| Binary 56-Byte Header Codec | `Protocol/ProtocolCodec.cs` | L12–L40 | Binary Big-Endian header encoding with stream ID, sequence, GUID correlation |
| Group Owner vs Client IP Selection| `Services/WiFiDirectEndpointSelector.cs` | L18–L50 | Compares IPv4 byte order: lower IP becomes Group Owner |
| Multiplexed TCP Sockets | `Services/SessionManager.cs` | L180–L210 | Manages Control and Bulk TCP streams with 5s heartbeat and 15s timeout |
| Streaming SHA-256 Checksum | `Services/FileTransferService.cs` | L140–L185 | Computes incremental SHA-256 across chunks and checks against declared hash |
| Path Traversal Sanitization | `Services/BulkProtocol.cs` | L30–L65 | `SafeFileName` strips traversal `..` and sanitizes reserved names (`CON`, `PRN`, `AUX`) |

---

## 3. AirFlick Orchestration & PIN Authorization Evidence

| Topic / Conclusion | File Path | Line Range | Evidence Description |
| :--- | :--- | :--- | :--- |
| AirFlick State Machine Transitions | `Services/AirFlickStateMachine.cs` | L15–L45 | Defines states `FileGrabbed`, `PinRequired`, `PinVerified`, `Transferring`, etc. |
| 3-Digit PIN Format | `Services/PinAuthorizationService.cs` | L68–L72, L93–L97 | `PinLength = 3`, `RandomNumberGenerator.GetInt32(0, 1000).ToString("D3")` |
| HMAC-SHA256 Proof Domain | `Services/PinAuthorizationService.cs` | L73, L120–L135 | `ProofDomain = "airflick.pin.v1"`; calculates proof from `key = PIN, data = Domain|AuthId|Nonce` |
| 3-Attempt Lockout | `Services/PinAuthorizationService.cs` | L70, L240–L255 | `DefaultMaxAttempts = 3`; returns `TooManyAttempts` when exceeded |
| Single-Use Ticket Consumption | `Services/PinAuthorizationService.cs` | L48–L50, L160–L175 | `TryConsume(authId)` returns true only once per authorized ticket |
| Control Frame Auth Wire Format | `Services/AirFlickAuthorizationProtocol.cs`| L35–L55 | Frames carry `kind = "airflick.auth"`, ops `challenge`, `proof`, `result` |
| Command Orchestration | `Services/AirFlickCommandService.cs` | L210–L244 | `PrepareFile()`, `RequestAuthorization()`, `SendPreparedFileAsync()` |

---

## 4. Evidence of Missing Gesture Subsystem

| Topic / Conclusion | File Path | Line Range | Evidence Description |
| :--- | :--- | :--- | :--- |
| Zero Python Scripts in Repository | Global Search | All Folders | No `.py` files exist in root or subdirectories |
| MediaPipe Placeholder Border | `UI/AirFlick/AirFlickPage.xaml` | L160–L183 | Explicit XML comment: `placeholder for future MediaPipe` |
| Temporary Manual Buttons | `UI/AirFlick/AirFlickPage.xaml` | L144–L156 | `SELECT FILE` and `SEND` marked as temporary stand-ins for GRAB and FLICK |
| Page Code-Behind Manual Stand-ins | `UI/AirFlick/AirFlickPage.xaml.cs`| L19–L23 | Explicit docstring explaining manual buttons stand in for Phases 4/5 gestures |
| Missing C# Process Manager | Global Search | `Services/` | No `IGestureProcessManager` or `PythonGestureProcessManager` exists |

