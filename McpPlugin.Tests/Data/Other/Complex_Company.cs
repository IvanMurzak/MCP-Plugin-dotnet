/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
// Complex class: Company with nested collections and references
using System.Collections.Generic;

namespace com.IvanMurzak.McpPlugin.Tests.Data.Other
{
    public class Company
    {
        public string Name { get; set; } = string.Empty;
        public Address? Headquarters { get; set; }
        public List<Person> Employees { get; } = new List<Person>();
        public Dictionary<string, List<Person>> Teams { get; } = new Dictionary<string, List<Person>>();
        public Dictionary<string, Dictionary<string, Person>> Directory { get; } = new();
    }
}
