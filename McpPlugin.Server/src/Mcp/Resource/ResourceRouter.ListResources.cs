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
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NLog;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static partial class ResourceRouter
    {
        // Mirrors ToolRouter.ListAll's bound. Without it this path called the token-less runner
        // overload, so ClientUtils.InvokeAsync ran its full 10-attempt ladder under
        // CancellationToken.None: against a connected-but-silent plugin that is 10 x PluginTimeoutMs
        // plus the retry delays (measured at 110s with the default 10s), which this caps at 15s.
        static readonly TimeSpan _listResourcesTimeout = TimeSpan.FromSeconds(15);

        public static async ValueTask<ListResourcesResult> List(RequestContext<ListResourcesRequestParams> request, CancellationToken cancellationToken)
        {
            var logger = LogManager.GetCurrentClassLogger();

            if (request.Services == null)
                return new ListResourcesResult().SetError("[Error] 'Services' is null");

            var resourceRunner = request.Services.GetService<IClientResourceHub>();
            if (resourceRunner == null)
                return new ListResourcesResult().SetError($"[Error] '{nameof(resourceRunner)}' is null");

            var requestData = new RequestListResources(cursor: request?.Params?.Cursor);

            using var timeoutCts = new CancellationTokenSource(_listResourcesTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            ResponseData<ResponseListResource[]>? response;
            try
            {
                response = await resourceRunner.RunListResources(requestData, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                logger.Warn("List timed out after {0}s: MCP Plugin not yet connected. Returning an empty resource list.", _listResourcesTimeout.TotalSeconds);
                return new ListResourcesResult().SetError("[Error] Timed out listing resources");
            }

            if (response == null)
            {
                logger.Warn("List response is null (plugin may not be connected yet). Returning an empty resource list.");
                return new ListResourcesResult().SetError("[Error] Resource is null");
            }

            if (response.Status == ResponseStatus.Error)
            {
                logger.Warn("List error (plugin may not be connected yet): {0}. Returning an empty resource list.", response.Message);
                return new ListResourcesResult().SetError(response.Message ?? "[Error] Got an error during getting resources");
            }

            if (response.Value == null)
            {
                logger.Warn("List response value is null (plugin may not be connected yet). Returning an empty resource list.");
                return new ListResourcesResult().SetError("[Error] Resource value is null");
            }

            // Trusted internal clients receive the unfiltered catalog — see ToolRouter.ListAll.
            return new ListResourcesResult()
            {
                Resources = response.Value.SelectVisible(x => x.Enabled, x => x.ToResource())
            };
        }
    }
}
