/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

namespace com.IvanMurzak.McpPlugin.Tests.Data.Annotations
{
    [McpPluginSkillType]
    public static class AnnotatedSkillClass
    {
        [McpPluginSkill("deploy-guide", "Step-by-step deployment instructions")]
        public const string DeployGuide = @"
# Deploy Guide

1. Build the project
2. Run migrations
3. Deploy to staging
";

        [McpPluginSkill("troubleshoot", "Troubleshooting common issues")]
        public const string Troubleshoot = @"
# Troubleshooting

- Check logs
- Restart service
";

        [McpPluginSkill("disabled-skill", "This skill is disabled", Enabled = false)]
        public const string DisabledSkill = @"
# Disabled

This should not appear.
";

        // Static property — should be picked up
        [McpPluginSkill("platform-info", "Platform-specific instructions")]
        public static string PlatformInfo => System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows)
            ? "# Windows\nUse PowerShell."
            : "# Linux\nUse bash.";

        // Static property with disabled attribute — should NOT be picked up
        [McpPluginSkill("disabled-prop", "Disabled property skill", Enabled = false)]
        public static string DisabledProp => "# Disabled prop";

        // Non-const field — should NOT be picked up
        public static string NotAConst = "I am not const";

        // No attribute — should NOT be picked up
        public const string NoAttribute = "I have no attribute";

        // Property without attribute — should NOT be picked up
        public static string NoPropAttribute => "I have no attribute";
    }
}
