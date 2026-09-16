using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace McpPlugin.E2E.Tests;

public class AntigravityE2ETests
{
    private int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public void Antigravity_Connects_Via_Stdio()
    {
        using var sandbox = new Sandbox();
        using var adapter = new AntigravityAdapter(sandbox);
        var logFile = sandbox.GetFilePath("dummy_stdio.log");

        adapter.GenerateConfig("stdio", logFile);
        adapter.LaunchAgy("-p \"There is an MCP tool named 'ping' available. Please call it exactly as named and return the result.\" --dangerously-skip-permissions");
        adapter.WaitForExit(30000);

        if (!File.Exists(logFile))
        {
            var (outStr, errStr) = adapter.GetOutputs();
            Assert.Fail($"Log file not created. Agy Out: {outStr}\nAgy Err: {errStr}");
        }
        var logContent = File.ReadAllText(logFile);
        Assert.Contains("[EVENT] Connected", logContent);
        Assert.Contains("[EVENT] Received: initialize", logContent);
        Assert.Contains("[EVENT] Tool Call: ping", logContent);
    }

    [Fact]
    public async Task Antigravity_Connects_Via_Sse()
    {
        using var sandbox = new Sandbox();
        using var adapter = new AntigravityAdapter(sandbox);
        var logFile = sandbox.GetFilePath("dummy_sse.log");
        var port = GetFreePort();

        var dummyServerDll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "McpPlugin.DummyServer.dll"));
        
        var psi = new ProcessStartInfo("dotnet", $"\"{dummyServerDll}\" --transport streamableHttp --log-file \"{logFile}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        
        using var dummyServerProcess = Process.Start(psi);
        Assert.NotNull(dummyServerProcess);

        try
        {
            // Give server a moment to start
            await Task.Delay(2000);

            adapter.GenerateConfig("streamableHttp", logFile, $"http://127.0.0.1:{port}/sse");
            adapter.LaunchAgy("-p \"There is an MCP tool named 'ping' available. Please call it exactly as named and return the result.\" --dangerously-skip-permissions");
            adapter.WaitForExit(30000);

            if (!File.Exists(logFile))
            {
                var (outStr, errStr) = adapter.GetOutputs();
                Assert.Fail($"Log file not created. Agy Out: {outStr}\nAgy Err: {errStr}\nDummyServer Out: {dummyServerProcess.StandardOutput.ReadToEnd()}\nDummyServer Err: {dummyServerProcess.StandardError.ReadToEnd()}");
            }
            var logContent = File.ReadAllText(logFile);
            Assert.Contains("[EVENT] Connected", logContent);
            Assert.Contains("[EVENT] Tool Call: ping", logContent);
        }
        finally
        {
            if (!dummyServerProcess.HasExited)
            {
                dummyServerProcess.Kill();
            }
        }
    }
}
