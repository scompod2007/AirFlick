# AirFlick Architecture Audit — Implementation Roadmap

This staged roadmap outlines the engineering steps required to transition AirFlick from its current state (a manual Wi-Fi Direct file transfer tool with a backend command stub) into a verified, gesture-driven application.

---

## Stage 1: Align AirFlick UI with AirFlickBackend

### Objective
Wire `UI/AirFlick/AirFlickPage.xaml.cs` to consume `AirFlickBackend.Command` rather than its own local `AirFlickStateMachine`.

### Tasks
1. Update `AirFlickPage.xaml.cs` to bind to `AirFlickBackend.Command.StateMachine`.
2. Replace direct calls to `FileTransferService.SendFileAsync` with `AirFlickBackend.Command.PrepareFile()` and `AirFlickBackend.Command.RequestRemoteAuthorizationAsync()`.
3. Add a PIN entry / display prompt in `AirFlickPage.xaml` hooked to `AirFlickBackend.Authorization.AuthorizationRequested` on the receiver side.
4. Gate `FileTransferService` incoming `BulkStart` frames with receiver-side verification before staging files on disk.

### Acceptance Criteria
- Two laptops running the UI can initiate a transfer with manual clicks, observe the 3-digit PIN displayed on Laptop A, enter the PIN on Laptop B, and complete the transfer over the persistent session.

---

## Stage 2: Develop the Python Gesture Recognition Engine

### Objective
Create an independent, production-ready Python vision module using OpenCV and MediaPipe.

### Tasks
1. Create a `gestures/` directory containing:
   - `detector.py`: Hand landmark extractor using MediaPipe Hands.
   - `classifier.py`: Algorithmic gesture recognition:
     - **GRAB**: Transition from open hand to closed fist.
     - **FLICK**: Rapid directional acceleration of wrist landmark exceeding a threshold velocity vector.
     - **PIN COUNT**: Finger-up counting heuristic (thumb, index, middle, ring, pinky).
   - `bridge_runner.py`: CLI entry point emitting structured JSON events to `sys.stdout` (`{"event": "grab", "timestamp": ...}`).
2. Implement debouncing and confidence hysteresis to eliminate false triggers.
3. Support headless and preview modes (so camera frames can be streamed or rendered).

### Acceptance Criteria
- Running `python bridge_runner.py` in a terminal and performing gestures outputs valid, clean JSON events to stdout without crashing or frame leaks.

---

## Stage 3: Build the C# Python Gesture Bridge

### Objective
Integrate the Python gesture engine into the .NET runtime via an asynchronous IPC bridge.

### Tasks
1. Implement `IGestureProcessManager` and `PythonGestureProcessManager.cs`:
   - Locate Python executable (virtual environment or PATH).
   - Spawn Python detector process with redirected `StandardOutput` and `StandardError`.
   - Monitor process lifetime, auto-restart on unexpected crashes, clean termination on app shutdown.
2. Implement `GestureCommandParser.cs`:
   - Non-blocking async stream reader processing stdout lines.
   - Parses JSON into strongly-typed `GestureEvent` objects.
3. Implement `GestureCommandRouter.cs`:
   - Maps `GRAB` $\rightarrow$ `AirFlickCommandService.PrepareFile(selectedFile)`.
   - Maps `FLICK` $\rightarrow$ `AirFlickCommandService.SendPreparedFileAsync()`.
   - Maps finger gestures to PIN submission.

### Acceptance Criteria
- Gesturing in front of Laptop A's camera triggers `AirFlickCommandService` actions automatically without manual button clicks.

---

## Stage 4: Live Camera Preview in WinUI

### Objective
Replace the `CameraPreviewPlaceholder` in `AirFlickPage.xaml` with a live visual feed.

### Tasks
1. Stream camera frames from the vision process (via shared memory, named pipe, or local MJPEG stream) or capture directly using `Windows.Media.Capture.MediaCapture`.
2. Overlay detected hand skeleton/landmarks on the preview border in `AirFlickPage.xaml`.

### Acceptance Criteria
- The user sees real-time video feedback with tracking points in the AirFlick page.

---

## Stage 5: Multi-Device Integration & Field Verification

### Objective
Validate end-to-end operation across two physical Windows 10/11 laptops.

### Tasks
1. Test Wi-Fi Direct association across various wireless chipset vendors (Intel, Realtek, Qualcomm).
2. Verify persistent connection maintenance across sequential file transfers.
3. Test edge-case recovery: laptop lid closed, camera obstruction, PIN entry timeout, Wi-Fi Direct adapter drop.

