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
        /// resource read in <see cref="RequestActivity"/>. List requests pass through uncounted. A tool or
        /// system tool call whose request id is already running or just finished is not run again
        /// (see <see cref="DuplicateRequestGuard{T}"/>), unless it answered that it did not run
        /// (<see cref="ResponseCallTool.NotExecuted"/>): the server's re-send of that id runs.
        /// </summary>
        sealed class TrackedHubs : IClientToolHub, IClientPromptHub, IClientResourceHub, IClientSystemToolHub
        {
            readonly McpManager _owner;
            readonly DuplicateRequestGuard<ResponseData<ResponseCallTool>> _toolCalls;
            readonly DuplicateRequestGuard<ResponseData<ResponseCallTool>> _systemToolCalls;

            public TrackedHubs(McpManager owner)
            {
                _owner = owner;
                _toolCalls = new(owner._logger, forget: ResponseCallTool.IsNotExecuted);
                _systemToolCalls = new(owner._logger, forget: ResponseCallTool.IsNotExecuted);
            }

            McpRequestActivity Activity => _owner.RequestActivity;

            public Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request)
                => _toolCalls.Run(request?.RequestID, () => Activity.Track(() => _owner._tools!.RunCallTool(request!)));

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
                => _systemToolCalls.Run(request?.RequestID, () => Activity.Track(() => _owner._systemTools!.RunSystemTool(request!, cancellationToken)));

            public Task<ResponseData<ResponseListTool[]>> RunListSystemTool(RequestListTool request, CancellationToken cancellationToken = default)
                => _owner._systemTools!.RunListSystemTool(request, cancellationToken);
        }
    }
}
