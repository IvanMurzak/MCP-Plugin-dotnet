/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System;
using com.IvanMurzak.McpPlugin.Server.Auth;

namespace com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure
{
    /// <summary>
    /// Opts the current async flow in to skill metadata for the lifetime of the scope, then
    /// restores whatever <see cref="McpSessionTokenContext.IsSkillMetaClient"/> held before.
    ///
    /// <para>Exists so a unit test that exercises the <c>_meta</c> skill keys WITHOUT running the
    /// HTTP middleware can still reach the gate. The restore is what keeps the ambient
    /// <c>AsyncLocal</c> from bleeding into a later test on the same flow — the same discipline the
    /// production middleware applies in its own <c>finally</c>.</para>
    ///
    /// <para>It deliberately touches ONLY the skill-metadata slot. A helper that also flipped
    /// <see cref="McpSessionTokenContext.IsTrustedInternalClient"/> would make every test using it
    /// blind to the two axes being conflated, which is precisely the regression the split exists to
    /// prevent.</para>
    /// </summary>
    public sealed class SkillMetaClientScope : IDisposable
    {
        readonly bool _previous;

        SkillMetaClientScope(bool previous)
        {
            _previous = previous;
        }

        /// <summary>Sets the skill-metadata flag for this flow; dispose to restore the prior value.</summary>
        public static SkillMetaClientScope OptIn()
        {
            var previous = McpSessionTokenContext.IsSkillMetaClient;
            McpSessionTokenContext.IsSkillMetaClient = true;
            return new SkillMetaClientScope(previous);
        }

        public void Dispose()
        {
            McpSessionTokenContext.IsSkillMetaClient = _previous;
        }
    }
}
