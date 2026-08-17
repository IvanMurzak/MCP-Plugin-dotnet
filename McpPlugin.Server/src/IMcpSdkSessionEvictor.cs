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
using System.Threading.Tasks;

namespace com.IvanMurzak.McpPlugin.Server
{
    /// <summary>What happened when an SDK-owned MCP session was asked to go away.</summary>
    public enum SdkEvictionOutcome
    {
        /// <summary>
        /// The session was found in the SDK's session manager and removed from it. This is the outcome
        /// that actually stops the leak — see <see cref="IMcpSdkSessionEvictor"/>.
        /// </summary>
        Evicted = 0,

        /// <summary>
        /// The SDK's session manager did not hold that id. Benign and expected: a session that already
        /// ended (client <c>DELETE</c>, idle timeout, process-level shutdown) is already gone.
        /// </summary>
        NotFound = 1,

        /// <summary>
        /// The eviction seam could not be bound at all — the SDK's shape changed under us.
        /// <b>This is the failure mode that silently restores the six-hour session pile-up</b>, so it is
        /// logged LOUDLY (once) rather than swallowed, and it is asserted by a test.
        /// </summary>
        Unsupported = 2,

        /// <summary>The seam is bound but the call threw. Logged with the exception; never swallowed.</summary>
        Failed = 3,
    }

    /// <summary>
    /// The result of one eviction attempt. Deliberately splits the two halves, because they have very
    /// different timing and very different consequences:
    ///
    /// <list type="bullet">
    /// <item><description><see cref="Outcome"/> is decided <b>synchronously</b>. Removal from the SDK's
    /// session dictionary is what makes the old session unreachable (subsequent requests bearing its id
    /// get <c>404 / -32001 Session not found</c>) and is what stops it being retained — i.e. the leak is
    /// fixed the instant <see cref="Outcome"/> is <see cref="SdkEvictionOutcome.Evicted"/>.</description></item>
    /// <item><description><see cref="DisposeCompletion"/> is the <b>asynchronous</b> half: it completes
    /// when the removed session's transport, SSE writer and <c>McpServer</c> have been disposed, which is
    /// also what cancels the displaced session's <c>RunSessionHandler</c> and therefore what removes its
    /// <see cref="IMcpSessionTracker"/> row. A caller may await it with a bound and continue on timeout
    /// WITHOUT reintroducing the leak, precisely because the synchronous half already ran.</description></item>
    /// </list>
    /// </summary>
    public sealed class SdkEvictionHandle
    {
        public static readonly SdkEvictionHandle Unsupported = new SdkEvictionHandle(SdkEvictionOutcome.Unsupported, Task.CompletedTask);
        public static readonly SdkEvictionHandle NotFound = new SdkEvictionHandle(SdkEvictionOutcome.NotFound, Task.CompletedTask);

        public SdkEvictionHandle(SdkEvictionOutcome outcome, Task disposeCompletion)
        {
            Outcome = outcome;
            DisposeCompletion = disposeCompletion ?? Task.CompletedTask;
        }

        /// <summary>Decided synchronously by <see cref="IMcpSdkSessionEvictor.Evict"/>.</summary>
        public SdkEvictionOutcome Outcome { get; }

        /// <summary>
        /// Completes when the evicted session has finished disposing. <see cref="Task.CompletedTask"/>
        /// when there was nothing to dispose. Never faults — a dispose failure is logged and folded in.
        /// </summary>
        public Task DisposeCompletion { get; }
    }

    /// <summary>
    /// Terminates an MCP session that the <c>ModelContextProtocol.AspNetCore</c> SDK owns.
    ///
    /// <para><b>Why this seam exists at all.</b> MCP session lifetime is SDK-owned, and — verified
    /// against <c>ModelContextProtocol.AspNetCore</c> <b>1.4.0</b> — <i>nothing in the SDK removes a
    /// session from its dictionary when that session's <c>RunSessionHandler</c> returns.</i> Returning
    /// early from the handler, or cancelling the token it was handed, therefore ends OUR bookkeeping and
    /// leaves the SDK's <c>StatefulSessionManager</c> entry in place until <c>IdleTimeout</c> or
    /// <c>MaxIdleSessionCount</c> prunes it. That shape is the trap this task was written to avoid: a
    /// replace rule built on cancellation alone would drop tracker rows, look fixed, and leave the actual
    /// pile-up untouched. The only operation that genuinely evicts is the one the SDK's own <c>DELETE</c>
    /// handler performs: <c>StatefulSessionManager.TryRemove(id, out session)</c> followed by
    /// <c>session.DisposeAsync()</c>.</para>
    ///
    /// <para><b>Why an interface.</b> The implementation has to reach an <c>internal</c> SDK type by
    /// reflection (see <see cref="McpSdkSessionEvictor"/>). Keeping it behind a seam lets the
    /// replace-by-identity ALGEBRA — which account and which installation displace which session — be
    /// tested without a live Kestrel host, while the real seam is proven separately against the real SDK.
    /// </para>
    /// </summary>
    public interface IMcpSdkSessionEvictor
    {
        /// <summary>
        /// Whether the eviction seam is bound to the running SDK. <c>false</c> means a replace can no
        /// longer terminate the SDK's session and the leak is back — treat it as a release blocker, not a
        /// degraded mode. Asserted by a test so an SDK bump fails CI instead of failing production.
        /// </summary>
        bool IsSupported { get; }

        /// <summary>
        /// Human-readable description of how the binding resolved (or why it did not). Safe to log: it
        /// contains type and member names only, never a session id, a credential, or an instance id.
        /// </summary>
        string BindingDiagnostic { get; }

        /// <summary>
        /// Removes <paramref name="mcpSessionId"/> from the SDK's session manager synchronously and
        /// starts disposing it. Never throws — every failure is reported through the returned handle.
        /// </summary>
        SdkEvictionHandle Evict(string mcpSessionId);
    }
}
