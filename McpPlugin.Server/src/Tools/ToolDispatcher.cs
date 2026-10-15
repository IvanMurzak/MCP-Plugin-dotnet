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
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Microsoft.Extensions.DependencyInjection;

namespace com.IvanMurzak.McpPlugin.Server.Tools
{
    /// <summary>One dispatch decision for MCP and REST, using the current authenticated request context.</summary>
    internal static class ToolDispatcher
    {
        internal static async Task<ResponseData<ResponseCallTool>?> CallAsync(
            IServiceProvider services, RequestCallTool request, CancellationToken cancellationToken)
        {
            var nativeTools = services.GetService<ServerNativeTools>();
            if (nativeTools != null && ServerNativeTools.IsServerNativeTool(request.Name))
            {
                var response = await nativeTools.HandleAsync(
                    request.Name, request.Arguments, SelectionToolContext.FromCurrent(), cancellationToken);
                return response.Pack(request.RequestID);
            }

            // Native tools must remain callable even with no editor connected. Only plugin calls
            // need resolution, and a project pin must never fall through to a sibling instance.
            if (nativeTools != null && services.GetService<IMcpConnectionStrategy>() is AccountMcpStrategy strategy)
            {
                var resolution = strategy.ResolveCurrentSession();
                if (resolution.Kind != InstanceResolutionKind.Resolved)
                {
                    var identity = McpSessionTokenContext.CurrentIdentity;
                    var message = AgentActionableErrors.ForResolution(
                        resolution, strategy.Instances, identity?.AccountId, identity?.BoundProjectPin);
                    var kind = resolution.Kind == InstanceResolutionKind.NoMatchPinned
                        ? ResponseErrorKind.NotFound : ResponseErrorKind.Unavailable;
                    return ResponseCallTool.Error(message, kind).Pack(request.RequestID);
                }
            }

            return await services.GetRequiredService<IClientToolHub>().RunCallTool(request);
        }
    }
}
