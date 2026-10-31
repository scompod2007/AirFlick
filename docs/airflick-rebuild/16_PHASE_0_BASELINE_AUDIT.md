# AirFlick — Phase 0: Baseline Verification Audit

**Audit Date**: 2026-10-25  
**Document**: `docs/airflick-rebuild/16_PHASE_0_BASELINE_AUDIT.md`  
**Purpose**: Establish a strict, evidence-based baseline of the repository state after renaming and frontend decommissioning, separating verified facts from unexecuted claims.

---

## 1. Executive Summary

A comprehensive Phase 0 audit was conducted across the AirFlick repository.
- **Solution & Project Structure**: `AirFlick.sln` has been created and references `AirFlick.csproj` and `AirFlick.Tests/AirFlick.Tests.csproj`. Both referenced projects physically exist.
- **Physical Frontend Files**: Frontend directories and XAML files (`UI/Connection/`, `UI/FileTransfer/`, `UI/Settings/`, `UI/AirFlick/`, `UI/Chat/`, `UI/SpeedTest/`, `UI/Audio/`) are **NOT physically deleted** on disk. Instead, their contents were replaced with placeholder comments/tags, and the `UI/` folder is explicitly excluded from compilation via `<Compile Remove="UI\**\*.cs" />` and `<Page Remove="UI\**\*.xaml" />` in `AirFlick.csproj`.
- **Minimal Host Bootstrap**: `MainWindow.xaml` and `App.xaml` remain as minimal WinUI 3 bootstrap files (16 lines each) required to launch the WinUI runtime host process.
- **Backend API Contract**: [`IAirFlickBackendClient`](file:///e:/PROJECT/AirFlick/Services/IAirFlickBackendClient.cs) and [`AirFlickBackendClient`](file:///e:/PROJECT/AirFlick/Services/AirFlickBackendClient.cs) are fully implemented and wire into existing backend singletons (`ServiceManager`, `AirFlickBackend`, `FileTransferService`).
- **Wire & Protocol Compatibility**: Protocol magic is `0x57444342` ("WDCB"), version is `2`, and network ports are `8987` (UDP Discovery), `8988` (TCP Control), and `8989` (TCP Bulk). AppId in wire frames is `"wdcable"` (retained intentionally for peer interoperability).
- **Build & Test Verification Status**: External CLI executions (`dotnet restore`, `dotnet build`, `dotnet test`) are **BLOCKED** in the current agent container due to an environment PATH misconfiguration (`exec: "e:\PROJECT\AirFlick\powershell": executable file not found in %PATH%`). No build or test execution has actually succeeded in this container; claims of passed builds or tests in earlier reports were unverified.

---

## 2. Repository Root & Environment Audit

- **Repository Root**: `e:\PROJECT\AirFlick`
- **Operating System**: Windows (Host OS reported as Windows)
- **Subshell Execution State**: **BLOCKED**.
  - Attempting to invoke commands via the shell runner fails with:
    ```text
    exec: "e:\PROJECT\AirFlick\powershell": executable file not found in %PATH%.
    ```
  - The process runner attempts to locate `powershell` relative to the working directory (`e:\PROJECT\AirFlick\powershell`) instead of resolving `powershell.exe` from `C:\Windows\System32\WindowsPowerShell\v1.0\`.

---

## 3. Restore, Build & Test Results

| Command | Status | Exit Code | Evidence / Notes |
| :--- | :--- | :--- | :--- |
| `dotnet restore AirFlick.sln` | **BLOCKED** | N/A | Subshell PATH cannot resolve `powershell`. Command could not be executed. |
| `dotnet build AirFlick.sln -c Debug` | **BLOCKED** | N/A | Subshell PATH cannot resolve `powershell`. Command could not be executed. |
| `dotnet test AirFlick.Tests\AirFlick.Tests.csproj` | **BLOCKED** | N/A | Subshell PATH cannot resolve `powershell`. Command could not be executed. |

> [!WARNING]
> No claim of successful `.NET` compilation or test suite execution can be made within this automated environment. All such verification must be performed by the developer in an external Windows terminal.

---

## 4. Solution and Project-Reference Audit

- **`AirFlick.sln`** [STATUS: **PASS**]:
  - Physically exists at `e:\PROJECT\AirFlick\AirFlick.sln`.
  - References `Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "AirFlick", "AirFlick.csproj", "{D9EADE62-C8C9-4B1B-A4C1-F596149A1147}"`.
  - References `Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "AirFlick.Tests", "AirFlick.Tests\AirFlick.Tests.csproj", "{64B8C3DD-5F00-4F72-B18B-BDA94712D557}"`.
  - No references to decommissioned `.csproj` files remain in `AirFlick.sln`.
- **`AirFlick.csproj`** [STATUS: **PASS**]:
  - Physically exists at `e:\PROJECT\AirFlick\AirFlick.csproj`.
  - Declares `<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>`.
  - Declares `<RootNamespace>AirFlick</RootNamespace>`.
  - Declares `<AssemblyName>AirFlick</AssemblyName>`.
  - Contains build item removals for `UI\**` and tests.
- **`AirFlick.Tests/AirFlick.Tests.csproj`** [STATUS: **PASS**]:
  - Physically exists at `e:\PROJECT\AirFlick\AirFlick.Tests\AirFlick.Tests.csproj`.
  - Declares `<TargetFramework>net8.0</TargetFramework>`.
  - Declares `<RootNamespace>AirFlick.Tests</RootNamespace>`.
  - Declares `<AssemblyName>AirFlick.Tests</AssemblyName>`.
  - Links source files from `..\Protocol\*.cs` and `..\Services\*.cs`.
- **Decommissioned Files** [STATUS: **PASS**]:
  - `WDCableWUI.csproj` and `WDCableWUI.Tests\WDCableWUI.Tests.csproj` exist as single-line decommissioned tombstone comments to prevent accidental legacy builds.

---

## 5. Physical Frontend Removal Audit

The audit inspected whether presentation-layer files were physically deleted or merely excluded.

| Directory / File | Physical State on Disk | Project Compilation State | Classification |
| :--- | :--- | :--- | :--- |
| `UI/Connection/ConnectionPage.xaml(.cs)` | Present (Placeholder content) | Excluded (`<Compile Remove>`, `<Page Remove>`) | Excluded on disk |
| `UI/FileTransfer/FileTransferPage.xaml(.cs)` | Present (Placeholder content) | Excluded (`<Compile Remove>`, `<Page Remove>`) | Excluded on disk |
| `UI/Settings/SettingsPage.xaml(.cs)` | Present (Placeholder content) | Excluded (`<Compile Remove>`, `<Page Remove>`) | Excluded on disk |
| `UI/AirFlick/AirFlickPage.xaml(.cs)` | Present (Placeholder content) | Excluded (`<Compile Remove>`, `<Page Remove>`) | Excluded on disk |
| `UI/Chat/ChatPage.xaml(.cs)` | Present (Placeholder content) | Excluded (`<Compile Remove>`, `<Page Remove>`) | Excluded on disk |
| `UI/SpeedTest/SpeedTestPage.xaml(.cs)` | Present (Placeholder content) | Excluded (`<Compile Remove>`, `<Page Remove>`) | Excluded on disk |
| `UI/Audio/AudioPage.xaml(.cs)` | Present (Placeholder content) | Excluded (`<Compile Remove>`, `<Page Remove>`) | Excluded on disk |
| `MainWindow.xaml` & `MainWindow.xaml.cs` | Present (16 lines minimal host) | **Compiled** | Intentionally retained minimal WinUI host |
| `App.xaml` & `App.xaml.cs` | Present (14 / 41 lines minimal host) | **Compiled** | Intentionally retained minimal WinUI host |

> [!NOTE]
> Physical directories in `UI/` still exist on disk containing truncated placeholder files. They are excluded from compilation in `AirFlick.csproj`. In a future cleanup phase, the `UI/` folder can be completely deleted from the filesystem once the new UI workspace is initiated.

---

## 6. Stale Names and Legacy References Audit

| Reference String | Location | Classification | Justification |
| :--- | :--- | :--- | :--- |
| `0x57444342` | `Protocol/ProtocolConstants.cs` (line 5) | **Required Protocol Value** | Binary magic for wire framing ("WDCB"). Must be preserved for compatibility with peer nodes. |
| `"wdcable"` | `Protocol/ProtocolConstants.cs` (line 11), `SessionRendezvousPayload.cs` | **Required Protocol Value** | AppId in JSON handshake frames. Modifying breaks wire handshake with existing devices. |
| `"WDCable.ico"` | ``, `AirFlick.csproj` (line 47) | **Asset Identifier** | Application window icon referenced in MSIX content rules. Harmless until brand artwork is provided. |
| `WDCableWUI` | `WDCableWUI.sln`, `WDCableWUI.csproj`, `WDCableWUI.Tests/` | **Decommissioned Files** | Legacy project files kept as tombstones so older tools do not fail silently. |
| `WDCable` | `docs/architecture-audit/`, `docs/airflick-rebuild/` | **Historical Documentation** | Audit trail and provenance documentation. Intentionally retained for historical reference. |
| `WDCableWUI.UI.Chat` | `UI/Chat/ChatPage.xaml` | **Excluded File Content** | Present only in excluded placeholder file. Does not participate in compilation. |
| `WDCableWUI.UI.SpeedTest` | `UI/SpeedTest/SpeedTestPage.xaml` | **Excluded File Content** | Present only in excluded placeholder file. Does not participate in compilation. |

---

## 7. Documentation Accuracy Review

- **Report 14 (`14_RENAMING_AND_REMOVAL_INVENTORY.md`)**:
  - *Claim*: Frontend removed.
  - *Reality*: Frontend is excluded from compilation and replaced with placeholders, but files still physically exist in the `UI/` directory on disk.
- **Report 15 (`15_FINAL_VERIFICATION_REPORT.md`)**:
  - *Claim*: Verification commands provided, but build/test results could not be executed due to environment limitations.
  - *Reality*: Confirmed accurate. The subshell environment cannot run `powershell.exe` due to PATH resolution.
- **Protocol Magic in User Prompt vs Source**:
  - Prompt referenced `0x574342` or `0x57444342`.
  - Source inspection confirmed: `0x57444342` (exact 4-byte ASCII `WDCB`).

---

## 8. Issues Grouped by Severity

### Critical (Must be resolved before deployment)
1. **Unexecuted Compilation Baseline**: The solution cannot be verified via automated CLI in this environment. The user must manually run `dotnet build` and `dotnet test` in a local Windows PowerShell prompt to verify compilation.

### Moderate (Cleanup & hygiene)
1. **Leftover Placeholder Files in `UI/`**: The `UI/` folder still contains 14 placeholder files across 7 subdirectories (`Connection`, `FileTransfer`, `Settings`, `AirFlick`, `Chat`, `SpeedTest`, `Audio`). While excluded from compilation, they should eventually be removed from disk to avoid confusing new frontend developers.
2. **Decommissioned Legacy Files**: `WDCableWUI.sln`, `WDCableWUI.csproj`, and the `WDCableWUI.Tests/` folder remain on disk as tombstoned files.

### Low (Cosmetic & informational)
1. **Asset Naming**: `` retains the legacy name until replaced by the design team.

---

## 9. Recommended Next Steps (Ordered by Dependency)

1. **Local Developer Build & Test Run**:
   Run the following commands in an external Windows terminal from `e:\PROJECT\AirFlick`:
   ```powershell
   dotnet restore AirFlick.sln
   dotnet build AirFlick.sln -c Debug
   dotnet test AirFlick.Tests\AirFlick.Tests.csproj --logger "console;verbosity=normal"
   ```
2. **Physical Disk Deletion of `UI/`**:
   Once the build is verified, physically delete the `UI/` directory and all legacy tombstone files (`WDCableWUI.csproj`, `WDCableWUI.sln`, `WDCableWUI.Tests/`).
3. **Phase 1: Receiver PIN Enforcement & Authorization Security**:
   Enforce strict PIN validation on incoming file transfers in `FileTransferService` before initiating chunk writes.

