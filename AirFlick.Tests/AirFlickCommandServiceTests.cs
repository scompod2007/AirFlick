using Microsoft.VisualStudio.TestTools.UnitTesting;
using AirFlick.Services;

namespace AirFlick.Tests;

[TestClass]
public sealed class AirFlickCommandServiceTests
{
    private sealed class FakeSession : IAirFlickSessionGateway
    {
        public SessionPhase Phase { get; private set; } = SessionPhase.Disconnected;
        public bool IsReady => Phase == SessionPhase.Ready;
        public string? PeerName { get; set; } = "Laptop B";
        public event EventHandler<AirFlickSessionChangedEventArgs>? PhaseChanged;
        public event EventHandler<string>? DeviceDiscovered;

        public void SetPhase(SessionPhase phase)
        {
            Phase = phase;
            PhaseChanged?.Invoke(this, new AirFlickSessionChangedEventArgs(phase, PeerName));
        }

        public void Discover(string name) => DeviceDiscovered?.Invoke(this, name);
        public Task<bool> StartDiscoveryAsync() => Task.FromResult(true);
        public Task<bool> ConnectToDeviceAsync(string deviceName) => Task.FromResult(true);
    }

    private sealed class FakeTransfer : IAirFlickTransferGateway
    {
        public int Calls { get; private set; }
        public bool Succeed { get; set; } = true;
        public TaskCompletionSource<bool>? Gate { get; set; }

        public async Task<AirFlickTransferOutcome> SendFileAsync(string filePath, CancellationToken cancellationToken)
        {
            Calls++;
            if (Gate != null)
            {
                using var registration = cancellationToken.Register(() => Gate.TrySetCanceled());
                await Gate.Task;
            }

            return Succeed
                ? new AirFlickTransferOutcome(true, null, Path.GetFileName(filePath), 1)
                : new AirFlickTransferOutcome(false, "receiver rejected", Path.GetFileName(filePath), 0);
        }
    }

    /// <summary>Stands in for the control-channel authorization exchange (see AirFlickAuthorizationProtocol).</summary>
    private sealed class FakeAuthorizationChannel : IAirFlickAuthorizationChannel
    {
        /// <summary>The PIN the "receiver" will type. Null means the peer never answers.</summary>
        public string? PeerPin { get; set; }

        public string? Error { get; set; }

        public AirFlickAuthorizationChallenge? LastChallenge { get; private set; }

        public bool? LastPublishedVerdict { get; private set; }

        public Task<AirFlickAuthorizationResponse> RequestProofAsync(
            AirFlickAuthorizationChallenge challenge,
            CancellationToken cancellationToken)
        {
            LastChallenge = challenge;
            if (PeerPin == null)
            {
                return Task.FromResult(new AirFlickAuthorizationResponse(null, Error ?? "no answer"));
            }

            var proof = PinAuthorizationService.ComputeProof(PeerPin, challenge.AuthorizationId, challenge.Nonce);
            return Task.FromResult(new AirFlickAuthorizationResponse(proof, null));
        }

        public Task PublishResultAsync(string authorizationId, bool authorized, string? message)
        {
            LastPublishedVerdict = authorized;
            return Task.CompletedTask;
        }
    }

    private static string TempFile()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "airflick");
        return path;
    }

    private static (AirFlickCommandService Service, FakeSession Session, FakeTransfer Transfer) Create(bool ready = true)
    {
        var session = new FakeSession();
        var transfer = new FakeTransfer();
        var service = new AirFlickCommandService(session, transfer, new PinAuthorizationService());
        if (ready)
        {
            session.SetPhase(SessionPhase.Ready);
        }

        return (service, session, transfer);
    }

    private static void Authorize(AirFlickCommandService service, string file)
    {
        Assert.IsTrue(service.PrepareFile(file).Success);
        var request = service.RequestAuthorization();
        Assert.IsTrue(request.Success);
        Assert.IsTrue(service.VerifyPin(request.Ticket!.Pin).Success);
    }

    [TestMethod]
    public void BackendKnowsSessionIsReadyWithoutAnyUi()
    {
        var session = new FakeSession();
        session.SetPhase(SessionPhase.Ready);

        var service = new AirFlickCommandService(session, new FakeTransfer(), new PinAuthorizationService());

        Assert.AreEqual(AirFlickState.Waiting, service.State);
    }

    [TestMethod]
    public async Task CannotSendWhenSessionIsNotReady()
    {
        var (service, _, transfer) = Create(ready: false);
        var file = TempFile();

        Assert.AreEqual(AirFlickCommandStatus.NotReady, service.PrepareFile(file).Status);
        var result = await service.SendPreparedFileAsync();

        Assert.AreEqual(AirFlickCommandStatus.NotReady, result.Status);
        Assert.AreEqual(0, transfer.Calls);
    }

    [TestMethod]
    public async Task CannotSendWithoutAuthorization()
    {
        var (service, _, transfer) = Create();
        var file = TempFile();
        Assert.IsTrue(service.PrepareFile(file).Success);

        var noRequest = await service.SendPreparedFileAsync();
        Assert.AreEqual(AirFlickCommandStatus.NotAuthorized, noRequest.Status);

        service.RequestAuthorization();
        var unverified = await service.SendPreparedFileAsync();
        Assert.AreEqual(AirFlickCommandStatus.NotAuthorized, unverified.Status);

        Assert.AreEqual(0, transfer.Calls);
    }

    [TestMethod]
    public async Task WrongPinDoesNotAuthorize()
    {
        var (service, _, transfer) = Create();
        var file = TempFile();
        service.PrepareFile(file);
        var pin = service.RequestAuthorization().Ticket!.Pin;

        var wrong = service.VerifyPin(pin == "000" ? "001" : "000");
        var send = await service.SendPreparedFileAsync();

        Assert.AreEqual(AirFlickCommandStatus.AuthorizationFailed, wrong.Status);
        Assert.AreEqual(AirFlickCommandStatus.NotAuthorized, send.Status);
        Assert.AreEqual(0, transfer.Calls);
    }

    [TestMethod]
    public async Task CanSendWhenReadyAndAuthorizedAndAuthorizationIsSingleUse()
    {
        var (service, _, transfer) = Create();
        var file = TempFile();
        Authorize(service, file);

        var result = await service.SendPreparedFileAsync();

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, transfer.Calls);
        Assert.AreEqual(AirFlickState.Waiting, service.State);

        // The prepared file and its authorization were spent.
        Assert.AreEqual(AirFlickCommandStatus.NoFilePrepared, (await service.SendPreparedFileAsync()).Status);
    }

    [TestMethod]
    public async Task SessionRemainsReusableAfterTransfer()
    {
        var (service, session, transfer) = Create();

        for (var i = 0; i < 3; i++)
        {
            Authorize(service, TempFile());
            Assert.IsTrue((await service.SendPreparedFileAsync()).Success);
            Assert.IsTrue(session.IsReady);
            Assert.AreEqual(AirFlickState.Waiting, service.State);
        }

        Assert.AreEqual(3, transfer.Calls);
    }

    [TestMethod]
    public async Task FailedTransferRecoversAndNextTransferWorks()
    {
        var (service, _, transfer) = Create();
        transfer.Succeed = false;
        Authorize(service, TempFile());

        var failed = await service.SendPreparedFileAsync();

        Assert.AreEqual(AirFlickCommandStatus.TransferFailed, failed.Status);
        Assert.AreEqual(AirFlickState.Waiting, service.State);

        transfer.Succeed = true;
        Authorize(service, TempFile());
        Assert.IsTrue((await service.SendPreparedFileAsync()).Success);
    }

    [TestMethod]
    public async Task ConnectionLossDuringTransferEndsInDiscoveringNotStuckFailed()
    {
        var (service, session, transfer) = Create();
        transfer.Gate = new TaskCompletionSource<bool>();
        transfer.Succeed = false;
        Authorize(service, TempFile());

        var sending = service.SendPreparedFileAsync();
        session.SetPhase(SessionPhase.Disconnected);
        transfer.Gate.SetResult(true);
        var result = await sending;

        Assert.AreEqual(AirFlickCommandStatus.TransferFailed, result.Status);
        Assert.AreEqual(AirFlickState.Discovering, service.State);

        session.SetPhase(SessionPhase.Ready);
        Assert.AreEqual(AirFlickState.Waiting, service.State);
    }

    [TestMethod]
    public async Task CancelStopsARunningTransferAndBackendRecovers()
    {
        var (service, _, transfer) = Create();
        transfer.Gate = new TaskCompletionSource<bool>();
        Authorize(service, TempFile());

        var sending = service.SendPreparedFileAsync();
        Assert.IsTrue(service.Cancel().Success);
        var result = await sending;

        Assert.AreEqual(AirFlickCommandStatus.Cancelled, result.Status);
        Assert.AreEqual(AirFlickState.Waiting, service.State);
    }

    [TestMethod]
    public void SessionLossClearsPreparedFile()
    {
        var (service, session, _) = Create();
        service.PrepareFile(TempFile());

        session.SetPhase(SessionPhase.Disconnected);

        Assert.IsNull(service.PreparedFilePath);
        Assert.AreNotEqual(AirFlickState.FileGrabbed, service.State);
    }

    [TestMethod]
    public void SelectTargetMustBeTheConnectedPeer()
    {
        var (service, _, _) = Create();

        Assert.AreEqual(AirFlickCommandStatus.TargetMismatch, service.SelectTarget("Someone Else").Status);
        Assert.IsTrue(service.SelectTarget("laptop b").Success);
    }

    [TestMethod]
    public async Task RemoteAuthorizationOverTheControlChannelAuthorizesAndSends()
    {
        var session = new FakeSession();
        var transfer = new FakeTransfer();
        var channel = new FakeAuthorizationChannel();
        var service = new AirFlickCommandService(
            session,
            transfer,
            new PinAuthorizationService(),
            stateMachine: null,
            authorizationChannel: channel);
        session.SetPhase(SessionPhase.Ready);

        // The local side displays the PIN; the peer's user types it and the proof comes back.
        service.AuthorizationRequested += (_, ticket) => channel.PeerPin = ticket.Pin;

        var file = TempFile();
        Assert.IsTrue(service.PrepareFile(file).Success);
        var authorization = await service.RequestRemoteAuthorizationAsync();

        Assert.IsTrue(authorization.Success);
        Assert.AreEqual(AirFlickState.PinVerified, service.State);
        Assert.IsTrue(channel.LastPublishedVerdict == true);
        Assert.IsNotNull(channel.LastChallenge);
        Assert.AreEqual(Path.GetFileName(file), channel.LastChallenge!.FileName);

        Assert.IsTrue((await service.SendPreparedFileAsync()).Success);
        Assert.AreEqual(1, transfer.Calls);
        Assert.AreEqual(AirFlickState.Waiting, service.State);
    }

    [TestMethod]
    public async Task RemoteAuthorizationWithTheWrongPinDoesNotAuthorize()
    {
        var session = new FakeSession();
        var transfer = new FakeTransfer();
        var channel = new FakeAuthorizationChannel();
        var service = new AirFlickCommandService(
            session,
            transfer,
            new PinAuthorizationService(),
            stateMachine: null,
            authorizationChannel: channel);
        session.SetPhase(SessionPhase.Ready);
        service.AuthorizationRequested += (_, ticket) => channel.PeerPin = ticket.Pin == "000" ? "001" : "000";

        service.PrepareFile(TempFile());
        var authorization = await service.RequestRemoteAuthorizationAsync();

        Assert.AreEqual(AirFlickCommandStatus.AuthorizationFailed, authorization.Status);
        Assert.IsTrue(channel.LastPublishedVerdict == false);
        Assert.AreEqual(AirFlickCommandStatus.NotAuthorized, (await service.SendPreparedFileAsync()).Status);
        Assert.AreEqual(0, transfer.Calls);
    }

    [TestMethod]
    public async Task RemoteAuthorizationWithoutAPeerAnswerFailsAndLeavesTheFilePrepared()
    {
        var session = new FakeSession();
        var channel = new FakeAuthorizationChannel { PeerPin = null, Error = "timed out" };
        var service = new AirFlickCommandService(
            session,
            new FakeTransfer(),
            new PinAuthorizationService(),
            stateMachine: null,
            authorizationChannel: channel);
        session.SetPhase(SessionPhase.Ready);

        service.PrepareFile(TempFile());
        var authorization = await service.RequestRemoteAuthorizationAsync();

        Assert.AreEqual(AirFlickCommandStatus.AuthorizationFailed, authorization.Status);
        Assert.AreEqual("timed out", authorization.Message);

        // The backend recovers: the file stays prepared and a new PIN can be requested.
        Assert.AreEqual(AirFlickState.FileGrabbed, service.State);
        Assert.IsTrue(service.RequestAuthorization().Success);
    }

    [TestMethod]
    public async Task RemoteAuthorizationRequiresAConfiguredChannel()
    {
        var (service, _, _) = Create();
        service.PrepareFile(TempFile());

        var result = await service.RequestRemoteAuthorizationAsync();

        Assert.AreEqual(AirFlickCommandStatus.InvalidState, result.Status);
    }

    [TestMethod]
    public void SelectingTheTargetAfterGrabbingMovesToTargetSelectedAndStillAllowsAuthorization()
    {
        var (service, _, _) = Create();
        Assert.IsTrue(service.PrepareFile(TempFile()).Success);

        Assert.IsTrue(service.SelectTarget("Laptop B").Success);

        Assert.AreEqual(AirFlickState.TargetSelected, service.State);
        Assert.IsTrue(service.RequestAuthorization().Success);
        Assert.AreEqual(AirFlickState.PinRequired, service.State);
    }

    [TestMethod]
    public void DiscoveryEventsReachTheStateMachine()
    {
        var (service, session, _) = Create(ready: false);

        session.Discover("Laptop B");

        Assert.AreEqual(AirFlickState.DeviceFound, service.State);
    }

    [TestMethod]
    public void InitiatePendingShareWorksWhileDisconnected()
    {
        var (service, _, _) = Create(ready: false);
        var file = TempFile();

        var result = service.InitiatePendingShare(file);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Ticket);
        Assert.IsTrue(PinAuthorizationService.IsValidPinFormat(result.Ticket!.Pin));
        Assert.AreEqual(AirFlickState.PinRequired, service.State);
        Assert.IsTrue(service.HasPendingShare);
        Assert.AreEqual(file, service.PreparedFilePath);
    }

    [TestMethod]
    public void InitiatePendingShareRejectsMissingFile()
    {
        var (service, _, _) = Create(ready: false);

        var result = service.InitiatePendingShare("non_existent_file_xyz.txt");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(AirFlickCommandStatus.FileNotFound, result.Status);
        Assert.IsFalse(service.HasPendingShare);
    }

    [TestMethod]
    public void ReplacingPendingShareInvalidatesOldTicket()
    {
        var (service, _, _) = Create(ready: false);
        var file1 = TempFile();
        var file2 = TempFile();

        var first = service.InitiatePendingShare(file1);
        Assert.IsTrue(first.Success);
        var oldTicketId = first.Ticket!.AuthorizationId;

        var second = service.InitiatePendingShare(file2);
        Assert.IsTrue(second.Success);
        var newTicketId = second.Ticket!.AuthorizationId;

        Assert.AreNotEqual(oldTicketId, newTicketId);
        Assert.AreEqual(file2, service.PreparedFilePath);
        Assert.AreEqual(newTicketId, service.CurrentTicket!.AuthorizationId);
    }

    [TestMethod]
    public void CancellingPendingShareCleansUpTicketAndRecovers()
    {
        var (service, _, _) = Create(ready: false);
        var file = TempFile();

        service.InitiatePendingShare(file);
        Assert.IsTrue(service.HasPendingShare);

        var cancel = service.Cancel();

        Assert.IsTrue(cancel.Success);
        Assert.IsFalse(service.HasPendingShare);
        Assert.IsNull(service.PreparedFilePath);
        Assert.AreEqual(AirFlickState.Waiting, service.State);
    }

    [TestMethod]
    public async Task OfflinePendingShareTransfersWhenSessionBecomesReady()
    {
        var session = new FakeSession();
        var transfer = new FakeTransfer();
        var channel = new FakeAuthorizationChannel();
        var service = new AirFlickCommandService(
            session,
            transfer,
            new PinAuthorizationService(),
            stateMachine: null,
            authorizationChannel: channel);

        // Session starts disconnected
        Assert.IsFalse(session.IsReady);

        var file = TempFile();
        var shareResult = service.InitiatePendingShare(file);
        Assert.IsTrue(shareResult.Success);
        Assert.AreEqual(AirFlickState.PinRequired, service.State);

        // Peer enters the PIN shown on sender screen
        channel.PeerPin = shareResult.Ticket!.Pin;

        // Session connects and becomes ready
        session.SetPhase(SessionPhase.Ready);

        // Remote authorization exchanges challenge/proof over the newly ready channel
        var auth = await service.RequestRemoteAuthorizationAsync();
        Assert.IsTrue(auth.Success);
        Assert.AreEqual(AirFlickState.PinVerified, service.State);

        // Transfer executes
        var send = await service.SendPreparedFileAsync();
        Assert.IsTrue(send.Success);
        Assert.AreEqual(1, transfer.Calls);
        Assert.AreEqual(AirFlickState.Waiting, service.State);
    }

    [TestMethod]
    public async Task OfflinePendingShareRejectsIncorrectPinOnConnectedSession()
    {
        var session = new FakeSession();
        var transfer = new FakeTransfer();
        var channel = new FakeAuthorizationChannel();
        var service = new AirFlickCommandService(
            session,
            transfer,
            new PinAuthorizationService(),
            stateMachine: null,
            authorizationChannel: channel);

        var file = TempFile();
        var shareResult = service.InitiatePendingShare(file);
        var pin = shareResult.Ticket!.Pin;

        // Peer submits the wrong PIN
        channel.PeerPin = pin == "000" ? "001" : "000";

        session.SetPhase(SessionPhase.Ready);

        var auth = await service.RequestRemoteAuthorizationAsync();
        Assert.AreEqual(AirFlickCommandStatus.AuthorizationFailed, auth.Status);
        Assert.AreEqual(0, transfer.Calls);

        // Cannot transfer without authorization
        var send = await service.SendPreparedFileAsync();
        Assert.AreEqual(AirFlickCommandStatus.NotAuthorized, send.Status);
        Assert.AreEqual(0, transfer.Calls);
    }

    [TestMethod]
    public void OfflinePendingSharePreservedAcrossDiscoverySessionEvents()
    {
        var session = new FakeSession();
        var service = new AirFlickCommandService(
            session,
            new FakeTransfer(),
            new PinAuthorizationService());

        var file = TempFile();
        service.InitiatePendingShare(file);

        // Discovered devices arrive
        session.Discover("Laptop C");
        session.Discover("Laptop D");

        // Pending share remains intact
        Assert.IsTrue(service.HasPendingShare);
        Assert.AreEqual(file, service.PreparedFilePath);
        Assert.AreEqual(AirFlickState.PinRequired, service.State);
    }

    [TestMethod]
    public void SecondPendingShareAfterCancelGeneratesFreshTicket()
    {
        var session = new FakeSession();
        var pinService = new PinAuthorizationService();
        var service = new AirFlickCommandService(
            session,
            new FakeTransfer(),
            pinService);

        var file1 = TempFile();
        var file2 = TempFile();

        var share1 = service.InitiatePendingShare(file1);
        Assert.IsTrue(share1.Success);
        var authId1 = share1.Ticket!.AuthorizationId;
        Assert.IsTrue(service.HasPendingShare);

        // User or timeout cancels first share
        service.Cancel();
        Assert.IsFalse(service.HasPendingShare);
        Assert.IsNull(service.CurrentTicket);

        // Previous ticket should be cancelled/invalid in pin service
        Assert.IsFalse(pinService.IsAuthorized(authId1));

        // Create second share
        var share2 = service.InitiatePendingShare(file2);
        Assert.IsTrue(share2.Success);
        var authId2 = share2.Ticket!.AuthorizationId;

        // Must be a fresh authorization ID
        Assert.AreNotEqual(authId1, authId2);
        Assert.IsTrue(service.HasPendingShare);
        Assert.AreEqual(file2, service.PreparedFilePath);
    }

    [TestMethod]
    public void ReceiverPinFormatValidationEnforcesThreeDigits()
    {
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat(null));
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat(""));
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("12"));
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("1234"));
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("abc"));
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat(" 12"));
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("12 "));
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("-12"));

        // Valid: exactly 3 digits, all within 1-5 inclusive
        Assert.IsTrue(PinAuthorizationService.IsValidPinFormat("111"));
        Assert.IsTrue(PinAuthorizationService.IsValidPinFormat("123"));
        Assert.IsTrue(PinAuthorizationService.IsValidPinFormat("555"));
        Assert.IsTrue(PinAuthorizationService.IsValidPinFormat("351"));

        // Invalid: digits outside 1-5 (0, 6, 7, 8, 9)
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("000"));
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("106"));
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("560"));
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("678"));
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("999"));
    }

    [TestMethod]
    public void InitiatePendingShareRecoversFromFailedStateMachineState()
    {
        var session = new FakeSession();
        var stateMachine = new AirFlickStateMachine();
        var service = new AirFlickCommandService(
            session,
            new FakeTransfer(),
            new PinAuthorizationService(),
            stateMachine);

        stateMachine.ReportTransferFailed("Previous attempt crashed");
        Assert.AreEqual(AirFlickState.Failed, service.State);

        var file = TempFile();
        var result = service.InitiatePendingShare(file);

        Assert.IsTrue(result.Success, $"Expected success, got {result.Status}: {result.Message}");
        Assert.AreEqual(AirFlickState.PinRequired, service.State);
        Assert.IsTrue(service.HasPendingShare);
    }

    [TestMethod]
    public void CancelRecoversFromFailedStateMachineState()
    {
        var session = new FakeSession();
        var stateMachine = new AirFlickStateMachine();
        var service = new AirFlickCommandService(
            session,
            new FakeTransfer(),
            new PinAuthorizationService(),
            stateMachine);

        stateMachine.ReportTransferFailed("Socket died");
        Assert.AreEqual(AirFlickState.Failed, service.State);

        var cancelResult = service.Cancel();
        Assert.IsTrue(cancelResult.Success);
        Assert.AreEqual(AirFlickState.Waiting, service.State);
        Assert.IsFalse(service.HasPendingShare);
    }
}

