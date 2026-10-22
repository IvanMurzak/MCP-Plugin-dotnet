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
    /// <summary>
    /// A server-side tool hub whose call takes the caller's token, so a caller that goes away stops
    /// the wait. Not on <see cref="IClientToolHub"/>: the plugin implements that interface too, and
    /// a new member would break it.
    /// </summary>
    internal interface ICancellableToolHub
    {
        Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request, CancellationToken cancellationToken);
    }

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
                // A session whose instance is reconnecting goes on to the runner, which waits for it
                // (bounded) before sending the call once.
                if (resolution.Kind != InstanceResolutionKind.Resolved
                    && services.GetService<PluginCatalogCache>()?.HasOfflineTargetForCurrentSession() != true)
                {
                    var identity = McpSessionTokenContext.CurrentIdentity;
                    var message = AgentActionableErrors.ForResolution(
                        resolution, strategy.Instances, identity?.AccountId, identity?.BoundProjectPin);
                    var kind = resolution.Kind == InstanceResolutionKind.NoMatchPinned
                        ? ResponseErrorKind.NotFound : ResponseErrorKind.Unavailable;
                    return ResponseCallTool.Error(message, kind).Pack(request.RequestID);
                }
            }

            var toolHub = services.GetRequiredService<IClientToolHub>();
            return toolHub is ICancellableToolHub cancellable
                ? await cancellable.RunCallTool(request, cancellationToken)
                : await toolHub.RunCallTool(request);
        }
    }
}
