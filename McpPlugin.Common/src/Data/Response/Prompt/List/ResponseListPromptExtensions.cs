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
using System.Text.Json;
using com.IvanMurzak.McpPlugin.Common.Utils;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Common.Model
{
    public static class ResponseListPromptExtensions
    {
        public static ResponseListPrompts Log(this ResponseListPrompts response, ILogger logger, Exception? ex = null)
        {
            if (!logger.IsEnabled(LogLevel.Information))
                return response;

            foreach (var item in response.Prompts)
                logger.LogInformation(ex, $"Prompt: {item.Name}:\n{item.ToPrettyJson()}");

            return response;
        }

        public static ResponseData<ResponseListPrompts> Pack(this ResponseListPrompts response, string requestId, string? message = null)
            => ResponseData<ResponseListPrompts>.Success(requestId, message ?? "List Prompt execution completed.")
                .SetData(response);
    }
}
