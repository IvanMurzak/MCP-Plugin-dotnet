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
    /// Provides a direct HTTP API for calling MCP tools without going through the full MCP protocol.
    /// This enables AI agents and automation scripts to invoke tools using standard HTTP/JSON (e.g., curl).
    ///
    /// Endpoints (unpinned group):
    ///   GET  /api/tools           — list all available tools with schemas
    ///   POST /api/tools/{name}    — execute a named tool with JSON arguments
    ///
    /// Endpoints (project-pinned group — design 06 / zero-config-engine-connect b1):
    ///   GET  /p/{pin}/api/tools        — list tools for the pinned project's engine instance
    ///   POST /p/{pin}/api/tools/{name} — execute a named tool against the pinned project's instance
    ///
    /// The pinned group makes multi-project tool dispatch deterministic: with 2+ engine instances on one
    /// account the unpinned group resolves via <c>sticky → single → MRU</c> (<c>AccountInstances.Resolve</c>)
    /// and can bind the agent to the wrong project, while the pinned group resolves STRICTLY by pin (a pin
    /// never falls through to another project — an unmatched pin yields <c>NoMatchPinned</c>/<c>AccountEmpty</c>,
    /// never MRU). The pin is a route token only; its value is captured from the ORIGINAL request path by
    /// <see cref="Auth.McpSessionTokenMiddleware"/> and flows through the ambient session context to
    /// <c>AccountMcpStrategy.ResolveCurrentSession</c> — so the two groups share the SAME handlers and
    /// resolution mechanism, differing only in whether a pin is present on the path. There is deliberately
    /// NO pinned system-tools route (design 06 D15). The public <c>/mcp/p/{pin}/…</c> form is served by the
    /// same routes after nginx strips the <c>/mcp</c> prefix (mirroring the existing unpinned convention,
    /// where nginx strips <c>/mcp</c> ahead of <c>/api/tools</c>).
    /// </summary>
    public static class DirectToolCallEndpoints
    {
        const string RoutePrefix = "/api/tools";

        // The project-pinned variant of RoutePrefix. {pin} is a route token only so the endpoint matches;
        // the pin VALUE is captured from the original path by McpSessionTokenMiddleware, exactly like the
        // pinned MCP routes (StreamableHttpTransportLayer.MapPinnedMcp). No route constraint on {pin} — the
        // middleware validates it loosely (1–64 hex) and ignores a malformed segment, matching the MCP path.
        const string PinnedRoutePrefix = "/p/{pin}" + RoutePrefix;

        /// <summary>
        /// Maps the UNPINNED direct tool call API endpoints (<c>/api/tools</c>) onto the given
        /// <see cref="WebApplication"/>.
        /// Authorization is required in every credential-bearing mode — <see cref="Consts.MCP.Server.AuthOption.oauth"/>,
        /// the offline <see cref="Consts.MCP.Server.AuthOption.token"/> (mcp-authorize g6), and the deprecated
        /// <see cref="Consts.MCP.Server.AuthOption.required"/> alias — so the REST tool surface (which can EXECUTE
        /// tools) is never an unauthenticated bypass of the endpoint's credential gate (fail closed). It is open only
        /// in <see cref="Consts.MCP.Server.AuthOption.none"/> mode. (Before mcp-authorize b7 this gated on the then-unreachable
        /// <c>required</c> value, so it was never gated in oauth mode; g6 additionally closes the token-mode gap.)
        /// </summary>
        public static WebApplication MapDirectToolCallApi(this WebApplication app, IDataArguments dataArguments)
        {
            MapToolCallGroup(app, dataArguments, RoutePrefix);
            return app;
        }

        /// <summary>
        /// Maps the PROJECT-PINNED direct tool call API endpoints (<c>/p/{pin}/api/tools</c>) onto the given
        /// <see cref="WebApplication"/> (design 06 / zero-config-engine-connect b1). Registered alongside — and
        /// byte-identical in behavior to — the unpinned group (both delegate to <see cref="MapToolCallGroup"/>
        /// with the SAME handlers and the SAME auth gate); the ONLY difference is the route prefix carries a
        /// <c>{pin}</c> segment, which <see cref="Auth.McpSessionTokenMiddleware"/> captures from the request path
        /// so tool resolution is pinned STRICTLY to that project's engine instance (never MRU-routed to a sibling).
        /// There is deliberately no pinned system-tools analog (design 06 D15).
        /// </summary>
        public static WebApplication MapPinnedDirectToolCallApi(this WebApplication app, IDataArguments dataArguments)
        {
            MapToolCallGroup(app, dataArguments, PinnedRoutePrefix);
            return app;
        }

        /// <summary>
        /// Shared registrar for a direct tool-call route group. Both the unpinned (<c>/api/tools</c>) and the
        /// project-pinned (<c>/p/{pin}/api/tools</c>) groups route through this ONE method with the SAME list/call
        /// handlers and the SAME <see cref="AuthGating"/> decision, so they are byte-identical in behavior by
        /// construction — the pinned group differs only by the <c>{pin}</c> path token (captured by the middleware,
        /// not read here). Keeping the auth gate in lockstep guarantees a credential-bearing mode can never leave
        /// one group gated and the other open.
        /// </summary>
        static void MapToolCallGroup(WebApplication app, IDataArguments dataArguments, string routePrefix)
        {
            if (app == null)
                throw new ArgumentNullException(nameof(app));

            if (dataArguments == null)
                throw new ArgumentNullException(nameof(dataArguments));

            var requireAuth = AuthGating.RequiresAuthorization(dataArguments.Authorization);
            var group = app.MapGroup(routePrefix);

            // GET <prefix> — list all registered tools
            var listEndpoint = group.MapGet("/", ListToolsHandler);

            // POST <prefix>/{name} — invoke a tool by name
            var callEndpoint = group.MapPost("/{name}", CallToolHandler);

            if (requireAuth)
            {
                listEndpoint.RequireAuthorization();
                callEndpoint.RequireAuthorization();
            }
        }

        /// <summary>
        /// Returns a JSON array of all available tools with their name, title, description, and schemas.
        /// </summary>
        static async Task ListToolsHandler(HttpContext context)
        {
            var toolHub = context.RequestServices.GetRequiredService<IClientToolHub>();
            var request = new RequestListTool();
            var response = await toolHub.RunListTool(request, context.RequestAborted);

            if (response == null)
            {
                context.Response.StatusCode = 502;
                await context.Response.WriteAsJsonAsync(new { error = "Tool hub returned a null response." });
                return;
            }

            if (response.Status == ResponseStatus.Error)
            {
                context.Response.StatusCode = DirectHttpErrorMapper.GetStatusCode(response, 500);
                await context.Response.WriteAsJsonAsync(new { error = response.Message ?? "Failed to list tools." });
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
        /// Executes a named tool by forwarding the JSON request body as tool arguments via SignalR.
        /// The tool name is taken from the route parameter <c>{name}</c>.
        /// </summary>
        static async Task CallToolHandler(HttpContext context)
        {
            var toolHub = context.RequestServices.GetRequiredService<IClientToolHub>();
            var name = context.Request.RouteValues["name"]?.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "Tool name must not be empty." });
                return;
            }

            // Parse the request body as a JSON object of named arguments
            IReadOnlyDictionary<string, JsonElement> arguments;
            try
            {
                if (context.Request.ContentLength == 0 || !context.Request.HasJsonContentType())
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
            var response = await toolHub.RunCallTool(requestData);

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
                        errorDetails = response?.Message;
                    }
                    else if (response?.Value != null)
                    {
                        try
                        {
                            responseSize = System.Text.Encoding.UTF8.GetByteCount(
                                JsonSerializer.Serialize(response.Value));
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
                await context.Response.WriteAsJsonAsync(new { error = "Tool hub returned a null response." });
                return;
            }

            if (response.Status == ResponseStatus.Error)
            {
                context.Response.StatusCode = DirectHttpErrorMapper.GetStatusCode(response, 400);
                await context.Response.WriteAsJsonAsync(new { error = response.Message ?? $"Tool '{name}' returned an error." });
                return;
            }

            if (response.Value == null)
            {
                await context.Response.WriteAsJsonAsync(new { status = "success", content = Array.Empty<object>() });
                return;
            }

            // Return structured content if available, otherwise return the text content blocks
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
