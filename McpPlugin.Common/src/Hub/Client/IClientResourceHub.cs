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
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin.Common.Hub.Client
{
    public interface IClientResourceHub
    {
        Task<ResponseData<ResponseResourceContent[]>> RunResourceContent(RequestResourceContent request);

        // The list methods take a CancellationToken (defaulted, so existing token-less call sites keep
        // compiling) for the same reason IClientToolHub.RunListTool does: without it the CALLER cannot
        // bound the runner's retry ladder at all. Omitting it forwards `default`, so the only token
        // reaching the hub invoke is the runner's own disposal token, which no caller controls.
        Task<ResponseData<ResponseListResource[]>> RunListResources(RequestListResources request, CancellationToken cancellationToken = default);
        Task<ResponseData<ResponseResourceTemplate[]>> RunResourceTemplates(RequestListResourceTemplates request, CancellationToken cancellationToken = default);
    }
}
