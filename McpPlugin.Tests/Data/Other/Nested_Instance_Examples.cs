/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
// Cases: properties/fields as instances of another class (including generics and self-reference)
namespace com.IvanMurzak.McpPlugin.Tests.Data.Other
{
    public class Node
    {
        public int Value { get; set; }
        public Node? Next { get; set; }
    }

    public class Holder
    {
        public Person? Owner { get; set; }
        public Container<Address> AddressBox { get; } = new Container<Address>();
        private Pair<string, Person>? _primary;

        public void SetPrimary(Pair<string, Person> p) => _primary = p;
        public Pair<string, Person>? GetPrimary() => _primary;
    }
}
