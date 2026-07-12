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
using System.IO;

namespace com.IvanMurzak.McpPlugin.Server.Auth.OAuth
{
    /// <summary>
    /// Persists the fetched JWKS document (public keys only) for offline-grace validation
    /// (mcp-authorize b2).
    /// </summary>
    public interface IJwksDiskCache
    {
        /// <summary>Read the cached JWKS JSON, or <c>null</c> when none is cached.</summary>
        string? Read();

        /// <summary>Persist the JWKS JSON.</summary>
        void Write(string json);
    }

    /// <summary>
    /// File-backed <see cref="IJwksDiskCache"/> co-located with the b1 machine credential store at
    /// <c>~/.ai-game-dev/jwks-cache.json</c>. The path constants intentionally mirror the b1
    /// primitive <c>McpPlugin.AgentConfig.MachineCredentialStore</c> (which lives in the client
    /// library the server does not reference) so both sides share ONE on-disk cache location without
    /// coupling the server package to the client library. JWKS holds only public keys, so it is
    /// stored plaintext inside the user-only directory.
    /// </summary>
    public sealed class FileJwksDiskCache : IJwksDiskCache
    {
        // Mirror of MachineCredentialStore.DirectoryName / JwksCacheFileName (b1).
        private const string DirectoryName = ".ai-game-dev";
        private const string JwksCacheFileName = "jwks-cache.json";

        private readonly string _path;

        /// <summary>
        /// Create a cache rooted at <paramref name="baseDirectory"/>, or at <c>~/.ai-game-dev</c>
        /// when null. The override exists for tests.
        /// </summary>
        public FileJwksDiskCache(string? baseDirectory = null)
        {
            var dir = baseDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                DirectoryName);
            _path = Path.Combine(dir, JwksCacheFileName);
        }

        public string? Read() => File.Exists(_path) ? File.ReadAllText(_path) : null;

        public void Write(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir!);
            File.WriteAllText(_path, json);
        }
    }
}
