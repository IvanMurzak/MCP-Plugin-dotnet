/*
┌──────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)             │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)    │
│  Copyright (c) 2025 Ivan Murzak                                  │
│  Licensed under the Apache License, Version 2.0.                 │
│  See the LICENSE file in the project root for more information.  │
└──────────────────────────────────────────────────────────────────┘
*/

using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin.Common.Hub.Server
{
    public interface IServerHub
    {
        Task<ResponseData> NotifyAboutUpdatedTools(CancellationToken cancellationToken = default);
        Task<ResponseData> NotifyAboutUpdatedPrompts(CancellationToken cancellationToken = default);
        Task<ResponseData> NotifyAboutUpdatedResources(CancellationToken cancellationToken = default);
        Task<ResponseData> NotifyToolRequestCompleted(ResponseCallTool response, CancellationToken cancellationToken = default);
        Task<VersionHandshakeResponse?> PerformVersionHandshake(CancellationToken cancellationToken = default);

        // Task<ResponseData> OnListToolsUpdated(string data);
        // Task<ResponseData> OnListResourcesUpdated(string data);
        // Task<ResponseData> OnToolRequestCompleted(ToolRequestCompletedData data);
    }
}
