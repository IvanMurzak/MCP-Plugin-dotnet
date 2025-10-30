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
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.ReflectorNet;
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

            var mcpServerService = McpServerService.Instance;
            if (mcpServerService == null)
                return new CallToolResult().SetError($"[Error] '{nameof(mcpServerService)}' instance is null");

            var toolRunner = mcpServerService.ToolRunner; // if has local tool

            logger.Trace("Using ToolRunner: {0}", toolRunner?.GetType().GetTypeShortName());

            if (toolRunner == null)
                return new CallToolResult().SetError($"[Error] '{nameof(toolRunner)}' is null");

            var requestData = new RequestCallTool(request.Params.Name, request.Params.Arguments);
            if (logger.IsTraceEnabled)
                logger.Trace("Call remote tool '{0}':\n{1}", request.Params.Name, requestData.ToPrettyJson());

            var response = await toolRunner.RunCallTool(requestData, cancellationToken: cancellationToken);
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
