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

namespace com.IvanMurzak.McpPlugin
{
    public partial class McpManager
    {
        static async Task<T> Track<T>(McpRequestActivity activity, Func<Task<T>> run)
        {
            using (activity.BeginRequest())
                return await run().ConfigureAwait(false);
        }

        sealed class TrackedToolHub : IClientToolHub
        {
            readonly IClientToolHub _inner;
            readonly McpRequestActivity _activity;

            public TrackedToolHub(IClientToolHub inner, McpRequestActivity activity)
            {
                _inner = inner;
                _activity = activity;
            }

            public Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request)
                => Track(_activity, () => _inner.RunCallTool(request));

            public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request, CancellationToken cancellationToken = default)
                => _inner.RunListTool(request, cancellationToken);
        }

        sealed class TrackedPromptHub : IClientPromptHub
        {
            readonly IClientPromptHub _inner;
            readonly McpRequestActivity _activity;

            public TrackedPromptHub(IClientPromptHub inner, McpRequestActivity activity)
            {
                _inner = inner;
                _activity = activity;
            }

            public Task<ResponseData<ResponseGetPrompt>> RunGetPrompt(RequestGetPrompt request)
                => Track(_activity, () => _inner.RunGetPrompt(request));

            public Task<ResponseData<ResponseListPrompts>> RunListPrompts(RequestListPrompts request, CancellationToken cancellationToken = default)
                => _inner.RunListPrompts(request, cancellationToken);
        }

        sealed class TrackedResourceHub : IClientResourceHub
        {
            readonly IClientResourceHub _inner;
            readonly McpRequestActivity _activity;

            public TrackedResourceHub(IClientResourceHub inner, McpRequestActivity activity)
            {
                _inner = inner;
                _activity = activity;
            }

            public Task<ResponseData<ResponseResourceContent[]>> RunResourceContent(RequestResourceContent request)
                => Track(_activity, () => _inner.RunResourceContent(request));

            public Task<ResponseData<ResponseListResource[]>> RunListResources(RequestListResources request, CancellationToken cancellationToken = default)
                => _inner.RunListResources(request, cancellationToken);

            public Task<ResponseData<ResponseResourceTemplate[]>> RunResourceTemplates(RequestListResourceTemplates request, CancellationToken cancellationToken = default)
                => _inner.RunResourceTemplates(request, cancellationToken);
        }

        sealed class TrackedSystemToolHub : IClientSystemToolHub
        {
            readonly IClientSystemToolHub _inner;
            readonly McpRequestActivity _activity;

            public TrackedSystemToolHub(IClientSystemToolHub inner, McpRequestActivity activity)
            {
                _inner = inner;
                _activity = activity;
            }

            public Task<ResponseData<ResponseCallTool>> RunSystemTool(RequestCallTool request, CancellationToken cancellationToken = default)
                => Track(_activity, () => _inner.RunSystemTool(request, cancellationToken));

            public Task<ResponseData<ResponseListTool[]>> RunListSystemTool(RequestListTool request, CancellationToken cancellationToken = default)
                => _inner.RunListSystemTool(request, cancellationToken);
        }
    }
}
