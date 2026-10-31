# AirFlick Rebuild Audit — 09: Backend Implementation Roadmap

This roadmap defines the sequential, verifiable engineering tasks required to build the AirFlick backend after the repository cleanup.

---

## Stage 1: Safe Repository Cleanup & Pruning

- **Objective**: Purge unrelated audio, speed test, and chat features while validating that the solution compiles cleanly.
- **Tasks**:
  1. Create a safe, committed Git checkpoint or directory backup.
  2. Execute Batch 1 (Audio subsystem removal & NuGet pruning).
  3. Execute Batch 2 (Speed test subsystem removal).
  4. Execute Batch 3 (Chat subsystem removal).
  5. Validate that `WDCableWUI.Tests` builds and passes all remaining tests.
- **Acceptance Criteria**: The project compiles with zero warnings related to missing audio/chat files; `NAudio` and `OpusSharp` references are gone.

---

## Stage 2: Enforce Receiver-Side Transfer Authorization (SEC-02 Fix)

- **Objective**: Prevent unauthorized peers from staging `.part` files on the receiver's disk.
- **Tasks**:
  1. In `AirFlickBackend.cs`, create `AirFlickIncomingAuthorizationRegistry` to track authorization tickets accepted by the receiver.
  2. Update `FileTransferService.cs` with an authorization delegate (`Func<string, bool> isTransferAuthorized`).
  3. When an incoming `BulkStart` frame arrives, verify with the registry before invoking `File.Create()`. If unauthorized, reject the transfer with an error Ack.
  4. Add unit tests in `WDCableWUI.Tests/` verifying unauthorized `BulkStart` frames are dropped without creating files.
- **Acceptance Criteria**: Incoming transfers without a verified PIN proof are rejected before any file handle is opened.

---

## Stage 3: Wire AirFlick UI Shell to AirFlickBackend (COR-01 Fix)

- **Objective**: Ensure the UI page exercises the authoritative `AirFlickCommandService`.
- **Tasks**:
  1. Update `UI/AirFlick/AirFlickPage.xaml.cs` to bind to `AirFlickBackend.Command.StateMachine`.
  2. Implement an authorization modal dialog / prompt in `AirFlickPage.xaml` that displays when `SessionAuthorizationChannel.AuthorizationRequested` fires on the receiver.
  3. Connect the dialog submit button to `SessionAuthorizationChannel.SubmitPinAsync(authId, pin)`.
  4. Connect the sender side PIN display to `AirFlickCommandService.AuthorizationRequested`.
- **Acceptance Criteria**: Running two instances of the app allows a user on Laptop A to select a file, prompts Laptop A with a PIN, prompts Laptop B to enter the PIN, and automatically transfers the file upon confirmation.

---

## Stage 4: Formalize the Gesture Integration Contract

- **Objective**: Define a stable C# interface and data contract for the future gesture recognition subsystem.
- **Tasks**:
  1. Create `Services/Gesture/IGestureReceiver.cs`:
     ```csharp
     public interface IGestureReceiver
     {
         Task OnGestureDetectedAsync(GestureType gesture, double confidence);
     }
     ```
  2. Create `Services/Gesture/GestureCommandRouter.cs` mapping:
     - `GestureType.Grab` $\rightarrow$ `AirFlickCommandService.PrepareFile()`
     - `GestureType.Flick` $\rightarrow$ `AirFlickCommandService.SendPreparedFileAsync()`
     - `GestureType.PinDigit` $\rightarrow$ PIN buffer entry.
  3. Document the JSON schema expected from the gesture engine (`{"event": "grab", "confidence": 0.95}`).
- **Acceptance Criteria**: A mock gesture generator class can trigger a file transfer through the router without touching UI controls.

---

## Stage 5: Multi-Device Verification

- **Objective**: Verify Wi-Fi Direct association, persistent sockets, and transfer integrity across two physical Windows 10/11 laptops.
- **Tasks**:
  1. Test Wi-Fi Direct association across Intel and Realtek wireless adapters.
  2. Validate 10 sequential file transfers over the same persistent connection without disconnects.
  3. Verify file hash integrity for files up to 2 GB.
- **Acceptance Criteria**: Transfers complete reliably; connection remains alive after completion; incorrect PIN entry cancels transfer without dropping the connection.

