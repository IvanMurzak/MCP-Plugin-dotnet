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
using com.IvanMurzak.McpPlugin.Common.Utils;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Common.Model
{
    public static class ResponseListToolExtensions
    {
        public static ResponseListTool[] Log(this ResponseListTool[] response, ILogger logger, Exception? ex = null)
        {
            if (!logger.IsEnabled(LogLevel.Debug))
                return response;

            foreach (var item in response)
                logger.LogDebug(ex, $"Tool: {item.Name}:\n{item.ToPrettyJson()}");

            return response;
        }

        public static ResponseData<ResponseListTool[]> Pack(this ResponseListTool[] response, string requestId, string? message = null)
            => ResponseData<ResponseListTool[]>
                .Success(requestId, message ?? "List Tool execution completed.")
                .SetData(response);
    }
}
