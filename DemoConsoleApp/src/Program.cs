/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging;
using R3;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

var version = new Version()
{
    Api = Consts.ApiVersion,
    Plugin = Consts.PluginVersion,
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