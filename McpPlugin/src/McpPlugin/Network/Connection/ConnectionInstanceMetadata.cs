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
using System.Text;
using com.IvanMurzak.McpPlugin.AgentConfig;
using com.IvanMurzak.McpPlugin.Common;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// The engine-side instance-metadata handshake payload (mcp-authorize b7, design docs 04/06). It
    /// identifies THIS editor session to the server's account+instance pairing plane (b3):
    /// <c>{instanceId, engine, projectName, projectPathHash, machineName}</c>. The fields are
    /// <b>non-secret</b> and travel as query parameters on the SignalR hub connection (keyed by
    /// <see cref="Consts.MCP.Server.HubQuery"/>); the credential (JWT) itself always travels separately in
    /// the <c>Authorization</c> header. The server (<c>McpServerHub</c>) reads these query keys in
    /// <c>oauth</c> mode and registers the instance under the account resolved from the validated token.
    /// </summary>
    public sealed class ConnectionInstanceMetadata
    {
        /// <summary>GUID minted per editor session (engine-side). Stable across reconnects of the same editor.</summary>
        public string InstanceId { get; }

        /// <summary>The engine identifier: <c>"unity"</c> | <c>"godot"</c> | <c>"unreal"</c>.</summary>
        public string Engine { get; }

        /// <summary>Human-facing project name, e.g. <c>"MyGame"</c>.</summary>
        public string ProjectName { get; }

        /// <summary>Full SHA-256 hex of the normalized project path (see <see cref="ProjectIdentity.DeriveProjectPathHash"/>). Stable across editor restarts; pin-matched by prefix.</summary>
        public string ProjectPathHash { get; }

        /// <summary>The host machine name.</summary>
        public string MachineName { get; }

        public ConnectionInstanceMetadata(
            string instanceId,
            string engine,
            string projectName,
            string projectPathHash,
            string machineName)
        {
            if (string.IsNullOrEmpty(instanceId))
                throw new ArgumentException("InstanceId must be non-empty.", nameof(instanceId));

            InstanceId = instanceId;
            Engine = engine ?? string.Empty;
            ProjectName = projectName ?? string.Empty;
            ProjectPathHash = projectPathHash ?? string.Empty;
            MachineName = machineName ?? string.Empty;
        }

        /// <summary>
        /// Builds metadata for a project. The <see cref="ProjectPathHash"/> is derived deterministically
        /// from <paramref name="projectRootPath"/> (same hash whose first 8 hex chars are the routing pin);
        /// <paramref name="instanceId"/> defaults to a fresh GUID (mint one per editor session and reuse it
        /// across reconnects); <paramref name="machineName"/> defaults to <see cref="Environment.MachineName"/>.
        /// </summary>
        public static ConnectionInstanceMetadata Create(
            string engine,
            string projectName,
            string projectRootPath,
            string? instanceId = null,
            string? machineName = null)
        {
            if (projectRootPath == null)
                throw new ArgumentNullException(nameof(projectRootPath));

            return new ConnectionInstanceMetadata(
                instanceId: string.IsNullOrEmpty(instanceId) ? Guid.NewGuid().ToString() : instanceId!,
                engine: engine ?? string.Empty,
                projectName: projectName ?? string.Empty,
                projectPathHash: ProjectIdentity.DeriveProjectPathHash(projectRootPath),
                machineName: string.IsNullOrEmpty(machineName) ? SafeMachineName() : machineName!);
        }

        /// <summary>
        /// The hub-handshake query parameters, keyed by <see cref="Consts.MCP.Server.HubQuery"/>. Empty
        /// fields are omitted so a partial handshake never sends blank keys. <see cref="InstanceId"/> is
        /// always present.
        /// </summary>
        public IReadOnlyDictionary<string, string> ToQuery()
        {
            var query = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Consts.MCP.Server.HubQuery.InstanceId] = InstanceId
            };
            AddIfPresent(query, Consts.MCP.Server.HubQuery.Engine, Engine);
            AddIfPresent(query, Consts.MCP.Server.HubQuery.ProjectName, ProjectName);
            AddIfPresent(query, Consts.MCP.Server.HubQuery.ProjectPathHash, ProjectPathHash);
            AddIfPresent(query, Consts.MCP.Server.HubQuery.MachineName, MachineName);
            return query;
        }

        /// <summary>
        /// Appends <see cref="ToQuery"/> as URL query parameters onto <paramref name="url"/>, preserving
        /// any existing query string and URL-encoding every value. The token is never included here — it
        /// is presented in the <c>Authorization</c> header.
        /// </summary>
        public string AppendToUrl(string url)
        {
            if (url == null)
                throw new ArgumentNullException(nameof(url));

            var query = ToQuery();
            if (query.Count == 0)
                return url;

            var builder = new StringBuilder(url);
            var first = url.IndexOf('?') < 0;
            foreach (var kvp in query)
            {
                builder.Append(first ? '?' : '&');
                first = false;
                builder.Append(Uri.EscapeDataString(kvp.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(kvp.Value));
            }
            return builder.ToString();
        }

        static void AddIfPresent(IDictionary<string, string> query, string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
                query[key] = value;
        }

        static string SafeMachineName()
        {
            try
            {
                return Environment.MachineName ?? string.Empty;
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
        }
    }
}
