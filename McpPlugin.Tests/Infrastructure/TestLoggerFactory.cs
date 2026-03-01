/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace com.IvanMurzak.McpPlugin.Tests.Infrastructure
{
    public static class TestLoggerFactory
    {
        public static ILoggerFactory Create(ITestOutputHelper output, LogLevel minLevel = LogLevel.Information)
        {
            return LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(minLevel);
                builder.AddXunitTestOutput(output);
            });
        }
    }
}
