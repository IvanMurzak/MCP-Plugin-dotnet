
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

var version = new Version()
{
    Api = "1.0.0",
    Plugin = "1.0.0",
    Environment = "DemoConsoleApp v1.0.0"
};
var reflector = new Reflector();
var mcpPlugin = new McpPluginBuilder(version)
    .AddMcpManager()
    .Build(reflector);

var result = await mcpPlugin.Connect();

Console.WriteLine($"Connected: {result}");



Console.WriteLine("Press any key to exit...");
Console.ReadKey();
