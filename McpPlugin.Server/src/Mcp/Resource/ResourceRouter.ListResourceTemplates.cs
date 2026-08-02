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
        // See _listResourcesTimeout — same bound, same reason.
        static readonly TimeSpan _listResourceTemplatesTimeout = TimeSpan.FromSeconds(15);

        public static async ValueTask<ListResourceTemplatesResult> ListTemplates(RequestContext<ListResourceTemplatesRequestParams> request, CancellationToken cancellationToken)
        {
            var logger = LogManager.GetCurrentClassLogger();

            if (request.Services == null)
            {
                logger.Warn("ListTemplates: 'Services' is null - the server is misconfigured. Returning an empty template list.");
                return new ListResourceTemplatesResult().SetError("[Error] 'Services' is null");
            }

            var resourceRunner = request.Services.GetService<IClientResourceHub>();
            if (resourceRunner == null)
            {
                logger.Warn("ListTemplates: no '{0}' is registered - the server is misconfigured. Returning an empty template list.", nameof(IClientResourceHub));
                return new ListResourceTemplatesResult().SetError($"[Error] '{nameof(resourceRunner)}' is null");
            }

            var requestData = new RequestListResourceTemplates();

            using var timeoutCts = new CancellationTokenSource(_listResourceTemplatesTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            ResponseData<ResponseResourceTemplate[]>? response;
            try
            {
                response = await resourceRunner.RunResourceTemplates(requestData, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Defensive only - see ResourceRouter.List: the runner converts every failure, including
                // cancellation, into an error ResponseData, so a timeout lands on the Error branch below.
                logger.Warn("ListTemplates timed out after {0}s: MCP Plugin not yet connected. Returning an empty template list.", _listResourceTemplatesTimeout.TotalSeconds);
                return new ListResourceTemplatesResult().SetError("[Error] Timed out listing resource templates");
            }

            if (response == null)
            {
                logger.Warn("ListTemplates response is null (plugin may not be connected yet). Returning an empty template list.");
                return new ListResourceTemplatesResult().SetError("[Error] Resource is null");
            }

            if (response.Status == ResponseStatus.Error)
            {
                // Both cases degrade identically - see ResourceRouter.List - so only the warning differs.
                if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    logger.Warn("ListTemplates timed out after {0}s: MCP Plugin not yet connected. Returning an empty template list.", _listResourceTemplatesTimeout.TotalSeconds);
                }
                else
                {
                    logger.Warn("ListTemplates error (plugin may not be connected yet): {0}. Returning an empty template list.", response.Message);
                }

                return new ListResourceTemplatesResult().SetError(response.Message ?? "[Error] Got an error during getting resource templates");
            }

            if (response.Value == null)
            {
                logger.Warn("ListTemplates response value is null (plugin may not be connected yet). Returning an empty template list.");
                return new ListResourceTemplatesResult().SetError("[Error] Resource template value is null");
            }

            // Trusted internal clients receive the unfiltered catalog — see ToolRouter.ListAll.
            return new ListResourceTemplatesResult()
            {
                ResourceTemplates = response.Value.SelectVisible(x => x.Enabled, x => x.ToResourceTemplate())
            };
        }
    }
}
