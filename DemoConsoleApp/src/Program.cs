
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
    .WithConfigFromArgsOrEnv(args)
    .AddLogging(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Trace);
    })
    .AddMcpManager()
    .WithToolsFromAssembly(typeof(McpToolConsole).Assembly)
    .WithPromptsFromAssembly(typeof(McpToolConsole).Assembly)
    .WithResourcesFromAssembly(typeof(McpToolConsole).Assembly)
    .Build(reflector);

mcpPlugin.ConnectionState
    .Subscribe(state => Console.WriteLine($"Connection state changed: {state}"));

var result = await mcpPlugin.Connect();

Console.WriteLine($"Connected: {result}");
Console.WriteLine($"Time: {DateTime.Now}");

while (true)
{
    await Task.Delay(1000);
    Console.WriteLine($"Time: {DateTime.Now}");
}