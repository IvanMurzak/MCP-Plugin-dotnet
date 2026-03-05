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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NLog;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static partial class ToolRouter
    {
        static readonly TimeSpan _listToolsTimeout = TimeSpan.FromSeconds(15);

        public static async ValueTask<ListToolsResult> ListAll(RequestContext<ListToolsRequestParams> request, CancellationToken cancellationToken)
        {
            var logger = LogManager.GetCurrentClassLogger();
            logger.Trace("ListAll");

            if (request.Services == null)
                return new ListToolsResult().SetError("[Error] 'request.Services' is null");

            var clientToolHub = request.Services.GetRequiredService<IClientToolHub>();
            logger.Trace("ListAll Using ClientToolHub: {0}", clientToolHub.GetType().GetTypeShortName());

            var requestData = new RequestListTool();

            using var timeoutCts = new CancellationTokenSource(_listToolsTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            ResponseData<ResponseListTool[]>? response = null;
            try
            {
                response = await clientToolHub.RunListTool(requestData, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                logger.Warn("ListAll timed out after {0}s: MCP Plugin not yet connected. Returning empty tools list.", _listToolsTimeout.TotalSeconds);
                return new ListToolsResult() { Tools = new List<Tool>() };
            }

            if (response == null)
            {
                logger.Warn("ListAll response is null. Returning empty tools list.");
                return new ListToolsResult() { Tools = new List<Tool>() };
            }

            if (response.Status == ResponseStatus.Error)
            {
                logger.Warn("ListAll error (plugin may not be connected yet): {0}. Returning empty tools list.", response.Message);
                return new ListToolsResult() { Tools = new List<Tool>() };
            }

            if (response.Value == null)
            {
                logger.Warn("ListAll response value is null. Returning empty tools list.");
                return new ListToolsResult() { Tools = new List<Tool>() };
            }

            var result = new ListToolsResult()
            {
                Tools = response.Value
                    .Where(x => x?.Enabled == true)
                    .Select(x => x!.ToTool())
                    .ToList()
            };

            if (logger.IsTraceEnabled)
                logger.Trace("ListAll, result: {0}", result.ToPrettyJson());

            return result;
        }
    }
}
