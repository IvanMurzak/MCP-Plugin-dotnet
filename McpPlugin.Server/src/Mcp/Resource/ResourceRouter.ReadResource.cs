/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static partial class ResourceRouter
    {
        public static async ValueTask<ReadResourceResult> Read(
            RequestContext<ReadResourceRequestParams> request,
            CancellationToken cancellationToken
        )
        {
            if (request?.Params?.Uri == null)
                return new ReadResourceResult().SetError("null", "[Error] Request or Uri is null");

            var resourceRunner = request.Services?.GetRequiredService<IClientResourceHub>();
            if (resourceRunner == null)
                return new ReadResourceResult().SetError(request.Params.Uri, $"[Error] '{nameof(resourceRunner)}' is null");

            var requestData = new RequestResourceContent(request.Params.Uri);

            var response = await resourceRunner.RunResourceContent(requestData);
            if (response == null)
                return new ReadResourceResult().SetError(request.Params.Uri, "[Error] Resource is null");

            if (response.Status == ResponseStatus.Error)
                return new ReadResourceResult().SetError(request.Params.Uri, response.Message ?? "[Error] Got an error during reading resources");

            if (response.Value == null)
                return new ReadResourceResult().SetError(request.Params.Uri, "[Error] Resource value is null");

            return new ReadResourceResult()
            {
                Contents = response.Value
                    .Where(x => x != null)
                    .Where(x => x!.Text != null || x!.Blob != null)
                    .Select(x => x!.ToResourceContents())
                    .ToList()
            };
        }
    }
}
