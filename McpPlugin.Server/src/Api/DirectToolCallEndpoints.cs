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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace com.IvanMurzak.McpPlugin.Server.Api
{
    /// <summary>
    /// Provides a direct HTTP API for calling MCP tools without going through the full MCP protocol.
    /// This enables AI agents and automation scripts to invoke tools using standard HTTP/JSON (e.g., curl).
    ///
    /// Endpoints:
    ///   GET  /api/tools           — list all available tools with schemas
    ///   POST /api/tools/{name}    — execute a named tool with JSON arguments
    /// </summary>
    public static class DirectToolCallEndpoints
    {
        const string RoutePrefix = "/api/tools";

        /// <summary>
        /// Maps the direct tool call API endpoints onto the given <see cref="WebApplication"/>.
        /// Authorization is required when <paramref name="dataArguments"/> has <see cref="IDataArguments.Authorization"/>
        /// set to <see cref="Consts.MCP.Server.AuthOption.required"/>.
        /// </summary>
        public static WebApplication MapDirectToolCallApi(this WebApplication app, IDataArguments dataArguments)
        {
            if (app == null)
                throw new ArgumentNullException(nameof(app));

            if (dataArguments == null)
                throw new ArgumentNullException(nameof(dataArguments));

            var requireAuth = dataArguments.Authorization == Consts.MCP.Server.AuthOption.required;
            var group = app.MapGroup(RoutePrefix);

            // GET /api/tools — list all registered tools
            var listEndpoint = group.MapGet("/", ListToolsHandler);

            // POST /api/tools/{name} — invoke a tool by name
            var callEndpoint = group.MapPost("/{name}", CallToolHandler);

            if (requireAuth)
            {
                listEndpoint.RequireAuthorization();
                callEndpoint.RequireAuthorization();
            }

            return app;
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
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { error = "Tool hub returned a null response." });
                return;
            }

            if (response.Status == ResponseStatus.Error)
            {
                context.Response.StatusCode = 500;
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

            var requestData = new RequestCallTool(name, arguments);
            var response = await toolHub.RunCallTool(requestData);

            if (response == null)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { error = "Tool hub returned a null response." });
                return;
            }

            if (response.Status == ResponseStatus.Error)
            {
                context.Response.StatusCode = 500;
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
