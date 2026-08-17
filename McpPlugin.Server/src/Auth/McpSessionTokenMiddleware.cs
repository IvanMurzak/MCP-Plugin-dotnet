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
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Server.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace com.IvanMurzak.McpPlugin.Server.Auth
{
    /// <summary>
    /// The outcome of parsing the project pin out of a request path (design 04 D14).
    /// The three states are deliberately distinct because <b>absent is not malformed</b>:
    /// an unpinned request is a supported, unprivileged mode, while a present-but-unparseable
    /// pin is a rejected request.
    /// </summary>
    public enum ProjectPinParse
    {
        /// <summary>No <c>/p/&lt;pin&gt;</c> marker on the path — the request is genuinely unpinned.</summary>
        Absent = 0,

        /// <summary>A well-formed pin (1–64 hex chars) was captured.</summary>
        Valid = 1,

        /// <summary>
        /// A <c>/p/</c> marker is present but its value is not a well-formed pin. The request MUST be
        /// rejected — downgrading it to <see cref="Absent"/> would silently re-enable MRU fallthrough.
        /// </summary>
        Malformed = 2,
    }

    /// <summary>
    /// Propagates the bearer token into <see cref="McpSessionTokenContext.CurrentToken"/>
    /// so downstream services (e.g. RemoteToolRunner) can route calls to the correct plugin.
    ///
    /// Resolution order:
    ///   1. <see cref="TokenAuthenticationHandler.TokenClaimType"/> claim on <see cref="HttpContext.User"/>
    ///      (present when auth is required and the handler validated the token).
    ///   2. Raw <c>Authorization: Bearer …</c> header fallback — covers the case where auth
    ///      is not required (<see cref="TokenAuthenticationHandler"/> returns <c>NoResult</c>)
    ///      but the caller still sends a token for plugin routing.
    ///
    /// Must run after <c>UseAuthentication()</c> and before endpoint handlers.
    ///
    /// <para><b>Fail-closed pin gate.</b> The <c>/p/&lt;pin&gt;</c> segment is parsed FIRST, and a pin that is
    /// PRESENT but unparseable short-circuits the pipeline with <c>400 invalid_project_pin</c> — see
    /// <see cref="TryExtractProjectPin(string?, out string?)"/> for why that is a security boundary and not a
    /// convenience.</para>
    /// </summary>
    public class McpSessionTokenMiddleware
    {
        /// <summary>The MCP Streamable-HTTP session header (case-insensitive lookup).</summary>
        public const string SessionIdHeader = "Mcp-Session-Id";

        /// <summary>
        /// The path segment that marks the following segment as a project pin (<c>/p/&lt;pin&gt;</c>).
        /// Matched CASE-INSENSITIVELY, because ASP.NET Core matches the literal <c>p</c> segment of the
        /// pinned route templates case-insensitively too: an Ordinal match here would let <c>/P/&lt;pin&gt;</c>
        /// route as PINNED while parsing as unpinned — the exact downgrade this gate exists to stop.
        /// </summary>
        const string PinMarkerSegment = "p";

        /// <summary>Machine-readable error code returned when a present pin cannot be parsed.</summary>
        public const string InvalidProjectPinError = "invalid_project_pin";

        private readonly RequestDelegate _next;

        public McpSessionTokenMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            // ── Project-pin gate (design 04 D14) — runs BEFORE any ambient session state is set. ──
            // A pin that is PRESENT on the path but unparseable is REJECTED, never downgraded to
            // "unpinned": the downgrade made AccountInstances.Resolve skip its strict pinned branch and
            // fall back to sticky → single → MRU, so a caller that believed it was pinned to project A
            // silently executed tool calls against an arbitrary sibling project B. ABSENT ≠ MALFORMED —
            // a path carrying no /p/ marker stays genuinely unpinned and is unaffected.
            var pinParse = TryExtractProjectPin(context.Request.Path.Value, out var projectPin);
            if (pinParse == ProjectPinParse.Malformed)
            {
                await WriteInvalidProjectPinAsync(context).ConfigureAwait(false);
                return;
            }

            // ── Instance-identity gate (design 07 §2.3, D10.2) — same fail-closed shape as the pin. ──
            // ABSENT is a supported state and is NOT touched here: every currently released client sends
            // no AI-Game-Dev-Instance-Id header, and those requests must behave exactly as they do today.
            // A header that is PRESENT but not a well-formed opaque token is REJECTED rather than ignored,
            // for the same reason a malformed pin is: ignoring it would leave a client that believes it is
            // participating in the replace rule silently stacking sessions instead — and the only symptom
            // would be the six-hour pile-up this rule was written to end. Validating here, before the value
            // is used or stored anywhere, is also what makes every downstream use injection-proof by
            // construction rather than by remembering to escape.
            if (InstanceIdentityHeader.TryRead(context.Request.Headers, out _) == InstanceIdentityParse.Malformed)
            {
                await WriteInvalidInstanceIdAsync(context).ConfigureAwait(false);
                return;
            }

            var tokenClaim = context.User?.FindFirst(TokenAuthenticationHandler.TokenClaimType);
            if (tokenClaim != null)
            {
                McpSessionTokenContext.CurrentToken = tokenClaim.Value;
            }
            else
            {
                // Fallback: extract from raw header when auth handler did not populate claims
                // (e.g. auth not required, but caller sends a token for plugin routing).
                var authHeader = context.Request.Headers.Authorization.ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    McpSessionTokenContext.CurrentToken = authHeader.Substring("Bearer ".Length).Trim();
            }

            // OAuth account routing (mcp-authorize b3): resolve the request identity from the validated
            // claims the TokenAuthenticationHandler OAuth path issued (sub → AccountId, scope → Role).
            // Null-safe in legacy modes — no sub claim ⇒ null identity ⇒ token-equality routing unchanged.
            McpSessionTokenContext.CurrentIdentity = ConnectionIdentity.FromPrincipal(context.User);

            // Publish the project pin captured above (null when the path carried no /p/ marker at all).
            McpSessionTokenContext.CurrentProjectPin = projectPin;

            // Capture the MCP session id (mcp-authorize b4) and reload this session's sticky
            // engine-instance selection so account routing honors it (design 04 step 2). The store is
            // registered only in oauth mode — null in legacy/no-auth modes leaves selection untouched.
            var sessionId = context.Request.Headers.TryGetValue(SessionIdHeader, out var sid) ? sid.ToString() : null;
            McpSessionTokenContext.CurrentSessionId = string.IsNullOrEmpty(sessionId) ? null : sessionId;
            var selectionStore = context.RequestServices?.GetService<ISessionSelectionStore>();
            if (selectionStore != null)
                McpSessionTokenContext.CurrentSelectedInstanceId = selectionStore.Get(sessionId);

            var remoteIp = context.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrEmpty(remoteIp))
                McpSessionTokenContext.CurrentClientIp = remoteIp;

            var userAgent = context.Request.Headers.UserAgent.ToString();
            if (!string.IsNullOrEmpty(userAgent))
                McpSessionTokenContext.CurrentUserAgent = userAgent;

            if (context.Request.Headers.TryGetValue(Consts.MCP.Server.Headers.TrustedInternalClient, out var trustedHeader))
            {
                McpSessionTokenContext.IsTrustedInternalClient = string.Equals(
                    trustedHeader.ToString(),
                    Consts.MCP.Server.Headers.TrustedInternalClientOptInValue,
                    StringComparison.Ordinal);
            }

            try
            {
                await _next(context);
            }
            finally
            {
                McpSessionTokenContext.CurrentToken = null;
                McpSessionTokenContext.CurrentClientIp = null;
                McpSessionTokenContext.CurrentUserAgent = null;
                McpSessionTokenContext.IsTrustedInternalClient = false;
                McpSessionTokenContext.CurrentIdentity = null;
                McpSessionTokenContext.CurrentProjectPin = null;
                McpSessionTokenContext.CurrentSelectedInstanceId = null;
                McpSessionTokenContext.CurrentSessionId = null;
            }
        }

        /// <summary>
        /// Parses the project pin out of a request path whose config URL ends in a <c>/p/&lt;pin&gt;</c>
        /// segment (design 04 D14). The pin is the leading hex prefix of the project SHA-256, validated as
        /// 1–64 hex chars; when several well-formed markers appear the LAST one wins (the config URL suffix).
        /// The <c>p</c> marker is matched CASE-INSENSITIVELY to stay in lockstep with routing (see
        /// <see cref="PinMarkerSegment"/>); the pin VALUE may be upper- or lower-case hex and is lower-cased.
        ///
        /// <para><b>Tri-state on purpose — fail closed.</b> This used to return <c>null</c> for BOTH "no
        /// <c>/p/</c> marker" and "marker present, value unparseable", which made the two indistinguishable
        /// to the caller. Because the pinned routes carry no route constraint on <c>{pin}</c>, a request
        /// like <c>/p/not-a-pin/api/tools/{name}</c> still matched an endpoint, arrived with a null pin, and
        /// was resolved as UNPINNED — <see cref="Strategy.AccountInstances.Resolve"/> skipped its strict
        /// pinned branch and fell through to <c>sticky → single → MRU</c>, executing the call against an
        /// arbitrary sibling project. The failure was asymmetric: a wrong-but-HEX pin failed closed
        /// (<c>NoMatchPinned</c>), only a MALFORMED one failed open. Callers must now distinguish
        /// <see cref="ProjectPinParse.Absent"/> (supported, unpinned) from
        /// <see cref="ProjectPinParse.Malformed"/> (reject the request).</para>
        /// </summary>
        /// <param name="path">The ORIGINAL request path (route-parameter matching preserves it).</param>
        /// <param name="pin">The lower-cased pin when the result is <see cref="ProjectPinParse.Valid"/>; else null.</param>
        /// <returns>Whether the path carried no pin, a well-formed pin, or an unparseable one.</returns>
        public static ProjectPinParse TryExtractProjectPin(string? path, out string? pin)
        {
            pin = null;
            if (string.IsNullOrEmpty(path))
                return ProjectPinParse.Absent;

            var segments = path!.Split('/');
            var sawMarker = false;
            for (var i = 0; i + 1 < segments.Length; i++)
            {
                // Case-INSENSITIVE: routing matches the literal "p" segment that way, so "/P/<pin>" reaches
                // the pinned endpoints and must be parsed as pinned (see PinMarkerSegment).
                if (!string.Equals(segments[i], PinMarkerSegment, StringComparison.OrdinalIgnoreCase))
                    continue;

                sawMarker = true;
                var candidate = segments[i + 1];
                if (!IsHex(candidate))
                {
                    // Present but unparseable (incl. an empty segment such as "/mcp/p/") ⇒ fail closed.
                    // Never keep an earlier well-formed pin either: the caller's intent is ambiguous.
                    pin = null;
                    return ProjectPinParse.Malformed;
                }

                pin = candidate.ToLowerInvariant();
            }

            return sawMarker ? ProjectPinParse.Valid : ProjectPinParse.Absent;
        }

        /// <summary>
        /// Rejects a request whose path carries an unparseable project pin. Deliberately terminal — the
        /// pipeline is NOT continued, so no handler, hub, or tool ever observes the request.
        /// </summary>
        static Task WriteInvalidProjectPinAsync(HttpContext context)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return context.Response.WriteAsJsonAsync(new
            {
                error = InvalidProjectPinError,
                message = "The project pin path segment is malformed; expected 1-64 hexadecimal characters."
            }, context.RequestAborted);
        }

        /// <summary>
        /// Rejects a request whose <c>AI-Game-Dev-Instance-Id</c> header is present but not a well-formed
        /// opaque token. Terminal, exactly like the pin rejection — the pipeline is NOT continued, so no
        /// handler, hub, session tracker or log formatter ever observes the offending value.
        ///
        /// <para><b>The rejected value is deliberately not echoed.</b> Reflecting an unvalidated,
        /// arbitrary-length client string back into a response body is how a log-injection or a
        /// response-splitting payload gets a second chance; the caller already knows what it sent, and the
        /// error code plus the expected shape is everything a client needs to fix itself.</para>
        /// </summary>
        static Task WriteInvalidInstanceIdAsync(HttpContext context)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return context.Response.WriteAsJsonAsync(new
            {
                error = InstanceIdentityHeader.InvalidInstanceIdError,
                message = $"The {InstanceIdentityHeader.HeaderName} header is malformed; expected exactly " +
                          $"{InstanceIdentityHeader.ExpectedLength} hexadecimal characters, sent at most once."
            }, context.RequestAborted);
        }

        static bool IsHex(string? s)
        {
            if (string.IsNullOrEmpty(s) || s!.Length > 64)
                return false;
            foreach (var c in s)
            {
                var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex)
                    return false;
            }
            return true;
        }
    }
}
