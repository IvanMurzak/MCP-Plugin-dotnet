using System;
using System.IO;
using Xunit;

namespace McpPlugin.E2E.Tests;

public class SandboxAndLogParserTests
{
    [Fact]
    public void Sandbox_CreatesAndCleansUpDirectory()
    {
        string directoryPath;

        using (var sandbox = new Sandbox())
        {
            directoryPath = sandbox.DirectoryPath;
            Assert.True(Directory.Exists(directoryPath));

            var filePath = sandbox.GetFilePath("test.txt");
            File.WriteAllText(filePath, "content");
            Assert.True(File.Exists(filePath));
        }

        // Assert sandbox deleted
        Assert.False(Directory.Exists(directoryPath));
    }

    [Fact]
    public void LogParser_ReadsWithoutLocking_AndAssertsEvents()
    {
        using var sandbox = new Sandbox();
        var logFilePath = sandbox.GetFilePath("dummy.log");
        
        var logParser = new LogParser(logFilePath);

        // Should return empty if file not exists
        Assert.Empty(logParser.ReadAllText());

        // Simulate server writing to log file, locking it momentarily
        using (var fs = new FileStream(logFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(fs))
        {
            writer.WriteLine("[EVENT] Connected");
            writer.Flush();

            // LogParser should still be able to read it because FileShare.Read is allowed
            Assert.Contains("[EVENT] Connected", logParser.ReadAllText());
        }

        // Test AssertContainsEvent
        File.AppendAllText(logFilePath, "[EVENT] Received: initialize\n");
        logParser.AssertContainsEvent("[EVENT] Received: initialize", timeoutMs: 1000);
    }
}
