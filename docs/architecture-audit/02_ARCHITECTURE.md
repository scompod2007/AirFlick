# AirFlick Architecture Audit — C# Application Architecture

## 1. Application Lifecycle & Startup Sequence

```mermaid
sequenceDiagram
    autonumber
    participant OS as Windows OS
    participant Prog as Program.cs
    participant App as App.xaml.cs
    participant SM as ServiceManager
    participant WD as WiFiDirectService
    participant Sess as SessionManager
    participant Backend as AirFlickBackend
    participant Win as MainWindow.xaml.cs

    OS->>Prog: Main(args)
    Prog->>Prog: InitializeComWrappers()
    Prog->>Prog: RedirectToExistingInstance() (InstanceKey = WDCable.MainInstance)
    Prog->>App: Application.Start -> new App()
    App->>App: InitializeComponent(), Wire UnhandledException
    App->>App: InitializeLocalization(), InitializeTheme()
    App->>SM: ServiceManager.Initialize()
    SM->>SM: Initialize DataManager
    SM->>SM: ValidateWiFiDirectSupport() (netsh capability probe)
    SM->>WD: new WiFiDirectService()
    SM->>Sess: SessionManager.Instance.Initialize(_wifiDirectService)
    SM->>SM: InitializeAdditionalServices() (Chat, SpeedTest, FileTransfer, Audio)
    SM->>Backend: AirFlickBackend.Initialize(_wifiDirectService, _sessionManager, _fileTransferService)
    Backend->>Backend: Create SessionAuthorizationChannel, gateways, AirFlickCommandService
    App->>Win: new MainWindow() -> Activate()
    Win->>Win: Setup CustomTitleBar, Initialize Navigation Items
    Win->>Win: ContentFrame.Navigate(typeof(AirFlickPage)) or ConnectionPage
    Win->>WD: EnsureDiscoverableOnStartupAsync() (Auto-Advertise / Scan)
```

---

## 2. Component Boundaries and Dependency Map

```mermaid
flowchart TD
    subgraph UI_Layer["WinUI 3 Presentation Layer"]
        MainWin["MainWindow.xaml/.cs"]
        AirFlickP["AirFlickPage.xaml/.cs"]
        ConnP["ConnectionPage.xaml/.cs"]
        FileP["FileTransferPage.xaml/.cs"]
        ChatP["ChatPage.xaml/.cs"]
        AudioP["AudioPage.xaml/.cs"]
        SpeedP["SpeedTestPage.xaml/.cs"]
        SetP["SettingsPage.xaml/.cs"]
    end

    subgraph Orchestration_Layer["AirFlick Orchestration Layer"]
        AFBackend["AirFlickBackend (Static Singleton Host)"]
        AFCmd["AirFlickCommandService (Hardware-Independent)"]
        AFState["AirFlickStateMachine"]
        AFAuthChan["SessionAuthorizationChannel"]
        PinSvc["PinAuthorizationService (HMAC-SHA256)"]
        AFAuthProto["AirFlickAuthorizationProtocol (Codec)"]
    end

    subgraph Core_Services["Core Networking & Feature Services"]
        SM["ServiceManager (Lifecycle Coordinator)"]
        WDSvc["WiFiDirectService (WinRT Discovery & P2P)"]
        SessMgr["SessionManager (Multiplexed TCP & Heartbeats)"]
        FTSvc["FileTransferService (Chunked Streamer & Hash Check)"]
        BulkProto["BulkProtocol (Framing & Path Sanitizer)"]
        ChatSvc["ChatService"]
        AudioSvc["AudioService"]
        SpeedSvc["SpeedTestService"]
        DataMgr["DataManager (Settings & JSON History)"]
    end

    subgraph Wire_Protocol["Wire Protocol Layer"]
        ProtoCodec["ProtocolCodec (56-byte Header Binary Codec)"]
        ProtoFrame["ProtocolFrame & FrameType"]
    end

    MainWin --> AirFlickP
    MainWin --> ConnP
    MainWin --> FileP
    MainWin --> SM

    AirFlickP --> SM
    AirFlickP --> WDSvc
    AirFlickP --> SessMgr
    AirFlickP --> FTSvc

    SM --> WDSvc
    SM --> SessMgr
    SM --> FTSvc
    SM --> AFBackend

    AFBackend --> AFCmd
    AFBackend --> AFAuthChan
    AFCmd --> AFState
    AFCmd --> PinSvc
    AFAuthChan --> SessMgr
    AFAuthChan --> AFAuthProto

    SessMgr --> ProtoCodec
    FTSvc --> SessMgr
    FTSvc --> BulkProto
    BulkProto --> ProtoCodec
    AFAuthProto --> ProtoCodec
```

---

## 3. Service Lifecycles, Scope, and Singletons

- **`ServiceManager`**: Global static class. Controls the startup and teardown of all communication singletons via `SemaphoreSlim LifecycleSemaphore = new(1, 1)`.
- **`WiFiDirectService`**: Created once during `ServiceManager.Initialize()`. Owns the native WinRT `WiFiDirectAdvertisementPublisher` and `WiFiDirectWatcher`.
- **`SessionManager`**: Pure singleton accessed via `SessionManager.Instance`. Owns the TCP sockets (Rendezvous UDP 8987, Control TCP 8988, Bulk TCP 8989). Connections are maintained across multiple transfers until explicitly torn down.
- **`AirFlickBackend`**: Static wrapper created inside `ServiceManager.InitializeAdditionalServices()`. Bridges `SessionManager` and `FileTransferService` to `AirFlickCommandService`.
- **`AirFlickCommandService`**: Long-lived instance owned by `AirFlickBackend`. Does not depend on any WinUI page being active.

---

## 4. Teardown and Cleanup Paths

When the application closes or Wi-Fi Direct resets:
1. `MainWindow.xaml.cs` traps the `AppWindow.Closing` event.
2. `ServiceManager.DisposeFeatureServices()` is invoked:
   - Resets `AirFlickBackend` (which calls `AirFlickCommandService.Dispose()`, cancelling pending transfers and unregistering control frame handlers).
   - Resets `ChatService`, `SpeedTestService`, `FileTransferService`, and `AudioService`.
3. `ServiceManager.DisposeWiFiDirectServices()`:
   - Unsubscribes `SessionManager.PeerProtocolMissing`.
   - Calls `SessionManager.ResetInstance()`, terminating TCP listeners and active client sockets.
   - Calls `WiFiDirectService.Dispose()`, stopping advertisements, stopping watchers, and releasing paired device references.

