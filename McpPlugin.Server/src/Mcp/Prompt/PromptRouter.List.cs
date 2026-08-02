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
using com.IvanMurzak.McpPlugin.Common;
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
    public static partial class PromptRouter
    {
        // See ResourceRouter._listResourcesTimeout — same bound, same reason.
        static readonly TimeSpan _listPromptsTimeout = TimeSpan.FromSeconds(15);

        public static async ValueTask<ListPromptsResult> List(RequestContext<ListPromptsRequestParams> request, CancellationToken cancellationToken)
        {
            var logger = LogManager.GetCurrentClassLogger();
            logger.Trace("List");

            if (request.Services == null)
            {
                logger.Warn("List: 'Services' is null - the server is misconfigured. Returning an empty prompt list.");
                return new ListPromptsResult().SetError("[Error] 'Services' is null");
            }

            // GetRequiredService throws on a missing registration and never returns null, so there is no
            // null branch to degrade here - matching ToolRouter.ListAll. WithMcpPluginServer registers
            // every hub unconditionally, so a missing one is a wiring bug, not a runtime condition.
            var promptRunner = request.Services.GetRequiredService<IClientPromptHub>();

            logger.Trace("Using PromptRunner: {0}", promptRunner.GetType().GetTypeShortName());

            var requestData = new RequestListPrompts();

            using var timeoutCts = new CancellationTokenSource(_listPromptsTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            ResponseData<ResponseListPrompts>? response;
            try
            {
                response = await promptRunner.RunListPrompts(requestData, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Defensive only - see ResourceRouter.List: the runner converts every failure, including
                // cancellation, into an error ResponseData, so a timeout lands on the Error branch below.
                logger.Warn("List timed out after {0}s: MCP Plugin not yet connected. Returning an empty prompt list.", _listPromptsTimeout.TotalSeconds);
                return new ListPromptsResult().SetError("[Error] Timed out listing prompts");
            }

            if (response == null)
            {
                logger.Warn("List response is null (plugin may not be connected yet). Returning an empty prompt list.");
                return new ListPromptsResult().SetError($"[Error] '{nameof(response)}' is null");
            }

            if (response.Status == ResponseStatus.Error)
            {
                if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    logger.Warn("List timed out after {0}s: MCP Plugin not yet connected. Returning an empty prompt list.", _listPromptsTimeout.TotalSeconds);
                    return new ListPromptsResult().SetError("[Error] Timed out listing prompts");
                }

                logger.Warn("List error (plugin may not be connected yet): {0}. Returning an empty prompt list.", response.Message);
                return new ListPromptsResult().SetError(response.Message ?? "[Error] Got an error during listing prompts");
            }

            if (response.Value == null)
            {
                logger.Warn("List response value is null (plugin may not be connected yet). Returning an empty prompt list.");
                return new ListPromptsResult().SetError("[Error] Prompt value is null");
            }

            // Trusted internal clients receive the unfiltered catalog — see ToolRouter.ListAll.
            var result = new ListPromptsResult()
            {
                Prompts = response.Value.Prompts.SelectVisible(x => x.Enabled, x => x.ToPrompt())
            };

            if (logger.IsTraceEnabled)
                logger.Trace("ListAll, result: {0}", result.ToPrettyJson());

            return result;
        }
    }
}
