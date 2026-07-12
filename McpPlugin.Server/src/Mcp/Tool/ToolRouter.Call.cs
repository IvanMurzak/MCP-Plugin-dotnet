/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tools;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NLog;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static partial class ToolRouter
    {
        public static async ValueTask<CallToolResult> Call(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
        {
            var logger = LogManager.GetCurrentClassLogger();
            logger.Trace("{0}.Call", nameof(ToolRouter));

            if (request == null)
                return new CallToolResult().SetError("[Error] Request is null");

            if (request.Params == null)
                return new CallToolResult().SetError("[Error] Request.Params is null");

            if (request.Params.Arguments == null)
                return new CallToolResult().SetError("[Error] Request.Params.Arguments is null");

            if (request.Services == null)
                return new CallToolResult().SetError("[Error] Request.Services is null");

            var toolRunner = request.Services.GetRequiredService<IClientToolHub>();
            logger.Trace("Using ToolRunner: {0}", toolRunner.GetType().GetTypeShortName());

            var argumentsDict = request.Params.Arguments as IReadOnlyDictionary<string, JsonElement>
                ?? new Dictionary<string, JsonElement>();

            // Server-native tools (mcp-authorize b4) — the selection + enrollment surface handled by
            // the RS itself, NEVER proxied to a plugin. Registered only in oauth mode; null otherwise
            // (today's pure pass-through preserved). Checked BEFORE resolution so an engine-less agent
            // can still call enroll_engine_plugin.
            var nativeTools = request.Services.GetService<ServerNativeTools>();
            if (nativeTools != null && ServerNativeTools.IsServerNativeTool(request.Params.Name))
            {
                var context = SelectionToolContext.FromCurrent();
                var nativeResponse = await nativeTools.HandleAsync(request.Params.Name, argumentsDict, context, cancellationToken);
                return nativeResponse.ToCallToolResult();
            }

            // oauth: when this agent session resolves to NO plugin instance, surface the design-04
            // step-5 agent-actionable error (pinned-no-match vs account-empty) instead of the legacy
            // 10x-retry → generic invoke failure. A Resolved session falls through to the proxy below
            // (whose own retry loop still covers a transient mid-restart reconnect window).
            if (nativeTools != null && request.Services.GetService<IMcpConnectionStrategy>() is AccountMcpStrategy accountStrategy)
            {
                var resolution = accountStrategy.ResolveCurrentSession();
                if (resolution.Kind != InstanceResolutionKind.Resolved)
                {
                    var accountId = McpSessionTokenContext.CurrentIdentity?.AccountId;
                    var actionable = AgentActionableErrors.ForResolution(resolution, accountStrategy.Instances, accountId);
                    logger.Trace("Call '{0}': no instance resolved ({1}); returning agent-actionable error.", request.Params.Name, resolution.Kind);
                    return new CallToolResult().SetError(actionable);
                }
            }

            var requestData = new RequestCallTool(request.Params.Name, argumentsDict);
            if (logger.IsTraceEnabled)
                logger.Trace("Call remote tool '{0}':\n{1}", request.Params.Name, requestData.ToPrettyJson());

            var response = await toolRunner.RunCallTool(requestData);
            if (response == null)
                return new CallToolResult().SetError($"[Error] '{nameof(response)}' is null");

            if (logger.IsTraceEnabled)
                logger.Trace("Call tool response:\n{0}", response.ToPrettyJson());

            if (response.Status == ResponseStatus.Error)
                return new CallToolResult().SetError(response.Message ?? "[Error] Got an error during running tool");

            if (response.Value == null)
                return new CallToolResult().SetError("[Error] Tool returned null value");

            return response.Value.ToCallToolResult();
        }
    }
}
