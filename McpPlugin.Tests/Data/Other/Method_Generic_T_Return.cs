/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
// Generic method that echoes input of type T
namespace com.IvanMurzak.McpPlugin.Tests.Data.Other
{
    public class Method_Generic_T_Return<T>
    {
        public T Echo(T value)
        {
            return value;
        }
    }
}
