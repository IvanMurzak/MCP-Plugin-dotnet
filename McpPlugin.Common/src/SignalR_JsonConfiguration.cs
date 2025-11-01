/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using com.IvanMurzak.ReflectorNet;

namespace com.IvanMurzak.McpPlugin.Common
{
    public static class SignalR_JsonConfiguration
    {
        public static void ConfigureJsonSerializer(Reflector reflector, Microsoft.AspNetCore.SignalR.JsonHubProtocolOptions options)
        {
            var jsonSerializerOptions = reflector.JsonSerializer.JsonSerializerOptions;

            options.PayloadSerializerOptions.DefaultIgnoreCondition = jsonSerializerOptions.DefaultIgnoreCondition;
            options.PayloadSerializerOptions.PropertyNamingPolicy = jsonSerializerOptions.PropertyNamingPolicy;
            options.PayloadSerializerOptions.WriteIndented = jsonSerializerOptions.WriteIndented;

            foreach (var converter in jsonSerializerOptions.Converters)
                options.PayloadSerializerOptions.Converters.Add(converter);
        }
    }
}