# AirFlick Rebuild Audit — 08: Cleanup Plan

This document defines the safe, staged plan for decoupling and removing unrelated features from the repository.

---

## 1. Safety Rules for Cleanup

1. **Non-Destructive First**: No files will be permanently deleted without creating a backup record.
2. **Compile-Check Every Step**: Modifications to `ServiceManager` and `MainWindow` must not break project compilation or existing tests.
3. **Preserve Third-Party Attributions**: Keep all copyright headers in third-party files.
4. **No Premature Architecture Rewrites**: Do not alter `SessionManager` or `ProtocolCodec` while removing peripheral services.

---

## 2. Staged Decoupling Batches

### Batch 1: Remove Audio Subsystem (Heavy Third-Party Footprint) — [COMPLETED]

### Batch 2: Remove Speed Test Subsystem — [COMPLETED]

### Batch 3: Remove Chat Subsystem — [COMPLETED]

### Batch 4: Application Branding & Project Structure Cleanup — [COMPLETED]
- Propose renaming `WDCableWUI.csproj` $\rightarrow$ `AirFlick.csproj` and `WDCableWUI` namespace $\rightarrow$ `AirFlick`.
- Update assembly metadata and MSIX display name in `Package.appxmanifest`.
- Update `Program.cs` single-instance key from `WDCable.MainInstance` to `AirFlick.MainInstance`.

---

## 3. What Must NOT Be Touched During Cleanup

- **`Protocol/`**: Do not touch `ProtocolCodec.cs`, `ProtocolFrame.cs`, or `ProtocolConstants.cs`.
- **`WiFiDirectService.cs`** & **`SessionManager.cs`**: The core transport engine.
- **`FileTransferService.cs`** & **`BulkProtocol.cs`**: The core bulk file engine.
- **`AirFlickCommandService.cs`**, **`AirFlickStateMachine.cs`**, **`PinAuthorizationService.cs`**, **`AirFlickAuthorizationProtocol.cs`**: The core AirFlick backend.
- **`Assets/`**: Do not delete logo PNGs or icons.

