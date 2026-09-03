/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
#nullable enable
using System.ComponentModel;

namespace com.IvanMurzak.McpPlugin.NullEngine.Prompts
{
    /// <summary>
    /// The null engine's prompt surface: one prompt that takes an argument and one that does not,
    /// so a consumer can exercise both shapes of <c>prompts/get</c>. Both are pure functions of
    /// their arguments, like every tool in this host.
    /// </summary>
    [AiPromptType]
    public static class NullEnginePrompts
    {
        public const string GreetingName = "null-engine/greeting";
        public const string StaticName = "null-engine/static";

        /// <summary>The exact body <see cref="Static"/> returns. Fixed so a consumer can assert on it.</summary>
        public const string StaticBody = "null-engine: a fixed prompt body with no arguments.";

        [AiPrompt(Name = GreetingName)]
        [Description("Greets the supplied name. Exercises a prompt WITH an argument.")]
        public static string Greeting(
            [Description("Who to greet. Defaults to 'world'.")] string name = "world")
            => "null-engine: hello, " + name + ".";

        [AiPrompt(Name = StaticName)]
        [Description("Returns a fixed body. Exercises a prompt with NO arguments.")]
        public static string Static() => StaticBody;
    }
}
