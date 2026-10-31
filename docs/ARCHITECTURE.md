# AirFlick Architecture

## 1. System High-Level Design

AirFlick delivers offline peer-to-peer file transfer coupled with a computer vision gesture engine for natural user interactions.

```text
+-------------------------------------------------------------+
|                      WinUI 3 Front-End                      |
| (MainWindow.xaml, MainViewModel, DispatcherQueue)           |
+------------------------------+------------------------------+
                               |
            +------------------+------------------+
            |                                     |
+-----------v------------+            +-----------v------------+
|  Gesture Bridge Service|            |  P2P Session Manager   |
| (IPC JSON over Stdout) |            | (AirFlickBackendClient)|
+-----------+------------+            +-----------+------------+
            |                                     |
+-----------v------------+            +-----------v------------+
| Python MediaPipe Engine|            | Windows Wi-Fi Direct   |
| (OpenCV + Tasks API)   |            | & Bulk TCP Socket Strm |
+------------------------+            +------------------------+
```

## 2. Component Breakdown

### 2.1 Front-End UI Layer (`MainWindow` & `MainViewModel`)
- Built using **WinUI 3 / Windows App SDK 1.7**.
- MVVM design pattern with asynchronous commands and thread-safe UI updates dispatched via `DispatcherQueue`.
- Features real-time connection state banners, dynamic progress bars, camera preview status indicator, and fallback buttons for manual operations.

### 2.2 Gesture Bridge Subsystem (`Services/GestureProcessLauncher.cs`)
- Manages the lifecycle of the Python child process (`gesture/main_gesture_service.py`).
- Captures newline-delimited JSON messages on `stdout` (`{"gesture": "GRAB", "confidence": 0.95}`).
- Implements exponential backoff restart policies, automatic path discovery across virtual environments and system PATHs, and pipe buffer flushing.

### 2.3 Wi-Fi Direct & Networking (`Services/WiFiDirectService.cs` & `FileTransferService.cs`)
- Uses Windows `Windows.Devices.WiFiDirect` APIs for device advertisement, peer discovery, and connection negotiation.
- Establishes a TCP control socket on port **8987** for message exchange and PIN authentication.
- Establishes a dedicated high-throughput TCP bulk stream socket on port **8988** for chunked binary file transfers.
- Computes SHA-256 hashes incrementally during read/write operations to guarantee payload integrity.

### 2.4 Cryptographic PIN Authorization (`Services/PinAuthorizationService.cs`)
- Generates 3-digit numeric codes with each digit constrained strictly to `[1, 5]`.
- Enforces an immutable **120-second monotonic ticket lifetime**.
- Verifies proofs using `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals` to prevent side-channel timing attacks.

