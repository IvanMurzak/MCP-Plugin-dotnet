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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// One logical MCP config written to several candidate files — for an agent whose config lives in one of
    /// several locations that cannot be predicted (e.g. Antigravity). Every file-level operation fans out to
    /// each child config:
    /// <list type="bullet">
    /// <item><see cref="Configure"/> writes EVERY candidate (creating missing files); true only when all succeed.
    /// A failing path is logged and listed in <see cref="FailedConfigPaths"/> — the other files are still written.</item>
    /// <item><see cref="IsConfigured"/> is true ⇔ at least one candidate file exists AND every candidate that
    /// exists is correctly configured. A missing file is ignored; an existing stale file fails the check.</item>
    /// <item><see cref="Unconfigure"/> removes the entry from every existing candidate; never creates a file.</item>
    /// </list>
    /// <see cref="AiAgentConfig.ConfigPath"/> is a display string listing every path (so a UI or CLI that prints
    /// it shows all of them); use <see cref="ConfigPaths"/> for file access.
    /// </summary>
    public sealed class CompositeAiAgentConfig : AiAgentConfig
    {
        /// <summary>Separator used to join the child paths into the display <see cref="AiAgentConfig.ConfigPath"/>.</summary>
        public const string PathDisplaySeparator = "; ";

        private readonly IReadOnlyList<AiAgentConfig> _configs;
        private List<string> _failedConfigPaths = new();

        /// <summary>The per-file child configs, in priority order.</summary>
        public IReadOnlyList<AiAgentConfig> Configs => _configs;

        /// <summary>The candidate file paths, one per child, in priority order.</summary>
        public override IReadOnlyList<string> ConfigPaths => _configs.Select(c => c.ConfigPath).ToArray();

        /// <summary>The paths whose write failed during the last <see cref="Configure"/> call (empty on success).</summary>
        public IReadOnlyList<string> FailedConfigPaths => _failedConfigPaths;

        /// <summary>Every child writes the same entry, so the expected content is the first child's.</summary>
        public override string ExpectedFileContent => _configs[0].ExpectedFileContent;

        public CompositeAiAgentConfig(string name, IReadOnlyList<AiAgentConfig> configs, ILogger? logger = null)
            : base(
                name: name,
                configPath: string.Join(PathDisplaySeparator, (configs ?? throw new ArgumentNullException(nameof(configs))).Select(c => c.ConfigPath)),
                bodyPath: configs.Count > 0 ? configs[0].BodyPath : Common.Consts.MCP.Server.DefaultBodyPath,
                logger: logger)
        {
            if (configs.Count == 0)
                throw new ArgumentException("A composite config needs at least one child config.", nameof(configs));
            _configs = configs.ToArray();
            foreach (var key in _configs[0].IdentityKeys)
                base.AddIdentityKey(key);
        }

        public override bool Configure()
        {
            var failed = new List<string>();
            foreach (var config in _configs)
            {
                if (config.Configure())
                    continue;
                failed.Add(config.ConfigPath);
                _logger?.LogError("Failed to write the {Name} MCP config to {ConfigPath}.", Name, config.ConfigPath);
            }
            _failedConfigPaths = failed;
            return failed.Count == 0;
        }

        public override bool Unconfigure()
        {
            var removed = false;
            foreach (var config in _configs)
                removed |= config.Unconfigure(); // no short-circuit: every existing file is cleaned
            return removed;
        }

        public override bool IsDetected() => _configs.Any(c => c.IsDetected());

        public override bool IsConfigured()
        {
            var anyExists = false;
            foreach (var config in _configs)
            {
                if (!File.Exists(config.ConfigPath))
                    continue;
                anyExists = true;
                if (!config.IsConfigured())
                    return false;
            }
            return anyExists;
        }

        public override void ApplyHttpAuthorization(bool isRequired, string? token)
        {
            foreach (var config in _configs)
                config.ApplyHttpAuthorization(isRequired, token);
        }

        public override void ApplyStdioAuthorization(bool isRequired, string? token)
        {
            foreach (var config in _configs)
                config.ApplyStdioAuthorization(isRequired, token);
        }
    }
}
