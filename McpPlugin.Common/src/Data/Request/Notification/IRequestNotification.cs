/*
┌──────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)             │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)    │
│  Copyright (c) 2025 Ivan Murzak                                  │
│  Licensed under the Apache License, Version 2.0.                 │
│  See the LICENSE file in the project root for more information.  │
└──────────────────────────────────────────────────────────────────┘
*/
using System;
using System.Collections.Generic;

namespace com.IvanMurzak.McpPlugin.Common.Model
{
    public interface IRequestNotification : IRequestID, IDisposable
    {
        string? Path { get; set; }
        string? Name { get; set; }
        IDictionary<string, object?>? Parameters { get; set; }
    }
}
