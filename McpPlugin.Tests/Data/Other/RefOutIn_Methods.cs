/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
// Cases: ref/out/in parameters
namespace com.IvanMurzak.McpPlugin.Tests.Data.Other
{
    public class RefOutIn_Methods
    {
        public void Increment(ref int value)
        {
            value++;
        }

        public bool TryParseInt(string text, out int value)
        {
            return int.TryParse(text, out value);
        }

        public int Sum(in int a, in int b)
        {
            return a + b;
        }
    }
}
