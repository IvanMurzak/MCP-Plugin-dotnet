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
        public static async ValueTask<GetPromptResult> Get(RequestContext<GetPromptRequestParams> request, CancellationToken cancellationToken)
        {
            var logger = LogManager.GetCurrentClassLogger();
            logger.Trace("{0}.Get", nameof(PromptRouter));

            if (request == null)
                return new GetPromptResult().SetError("[Error] Request is null");

            if (request.Params == null)
                return new GetPromptResult().SetError("[Error] Request.Params is null");

            if (request.Params.Arguments == null)
                return new GetPromptResult().SetError("[Error] Request.Params.Arguments is null");

            var promptRunner = request.Services?.GetRequiredService<IClientPromptHub>();
            if (promptRunner == null)
                return new GetPromptResult().SetError($"[Error] '{nameof(promptRunner)}' is null");

            logger.Trace("Using PromptRunner: {0}", promptRunner.GetType().GetTypeShortName());

            var argumentsDict = request.Params.Arguments as IReadOnlyDictionary<string, JsonElement>
                ?? new Dictionary<string, JsonElement>();

            var requestData = new RequestGetPrompt(request.Params.Name, argumentsDict);
            if (logger.IsTraceEnabled)
                logger.Trace("Get remote prompt '{0}':\n{1}", request.Params.Name, requestData.ToPrettyJson());

            var response = await promptRunner.RunGetPrompt(requestData);
            if (response == null)
                return new GetPromptResult().SetError($"[Error] '{nameof(response)}' is null");

            if (logger.IsTraceEnabled)
                logger.Trace("Get prompt response:\n{0}", response.ToPrettyJson());

            if (response.Status == ResponseStatus.Error)
                return new GetPromptResult().SetError(response.Message ?? "[Error] Got an error during running tool");

            if (response.Value == null)
                return new GetPromptResult().SetError("[Error] Prompt returned null value");

            return response.Value.ToGetPromptResult();
        }
    }
}
