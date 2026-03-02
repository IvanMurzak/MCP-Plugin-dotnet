/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

namespace com.IvanMurzak.McpPlugin.Common
{
    public static partial class Consts
    {
        public static partial class MCP
        {
            public static partial class Server
            {
                public static partial class Args
                {
                    public const string WebhookToolUrl = "webhook-tool-url";
                    public const string WebhookPromptUrl = "webhook-prompt-url";
                    public const string WebhookResourceUrl = "webhook-resource-url";
                    public const string WebhookConnectionUrl = "webhook-connection-url";
                    public const string WebhookToken = "webhook-token";
                    public const string WebhookHeader = "webhook-header";
                    public const string WebhookTimeout = "webhook-timeout";
                }

                public static partial class Env
                {
                    public const string WebhookToolUrl = "MCP_PLUGIN_WEBHOOK_TOOL_URL";
                    public const string WebhookPromptUrl = "MCP_PLUGIN_WEBHOOK_PROMPT_URL";
                    public const string WebhookResourceUrl = "MCP_PLUGIN_WEBHOOK_RESOURCE_URL";
                    public const string WebhookConnectionUrl = "MCP_PLUGIN_WEBHOOK_CONNECTION_URL";
                    public const string WebhookToken = "MCP_PLUGIN_WEBHOOK_TOKEN";
                    public const string WebhookHeader = "MCP_PLUGIN_WEBHOOK_HEADER";
                    public const string WebhookTimeout = "MCP_PLUGIN_WEBHOOK_TIMEOUT";
                }
            }
        }
    }
}
