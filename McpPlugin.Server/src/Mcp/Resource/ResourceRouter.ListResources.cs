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
        public static async ValueTask<ListResourcesResult> List(RequestContext<ListResourcesRequestParams> request, CancellationToken cancellationToken)
        {
            if (request.Services == null)
                return new ListResourcesResult().SetError("[Error] 'Services' is null");

            var resourceRunner = request.Services.GetRequiredService<IClientResourceHub>();
            if (resourceRunner == null)
                return new ListResourcesResult().SetError($"[Error] '{nameof(resourceRunner)}' is null");

            var requestData = new RequestListResources(cursor: request?.Params?.Cursor);

            var response = await resourceRunner.RunListResources(requestData);
            if (response == null)
                return new ListResourcesResult().SetError("[Error] Resource is null");

            if (response.Status == ResponseStatus.Error)
                return new ListResourcesResult().SetError(response.Message ?? "[Error] Got an error during getting resources");

            if (response.Value == null)
                return new ListResourcesResult().SetError("[Error] Resource value is null");

            return new ListResourcesResult()
            {
                Resources = response.Value
                    .Where(x => x?.Enabled == true)
                    .Select(x => x!.ToResource())
                    .ToList() ?? new List<Resource>(),
            };
        }
    }
}
