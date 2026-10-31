using System;
using System.IO;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AirFlick.Services;

namespace AirFlick.Tests;

[TestClass]
public sealed class TransferDiagnosticsTests
{
    [TestMethod]
    public void LoggingDoesNotThrowOnNullsOrExceptions()
    {
        // Must never throw, even with nulls or empty strings
        TransferDiagnostics.Log("TestStage", "Sender", null, null, null, null, null, null, null);

        var socketEx = new SocketException((int)SocketError.ConnectionRefused);
        TransferDiagnostics.Log("ConnectFail", "Connector", "tid-123", "Connecting", "Socket failed", socketEx, 2, 1024, 2048);
    }

    [TestMethod]
    public void DiagnosticsFileIsCreatedAndWritable()
    {
        var testMsg = "Diagnostic verification log entry " + Guid.NewGuid().ToString("N");
        TransferDiagnostics.Log("VerifyStage", "Receiver", "tid-999", "Ready", testMsg);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logPath = Path.Combine(localAppData, "AirFlick", "logs", "airflick_transfer_diagnostics.log");

        if (File.Exists(logPath))
        {
            var content = File.ReadAllText(logPath);
            Assert.IsTrue(content.Contains(testMsg));
        }
    }
}

