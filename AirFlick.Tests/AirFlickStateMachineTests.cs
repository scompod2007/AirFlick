using Microsoft.VisualStudio.TestTools.UnitTesting;
using AirFlick.Services;

namespace AirFlick.Tests;

[TestClass]
public sealed class AirFlickStateMachineTests
{
    private static List<AirFlickState> Record(AirFlickStateMachine machine)
    {
        var seen = new List<AirFlickState>();
        machine.StateChanged += (_, e) => seen.Add(e.State);
        return seen;
    }

    [TestMethod]
    public void FullHappyPathEndsBackInWaiting()
    {
        var machine = new AirFlickStateMachine();
        var seen = Record(machine);

        machine.ReportDiscovering();
        machine.ReportDeviceFound("Laptop B");
        machine.ReportDeviceConnecting("Laptop B");
        machine.ReportSessionPhase(SessionPhase.Ready, "Laptop B");
        Assert.IsTrue(machine.ReportFileGrabbed("a.jpg"));
        Assert.IsTrue(machine.ReportPinRequired());
        Assert.IsTrue(machine.ReportPinVerified());
        machine.ReportTransferStarted("a.jpg");
        machine.ReportTransferComplete("a.jpg");

        CollectionAssert.AreEqual(
            new[]
            {
                AirFlickState.Discovering,
                AirFlickState.DeviceFound,
                AirFlickState.Connecting,
                AirFlickState.SessionReady,
                AirFlickState.Waiting,
                AirFlickState.FileGrabbed,
                AirFlickState.PinRequired,
                AirFlickState.PinVerified,
                AirFlickState.Transferring,
                AirFlickState.TransferComplete,
                AirFlickState.Waiting
            },
            seen);
        Assert.AreEqual(AirFlickState.Waiting, machine.Current);
    }

    [TestMethod]
    public void FailedTransferRecoversToWaitingAndAcceptsNextFile()
    {
        var machine = new AirFlickStateMachine();
        machine.ReportSessionPhase(SessionPhase.Ready);
        machine.ReportFileGrabbed("a.jpg");
        machine.ReportTransferStarted("a.jpg");

        machine.ReportTransferFailed("receiver rejected");
        Assert.AreEqual(AirFlickState.Failed, machine.Current);

        Assert.IsTrue(machine.Recover());
        Assert.AreEqual(AirFlickState.Waiting, machine.Current);
        Assert.IsTrue(machine.ReportFileGrabbed("b.jpg"));
    }

    [TestMethod]
    public void FileCannotBeGrabbedWhileNotWaiting()
    {
        var machine = new AirFlickStateMachine();

        Assert.IsFalse(machine.ReportFileGrabbed("a.jpg"));
        Assert.AreEqual(AirFlickState.Starting, machine.Current);
    }

    [TestMethod]
    public void ReadyReportedMidTransferDoesNotWipeTheWorkflow()
    {
        var machine = new AirFlickStateMachine();
        machine.ReportSessionPhase(SessionPhase.Ready);
        machine.ReportFileGrabbed("a.jpg");
        machine.ReportPinRequired();

        machine.ReportSessionPhase(SessionPhase.Ready);

        Assert.AreEqual(AirFlickState.PinRequired, machine.Current);
    }

    [TestMethod]
    public void TargetSelectionOnlyAppliesWhileAFileIsPrepared()
    {
        var machine = new AirFlickStateMachine();
        machine.ReportSessionPhase(SessionPhase.Ready);

        Assert.IsFalse(machine.ReportTargetSelected("Laptop B"));
        Assert.AreEqual(AirFlickState.Waiting, machine.Current);

        machine.ReportFileGrabbed("a.jpg");
        Assert.IsTrue(machine.ReportTargetSelected("Laptop B"));
        Assert.AreEqual(AirFlickState.TargetSelected, machine.Current);

        Assert.IsTrue(machine.ReportPinRequired());
        Assert.IsTrue(machine.ReportPinVerified());
        machine.ReportTransferStarted("a.jpg");
        machine.ReportTransferComplete("a.jpg");
        Assert.AreEqual(AirFlickState.Waiting, machine.Current);
    }

    [TestMethod]
    public void ReleasingTheFileFromTargetSelectedReturnsToWaiting()
    {
        var machine = new AirFlickStateMachine();
        machine.ReportSessionPhase(SessionPhase.Ready);
        machine.ReportFileGrabbed("a.jpg");
        machine.ReportTargetSelected("Laptop B");

        Assert.IsTrue(machine.ReportFileReleased());
        Assert.AreEqual(AirFlickState.Waiting, machine.Current);
    }

    [TestMethod]
    public void PinFailureReturnsToFileGrabbed()
    {
        var machine = new AirFlickStateMachine();
        machine.ReportSessionPhase(SessionPhase.Ready);
        machine.ReportFileGrabbed("a.jpg");
        machine.ReportPinRequired();

        Assert.IsTrue(machine.ReportPinFailed("TooManyAttempts"));

        Assert.AreEqual(AirFlickState.FileGrabbed, machine.Current);
        Assert.IsTrue(machine.ReportPinRequired());
    }

    [TestMethod]
    public void DisconnectDuringWorkflowFallsBackToDiscoveringAndSessionReadyRecovers()
    {
        var machine = new AirFlickStateMachine();
        machine.ReportSessionPhase(SessionPhase.Ready);
        machine.ReportFileGrabbed("a.jpg");

        machine.ReportSessionPhase(SessionPhase.Disconnected);
        Assert.AreEqual(AirFlickState.Discovering, machine.Current);

        machine.ReportSessionPhase(SessionPhase.Ready);
        Assert.AreEqual(AirFlickState.Waiting, machine.Current);
    }

    [TestMethod]
    public void SessionFailureIsReportedAndLaterReadyRecovers()
    {
        var machine = new AirFlickStateMachine();
        machine.ReportSessionPhase(SessionPhase.Failed, "heartbeat_timeout");
        Assert.AreEqual(AirFlickState.Failed, machine.Current);

        machine.ReportSessionPhase(SessionPhase.Ready);
        Assert.AreEqual(AirFlickState.Waiting, machine.Current);
    }

    [TestMethod]
    public void PendingShareTransitionsFromFileStartingOrDiscovering()
    {
        var machine = new AirFlickStateMachine();
        Assert.AreEqual(AirFlickState.Starting, machine.Current);

        Assert.IsTrue(machine.ReportPendingShare("file.pdf"));
        Assert.AreEqual(AirFlickState.FileGrabbed, machine.Current);

        Assert.IsTrue(machine.ReportPinRequired());
        Assert.AreEqual(AirFlickState.PinRequired, machine.Current);
    }

    [TestMethod]
    public void PendingShareSurvivesConnectingPhaseTransitions()
    {
        var machine = new AirFlickStateMachine();
        Assert.IsTrue(machine.ReportPendingShare("file.pdf"));
        Assert.IsTrue(machine.ReportPinRequired());

        // During incoming Wi-Fi Direct connection and handshake, transfer state must be preserved
        machine.ReportSessionPhase(SessionPhase.WifiDirectConnected);
        Assert.AreEqual(AirFlickState.PinRequired, machine.Current);

        machine.ReportSessionPhase(SessionPhase.ConnectingTransport);
        Assert.AreEqual(AirFlickState.PinRequired, machine.Current);

        machine.ReportSessionPhase(SessionPhase.Handshaking);
        Assert.AreEqual(AirFlickState.PinRequired, machine.Current);

        machine.ReportSessionPhase(SessionPhase.Ready);
        Assert.AreEqual(AirFlickState.PinRequired, machine.Current);
    }

    [TestMethod]
    public void PendingShareRecoversFromFailedState()
    {
        var machine = new AirFlickStateMachine();
        machine.ReportTransferFailed("Network dropped");
        Assert.AreEqual(AirFlickState.Failed, machine.Current);

        // Initiating a new pending share must succeed and transition to FileGrabbed
        Assert.IsTrue(machine.ReportPendingShare("new_file.png"));
        Assert.AreEqual(AirFlickState.FileGrabbed, machine.Current);
    }

    [TestMethod]
    public void CancelRecoversFromFailedState()
    {
        var machine = new AirFlickStateMachine();
        machine.ReportTransferFailed("Transfer aborted");
        Assert.AreEqual(AirFlickState.Failed, machine.Current);

        // Releasing file/cancelling must recover from Failed to Waiting
        Assert.IsTrue(machine.ReportFileReleased());
        Assert.AreEqual(AirFlickState.Waiting, machine.Current);
    }
}

