using System;
using System.IO;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace McpPlugin.DummyServer
{
    class InterceptingReader : TextReader
    {
        private readonly TextReader _inner;
        private readonly string _logFile;
        public InterceptingReader(TextReader inner, string logFile)
        {
            _inner = inner;
            _logFile = logFile;
        }

        private void Intercept(string? line)
        {
            if (line != null && line.Contains("\"method\":\"initialize\""))
            {
                File.AppendAllText(_logFile, "[EVENT] Received: initialize\n");
            }
        }

        public override string? ReadLine()
        {
            var line = _inner.ReadLine();
            Intercept(line);
            return line;
        }

        public override async Task<string?> ReadLineAsync()
        {
            var line = await _inner.ReadLineAsync();
            Intercept(line);
            return line;
        }

        public override int Read() => _inner.Read();
        public override int Peek() => _inner.Peek();
    }

    public class Program
    {
        public static async Task Main(string[] args)
        {
            string? transport = null;
            string? logFile = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--transport" && i + 1 < args.Length)
                {
                    transport = args[++i];
                }
                else if (args[i] == "--log-file" && i + 1 < args.Length)
                {
                    logFile = args[++i];
                }
            }

            if (string.IsNullOrEmpty(transport)) transport = "stdio";
            if (string.IsNullOrEmpty(logFile)) logFile = "dummy.log";
            DummyTools.LogFile = logFile;

            // Prepare args for DataArguments
            var appArgs = new[] { $"--mcp-server-transport={transport}" };
            var dataArguments = new DataArguments(appArgs);

            if (dataArguments.ClientTransport == Consts.MCP.Server.TransportMethod.stdio)
            {
                Console.SetIn(new InterceptingReader(Console.In, logFile));
                File.AppendAllText(logFile, "[EVENT] Connected\n");
            }

            var builder = WebApplication.CreateBuilder(appArgs);

            // Setup MCP Plugin Server
            builder.Services
                .WithMcpServer(dataArguments)
                .WithToolsFromAssembly(typeof(DummyTools).Assembly)
                .WithMcpPluginServer(dataArguments);
            
            builder.Services.AddLogging(logging => logging.ClearProviders());

            var app = builder.Build();

            app.Use(async (context, next) =>
            {
                if (dataArguments.ClientTransport == Consts.MCP.Server.TransportMethod.streamableHttp)
                {
                    if (context.Request.Path == "/sse")
                    {
                        File.AppendAllText(logFile, "[EVENT] Connected\n");
                    }
                    
                    if (context.Request.Path == "/message" || context.Request.Path.StartsWithSegments("/mcp"))
                    {
                        context.Request.EnableBuffering();
                        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                        var body = await reader.ReadToEndAsync();
                        context.Request.Body.Position = 0;

                        if (body.Contains("\"method\":\"initialize\""))
                        {
                            File.AppendAllText(logFile, "[EVENT] Received: initialize\n");
                        }
                    }
                }
                await next();
            });

            app.UseMcpPluginServer(dataArguments);

            await app.RunAsync();
        }
    }
}
