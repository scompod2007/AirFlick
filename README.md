# AirFlick

> **Secure, Gesture-Driven & Offline-First Wireless File Sharing for Windows**

AirFlick is a high-speed peer-to-peer file transfer system built with **WinUI 3 (.NET 8)** and an integrated **Python MediaPipe Gesture Engine**. It enables seamless laptop-to-laptop file sharing over direct Wi-Fi links with intuitive hand gestures and 3-digit PIN authentication.

---

## Table of Contents
1. [Overview](#overview)
2. [Key Features](#key-features)
3. [Architecture](#architecture)
4. [Technology Stack](#technology-stack)
5. [Repository Structure](#repository-structure)
6. [Prerequisites & System Requirements](#prerequisites--system-requirements)
7. [Installation & Setup](#installation--setup)
8. [Building & Running](#building--running)
9. [Gesture Recognition Subsystem](#gesture-recognition-subsystem)
10. [Security & Authentication](#security--authentication)
11. [Testing](#testing)
12. [Known Limitations & Hardware Notes](#known-limitations--hardware-notes)
13. [License](#license)

---

## Overview

Traditional wireless file sharing often depends on local network routers, cloud intermediaries, or cumbersome Bluetooth pairing. AirFlick solves this by establishing direct, high-bandwidth peer-to-peer Wi-Fi connections between devices, complemented by a computer vision pipeline that recognizes natural hand gestures for hands-free workflow initiation and authentication.

```text
+-------------------+           Wi-Fi Direct Peer Discovery          +-------------------+
|                   | <============================================> |                   |
|   Sender Laptop   |                                                |  Receiver Laptop  |
|                   | ---------- 3-Digit PIN Authorization --------> |                   |
|  [MediaPipe / CV] |                                                |  [MediaPipe / CV] |
|                   | ============= Bulk TCP File Stream ==========> |                   |
+-------------------+             (Port 8988, SHA-256)               +-------------------+
```

---

## Key Features

- **Offline-First High-Speed Transfer**: Peer-to-peer Wi-Fi Direct streaming without requiring an external internet connection or local router.
- **MediaPipe Gesture Recognition**:
  - `GRAB` (Fist: all fingers folded) - Selects and stages files for transfer.
  - `DROP` (Thumb pointing down) - Confirms dispatch to the selected receiver.
  - `PIN1` to `PIN5` - Single-hand gesture entry for digits 1 through 5.
  - `CONFIRM` (Thumbs Up) - Confirms pending dialogs or transfers.
- **3-Digit PIN Authentication (Digits 1–5)**:
  - Authorization challenge-response using 120-second monotonic expiry.
  - Uniform random PIN generation restricted to digits 1–5.
  - Constant-time proof comparison to defend against timing attacks.
  - Single-use authorization tickets with a 3-attempt brute-force lockout.
- **Integrity Verification**: End-to-end SHA-256 checksum calculation for streamed files.
- **Modern WinUI 3 Desktop Experience**: Fluent Design interface with real-time transfer progress, camera preview, and fallback manual UI controls.

---

## Architecture

AirFlick adopts a modular architecture separating UI presentation, P2P protocol coordination, and computer vision gesture processing:

```text
               +----------------------------------------+
               |        WinUI 3 UI (MainWindow)        |
               +-------------------+--------------------+
                                   | Data Binding & Commands
               +-------------------v--------------------+
               |       MainViewModel & Dispatcher       |
               +-------------------+--------------------+
                                   |
         +-------------------------+-------------------------+
         |                                                   |
+--------v-------------------+                     +---------v------------------+
|   AirFlickBackendClient    |                     |   GestureProcessLauncher   |
| (Wi-Fi Direct & Transfer)  |                     | (Python Child Process Mgr) |
+--------+-------------------+                     +---------+------------------+
         |                                                   |
         | IPC / Socket Control                              | Stdout JSON Stream
         v                                                   v
+----------------------------+                     +----------------------------+
| WiFiDirectService &        |                     |   main_gesture_service.py  |
| FileTransferService        |                     |   (MediaPipe HandLandmarker)|
+----------------------------+                     +----------------------------+
```

---

## Technology Stack

- **Desktop Framework**: [.NET 8](https://dotnet.microsoft.com/) / [Windows App SDK 1.7](https://learn.microsoft.com/windows/apps/windows-app-sdk/) (WinUI 3).
- **Computer Vision**: Python 3.10+, [OpenCV](https://opencv.org/) (`opencv-python`), Google [MediaPipe](https://developers.google.com/mediapipe) (`mediapipe.tasks.python.vision`).
- **Networking**: Windows Wi-Fi Direct (`Windows.Devices.WiFiDirect`), .NET `TcpClient` / `TcpListener`.
- **Cryptographic Security**: .NET `System.Security.Cryptography` (`HMACSHA256`, `SHA256`, `RandomNumberGenerator`).
- **Testing**: MSTest v3, Python unit tests.

---

## Repository Structure

```text
AirFlick/
├── AirFlick.sln                  # Visual Studio solution file
├── AirFlick.csproj               # Main WinUI 3 project file
├── App.xaml / App.xaml.cs        # Application lifecycle & bootstrap
├── MainWindow.xaml / .cs         # Main application window & UI
├── ViewModels/                   # MVVM presentation logic
│   └── MainViewModel.cs          # UI state, commands, and timers
├── Services/                     # Core services
│   ├── AirFlickAuthorizationProtocol.cs  # PIN challenge-proof negotiation
│   ├── AirFlickCommandService.cs         # Control commands & validation
│   ├── FileTransferService.cs            # TCP bulk file streaming & SHA-256
│   ├── GestureBridgeService.cs          # IPC bridge for gesture stream
│   ├── GestureProcessLauncher.cs        # Process supervisor for Python
│   ├── PinAuthorizationService.cs       # Ticket creation & constant-time check
│   ├── ServiceManager.cs                # Dependency injection & lifecycle
│   └── WiFiDirectService.cs             # Device discovery & P2P pairing
├── Protocol/                     # Wire protocol message definitions
├── Common/                       # Converters and UI helpers
├── Strings/                      # Localized string resources (.resw)
├── gesture/                      # Python Gesture Recognition Subsystem
│   ├── main_gesture_service.py   # OpenCV & MediaPipe runner (stdout stream)
│   ├── gesture_engine.py         # Geometric landmark classifier
│   └── models/                   # MediaPipe models
│       └── hand_landmarker.task  # Pre-trained hand tracking model
├── AirFlick.Tests/               # Comprehensive automated test suite
├── docs/                         # In-depth architectural & setup guides
├── artifacts/                    # Build scripts & packaging helpers
└── LICENSE                       # Proprietary software license
```

---

## Prerequisites & System Requirements

### Runtime Environment
- **Operating System**: Windows 10 (version 1809 / build 17763 or higher) or Windows 11.
- **Architecture**: x64 (recommended) or ARM64.
- **Hardware**: Wi-Fi adapter with Wi-Fi Direct (Virtual Wi-Fi Miniport) support; USB or built-in webcam.

### Development Prerequisites
- **Visual Studio 2022** (v17.8+) with:
  - *.NET Desktop Development* workload.
  - *Windows App SDK C# Templates*.
- **.NET 8 SDK** (v8.0.300+).
- **Python 3.10+** (64-bit) added to system `PATH`.
  - Install dependencies:
    ```powershell
    pip install opencv-python mediapipe
    ```

---

## Installation & Setup

1. **Clone the Repository**:
   ```powershell
   git clone https://github.com/scompod2007/AirFlick.git
   cd AirFlick
   ```

2. **Install Python Gesture Dependencies**:
   ```powershell
   pip install -r gesture/requirements.txt
   ```
   *(or `pip install opencv-python mediapipe`)*

3. **Restore .NET Dependencies**:
   ```powershell
   dotnet restore .\AirFlick.sln
   ```

---

## Building & Running

### Run via Command Line
```powershell
dotnet run --project .\AirFlick.csproj -c Debug -p:Platform=x64
```

### Build Self-Contained Release
```powershell
dotnet publish .\AirFlick.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsPackageType=None -o artifacts\AirFlick-Release\
```

---

## Gesture Recognition Subsystem

The gesture engine runs as an independent child process (`main_gesture_service.py`), analyzing webcam frames using Google MediaPipe Tasks Hand Landmarker.

| Gesture | Landmark Logic | Application Action |
| :--- | :--- | :--- |
| **`GRAB`** | All fingers folded into palm (fist conformation) | Stages selected file for transfer |
| **`DROP`** | Thumb extended downward, four fingers folded | Commits transfer and targets active peer |
| **`PIN1`** | Index extended; Middle, Ring, Pinky folded | Enters PIN digit `1` |
| **`PIN2`** | Index & Middle extended; Ring, Pinky folded | Enters PIN digit `2` |
| **`PIN3`** | Index, Middle, Ring extended; Pinky folded | Enters PIN digit `3` |
| **`PIN4`** | Four fingers extended; Thumb folded across palm | Enters PIN digit `4` |
| **`PIN5`** | All five digits extended (open palm) | Enters PIN digit `5` |
| **`CONFIRM`**| Thumb extended upward, remaining fingers folded | Confirms prompt / transfer request |

*Note: All gesture classifications are stream-debounced over consecutive frames before emitting IPC events to the C# application layer.*

---

## Security & Authentication

- **Restricted PIN Range**: Every digit in the 3-digit PIN is strictly bounded to `[1, 5]`, matching supported one-handed gestures.
- **120-Second Monotonic Expiry**: Transfer authorization tickets expire after exactly 120 seconds, calculated using monotonic timestamps to prevent system clock tampering.
- **Constant-Time Verification**: Proof checks use `CryptographicOperations.FixedTimeEquals` to prevent side-channel timing analysis.
- **Brute-Force Rate Limiting**: Sessions automatically lock out after 3 consecutive failed authorization attempts.
- **Zero Cloud Footprint**: Data travels directly over local peer-to-peer 802.11 frames with end-to-end SHA-256 verification.

---

## Testing

AirFlick includes automated tests covering state machines, PIN authorization, protocol validation, and gesture classifiers.

### Run .NET Unit Tests
```powershell
dotnet test .\AirFlick.Tests\AirFlick.Tests.csproj -c Release
```

### Run Python Gesture Tests
```powershell
python -m unittest discover -s gesture -p "*_test.py"
```

---

## Known Limitations & Hardware Notes

- **Wi-Fi Direct Hardware Driver Sensitivity**: Peer discovery relies on native Windows Wi-Fi Direct APIs (`Windows.Devices.WiFiDirect`). Some older USB Wi-Fi dongles or virtualized network adapters do not support P2P GO/Client negotiation.
- **Camera Access Exclusivity**: Windows permits only one application to access a camera device index at a time. Ensure other applications (Zoom, Teams, Camera app) are closed prior to activating the live gesture bridge.
- **Fallback Controls**: If a webcam is unavailable or lighting conditions impede gesture detection, all operations (PIN entry, transfer staging, confirmation) are fully accessible via standard UI buttons.

---

## License

This software and source code are distributed under the terms of the proprietary software license specified in [LICENSE](LICENSE). All rights reserved.
