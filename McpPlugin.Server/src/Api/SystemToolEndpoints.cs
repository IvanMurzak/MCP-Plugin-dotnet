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
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace com.IvanMurzak.McpPlugin.Server.Api
{
    /// <summary>
    /// Provides HTTP API endpoints for system tools — internal tools that are NOT exposed
    /// to MCP clients or AI agents. These are only accessible via direct HTTP calls.
    ///
    /// Endpoints (unpinned group):
    ///   GET  /api/system-tools        — list all available system tools with schemas
    ///   POST /api/system-tools/{name} — execute a named system tool with JSON arguments
    ///
    /// Endpoints (project-pinned group — owner ruling 2026-07-25, REVERSES design 06 D15):
    ///   GET  /p/{pin}/api/system-tools        — list system tools for the pinned project's engine instance
    ///   POST /p/{pin}/api/system-tools/{name} — execute a system tool against the pinned project's instance
    ///
    /// The pinned group mirrors <see cref="DirectToolCallEndpoints"/> exactly: both prefixes register through
    /// the SAME <see cref="MapSystemToolGroup"/> registrar (same handlers, same <see cref="AuthGating"/>
    /// decision), and the pin is a route token only — its value is captured from the ORIGINAL request path by
    /// <see cref="Auth.McpSessionTokenMiddleware"/> and flows through the ambient session context to
    /// <c>AccountMcpStrategy.ResolveCurrentSession</c>, making resolution STRICT by project (an unmatched pin
    /// yields <c>NoMatchPinned</c>/<c>AccountEmpty</c>, never MRU).
    ///
    /// <para><b>Why the pinned group exists (D15 reversed).</b> Design 06 D15 declined a pinned system-tools
    /// route on the premise that "no engine registers a system <c>ping</c> tool". That premise was FALSE: the
    /// 2026-07-20 review surveyed Godot/Unreal (where the defect was filed) and missed Unity, which declares
    /// <c>ping</c> as <c>ToolType = McpToolType.System</c>. Since the cloud golden path is pinned
    /// (<c>/mcp/p/&lt;pin&gt;/…</c>), without this group a client cannot probe engine liveness over the
    /// system-tools surface on the cloud path at all. The owner ruling of 2026-07-25 ("<c>ping</c> MUST be a
    /// system tool on every engine") therefore reverses D15 deliberately.</para>
    ///
    /// The public <c>/mcp/p/{pin}/…</c> form is served by the same routes after nginx strips the <c>/mcp</c>
    /// prefix (mirroring the existing unpinned convention, where nginx strips <c>/mcp</c> ahead of
    /// <c>/api/system-tools</c>).
    /// </summary>
    public static class SystemToolEndpoints
    {
        const string RoutePrefix = "/api/system-tools";

        // The project-pinned variant of RoutePrefix. {pin} is a route token only so the endpoint matches;
        // the pin VALUE is captured from the original path by McpSessionTokenMiddleware, exactly like the
        // pinned tool routes (DirectToolCallEndpoints.PinnedRoutePrefix) and the pinned MCP routes
        // (StreamableHttpTransportLayer.MapPinnedMcp). No route constraint on {pin} — the middleware
        // validates it loosely (1–64 hex) and ignores a malformed segment, matching the MCP path.
        const string PinnedRoutePrefix = "/p/{pin}" + RoutePrefix;

        /// <summary>
        /// Maps the UNPINNED system tool API endpoints (<c>/api/system-tools</c>) onto the given
        /// <see cref="WebApplication"/>.
        /// Authorization follows the same rules as the direct tool call endpoints: required in every
        /// credential-bearing mode — <see cref="Consts.MCP.Server.AuthOption.oauth"/>, the offline
        /// <see cref="Consts.MCP.Server.AuthOption.token"/> (mcp-authorize g6), and the deprecated
        /// <see cref="Consts.MCP.Server.AuthOption.required"/> alias (fail closed) — open only in
        /// <see cref="Consts.MCP.Server.AuthOption.none"/> mode. (Before mcp-authorize b7 this gated on the
        /// then-unreachable <c>required</c> value, so it was never gated in oauth mode; g6 closes the token-mode gap.)
        /// </summary>
        public static WebApplication MapSystemToolApi(this WebApplication app, IDataArguments dataArguments)
        {
            MapSystemToolGroup(app, dataArguments, RoutePrefix);
            return app;
        }

        /// <summary>
        /// Maps the PROJECT-PINNED system tool API endpoints (<c>/p/{pin}/api/system-tools</c>) onto the given
        /// <see cref="WebApplication"/> (owner ruling 2026-07-25, reversing design 06 D15 — see the type
        /// docblock). Registered alongside — and byte-identical in behavior to — the unpinned group (both
        /// delegate to <see cref="MapSystemToolGroup"/> with the SAME handlers and the SAME auth gate); the
        /// ONLY difference is the route prefix carries a <c>{pin}</c> segment, which
        /// <see cref="Auth.McpSessionTokenMiddleware"/> captures from the request path so system-tool
        /// resolution is pinned STRICTLY to that project's engine instance (never MRU-routed to a sibling).
        /// </summary>
        public static WebApplication MapPinnedSystemToolApi(this WebApplication app, IDataArguments dataArguments)
        {
            MapSystemToolGroup(app, dataArguments, PinnedRoutePrefix);
            return app;
        }

        /// <summary>
        /// Shared registrar for a system-tool route group. Both the unpinned (<c>/api/system-tools</c>) and the
        /// project-pinned (<c>/p/{pin}/api/system-tools</c>) groups route through this ONE method with the SAME
        /// list/call handlers and the SAME <see cref="AuthGating"/> decision, so they are byte-identical in
        /// behavior by construction — the pinned group differs only by the <c>{pin}</c> path token (captured by
        /// the middleware, not read here). Keeping the auth gate in lockstep guarantees a credential-bearing
        /// mode can never leave one group gated and the other open.
        /// </summary>
        static void MapSystemToolGroup(WebApplication app, IDataArguments dataArguments, string routePrefix)
        {
            if (app == null)
                throw new ArgumentNullException(nameof(app));

            if (dataArguments == null)
                throw new ArgumentNullException(nameof(dataArguments));

            var requireAuth = AuthGating.RequiresAuthorization(dataArguments.Authorization);
            var group = app.MapGroup(routePrefix);

            // GET <prefix> — list all registered system tools
            var listEndpoint = group.MapGet("/", ListSystemToolsHandler);

            // POST <prefix>/{name} — invoke a system tool by name
            var callEndpoint = group.MapPost("/{name}", CallSystemToolHandler);

            if (requireAuth)
            {
                listEndpoint.RequireAuthorization();
                callEndpoint.RequireAuthorization();
            }
        }

        /// <summary>
        /// Returns a JSON array of all available system tools with their name, title, description, and schemas.
        /// </summary>
        static async Task ListSystemToolsHandler(HttpContext context)
        {
            var systemToolHub = context.RequestServices.GetRequiredService<IClientSystemToolHub>();
            var request = new RequestListTool();
            var response = await systemToolHub.RunListSystemTool(request, context.RequestAborted);

            if (response == null)
            {
                context.Response.StatusCode = 502;
                await context.Response.WriteAsJsonAsync(new { error = "System tool hub returned a null response." });
                return;
            }

            if (response.Status == ResponseStatus.Error)
            {
                context.Response.StatusCode = DirectHttpErrorMapper.GetStatusCode(response, 500);
                await context.Response.WriteAsJsonAsync(new { error = response.Message ?? "Failed to list system tools." });
                return;
            }

            var tools = new List<JsonObject>();
            if (response.Value != null)
            {
                foreach (var tool in response.Value)
                {
                    if (tool == null) continue;

                    var entry = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["enabled"] = tool.Enabled
                    };
                    if (tool.Title != null)
                        entry["title"] = tool.Title;
                    if (tool.Description != null)
                        entry["description"] = tool.Description;
                    if (tool.InputSchema is JsonElement inputEl)
                        entry["inputSchema"] = JsonNode.Parse(inputEl.GetRawText());
                    if (tool.OutputSchema is JsonElement outputEl)
                        entry["outputSchema"] = JsonNode.Parse(outputEl.GetRawText());

                    tools.Add(entry);
                }
            }

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(tools);
        }

        /// <summary>
        /// Executes a named system tool by forwarding the JSON request body as arguments via SignalR.
        /// </summary>
        static async Task CallSystemToolHandler(HttpContext context)
        {
            var systemToolHub = context.RequestServices.GetRequiredService<IClientSystemToolHub>();
            var name = context.Request.RouteValues["name"]?.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "System tool name must not be empty." });
                return;
            }

            IReadOnlyDictionary<string, JsonElement> arguments;
            try
            {
                if ((context.Request.ContentLength ?? 0) == 0 || !context.Request.HasJsonContentType())
                {
                    arguments = new Dictionary<string, JsonElement>();
                }
                else
                {
                    using var doc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                        foreach (var prop in doc.RootElement.EnumerateObject())
                            dict[prop.Name] = prop.Value.Clone();
                        arguments = dict;
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Null)
                    {
                        arguments = new Dictionary<string, JsonElement>();
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new { error = "Request body must be a JSON object (or empty for tools with no parameters)." });
                        return;
                    }
                }
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = $"Invalid JSON body: {ex.Message}" });
                return;
            }
            catch (Exception)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { error = "Failed to read the request body." });
                return;
            }

            // ── Webhook instrumentation ─────────────────────────────────
            var webhookOptions = context.RequestServices.GetService<WebhookOptions>();
            Stopwatch? stopwatch = null;
            long requestSize = 0;
            if (webhookOptions?.IsToolEnabled == true)
            {
                stopwatch = Stopwatch.StartNew();
                if (arguments.Count > 0)
                {
                    try
                    {
                        requestSize = JsonSerializer.SerializeToUtf8Bytes(arguments).Length;
                    }
                    catch (Exception) { /* measurement failure is non-fatal */ }
                }
            }

            var requestData = new RequestCallTool(name, arguments);
            var response = await systemToolHub.RunSystemTool(requestData, context.RequestAborted);

            // ── Fire webhook ────────────────────────────────────────────
            if (webhookOptions?.IsToolEnabled == true)
            {
                stopwatch!.Stop();
                var collector = context.RequestServices.GetService<IWebhookEventCollector>();
                if (collector != null)
                {
                    var isError = response == null || response.Status == ResponseStatus.Error;
                    long responseSize = 0;
                    string? errorDetails = null;

                    if (isError)
                    {
                        errorDetails = response?.Message ?? "System tool hub returned a null response.";
                    }
                    else if (response?.Value != null)
                    {
                        try
                        {
                            var responseBytes = JsonSerializer.SerializeToUtf8Bytes(response.Value);
                            responseSize = responseBytes.LongLength;
                        }
                        catch (Exception) { /* measurement failure is non-fatal */ }
                    }

                    collector.OnToolCall(
                        name,
                        requestSize,
                        responseSize,
                        isError ? "failure" : "success",
                        stopwatch.ElapsedMilliseconds,
                        errorDetails,
                        channel: "http");
                }
            }

            if (response == null)
            {
                context.Response.StatusCode = 502;
                await context.Response.WriteAsJsonAsync(new { error = "System tool hub returned a null response." });
                return;
            }

            if (response.Status == ResponseStatus.Error)
            {
                context.Response.StatusCode = DirectHttpErrorMapper.GetStatusCode(response, 400);
                await context.Response.WriteAsJsonAsync(new { error = response.Message ?? $"System tool '{name}' returned an error." });
                return;
            }

            if (response.Value == null)
            {
                await context.Response.WriteAsJsonAsync(new { status = "success", content = Array.Empty<object>() });
                return;
            }

            if (response.Value.StructuredContent != null)
            {
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "success",
                    structured = response.Value.StructuredContent
                });
                return;
            }

            await context.Response.WriteAsJsonAsync(new
            {
                status = "success",
                content = response.Value.Content
            });
        }
    }
}
