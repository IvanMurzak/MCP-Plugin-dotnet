/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
// Method with no arguments returning List<T>
using System.Collections.Generic;

namespace com.IvanMurzak.McpPlugin.Tests.Data.Other
{
    public class Method_NoArgs_ListOfGenericReturn<T>
    {
        public List<T> Do()
        {
            return new List<T>();
        }
    }
}
