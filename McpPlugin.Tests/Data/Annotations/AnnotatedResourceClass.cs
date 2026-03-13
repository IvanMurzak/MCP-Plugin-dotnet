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

namespace com.IvanMurzak.McpPlugin.Tests.Data.Annotations
{
    [McpPluginResourceType]
    public static class AnnotatedResourceClass
    {
        [McpPluginResource(Route = "test://resource-enabled-default/{id}", Name = "resource-enabled-default", ListResources = nameof(ListResourcesEnabledDefault))]
        public static ResponseResourceContent[] GetResourceEnabledDefault(string id)
            => new[] { ResponseResourceContent.CreateText($"test://resource-enabled-default/{id}", "default") };

        public static ResponseListResource[] ListResourcesEnabledDefault()
            => new[] { new ResponseListResource("test://resource-enabled-default/1", "resource-enabled-default") };

        [McpPluginResource(Route = "test://resource-enabled-true/{id}", Name = "resource-enabled-true", Enabled = true, ListResources = nameof(ListResourcesEnabledTrue))]
        public static ResponseResourceContent[] GetResourceEnabledTrue(string id)
            => new[] { ResponseResourceContent.CreateText($"test://resource-enabled-true/{id}", "enabled") };

        public static ResponseListResource[] ListResourcesEnabledTrue()
            => new[] { new ResponseListResource("test://resource-enabled-true/1", "resource-enabled-true") };

        [McpPluginResource(Route = "test://resource-enabled-false/{id}", Name = "resource-enabled-false", Enabled = false, ListResources = nameof(ListResourcesEnabledFalse))]
        public static ResponseResourceContent[] GetResourceEnabledFalse(string id)
            => new[] { ResponseResourceContent.CreateText($"test://resource-enabled-false/{id}", "disabled") };

        public static ResponseListResource[] ListResourcesEnabledFalse()
            => new[] { new ResponseListResource("test://resource-enabled-false/1", "resource-enabled-false") };
    }
}
