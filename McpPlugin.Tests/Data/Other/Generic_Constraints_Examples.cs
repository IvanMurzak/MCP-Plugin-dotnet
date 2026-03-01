/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
// Cases: Generics with constraints and nested generics
using System;
using System.Collections.Generic;

namespace com.IvanMurzak.McpPlugin.Tests.Data.Other
{
    public interface IIdentifiable
    {
        Guid Id { get; }
    }

    public class Identifiable : IIdentifiable
    {
        public Guid Id { get; init; } = Guid.NewGuid();
    }

    public class Factory<T> where T : new()
    {
        public T Create() => new T();
    }

    public class Registry<TKey, TValue>
        where TKey : notnull
        where TValue : IIdentifiable
    {
        private readonly Dictionary<TKey, TValue> _map = new Dictionary<TKey, TValue>();
        public void Add(TKey key, TValue value) => _map[key] = value;
        public bool TryGet(TKey key, out TValue value) => _map.TryGetValue(key, out value!);
    }

    public class NestedGeneric<T>
    {
        public Container<List<T>> Wrapper { get; } = new Container<List<T>>();
    }
}
