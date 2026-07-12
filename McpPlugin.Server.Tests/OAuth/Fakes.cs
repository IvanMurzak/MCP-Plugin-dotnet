/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;

namespace com.IvanMurzak.McpPlugin.Server.Tests.OAuth
{
    /// <summary>In-memory JWKS provider: returns a fresh ECDsa per known kid, null otherwise.</summary>
    internal sealed class FakeJwksKeyProvider : IJwksKeyProvider
    {
        private readonly Dictionary<string, ECParameters> _keys = new(StringComparer.Ordinal);

        public FakeJwksKeyProvider Add(string kid, ECDsa key)
        {
            _keys[kid] = key.ExportParameters(false);
            return this;
        }

        public Task<ECDsa?> GetSigningKeyAsync(string kid, CancellationToken cancellationToken)
            => Task.FromResult(_keys.TryGetValue(kid, out var p) ? ECDsa.Create(p) : null);
    }

    /// <summary>Introspection stub driven by a caller-supplied map.</summary>
    internal sealed class FakeIntrospectionClient : IIntrospectionClient
    {
        private readonly Func<string, IntrospectionResult> _map;

        public FakeIntrospectionClient(Func<string, IntrospectionResult> map) => _map = map;

        public static FakeIntrospectionClient AlwaysInactive => new FakeIntrospectionClient(_ => IntrospectionResult.Inactive);

        public Task<IntrospectionResult> IntrospectAsync(string token, CancellationToken cancellationToken)
            => Task.FromResult(_map(token));
    }

    /// <summary>In-memory JWKS disk cache for JwksKeyProvider tests.</summary>
    internal sealed class InMemoryJwksDiskCache : IJwksDiskCache
    {
        public string? Value { get; set; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }

        public string? Read()
        {
            Reads++;
            return Value;
        }

        public void Write(string json)
        {
            Writes++;
            Value = json;
        }
    }
}
