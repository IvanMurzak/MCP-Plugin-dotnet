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
        /// (design 04 step 5, second variant). Engine-neutral: the install steps come from
        /// <c>enroll_engine_plugin</c>, whose instructions the authorization server supplies.
        /// </summary>
        public const string AccountEmpty =
            "No game engine is connected for your account. To set up: call enroll_engine_plugin with your " +
            "engine's identifier, follow the returned install instructions, then open the project in the editor.";

        /// <summary>
        /// The text for a pinned session whose project's editor is closed while the account HAS other
        /// live instances (design 04 step 5, first variant). Lists the other connected instances and
        /// never suggests re-installing. A project-key session (<paramref name="boundProjectPin"/> set) sees only
        /// its own project's instances (project-keys contract §5) — never a sibling project of the account.
        /// </summary>
        public static string PinnedNoMatch(AccountInstances instances, string? accountId, string? boundProjectPin = null)
        {
            var others = instances.GetInstances(accountId);
            if (!string.IsNullOrEmpty(boundProjectPin))
                others = others.Where(i => i.MatchesPin(boundProjectPin)).ToList();
            var list = others.Count == 0
                ? "(none)"
                : string.Join(", ", others.Select(i => $"{i.Engine}:{i.ProjectName} on {i.MachineName}"));
            return "The engine for this project is not connected — open the project in your editor. " +
                   $"Other connected instances: {list}.";
        }

        /// <summary>
        /// Maps an unresolved <see cref="InstanceResolution"/> to its agent-actionable text. A project-key session
        /// (<paramref name="boundProjectPin"/> set) never gets <see cref="AccountEmpty"/>: it cannot call
        /// <c>enroll_engine_plugin</c> (contract §4), so it is told to open its project instead.
        /// </summary>
        public static string ForResolution(InstanceResolution resolution, AccountInstances instances, string? accountId, string? boundProjectPin = null)
        {
            return resolution.Kind == InstanceResolutionKind.NoMatchPinned || !string.IsNullOrEmpty(boundProjectPin)
                ? PinnedNoMatch(instances, accountId, boundProjectPin)
                : AccountEmpty;
        }
    }
}
