/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin
{
    public partial class McpManager
    {
        /// <summary>
        /// The hubs the MCP server calls: they forward to the managers and count each call, prompt get and
        /// resource read in <see cref="RequestActivity"/>. List requests pass through uncounted.
        /// </summary>
        sealed class TrackedHubs : IClientToolHub, IClientPromptHub, IClientResourceHub, IClientSystemToolHub
        {
            readonly McpManager _owner;

            public TrackedHubs(McpManager owner) => _owner = owner;

            McpRequestActivity Activity => _owner.RequestActivity;

            public Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request)
                => Activity.Track(() => _owner._tools!.RunCallTool(request));

            public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request, CancellationToken cancellationToken = default)
                => _owner._tools!.RunListTool(request, cancellationToken);

            public Task<ResponseData<ResponseGetPrompt>> RunGetPrompt(RequestGetPrompt request)
                => Activity.Track(() => _owner._prompts!.RunGetPrompt(request));

            public Task<ResponseData<ResponseListPrompts>> RunListPrompts(RequestListPrompts request, CancellationToken cancellationToken = default)
                => _owner._prompts!.RunListPrompts(request, cancellationToken);

            public Task<ResponseData<ResponseResourceContent[]>> RunResourceContent(RequestResourceContent request)
                => Activity.Track(() => _owner._resources!.RunResourceContent(request));

            public Task<ResponseData<ResponseListResource[]>> RunListResources(RequestListResources request, CancellationToken cancellationToken = default)
                => _owner._resources!.RunListResources(request, cancellationToken);

            public Task<ResponseData<ResponseResourceTemplate[]>> RunResourceTemplates(RequestListResourceTemplates request, CancellationToken cancellationToken = default)
                => _owner._resources!.RunResourceTemplates(request, cancellationToken);

            public Task<ResponseData<ResponseCallTool>> RunSystemTool(RequestCallTool request, CancellationToken cancellationToken = default)
                => Activity.Track(() => _owner._systemTools!.RunSystemTool(request, cancellationToken));

            public Task<ResponseData<ResponseListTool[]>> RunListSystemTool(RequestListTool request, CancellationToken cancellationToken = default)
                => _owner._systemTools!.RunListSystemTool(request, cancellationToken);
        }
    }
}
