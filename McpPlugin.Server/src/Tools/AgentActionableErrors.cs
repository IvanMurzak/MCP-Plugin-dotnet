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
using System.Linq;
using com.IvanMurzak.McpPlugin.Server.Strategy;

namespace com.IvanMurzak.McpPlugin.Server.Tools
{
    /// <summary>
    /// Builds the two agent-actionable error texts of design 04 step 5, surfaced when an agent invokes
    /// a plugin tool but no instance resolves. Replaces today's silent 10× retry → generic invoke
    /// failure so agents can self-serve recovery. Both variants are PAT-session aware (the credential
    /// kind does not change the guidance — the enroll flow accepts either JWT or PAT).
    /// </summary>
    public static class AgentActionableErrors
    {
        /// <summary>
        /// The <c>enroll_engine_plugin</c>-first recovery text for an account with NO live instances
        /// (design 04 step 5, second variant). Uses unity as the illustrative CLI and names
        /// <c>enroll_engine_plugin</c> for the godot/unreal path.
        /// </summary>
        public const string AccountEmpty =
            "No game engine is connected for your account. To set up: run " +
            "'npx unity-mcp-cli install-plugin --enroll <code>' in the project folder " +
            "(also for godot/unreal via enroll_engine_plugin), then open the project in the editor.";

        /// <summary>
        /// The text for a pinned session whose project's editor is closed while the account HAS other
        /// live instances (design 04 step 5, first variant). Lists the other connected instances and
        /// never suggests re-installing.
        /// </summary>
        public static string PinnedNoMatch(AccountInstances instances, string? accountId)
        {
            var others = instances.GetInstances(accountId);
            var list = others.Count == 0
                ? "(none)"
                : string.Join(", ", others.Select(i => $"{i.Engine}:{i.ProjectName} on {i.MachineName}"));
            return "The engine for this project is not connected — open the project in your editor. " +
                   $"Other connected instances: {list}.";
        }

        /// <summary>Maps an unresolved <see cref="InstanceResolution"/> to its agent-actionable text.</summary>
        public static string ForResolution(InstanceResolution resolution, AccountInstances instances, string? accountId)
        {
            return resolution.Kind == InstanceResolutionKind.NoMatchPinned
                ? PinnedNoMatch(instances, accountId)
                : AccountEmpty;
        }
    }
}
