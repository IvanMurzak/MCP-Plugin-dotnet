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
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Common.Model
{
    public static class ResponseResourceContentExtensions
    {
        public static ResponseResourceContent[] Log(this ResponseResourceContent[] target, ILogger logger, Exception? ex = null)
        {
            if (!logger.IsEnabled(LogLevel.Information))
                return target;

            foreach (var item in target)
                logger.LogInformation(ex, "Resource: {0}", item.Uri);

            return target;
        }

        public static ResponseData<ResponseResourceContent[]> Pack(this ResponseResourceContent[] target, string requestId, string? message = null)
            => ResponseData<ResponseResourceContent[]>.Success(requestId, message ?? "List Tool execution completed.")
                .SetData(target);
    }
}
