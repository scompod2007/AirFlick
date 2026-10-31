using Microsoft.VisualStudio.TestTools.UnitTesting;
using AirFlick.Protocol;
using AirFlick.Services;

namespace AirFlick.Tests;

[TestClass]
public sealed class AirFlickAuthorizationProtocolTests
{
    private static AirFlickAuthorizationChallenge SampleChallenge(string? id = null)
    {
        return new AirFlickAuthorizationChallenge(
            id ?? Guid.NewGuid().ToString("N"),
            new byte[] { 1, 2, 3, 4, 5 },
            "photo.jpg",
            2048,
            DateTimeOffset.UtcNow.AddSeconds(120));
    }

    [TestMethod]
    public void AuthorizationFramesReuseTheExistingControlMessageType()
    {
        var frame = AirFlickAuthorizationProtocol.CreateChallengeFrame(SampleChallenge());

        // No new ProtocolFrameType is introduced, so older peers still decode the frame.
        Assert.AreEqual(ProtocolFrameType.ControlMessage, frame.Type);
        Assert.AreEqual(ProtocolChannel.Control, frame.Channel);
    }

    [TestMethod]
    public void ChallengeRoundTripsAndCarriesNoPin()
    {
        var challenge = SampleChallenge();
        var frame = AirFlickAuthorizationProtocol.CreateChallengeFrame(challenge);

        Assert.IsTrue(AirFlickAuthorizationProtocol.TryParseChallenge(frame, out var parsed));
        Assert.IsNotNull(parsed);
        Assert.AreEqual(challenge.AuthorizationId, parsed!.AuthorizationId);
        Assert.AreEqual("photo.jpg", parsed.FileName);
        Assert.AreEqual(2048L, parsed.SizeBytes);
        CollectionAssert.AreEqual(challenge.Nonce, parsed.Nonce);
        Assert.IsFalse(frame.MetadataJson.Contains("pin", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ProofRoundTrips()
    {
        var proof = PinAuthorizationService.ComputeProof("123", "abc", new byte[] { 9, 9 });
        var frame = AirFlickAuthorizationProtocol.CreateProofFrame("abc", proof);

        Assert.IsTrue(AirFlickAuthorizationProtocol.TryParseProof(frame, out var parsed));
        Assert.AreEqual("abc", parsed!.AuthorizationId);
        CollectionAssert.AreEqual(proof, parsed.Proof);
    }

    [TestMethod]
    public void ResultRoundTrips()
    {
        var frame = AirFlickAuthorizationProtocol.CreateResultFrame("abc", authorized: true, message: "ok");

        Assert.IsTrue(AirFlickAuthorizationProtocol.TryParseResult(frame, out var parsed));
        Assert.IsTrue(parsed!.Authorized);
        Assert.AreEqual("ok", parsed.Message);

        var denied = AirFlickAuthorizationProtocol.CreateResultFrame("abc", authorized: false, message: "WrongPin");
        Assert.IsTrue(AirFlickAuthorizationProtocol.TryParseResult(denied, out var deniedParsed));
        Assert.IsFalse(deniedParsed!.Authorized);
    }

    [TestMethod]
    public void OperationsDoNotCrossParse()
    {
        var challengeFrame = AirFlickAuthorizationProtocol.CreateChallengeFrame(SampleChallenge());

        Assert.IsFalse(AirFlickAuthorizationProtocol.TryParseProof(challengeFrame, out _));
        Assert.IsFalse(AirFlickAuthorizationProtocol.TryParseResult(challengeFrame, out _));
    }

    [TestMethod]
    public void MalformedOrForeignFramesAreRejected()
    {
        var bulkFrame = new ProtocolFrame(
            ProtocolFrameType.BulkStart,
            ProtocolChannel.Bulk,
            metadataJson: "{\"kind\":\"airflick.auth\",\"op\":\"challenge\"}");
        Assert.IsFalse(AirFlickAuthorizationProtocol.IsAuthorizationFrame(bulkFrame, out _));

        var garbage = new ProtocolFrame(
            ProtocolFrameType.ControlMessage,
            ProtocolChannel.Control,
            metadataJson: "not json");
        Assert.IsFalse(AirFlickAuthorizationProtocol.IsAuthorizationFrame(garbage, out _));

        var noNonce = new ProtocolFrame(
            ProtocolFrameType.ControlMessage,
            ProtocolChannel.Control,
            metadataJson: "{\"kind\":\"airflick.auth\",\"op\":\"challenge\",\"authorizationId\":\"abc\"}");
        Assert.IsFalse(AirFlickAuthorizationProtocol.TryParseChallenge(noNonce, out _));
    }

    [TestMethod]
    public void AuthorizationIdMapsOntoTheFrameCorrelationId()
    {
        var id = Guid.NewGuid().ToString("N");

        Assert.AreEqual(Guid.Parse(id), AirFlickAuthorizationProtocol.CorrelationIdFor(id));
        Assert.AreEqual(Guid.Parse(id), AirFlickAuthorizationProtocol.CreateProofFrame(id, new byte[] { 1 }).CorrelationId);
    }
}

