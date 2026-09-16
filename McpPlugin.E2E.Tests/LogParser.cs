using System;
using System.IO;
using System.Threading;
using Xunit;

namespace McpPlugin.E2E.Tests;

public class LogParser
{
    private readonly string _logFilePath;

    public LogParser(string logFilePath)
    {
        _logFilePath = logFilePath;
    }

    public string ReadAllText()
    {
        if (!File.Exists(_logFilePath))
        {
            return string.Empty;
        }

        try
        {
            using var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd();
        }
        catch (FileNotFoundException)
        {
            return string.Empty;
        }
    }

    public void AssertContainsEvent(string eventName, int timeoutMs = 5000)
    {
        var start = DateTime.UtcNow;
        while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
        {
            var content = ReadAllText();
            if (content.Contains(eventName))
            {
                return; // Event found
            }
            
            Thread.Sleep(100);
        }

        var finalContent = ReadAllText();
        Assert.Contains(eventName, finalContent);
    }
}
