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
using com.IvanMurzak.McpPlugin.Common;
using Microsoft.AspNetCore.Http;

namespace com.IvanMurzak.McpPlugin.Server.Auth
{
    /// <summary>
    /// The single definition of how a client opt-in header is read.
    ///
    /// <para>Two call sites read these headers, and they read them from DIFFERENT places in the
    /// request lifetime: <see cref="McpSessionTokenMiddleware"/> reads every request's headers, and
    /// <c>StreamableHttpTransportLayer.RunSessionHandler</c> reads the <c>initialize</c> request's
    /// headers to fix the value for the whole MCP session. A second, hand-copied comparison at the
    /// second site is exactly how the two would drift — one of them accepting <c>"true"</c> while
    /// the other did not, with the difference visible only over a real transport. Both sites call
    /// through here instead.</para>
    ///
    /// <para><b>Tri-state on purpose.</b> ABSENT is not the same as PRESENT-but-not-the-opt-in-value:
    /// the middleware leaves the ambient flag untouched when the header is absent (so it can never
    /// clobber a value another writer established) while the session handler treats absent as an
    /// explicit "no". Collapsing the two into a plain <see cref="bool"/> would silently pick one of
    /// those behaviours for both.</para>
    /// </summary>
    public static class ClientOptInHeaders
    {
        /// <summary>
        /// Reads one opt-in header. Returns <see langword="null"/> when the header is absent,
        /// otherwise whether its value is EXACTLY <paramref name="optInValue"/>.
        ///
        /// <para>The comparison is <see cref="StringComparison.Ordinal"/>, so <c>"true"</c>,
        /// <c>"yes"</c>, <c>"TRUE"</c>, <c>"01"</c> and <c>"1"</c> with stray whitespace all fail.
        /// That strictness is deliberate and pinned by
        /// <c>SkillMetaClientGateTests.Middleware_SkillMetaHeader_WithAnyOtherValue_IsTreatedAsAbsent</c>.</para>
        /// </summary>
        public static bool? Read(IHeaderDictionary? headers, string headerName, string optInValue)
        {
            if (headers == null)
                return null;
            if (!headers.TryGetValue(headerName, out var value))
                return null;
            return string.Equals(value.ToString(), optInValue, StringComparison.Ordinal);
        }

        /// <summary>
        /// The skill-metadata opt-in (<c>X-McpPlugin-Skill-Meta: 1</c>) — see
        /// <see cref="McpSessionTokenContext.IsSkillMetaClient"/> for the session semantics.
        /// </summary>
        public static bool? ReadSkillMetaClient(IHeaderDictionary? headers)
            => Read(headers, Consts.MCP.Server.Headers.SkillMetaClient, Consts.MCP.Server.Headers.SkillMetaClientOptInValue);

        /// <summary>
        /// The trusted-internal-client opt-in (<c>X-McpPlugin-Internal-Client: 1</c>) — see
        /// <see cref="McpSessionTokenContext.IsTrustedInternalClient"/>.
        /// </summary>
        public static bool? ReadTrustedInternalClient(IHeaderDictionary? headers)
            => Read(headers, Consts.MCP.Server.Headers.TrustedInternalClient, Consts.MCP.Server.Headers.TrustedInternalClientOptInValue);
    }
}
