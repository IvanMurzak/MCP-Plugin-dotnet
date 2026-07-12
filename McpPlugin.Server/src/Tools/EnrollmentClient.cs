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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Tools
{
    /// <summary>Outcome of proxying an enrollment-create to the authorization server (design 05).</summary>
    public readonly struct EnrollmentResult
    {
        public bool Success { get; }

        /// <summary>The one-time enrollment code when <see cref="Success"/>; else null.</summary>
        public string? EnrollCode { get; }

        /// <summary>An agent-facing failure reason when not <see cref="Success"/>; else null. Never carries the credential.</summary>
        public string? Error { get; }

        EnrollmentResult(bool success, string? enrollCode, string? error)
        {
            Success = success;
            EnrollCode = enrollCode;
            Error = error;
        }

        public static EnrollmentResult Ok(string enrollCode) => new EnrollmentResult(true, enrollCode, null);
        public static EnrollmentResult Fail(string error) => new EnrollmentResult(false, null, error);
    }

    /// <summary>
    /// POSTs the enroll/create request to the authorization server with (bearer, engine, publicUrl)
    /// and returns the raw JSON response body, or null on transport/HTTP failure. Mirrors the b2
    /// <c>JwksFetch</c>/<c>IntrospectionPost</c> delegate seam so tests inject a fake without a live
    /// <c>HttpClient</c> (design 05 <c>POST /api/auth/enroll/create</c>).
    /// </summary>
    public delegate Task<string?> EnrollCreatePost(string bearer, string engine, string publicUrl, CancellationToken cancellationToken);

    /// <summary>
    /// The RS-side enrollment proxy (mcp-authorize b4, design 04 <c>enroll_engine_plugin</c> + design
    /// 05 enrollment endpoints). Forwards the agent session's bearer credential (JWT or introspected
    /// <c>mcp:agent</c> PAT — both accepted at enroll/create) plus THIS RS's public URL to the AS, and
    /// returns the minted one-time code. <b>Never logs the credential.</b>
    /// </summary>
    public interface IEnrollmentClient
    {
        Task<EnrollmentResult> CreateAsync(string engine, string bearer, CancellationToken cancellationToken = default);
    }

    /// <inheritdoc cref="IEnrollmentClient"/>
    public sealed class EnrollmentClient : IEnrollmentClient
    {
        readonly EnrollCreatePost _post;
        readonly string _publicUrl;
        readonly ILogger? _logger;

        /// <param name="post">The transport delegate that performs the authenticated POST to the AS.</param>
        /// <param name="publicUrl">This RS's canonical public URL (<c>MCP_PUBLIC_URL</c>); embedded in the minted code.</param>
        public EnrollmentClient(EnrollCreatePost post, string publicUrl, ILogger? logger = null)
        {
            _post = post ?? throw new ArgumentNullException(nameof(post));
            if (string.IsNullOrWhiteSpace(publicUrl))
                throw new ArgumentException("publicUrl must be non-empty.", nameof(publicUrl));
            _publicUrl = publicUrl;
            _logger = logger;
        }

        public async Task<EnrollmentResult> CreateAsync(string engine, string bearer, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(engine))
                return EnrollmentResult.Fail("An engine (unity|godot|unreal) is required to enroll.");
            if (string.IsNullOrEmpty(bearer))
                return EnrollmentResult.Fail("No session credential is available to enroll with.");

            string? json;
            try
            {
                json = await _post(bearer, engine, _publicUrl, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Log the failure WITHOUT the credential (never include `bearer` in any log).
                _logger?.LogWarning(ex, "Enrollment create request to the authorization server failed for engine {engine}.", engine);
                return EnrollmentResult.Fail("Could not reach the authorization server to create an enrollment code. Try again shortly.");
            }

            if (string.IsNullOrEmpty(json))
                return EnrollmentResult.Fail("The authorization server rejected the enrollment request.");

            try
            {
                using var doc = JsonDocument.Parse(json!);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("enroll_code", out var codeEl)
                    && codeEl.ValueKind == JsonValueKind.String)
                {
                    var code = codeEl.GetString();
                    if (!string.IsNullOrEmpty(code))
                        return EnrollmentResult.Ok(code!);
                }
                return EnrollmentResult.Fail("The authorization server response did not contain an enrollment code.");
            }
            catch (JsonException)
            {
                return EnrollmentResult.Fail("The authorization server returned a malformed response.");
            }
        }
    }
}
