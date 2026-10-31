using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AirFlick.Services;

namespace AirFlick.Tests;

[TestClass]
public sealed class PinAuthorizationServiceTests
{
    private sealed class FakeClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    [TestMethod]
    public void GeneratedPinIsAlwaysExactlyThreeDigitsAndRestrictedToDigitsOneThroughFive()
    {
        for (var i = 0; i < 2000; i++)
        {
            var pin = PinAuthorizationService.GenerateThreeDigitPin();
            Assert.AreEqual(3, pin.Length);
            Assert.IsTrue(Regex.IsMatch(pin, "^[1-5]{3}$"), $"Generated PIN '{pin}' contains invalid digits outside 1-5.");
            Assert.IsTrue(PinAuthorizationService.IsValidPinFormat(pin), $"Generated PIN '{pin}' must satisfy IsValidPinFormat.");
        }
    }

    [TestMethod]
    public void IsValidPinFormatRejectsDigitsOutsideOneThroughFive()
    {
        // Valid PINs (digits 1-5)
        Assert.IsTrue(PinAuthorizationService.IsValidPinFormat("123"));
        Assert.IsTrue(PinAuthorizationService.IsValidPinFormat("111"));
        Assert.IsTrue(PinAuthorizationService.IsValidPinFormat("555"));
        Assert.IsTrue(PinAuthorizationService.IsValidPinFormat("145"));
        Assert.IsTrue(PinAuthorizationService.IsValidPinFormat("543"));

        // Invalid PINs: digits 0, 6, 7, 8, 9
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("012"), "Contains 0");
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("120"), "Contains 0");
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("126"), "Contains 6");
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("127"), "Contains 7");
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("128"), "Contains 8");
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("129"), "Contains 9");
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("000"), "All zeros");
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("999"), "All nines");

        // Invalid lengths and null
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat(null));
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat(""));
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("12"));
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("1234"));
        Assert.IsFalse(PinAuthorizationService.IsValidPinFormat("abc"));
    }

    [TestMethod]
    public void CorrectPinAuthorizes()
    {
        var service = new PinAuthorizationService();
        var ticket = service.CreateAuthorization();

        var result = service.VerifyPin(ticket.AuthorizationId, ticket.Pin);

        Assert.IsTrue(result.IsAuthorized);
        Assert.IsTrue(service.IsAuthorized(ticket.AuthorizationId));
    }

    [TestMethod]
    public void CorrectProofAuthorizesWithoutPinCrossingTheWire()
    {
        var service = new PinAuthorizationService();
        var ticket = service.CreateAuthorization();

        // Receiver only ever sends the proof, computed from the typed PIN + challenge.
        var proof = PinAuthorizationService.ComputeProof(ticket.Pin, ticket.AuthorizationId, ticket.Nonce);

        Assert.IsTrue(service.VerifyProof(ticket.AuthorizationId, proof).IsAuthorized);
    }

    [TestMethod]
    public void WrongPinFails()
    {
        var service = new PinAuthorizationService();
        var ticket = service.CreateAuthorization();
        var wrong = ticket.Pin == "000" ? "001" : "000";

        var result = service.VerifyPin(ticket.AuthorizationId, wrong);

        Assert.AreEqual(PinAuthorizationStatus.WrongPin, result.Status);
        Assert.IsFalse(service.IsAuthorized(ticket.AuthorizationId));
    }

    [TestMethod]
    public void MalformedPinFailsAndBurnsAnAttempt()
    {
        var service = new PinAuthorizationService(maxAttempts: 2);
        var ticket = service.CreateAuthorization();

        Assert.AreEqual(PinAuthorizationStatus.WrongPin, service.VerifyPin(ticket.AuthorizationId, "12").Status);
        Assert.AreEqual(PinAuthorizationStatus.TooManyAttempts, service.VerifyPin(ticket.AuthorizationId, "abc").Status);
    }

    [TestMethod]
    public void DefaultLifetimeIsExactlyOneHundredTwentySeconds()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(120), PinAuthorizationService.DefaultLifetime);
    }

    [TestMethod]
    public void TicketIsValidBefore120SecondsAndExpiresAtOrAfter120Seconds()
    {
        var clock = new FakeClock();
        var service = new PinAuthorizationService(clock: () => clock.Now);
        var ticket = service.CreateAuthorization();

        // 1. Valid before 120 seconds (e.g. at 119 seconds)
        clock.Now += TimeSpan.FromSeconds(119);
        var preCheck = service.VerifyPin(ticket.AuthorizationId, ticket.Pin);
        Assert.IsTrue(preCheck.IsAuthorized, "Ticket must be valid at 119 seconds");
        Assert.IsTrue(service.IsAuthorized(ticket.AuthorizationId));

        // Create a new unconsumed ticket to test exactly 120s and 121s
        var freshTicket = service.CreateAuthorization();

        // 2. Expired at 120 seconds
        clock.Now += TimeSpan.FromSeconds(120);
        var atExpiry = service.VerifyPin(freshTicket.AuthorizationId, freshTicket.Pin);
        Assert.AreEqual(PinAuthorizationStatus.Expired, atExpiry.Status, "Ticket must expire at 120 seconds");
        Assert.IsFalse(service.TryConsume(freshTicket.AuthorizationId));

        // 3. Expired after 120 seconds
        var thirdTicket = service.CreateAuthorization();
        clock.Now += TimeSpan.FromSeconds(121);
        var postExpiry = service.VerifyPin(thirdTicket.AuthorizationId, thirdTicket.Pin);
        Assert.AreEqual(PinAuthorizationStatus.Expired, postExpiry.Status, "Ticket must be expired after 120 seconds");
        Assert.IsFalse(service.TryConsume(thirdTicket.AuthorizationId));
    }

    [TestMethod]
    public void SenderAndReceiverShareIdentical120SecondExpiry()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(120), PinAuthorizationService.DefaultLifetime);
    }

    [TestMethod]
    public void ExpiredAuthorizationFails()
    {
        var clock = new FakeClock();
        var service = new PinAuthorizationService(TimeSpan.FromSeconds(30), clock: () => clock.Now);
        var ticket = service.CreateAuthorization();

        clock.Now += TimeSpan.FromSeconds(31);

        Assert.AreEqual(PinAuthorizationStatus.Expired, service.VerifyPin(ticket.AuthorizationId, ticket.Pin).Status);
        Assert.IsFalse(service.TryConsume(ticket.AuthorizationId));
    }

    [TestMethod]
    public void AuthorizationThatExpiresAfterVerificationCannotBeConsumed()
    {
        var clock = new FakeClock();
        var service = new PinAuthorizationService(TimeSpan.FromSeconds(30), clock: () => clock.Now);
        var ticket = service.CreateAuthorization();
        Assert.IsTrue(service.VerifyPin(ticket.AuthorizationId, ticket.Pin).IsAuthorized);

        clock.Now += TimeSpan.FromSeconds(31);

        Assert.IsFalse(service.TryConsume(ticket.AuthorizationId));
    }

    [TestMethod]
    public void TooManyAttemptsLocksOutEvenTheCorrectPin()
    {
        var service = new PinAuthorizationService(maxAttempts: 3);
        var ticket = service.CreateAuthorization();
        var wrong = ticket.Pin == "000" ? "001" : "000";

        service.VerifyPin(ticket.AuthorizationId, wrong);
        service.VerifyPin(ticket.AuthorizationId, wrong);
        var third = service.VerifyPin(ticket.AuthorizationId, wrong);
        var afterLockout = service.VerifyPin(ticket.AuthorizationId, ticket.Pin);

        Assert.AreEqual(PinAuthorizationStatus.TooManyAttempts, third.Status);
        Assert.AreEqual(PinAuthorizationStatus.TooManyAttempts, afterLockout.Status);
        Assert.IsFalse(service.IsAuthorized(ticket.AuthorizationId));
    }

    [TestMethod]
    public void NonceAndAuthorizationIdChangeBetweenChallenges()
    {
        var service = new PinAuthorizationService();
        var first = service.CreateAuthorization();
        var second = service.CreateAuthorization();

        Assert.AreNotEqual(first.AuthorizationId, second.AuthorizationId);
        CollectionAssert.AreNotEqual(first.Nonce, second.Nonce);
        Assert.AreEqual(PinAuthorizationService.NonceSize, first.Nonce.Length);
    }

    [TestMethod]
    public void ProofFromAnotherChallengeIsRejected()
    {
        var service = new PinAuthorizationService();
        var first = service.CreateAuthorization();
        var second = service.CreateAuthorization();

        var replayed = PinAuthorizationService.ComputeProof(first.Pin, first.AuthorizationId, first.Nonce);

        Assert.IsFalse(service.VerifyProof(second.AuthorizationId, replayed).IsAuthorized);
    }

    [TestMethod]
    public void AuthorizationIsSingleUse()
    {
        var service = new PinAuthorizationService();
        var ticket = service.CreateAuthorization();
        service.VerifyPin(ticket.AuthorizationId, ticket.Pin);

        Assert.IsTrue(service.TryConsume(ticket.AuthorizationId));
        Assert.IsFalse(service.TryConsume(ticket.AuthorizationId));
        Assert.IsFalse(service.IsAuthorized(ticket.AuthorizationId));
    }

    [TestMethod]
    public void CancelledAuthorizationFails()
    {
        var service = new PinAuthorizationService();
        var ticket = service.CreateAuthorization();

        service.Cancel(ticket.AuthorizationId);

        Assert.AreEqual(PinAuthorizationStatus.Cancelled, service.VerifyPin(ticket.AuthorizationId, ticket.Pin).Status);
    }

    [TestMethod]
    public void UnknownAuthorizationFails()
    {
        var service = new PinAuthorizationService();

        Assert.AreEqual(PinAuthorizationStatus.Unknown, service.VerifyPin("nope", "123").Status);
        Assert.IsFalse(service.TryConsume("nope"));
    }
}

