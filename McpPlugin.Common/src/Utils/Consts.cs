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
        public const string ApiVersion = "2.0.0";
        public const string PluginVersion = "1.1.0";

        public static class Guid
        {
            public const string Zero = "00000000-0000-0000-0000-000000000000";
        }

        public static partial class Command
        {
            public static partial class ResponseCode
            {
                public const string Success = "[Success]";
                public const string Error = "[Error]";
                public const string Cancel = "[Cancel]";
            }
        }
    }
}
