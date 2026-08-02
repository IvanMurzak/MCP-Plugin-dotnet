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
using System.Collections.Generic;
using System.Linq;
using ModelContextProtocol.Protocol;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ExtensionsPrompt
    {
        public static GetPromptResult SetError(this GetPromptResult target, string message)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            target.Description = message;
            target.Messages = new List<PromptMessage>();

            return target;
        }

        /// <summary>
        /// Degrades a failed <c>prompts/list</c> to an EMPTY catalog, mirroring the tool path
        /// (<see cref="ExtensionsTool.SetError(ListToolsResult, string)"/>) and the two resource
        /// list paths. This used to fabricate a single synthetic <c>Prompt</c> named
        /// <c>"Error"</c> carrying the failure message, which every MCP client rendered as a real,
        /// selectable prompt whose description leaked internal .NET type names to end users —
        /// an outcome worse than an empty list on the most common failure branch of
        /// <see cref="PromptRouter.List"/>, "the plugin has not connected yet". The failure
        /// message is not dropped: the router logs it as a warning before degrading.
        /// </summary>
        public static ListPromptsResult SetError(this ListPromptsResult target, string message)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            target.Prompts = new List<Prompt>();

            return target;
        }

        public static Prompt ToPrompt(this Common.Model.ResponsePrompt response) => new Prompt()
        {
            Name = response.Name,
            Title = response.Title,
            Description = response.Description,
            Arguments = response.Arguments?.Select(x => x.ToPromptArgument()).ToList(),
            // See ExtensionsListMeta — disabled primitives surface `_meta.enabled = false`
            // for trusted clients; null for enabled keeps the default wire shape unchanged.
            Meta = ExtensionsListMeta.BuildEnabledMeta(response.Enabled)
        };

        public static PromptMessage ToPromptMessage(this Common.Model.ResponsePromptMessage promptMessage) => new PromptMessage()
        {
            Role = promptMessage.Role switch
            {
                Common.Model.Role.User => Role.User,
                Common.Model.Role.Assistant => Role.Assistant,
                _ => throw new ArgumentOutOfRangeException(nameof(promptMessage.Role), $"Invalid role value: {promptMessage.Role}")
            },
            Content = promptMessage.Content.ToContent()
        };

        public static PromptArgument ToPromptArgument(this Common.Model.ResponsePromptArgument promptArgument) => new PromptArgument()
        {
            Name = promptArgument.Name,
            Title = promptArgument.Title,
            Description = promptArgument.Description,
            Required = promptArgument.Required,
        };

        public static GetPromptResult ToGetPromptResult(this Common.Model.ResponseGetPrompt response) => new GetPromptResult()
        {
            Description = response.Description,
            Messages = response.Messages
                .Select(x => x.ToPromptMessage())
                .ToList()
        };
    }
}
