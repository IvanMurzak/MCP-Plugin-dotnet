using System;
using System.IO;

namespace McpPlugin.E2E.Tests;

public class Sandbox : IDisposable
{
    public string DirectoryPath { get; }

    public Sandbox()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "mcp_e2e_sandbox_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
    }

    public string GetFilePath(string fileName)
    {
        return Path.Combine(DirectoryPath, fileName);
    }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            try
            {
                Directory.Delete(DirectoryPath, true);
            }
            catch
            {
                // Ignore errors during cleanup, test framework will eventually clean temp directory, 
                // or we could add retry logic if needed.
            }
        }
    }
}
