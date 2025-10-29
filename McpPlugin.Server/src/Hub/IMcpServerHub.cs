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
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin.Server
{
    public interface IMcpServerHub : IToolResponseReceiver, IResourceResponseReceiver, IDisposable
    {
        Task<ResponseData> OnListToolsUpdated(string data);
        Task<ResponseData> OnListPromptsUpdated(string data);
        Task<ResponseData> OnListResourcesUpdated(string data);
        Task<ResponseData> OnToolRequestCompleted(ToolRequestCompletedData data);
        Task<VersionHandshakeResponse> OnVersionHandshake(VersionHandshakeRequest request);
    }

    public interface IToolResponseReceiver
    {
        // Task RespondOnCallTool(ResponseData<IResponseCallTool> data, CancellationToken cancellationToken = default);
        // Task RespondOnListTool(ResponseData<List<IResponseListTool>> data, CancellationToken cancellationToken = default);
    }

    public interface IResourceResponseReceiver
    {
        // Task RespondOnResourceContent(ResponseData<List<IResponseResourceContent>> data, CancellationToken cancellationToken = default);
        // Task RespondOnListResources(ResponseData<List<ResponseListResource>> data, CancellationToken cancellationToken = default);
        // Task RespondOnListResourceTemplates(ResponseData<List<IResponseResourceTemplate>> data, CancellationToken cancellationToken = default);
    }
}
