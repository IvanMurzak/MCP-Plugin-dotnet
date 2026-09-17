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
            if (line != null)
            {
                if (line.Contains("\"ping\""))
                {
                    File.AppendAllText(_logFile, "[EVENT] Received: ping\n");
                }
                else if (line.Contains("\"initialize\""))
                {
                    File.AppendAllText(_logFile, "[EVENT] Received: initialize\n");
                }
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

        public override int Read(char[] buffer, int index, int count)
        {
            int read = _inner.Read(buffer, index, count);
            if (read > 0)
            {
                var text = new string(buffer, index, read);
                Intercept(text);
            }
            return read;
        }

        public override async Task<int> ReadAsync(char[] buffer, int index, int count)
        {
            int read = await _inner.ReadAsync(buffer, index, count);
            if (read > 0)
            {
                var text = new string(buffer, index, read);
                Intercept(text);
            }
            return read;
        }

        public override int Read(Span<char> buffer)
        {
            int read = _inner.Read(buffer);
            if (read > 0)
            {
                var text = new string(buffer.Slice(0, read));
                Intercept(text);
            }
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<char> buffer, System.Threading.CancellationToken cancellationToken = default)
        {
            int read = await _inner.ReadAsync(buffer, cancellationToken);
            if (read > 0)
            {
                var text = new string(buffer.Span.Slice(0, read));
                Intercept(text);
            }
            return read;
        }

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

                        if (body.Contains("\"initialize\""))
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
