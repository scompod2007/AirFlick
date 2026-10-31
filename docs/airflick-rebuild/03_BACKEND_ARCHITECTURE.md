# AirFlick Rebuild Audit — 03: Backend Architecture

## 1. Architectural Principles of the Cleaned AirFlick Backend

1. **Hardware & UI Independence**: The core AirFlick backend (`AirFlickCommandService`, `AirFlickStateMachine`, `PinAuthorizationService`) does not take dependencies on WinUI 3, XAML, or physical hardware. Gateways abstract session and file streaming.
2. **Deterministic Lifecycle**: Centralized initialization and teardown controlled by `ServiceManager`. Sockets, background worker loops, and event handlers are strictly disposed when a session drops.
3. **Dual-Channel Multiplexing**: Sockets are separated into a low-latency **Control Channel** (JSON metadata, commands, PIN verification) and a high-bandwidth **Bulk Channel** (chunked file streams).
4. **Zero-Knowledge Consent Barrier**: The 3-digit PIN never traverses the network in plaintext. Senders issue a cryptographically random 32-byte nonce, and receivers submit an HMAC-SHA256 proof.

---

## 2. AirFlick Backend Architecture Diagram

```mermaid
flowchart TD
    subgraph External_Consumers["External Integration Boundary (Frontend & Gestures)"]
        UI["AirFlick WinUI 3 Page (Frontend Team)"]
        Gestures["Gesture Process Bridge (Gesture Team)"]
    end

    subgraph Orchestration_Layer["AirFlick Command & Authorization Layer"]
        AFCmd["AirFlickCommandService\n• PrepareFile()\n• RequestRemoteAuthorizationAsync()\n• SendPreparedFileAsync()\n• Cancel()"]
        AFState["AirFlickStateMachine\n(Waiting, FileGrabbed, PinRequired, Transferring)"]
        PinSvc["PinAuthorizationService\n• GenerateThreeDigitPin()\n• ComputeProof() / VerifyProof()\n• 3-Attempt Lockout & 60s Expiry"]
        AuthChan["SessionAuthorizationChannel\n(Maps PIN proofs to TCP Control Channel)"]
    end

    subgraph Transport_Gateways["Abstraction Gateways (Hardware Decoupling)"]
        SessGate["SessionManagerGateway\n(IAirFlickSessionGateway)"]
        TransferGate["FileTransferGateway\n(IAirFlickTransferGateway)"]
    end

    subgraph Core_Transport["Underlying Networking & Transport Engine"]
        SessMgr["SessionManager\n• UDP Rendezvous (Port 8987)\n• 5s Heartbeats & 15s Timeout\n• Control Stream & Bulk Stream"]
        FTSvc["FileTransferService\n• 64 KB Bulk Chunks\n• Streaming SHA-256 Checksum\n• .part File Staging & Move"]
        BulkCodec["BulkProtocol\n• Path Traversal Defense\n• DuplicateSafePath()"]
        WDSvc["WiFiDirectService\n• WinRT Discovery & Pairing\n• Endpoint IP Resolution"]
    end

    subgraph Wire_Layer["Wire Protocol Encoding"]
        ProtoCodec["ProtocolCodec\n(56-byte Big-Endian Binary Framing)"]
    end

    UI --> AFCmd
    Gestures --> AFCmd
    AFCmd --> AFState
    AFCmd --> PinSvc
    AFCmd --> SessGate
    AFCmd --> TransferGate
    AFCmd --> AuthChan

    AuthChan --> SessMgr
    SessGate --> SessMgr
    SessGate --> WDSvc
    TransferGate --> FTSvc

    FTSvc --> SessMgr
    FTSvc --> BulkCodec
    SessMgr --> ProtoCodec
```

---

## 3. End-to-End File Transfer Sequence

```mermaid
sequenceDiagram
    autonumber
    participant A_Cmd as Laptop A: AirFlickCommandService
    participant A_Chan as Laptop A: AuthorizationChannel
    participant A_Sess as Laptop A: SessionManager
    participant B_Sess as Laptop B: SessionManager
    participant B_Chan as Laptop B: AuthorizationChannel
    participant B_UI as Laptop B: Receiver UI / Prompt

    Note over A_Cmd: 1. User/Gesture prepares file: PrepareFile("video.mp4")
    A_Cmd->>A_Cmd: Generate 3-digit PIN "821" & 32-byte Nonce
    A_Cmd->>A_Chan: RequestRemoteAuthorizationAsync(ticket)
    A_Chan->>A_Sess: Send ControlFrame (kind="airflick.auth", op="challenge")
    A_Sess->>B_Sess: TCP 8988 [ControlMessage: Challenge]
    B_Sess->>B_Chan: ControlFrameReceived -> ParseChallenge()
    B_Chan->>B_UI: AuthorizationRequested event (FileName, Size, Nonce)

    Note over B_UI: 2. Laptop B displays prompt or gesticulates PIN "821"
    B_UI->>B_Chan: SubmitPinAsync("821")
    B_Chan->>B_Chan: proof = HMAC-SHA256(key="821", data="airflick.pin.v1"|AuthId|Nonce)
    B_Chan->>B_Sess: Send ControlFrame (kind="airflick.auth", op="proof")
    B_Sess->>A_Sess: TCP 8988 [ControlMessage: Proof]
    A_Sess->>A_Chan: Proof Frame Received -> Deliver to waiter
    A_Chan->>A_Cmd: Deliver Proof

    Note over A_Cmd: 3. Verify Proof with local PIN
    A_Cmd->>A_Cmd: PinAuthorizationService.VerifyProof() -> Authorized!
    A_Cmd->>A_Chan: PublishResultAsync(authorized=true)
    A_Chan->>B_Chan: ControlMessage: Result (authorized=true)

    Note over A_Cmd: 4. Immediate File Streaming over Persistent Bulk Socket
    A_Cmd->>A_Cmd: TryConsume(ticket) -> Single-Use Spent
    A_Cmd->>A_Sess: FileTransferService.SendStreamAsync("video.mp4")
    A_Sess->>B_Sess: TCP 8989 BulkStart [transferId, fileName, size]
    B_Sess->>B_Sess: Create staging container "video.mp4.part"
    loop 64 KB Streaming
        A_Sess->>B_Sess: TCP 8989 BulkChunk [chunkBytes, offset]
        A_Sess->>A_Sess: Update SHA-256
        B_Sess->>B_Sess: Write chunk, Update SHA-256
    end
    A_Sess->>B_Sess: TCP 8989 BulkComplete [declaredSha256]
    B_Sess->>B_Sess: Finalize SHA-256 & Compare
    alt Hash Matches
        B_Sess->>B_Sess: Move "video.mp4.part" -> "video.mp4"
        B_Sess-->>A_Sess: TCP 8988 Ack [transferId, completed, verified=true]
        Note over A_Cmd: Outcome: Success! State returns to Waiting.
    else Hash Mismatch
        B_Sess->>B_Sess: Delete "video.mp4.part"
        B_Sess-->>A_Sess: TCP 8988 Ack [transferId, failed, error="hash_mismatch"]
        Note over A_Cmd: Outcome: Failed! Recover to Waiting.
    end
```

---

## 4. State Machine Reconciled States

`AirFlickStateMachine` manages high-level application states independently of low-level socket connections:
- `Starting`: Initialization.
- `Discovering`: Scanning for nearby laptops.
- `DeviceFound`: Peer discovered.
- `Connecting`: Wi-Fi Direct association in progress.
- `SessionReady`: TCP channels established and heartbeats running.
- `Waiting`: Idle, ready to grab a file.
- `FileGrabbed`: File selected and staged for authorization.
- `PinRequired`: Challenge issued; awaiting peer proof.
- `PinVerified`: Proof validated; ready to stream.
- `Transferring`: File transmitting over Bulk channel.
- `TransferComplete`: Transmission and receiver hash verification finished.
- `Failed`: Error occurred (recovers back to `Waiting` if connection is healthy).

