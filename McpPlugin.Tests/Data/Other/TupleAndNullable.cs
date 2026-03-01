/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
// Cases: value tuples and nullable reference types
using System;

namespace com.IvanMurzak.McpPlugin.Tests.Data.Other
{
    public class TupleAndNullable
    {
        public (int Sum, int Product) Calc(int a, int b) => (a + b, a * b);
        public string? MaybeString(bool flag) => flag ? "hi" : null;
        public Person? MaybePerson(bool flag) => flag ? new Person { Age = 1 } : null;
    }
}
