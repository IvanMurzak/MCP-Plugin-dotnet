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
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
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
        static readonly TimeSpan _listToolsTimeout = TimeSpan.FromSeconds(15);

        public static async ValueTask<ListToolsResult> ListAll(RequestContext<ListToolsRequestParams> request, CancellationToken cancellationToken)
        {
            var logger = LogManager.GetCurrentClassLogger();
            logger.Trace("ListAll");

            if (request.Services == null)
                return new ListToolsResult().SetError("[Error] 'request.Services' is null");

            // Server-native tools (mcp-authorize b4) — the account+instance selection/enrollment
            // surface served by the RS itself, merged alongside the paired plugin's tools (design 04).
            // Registered only in oauth mode; null otherwise (leaving today's pure pass-through). They
            // MUST appear even when NO plugin is connected so an engine-less agent can still call
            // enroll_engine_plugin — hence they are merged into every return path below.
            var nativeTools = request.Services.GetService<ServerNativeTools>()?.Descriptors;

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
                logger.Warn("ListAll timed out after {0}s: MCP Plugin not yet connected. Returning server-native tools only.", _listToolsTimeout.TotalSeconds);
                return Merge(null, nativeTools);
            }

            if (response == null)
            {
                logger.Warn("ListAll response is null. Returning server-native tools only.");
                return Merge(null, nativeTools);
            }

            if (response.Status == ResponseStatus.Error)
            {
                logger.Warn("ListAll error (plugin may not be connected yet): {0}. Returning server-native tools only.", response.Message);
                return Merge(null, nativeTools);
            }

            if (response.Value == null)
            {
                logger.Warn("ListAll response value is null. Returning server-native tools only.");
                return Merge(null, nativeTools);
            }

            // Trusted internal clients (cli/desktop) opt in via the
            // `X-McpPlugin-Internal-Client` header to receive the FULL catalog,
            // including `Enabled = false` tools tagged with `_meta.enabled = false`.
            // Every other caller continues to get the pre-existing filtered view.
            // See ExtensionsListMeta.SelectVisible for the predicate.
            var result = Merge(response.Value.SelectVisible(x => x.Enabled, x => x.ToTool()), nativeTools);

            if (logger.IsTraceEnabled)
                logger.Trace("ListAll, result: {0}", result.ToPrettyJson());

            return result;
        }

        /// <summary>Combines the plugin's tools with the server-native tools (either may be null/empty).</summary>
        static ListToolsResult Merge(IList<Tool>? pluginTools, IReadOnlyList<Tool>? nativeTools)
        {
            var tools = pluginTools != null ? new List<Tool>(pluginTools) : new List<Tool>();
            if (nativeTools != null && nativeTools.Count > 0)
                tools.AddRange(nativeTools);
            return new ListToolsResult() { Tools = tools };
        }
    }
}
