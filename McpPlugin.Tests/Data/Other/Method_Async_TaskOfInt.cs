/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
// Async method returning Task<int>
using System.Threading.Tasks;

namespace com.IvanMurzak.McpPlugin.Tests.Data.Other
{
    public class Method_Async_TaskOfInt
    {
        public async Task<int> ComputeAsync(int a, int b)
        {
            await Task.Yield();
            return a + b;
        }
    }
}
