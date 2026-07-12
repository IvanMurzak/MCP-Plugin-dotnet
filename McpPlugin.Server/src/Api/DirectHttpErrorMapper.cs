/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin.Server.Api
{
    static class DirectHttpErrorMapper
    {
        public static int GetStatusCode(ResponseData response, int fallbackStatusCode)
        {
            if (response.HttpStatusCode is >= 400 and <= 599)
                return response.HttpStatusCode.Value;

            return response.ErrorKind switch
            {
                ResponseErrorKind.BadRequest => 400,
                ResponseErrorKind.NotFound => 404,
                ResponseErrorKind.Conflict => 409,
                ResponseErrorKind.Unavailable => 503,
                ResponseErrorKind.Timeout => 504,
                ResponseErrorKind.Internal => 500,
                _ => fallbackStatusCode
            };
        }
    }
}
