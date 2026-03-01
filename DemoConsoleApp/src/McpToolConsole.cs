/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System.ComponentModel;
using com.IvanMurzak.McpPlugin;

[McpPluginToolType]
public static class McpToolConsole
{
    [McpPluginTool("console-log", "Logs a message to the console.")]
    [Description("Logs a message to the console.")]
    public static void Log(string message)
    {
        Console.WriteLine(message);
    }
}