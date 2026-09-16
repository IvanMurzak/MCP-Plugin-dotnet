using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;

namespace McpPlugin.E2E.Tests;

public class AntigravityAdapter : IDisposable
{
    private readonly Sandbox _sandbox;
    private Process? _process;

    public AntigravityAdapter(Sandbox sandbox)
    {
        _sandbox = sandbox;
    }

    public void GenerateConfig(string transport, string logFile, string? httpUrl = null)
    {
        var mcpServers = new JsonObject();
        var dummyServer = new JsonObject();
        dummyServer["disabled"] = false;

        if (transport == "stdio")
        {
            dummyServer["command"] = "dotnet";
            var dummyServerDll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "McpPlugin.DummyServer.dll"));
            var logFileFwd = logFile.Replace('\\', '/');
            dummyServer["args"] = new JsonArray { dummyServerDll.Replace('\\', '/'), "--transport", "stdio", "--log-file", logFileFwd };
        }
        else if (transport == "streamableHttp")
        {
            dummyServer["serverUrl"] = httpUrl;
        }

        mcpServers["dummyserver"] = dummyServer;
        var config = new JsonObject { ["mcpServers"] = mcpServers };
        var jsonString = config.ToJsonString();

        var configPath1 = Path.Combine(_sandbox.DirectoryPath, ".gemini", "config");
        var configPath2 = Path.Combine(_sandbox.DirectoryPath, ".gemini", "antigravity");
        Directory.CreateDirectory(configPath1);
        Directory.CreateDirectory(configPath2);
        
        File.WriteAllText(Path.Combine(configPath1, "mcp_config.json"), jsonString);
        File.WriteAllText(Path.Combine(configPath2, "mcp_config.json"), jsonString);
        File.WriteAllText(Path.Combine(_sandbox.DirectoryPath, "antigravity.json"), jsonString);

        var settings = new JsonObject
        {
            ["permissions"] = new JsonObject
            {
                ["allow"] = new JsonArray { "command(dotnet)", "read_url(*)" }
            }
        };
        File.WriteAllText(Path.Combine(configPath1, "settings.json"), settings.ToJsonString());
    }

    public void LaunchAgy(string arguments = "-p \"use the ping tool and tell me the result\"")
    {
        var psi = new ProcessStartInfo("agy", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _sandbox.DirectoryPath
        };

        psi.Environment["HOME"] = _sandbox.DirectoryPath;
        psi.Environment["USERPROFILE"] = _sandbox.DirectoryPath;
        psi.Environment["APPDATA"] = _sandbox.DirectoryPath;
        psi.Environment["LOCALAPPDATA"] = _sandbox.DirectoryPath;

        _process = Process.Start(psi);
        if (_process == null) throw new Exception("Failed to start agy process");
    }

    public void WaitForExit(int milliseconds = 30000)
    {
        if (_process != null && !_process.HasExited)
        {
            _process.WaitForExit(milliseconds);
        }
    }

    public (string, string) GetOutputs()
    {
        if (_process == null) return ("", "");
        return (_process.StandardOutput.ReadToEnd(), _process.StandardError.ReadToEnd());
    }

    public void Dispose()
    {
        if (_process != null && !_process.HasExited)
        {
            try { _process.Kill(); } catch { }
        }
    }
}
