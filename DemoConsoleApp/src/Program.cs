
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging;
using R3;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

var version = new Version()
{
    Api = "1.0.0",
    Plugin = "1.0.0",
    Environment = "DemoConsoleApp v1.0.0"
};
var reflector = new Reflector();

var mcpPlugin = new McpPluginBuilder(version)
    .WithConfig(configure => ConnectionConfig.BuildFromArgsOrEnv(args))
    .AddLogging(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Trace);
    })
    .AddMcpManager()
    .Build(reflector);

mcpPlugin.ConnectionState
    .Subscribe(state => Console.WriteLine($"Connection state changed: {state}"));

var result = await mcpPlugin.Connect();

Console.WriteLine($"Connected: {result}");

try
{
    if (!Console.IsInputRedirected)
    {
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}
catch (InvalidOperationException)
{
    // Console input is not available, just exit
}
