# AirFlick Rebuild Audit — 06: Assets and Branding Inventory

## 1. Inventory of Visual and Branding Assets

All application icons and visual resources are stored in `Assets/`.

| File Path | Dimensions / Type | Referenced By | Role / Legacy Branding | Recommendation |
| :--- | :--- | :--- | :--- | :--- |
| `` | Multi-size Windows Icon (.ico) | `WDCableWUI.csproj` (CopyToOutput), Win32 window titlebar | Legacy WDCable icon. Displayed in title bar and taskbar. | **REPLACE LATER**: Keep until AirFlick custom icon is available to avoid breaking build. |
| `Assets/Square44x44Logo.png` | 44x44 PNG | `Package.appxmanifest`, `MainWindow.xaml` (line 33) | Small app icon in navigation titlebar and MSIX package. | **REPLACE LATER**: Required for packaging and titlebar rendering. |
| `Assets/Square44x44Logo.targetsize-24_altform-unplated.png` | 24x24 PNG | `Package.appxmanifest`, Windows Shell | Taskbar and list icon. | **REPLACE LATER**: Keep for MSIX build compliance. |
| `Assets/Square150x150Logo.scale-200.png` | 300x300 PNG | `Package.appxmanifest` | Windows Start Menu medium tile. | **REPLACE LATER**: Keep for MSIX build compliance. |
| `Assets/Wide310x150Logo.scale-200.png` | 620x300 PNG | `Package.appxmanifest` | Windows Start Menu wide tile. | **REPLACE LATER**: Keep for MSIX build compliance. |
| `Assets/SplashScreen.scale-200.png` | Splash PNG | `Package.appxmanifest` | Windows splash screen during process launch. | **REPLACE LATER**: Keep for MSIX build compliance. |
| `Assets/LockScreenLogo.scale-200.png` | Lock screen PNG | `Package.appxmanifest` | Windows lock screen badge. | **REPLACE LATER**: Keep for MSIX build compliance. |
| `Assets/StoreLogo.png` | Store icon PNG | `Package.appxmanifest` | Windows Store logo. | **REPLACE LATER**: Keep for MSIX build compliance. |

> [!CAUTION]
> **Do not delete assets from `Assets/` during cleanup.** The Windows App SDK build system (`Microsoft.Windows.SDK.BuildTools`) and single-project MSIX packager will fail compilation with error `APPX0702 / PRI257` if manifest-referenced logo files are missing. These files should be cosmetically updated or replaced with AirFlick branded imagery in a dedicated branding task, not deleted.

---

## 2. Branding in Manifests and Strings

### A. `Package.appxmanifest`
- Current `DisplayName`: `"WDCable"`
- Current `PublisherDisplayName`: `"JINGCJIE"`
- Current `Identity Name`: `"JINGCJIE.4084573DC88A9"`
- Current `Description`: `"WDCableWUI"`
- **Recommendation**: Update `DisplayName` to `"AirFlick"`, `Description` to `"AirFlick Wireless File Transfer"`, while leaving publisher ID intact until signing certificates are established.

### B. `Strings/en/Resources.resw` and `Strings/zh-CN/Resources.resw`
- Contains UI strings for all features.
- Contains legacy strings:
  - `MainWindow_AppTitle`: `"WDCable"`
  - `MainWindow_Title`: `"WiFi Direct Cable - WDCable"`
  - `Audio_*` strings (lines 550–608)
  - `Chat_*` strings
- **Recommendation**: Update main window titles to `"AirFlick"`. Prune audio and chat strings during feature decoupling.

### C. Legal & Provenance Notices
- `README.md` and `LICENSE`: Already state AirFlick proprietary ownership:
  `Copyright (c) 2026 GURUMOORTHI S AND SANJAY V S. All Rights Reserved.`
- Third-party licenses: When removing `OpusSharp` and `NAudio.Wasapi`, remove associated package notices. Preserve core .NET and Windows App SDK notices.

