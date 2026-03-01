/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
// Generic pair with constraints and methods
using System;
using System.Threading.Tasks;

namespace com.IvanMurzak.McpPlugin.Tests.Data.Other
{
    public class Pair<TKey, TValue>
        where TKey : notnull
    {
        public TKey Key { get; set; }
        public TValue? Value { get; set; }

        public Pair(TKey key, TValue? value = default)
        {
            Key = key;
            Value = value;
        }

        public (TKey, TValue?) ToTuple() => (Key, Value);

        public async Task<(TKey, TValue?)> ToTupleAsync()
        {
            await Task.Yield();
            return (Key, Value);
        }

        public bool TryGetValue(out TValue? value)
        {
            value = Value;
            return Value is not null;
        }
    }
}
