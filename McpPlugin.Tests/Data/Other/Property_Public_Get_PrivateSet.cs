/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
// Public getter, private setter property
namespace com.IvanMurzak.McpPlugin.Tests.Data.Other
{
    public class Property_Public_Get_PrivateSet
    {
        public string Name { get; private set; } = string.Empty;

        public void SetName(string name)
        {
            Name = name;
        }
    }
}
