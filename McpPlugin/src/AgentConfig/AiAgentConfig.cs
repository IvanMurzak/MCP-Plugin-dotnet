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
using System.Collections.Generic;
using com.IvanMurzak.McpPlugin.Common;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// How a configured value is compared against the value present on disk when
    /// deciding whether an AI-agent config file is already correctly configured.
    /// </summary>
    public enum ValueComparisonMode
    {
        /// <summary>Byte-for-byte string equality.</summary>
        Exact,
        /// <summary>Filesystem-path equality (separator-insensitive, trailing-slash-insensitive).</summary>
        Path,
        /// <summary>URL equality (scheme/host case-insensitive, trailing-slash-insensitive).</summary>
        Url
    }

    /// <summary>
    /// Engine-agnostic base for an AI-agent MCP config file (JSON or TOML).
    /// Holds the server-entry shape (name, body path, identity keys) and the
    /// install / remove / status contract. No editor-UI or engine dependency.
    /// </summary>
    public abstract class AiAgentConfig
    {
        /// <summary>Server-entry names written by older plugin versions that should be cleaned up on configure/unconfigure.</summary>
        public static readonly string[] DeprecatedMcpServerNames = { "Unity-MCP" };

        /// <summary>The canonical server-entry name written under the body path.</summary>
        public const string DefaultMcpServerName = "ai-game-developer";

        /// <summary>Property keys used to recognise the same server entry written under a different name.</summary>
        public static readonly string[] DefaultIdentityKeys = { "command", "url" };

        protected readonly List<string> _identityKeys = new(DefaultIdentityKeys);
        protected readonly ILogger? _logger;

        public string Name { get; set; }
        public string ConfigPath { get; set; }
        public string BodyPath { get; set; }
        public abstract string ExpectedFileContent { get; }
        public IReadOnlyList<string> IdentityKeys => _identityKeys;

        protected AiAgentConfig(
            string name,
            string configPath,
            string bodyPath = Consts.MCP.Server.DefaultBodyPath,
            ILogger? logger = null)
        {
            Name = name;
            ConfigPath = configPath;
            BodyPath = bodyPath;
            _logger = logger;
        }

        public AiAgentConfig AddIdentityKey(string key)
        {
            if (!_identityKeys.Contains(key))
                _identityKeys.Add(key);
            return this;
        }

        public abstract bool Configure();
        public abstract bool Unconfigure();
        public abstract bool IsDetected();
        public abstract bool IsConfigured();

        /// <summary>
        /// Applies HTTP authorization to this config.
        /// Override in format-specific subclasses to inject authorization headers or tokens.
        /// </summary>
        /// <param name="isRequired">True when auth is required and token is non-empty.</param>
        /// <param name="token">The bearer token value, or null/empty if not set.</param>
        public virtual void ApplyHttpAuthorization(bool isRequired, string? token)
        {
            // Default: no-op. Subclasses override for format-specific injection.
        }

        /// <summary>
        /// Applies STDIO authorization to this config.
        /// Override in format-specific subclasses to add or remove the token argument from args.
        /// </summary>
        /// <param name="isRequired">True when auth is required and token is non-empty.</param>
        /// <param name="token">The bearer token value, or null/empty if not set.</param>
        public virtual void ApplyStdioAuthorization(bool isRequired, string? token)
        {
            // Default: no-op. Subclasses override for format-specific injection.
        }
    }
}
