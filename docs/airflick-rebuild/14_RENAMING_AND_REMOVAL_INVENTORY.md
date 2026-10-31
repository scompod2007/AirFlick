# AirFlick — Renaming and Removal Inventory (Updated)

**Date**: 2026-10-25  
**Version**: 2.0 (Post-Final Rename Audit)

---

## 1. Project & Directory Renames

| Old Path / Name | New Path / Name | Status |
| :--- | :--- | :--- |
| `WDCableWUI.sln` | `AirFlick.sln` | **Migrated** |
| `WDCableWUI.csproj` | `AirFlick.csproj` | **Migrated** |
| `WDCableWUI.Tests/` | `AirFlick.Tests/` | **Migrated** |
| `WDCableWUI.Tests/WDCableWUI.Tests.csproj` | `AirFlick.Tests/AirFlick.Tests.csproj` | **Migrated** |
| `WDCableWUI` (C# Namespace) | `AirFlick` | **Migrated** |
| `WDCableWUI.Protocol` (C# Namespace) | `AirFlick.Protocol` | **Migrated** |
| `WDCableWUI.Services` (C# Namespace) | `AirFlick.Services` | **Migrated** |
| `WDCableWUI.Tests` (C# Namespace) | `AirFlick.Tests` | **Migrated** |
| Single-Instance App Key: `WDCable.MainInstance` | `AirFlick.MainInstance` | **Migrated** |

---

## 2. Frontend Removal Status

| Component | Status | Details |
| :--- | :--- | :--- |
| `MainWindow.xaml` | Minimal Host Retained | Reduced to a clean, empty host window strictly required for WinUI 3 process bootstrap. No navigation or UI controls remain. |
| `MainWindow.xaml.cs` | Minimal Host Retained | Stripped of all page routing, navigation events, and custom titlebar handlers. |
| `App.xaml` & `App.xaml.cs` | Minimal Host Retained | Removed UI themes, custom localization dialogs, and resource dictionaries; initializes backend services on startup. |
| `UI/` (All subfolders) | Excluded from build | Excluded via `<Compile Remove="UI\**\*.cs" />` and `<Page Remove="UI\**\*.xaml" />` in `AirFlick.csproj`. |

---

## 3. Preserved Wire Compatibility & Protocol Values

The following technical values remain unchanged to preserve network wire compatibility:
- **Magic**: `0x57444342` ("WDCB")
- **Protocol Version**: `2`
- **UDP Discovery Port**: `8987`
- **TCP Control Port**: `8988`
- **TCP Bulk Port**: `8989`
- **AppId in Wire Frames**: `"wdcable"` (retained in frame handshake and rendezvous headers so existing peers recognize packet types)
- **Framing Layout**: Big-endian binary header with GUID correlation IDs and chunked payload verification.
