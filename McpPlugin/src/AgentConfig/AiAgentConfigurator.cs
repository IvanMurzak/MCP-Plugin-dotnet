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
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// How a credential is written into an HTTP MCP config (mcp-authorize b6). The default is
    /// <see cref="Oauth"/>: the config carries no credential at all — the client performs native
    /// MCP OAuth against the server URL (design 03 Flow A). <see cref="AccessToken"/> is the
    /// advanced PAT escape hatch (Flow C) written ONLY on explicit request, preferring env-var /
    /// user-scope placement; writing it into a project-scoped file emits a warning.
    /// </summary>
    public enum HttpCredentialMode
    {
        /// <summary>Default: no credential written; the client authorizes natively via OAuth.</summary>
        Oauth,
        /// <summary>Advanced PAT path: write the legacy bearer/header shape (explicit opt-in only).</summary>
        AccessToken
    }

    /// <summary>
    /// Engine-agnostic base for an AI-agent configurator. Each subclass knows one agent's
    /// config-file location(s) and the stdio/http server-entry shape; it produces
    /// <see cref="AiAgentConfig"/> instances (install/remove/status) and an engine-agnostic
    /// <see cref="AgentConfiguratorDescription"/> for the UI. No editor-UI / engine dependency.
    /// </summary>
    /// <remarks>
    /// This is the pure-logic split of Unity's UIToolkit-coupled <c>AiAgentConfigurator</c>:
    /// the original mixed config building with <c>VisualElement</c> creation, <c>UnityMcpPluginEditor</c>
    /// statics, and <c>McpServerManager</c>. Here all engine state arrives via
    /// <see cref="AgentConfiguratorSettings"/> and the result is described, not rendered.
    /// </remarks>
    public abstract class AiAgentConfigurator
    {
        /// <summary>The display name of the AI agent.</summary>
        public abstract string AgentName { get; }

        /// <summary>The stable identifier for this agent (used for persisted keys and lookup).</summary>
        public abstract string AgentId { get; }

        /// <summary>The download URL for the AI agent.</summary>
        public abstract string DownloadUrl { get; }

        /// <summary>
        /// The relative (or absolute) path where skill files should be generated for this agent.
        /// Return null if the agent does not support skills.
        /// </summary>
        public virtual string? SkillsPath => null;

        /// <summary>Whether this agent supports skill-file generation.</summary>
        public bool SupportsSkills => SkillsPath != null;

        /// <summary>The tutorial URL for configuring the AI agent, or empty if none.</summary>
        public virtual string TutorialUrl => string.Empty;

        /// <summary>The display label for the tutorial link (Unity default "YouTube Tutorial").</summary>
        public virtual string TutorialLinkLabel => "YouTube Tutorial";

        /// <summary>The display label for the download link.</summary>
        public virtual string DownloadLinkLabel => "Download";

        /// <summary>
        /// Icon file NAME for this agent (e.g. "claude-64.png"), or null when no icon.
        /// Engines resolve the name to bytes themselves — the shared library never carries bytes.
        /// </summary>
        public abstract string? IconName { get; }

        /// <summary>
        /// Whether this agent can complete native MCP OAuth against the server URL (design 03 Flow A).
        /// Default <c>true</c> — the credential-free config is written and the client's own authorize
        /// flow completes the loop. The few clients that cannot do MCP OAuth override this to
        /// <c>false</c>, which is the signal for the engine UI to offer the "Advanced: use access
        /// token" (PAT) path (<see cref="HttpCredentialMode.AccessToken"/>, design 03 Flow C). The
        /// flag never changes what the DEFAULT path writes — that path is always credential-free.
        /// </summary>
        public virtual bool SupportsOAuth => true;

        /// <summary>
        /// Builds the STDIO transport config for the given settings. Override per agent.
        /// Authorization is applied by <see cref="GetStdioConfig"/>; subclasses build the base entry only.
        /// </summary>
        protected abstract AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger);

        /// <summary>
        /// Builds the HTTP transport config for the given settings. Override per agent.
        /// Authorization is applied by <see cref="GetHttpConfig"/>; subclasses build the base entry only.
        /// </summary>
        protected abstract AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger);

        /// <summary>
        /// Returns the STDIO config, ready to <c>Configure()</c> / inspect. Always credential-free
        /// (mcp-authorize b6): stdio spawns in <c>none</c> mode (design 03 Flow D), so any stray
        /// <c>token=</c> arg is stripped and no credential is ever written for the stdio transport.
        /// </summary>
        public AiAgentConfig GetStdioConfig(AgentConfiguratorSettings settings, ILogger? logger = null)
        {
            var config = CreateStdioConfig(settings, logger);
            ApplyStdioAuthorization(config, settings);
            return config;
        }

        /// <summary>
        /// Returns the HTTP config, ready to <c>Configure()</c> / inspect. On the default
        /// <see cref="HttpCredentialMode.Oauth"/> path the config is credential-free — the client
        /// authorizes natively against the pinned server URL (design 03 Flow A). Passing
        /// <see cref="HttpCredentialMode.AccessToken"/> is the explicit advanced PAT path (Flow C):
        /// it writes the legacy bearer shape and, when that credential would land in a
        /// project-scoped file, emits a warning (prefer env-var / user-scope placement).
        /// </summary>
        public AiAgentConfig GetHttpConfig(
            AgentConfiguratorSettings settings,
            ILogger? logger = null,
            HttpCredentialMode credentialMode = HttpCredentialMode.Oauth)
        {
            var config = CreateHttpConfig(settings, logger);
            ApplyHttpAuthorization(config, settings, credentialMode);
            WarnIfCredentialWouldLandInProjectFile(config, settings, credentialMode, logger);
            return config;
        }

        /// <summary>
        /// Applies STDIO authorization to a config. The default path is unconditionally
        /// credential-free — this strips any existing <c>token=</c> arg (and HTTP-only
        /// <c>headers</c>). Override only for agents whose stdio credential handling is not
        /// args-based (e.g. Open Code, which builds a single <c>command</c> array).
        /// </summary>
        protected virtual void ApplyStdioAuthorization(AiAgentConfig config, AgentConfiguratorSettings settings)
        {
            config.ApplyStdioAuthorization(isRequired: false, token: null);
        }

        /// <summary>
        /// Applies HTTP authorization to a config for the requested <paramref name="credentialMode"/>.
        /// <see cref="HttpCredentialMode.Oauth"/> (default) strips any credential; only
        /// <see cref="HttpCredentialMode.AccessToken"/> injects the legacy bearer shape, and only
        /// when a token is present. Override for agent-specific token placement (e.g. Codex's
        /// bearer-token env-var indirection, which keeps the secret out of the file entirely).
        /// </summary>
        protected virtual void ApplyHttpAuthorization(
            AiAgentConfig config,
            AgentConfiguratorSettings settings,
            HttpCredentialMode credentialMode)
        {
            var writeToken = credentialMode == HttpCredentialMode.AccessToken && !string.IsNullOrEmpty(settings.Token);
            config.ApplyHttpAuthorization(writeToken, settings.Token);
        }

        /// <summary>
        /// Warns when the advanced PAT path would write the raw token VALUE into a config file that
        /// lives under the project root (VCS-visible). Detected generically: the credential is
        /// considered "in the file" when the built <see cref="AiAgentConfig.ExpectedFileContent"/>
        /// contains the token value — so an env-var indirection (Codex) that keeps the secret out of
        /// the file never trips it, and neither does a user-global config path.
        /// </summary>
        private static void WarnIfCredentialWouldLandInProjectFile(
            AiAgentConfig config,
            AgentConfiguratorSettings settings,
            HttpCredentialMode credentialMode,
            ILogger? logger)
        {
            if (credentialMode != HttpCredentialMode.AccessToken || string.IsNullOrEmpty(settings.Token))
                return;

            if (!config.ExpectedFileContent.Contains(settings.Token!, StringComparison.Ordinal))
                return; // credential is not in the file (e.g. env-var placement) — nothing to warn about.

            if (!IsProjectScopedPath(config.ConfigPath, settings.ProjectRootPath))
                return; // user-global config path — the preferred placement for a PAT.

            logger?.LogWarning(
                "Writing an access token into project-scoped config file '{ConfigPath}' — it is under the project root and may be committed to version control. Prefer an env-var or user-scope placement for the access token.",
                config.ConfigPath);
        }

        /// <summary>
        /// True when <paramref name="configPath"/> resolves to a location under
        /// <paramref name="projectRoot"/> (separator- and case-insensitive). Used to decide whether a
        /// PAT write would land in a VCS-visible project file.
        /// </summary>
        internal static bool IsProjectScopedPath(string? configPath, string? projectRoot)
        {
            if (string.IsNullOrEmpty(configPath) || string.IsNullOrEmpty(projectRoot))
                return false;

            static string Norm(string p) => p.Replace('\\', '/').TrimEnd('/');
            var root = Norm(projectRoot!);
            var path = Norm(configPath!);
            return path.Equals(root, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when an MCP config for this agent is present on disk in either transport.
        /// </summary>
        public bool IsDetected(AgentConfiguratorSettings settings, ILogger? logger = null)
        {
            return GetStdioConfig(settings, logger).IsDetected()
                || GetHttpConfig(settings, logger, settings.ResolveHttpCredentialMode()).IsDetected();
        }

        /// <summary>
        /// True when the MCP config for the supplied transport is present AND matches the
        /// current settings. The Custom configurator has no detectable config and returns false.
        /// </summary>
        public virtual bool IsConfigured(
            AgentConfiguratorSettings settings,
            Common.Consts.MCP.Server.TransportMethod transport,
            ILogger? logger = null)
        {
            var config = transport == Common.Consts.MCP.Server.TransportMethod.stdio
                ? GetStdioConfig(settings, logger)
                : GetHttpConfig(settings, logger, settings.ResolveHttpCredentialMode());
            return config.IsConfigured();
        }

        /// <summary>
        /// Returns the three-state configuration status for the requested transport, mirroring
        /// Unity's <c>IsReconfigureNeeded</c> logic: a config detected on disk that no longer
        /// matches the current settings is <see cref="ConfiguratorStatus.ReconfigureNeeded"/>.
        /// Only the config matching <paramref name="transport"/> is consulted — both transports
        /// share the same server name, so a correctly-configured HTTP entry would otherwise make
        /// the STDIO check false-positive (and vice versa).
        /// </summary>
        public virtual ConfiguratorStatus GetStatus(
            AgentConfiguratorSettings settings,
            Common.Consts.MCP.Server.TransportMethod transport,
            ILogger? logger = null)
        {
            if (IsConfigured(settings, transport, logger))
                return ConfiguratorStatus.Configured;

            var config = transport == Common.Consts.MCP.Server.TransportMethod.stdio
                ? GetStdioConfig(settings, logger)
                : GetHttpConfig(settings, logger, settings.ResolveHttpCredentialMode());

            return config.IsDetected()
                ? ConfiguratorStatus.ReconfigureNeeded
                : ConfiguratorStatus.NotConfigured;
        }

        /// <summary>
        /// Builds the open-URL links (download + tutorial) the consuming engine should render,
        /// mirroring Unity's download/tutorial link emission. The tutorial link is omitted when
        /// <see cref="TutorialUrl"/> is empty.
        /// </summary>
        public virtual IReadOnlyList<ConfigurationItem> BuildLinks()
        {
            var links = new List<ConfigurationItem>();
            if (!string.IsNullOrEmpty(DownloadUrl))
                links.Add(ConfigurationItem.Link(DownloadLinkLabel, DownloadUrl));
            if (!string.IsNullOrEmpty(TutorialUrl))
                links.Add(ConfigurationItem.Link(TutorialLinkLabel, TutorialUrl));
            return links;
        }

        /// <summary>
        /// Builds the engine-agnostic UI description for the requested transport. Subclasses
        /// supply the per-transport sections via <see cref="BuildSections"/>; this method wraps
        /// them with identity + status so every engine sees a uniform shape. When the config is
        /// detected-but-stale (<see cref="ConfiguratorStatus.ReconfigureNeeded"/>) a
        /// "Reconfiguration Required" alert section is prepended, mirroring Unity's reconfigure alert.
        /// </summary>
        public AgentConfiguratorDescription Describe(
            AgentConfiguratorSettings settings,
            Common.Consts.MCP.Server.TransportMethod transport,
            ILogger? logger = null)
        {
            var sections = BuildSections(settings, transport, logger);
            var status = GetStatus(settings, transport, logger);

            // Append the per-agent Troubleshooting section(s) after the configuration sections,
            // mirroring Unity's per-configurator "Troubleshooting" foldout (emitted last). Agents
            // that declare none get an empty list and the sections are unchanged.
            var troubleshooting = BuildTroubleshootingSections(settings, transport, logger);
            if (troubleshooting.Count > 0)
            {
                var withTroubleshooting = new List<ConfigurationSection>(sections);
                withTroubleshooting.AddRange(troubleshooting);
                sections = withTroubleshooting;
            }

            if (status == ConfiguratorStatus.ReconfigureNeeded)
            {
                var withAlert = new List<ConfigurationSection>
                {
                    new ConfigurationSection("Reconfiguration Required", true, new[]
                    {
                        ConfigurationItem.Alert("Connection settings have changed. The existing MCP configuration is outdated and needs to be updated.")
                    })
                };
                withAlert.AddRange(sections);
                sections = withAlert;
            }

            return new AgentConfiguratorDescription(
                agentName: AgentName,
                agentId: AgentId,
                iconName: IconName,
                isConfigured: status == ConfiguratorStatus.Configured,
                isInstalled: false,
                sections: sections,
                status: status,
                links: BuildLinks());
        }

        /// <summary>
        /// Builds the ordered UI sections for the requested transport. Override per agent.
        /// </summary>
        protected abstract IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings,
            Common.Consts.MCP.Server.TransportMethod transport,
            ILogger? logger);

        /// <summary>
        /// Builds the per-agent "Troubleshooting" section(s) appended after the configuration
        /// sections by <see cref="Describe"/>. Engine-agnostic port of each Unity configurator's
        /// "Troubleshooting" foldout content. The base returns an empty list (no troubleshooting);
        /// each concrete agent overrides it with its ported guidance. The transport is supplied so
        /// agents whose stdio/http troubleshooting differs can branch on it.
        /// </summary>
        protected virtual IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(
            AgentConfiguratorSettings settings,
            Common.Consts.MCP.Server.TransportMethod transport,
            ILogger? logger)
            => System.Array.Empty<ConfigurationSection>();

        /// <summary>
        /// Convenience: wraps a set of plain-text troubleshooting lines into a single collapsed
        /// "Troubleshooting" section of <see cref="ConfigurationItem.Description"/> items.
        /// </summary>
        protected static IReadOnlyList<ConfigurationSection> TroubleshootingSection(params string[] lines)
        {
            var items = new ConfigurationItem[lines.Length];
            for (var i = 0; i < lines.Length; i++)
                items[i] = ConfigurationItem.Description(lines[i]);
            return new[] { new ConfigurationSection("Troubleshooting", false, items) };
        }

        /// <summary>
        /// Default single-section description used by agents that simply expose their
        /// expected config-file content for the user to write. Concrete agents with richer
        /// step-by-step UIs override <see cref="BuildSections"/> instead.
        /// </summary>
        protected IReadOnlyList<ConfigurationSection> DefaultConfigurationSections(
            AgentConfiguratorSettings settings,
            Common.Consts.MCP.Server.TransportMethod transport,
            ILogger? logger)
        {
            var config = transport == Common.Consts.MCP.Server.TransportMethod.stdio
                ? GetStdioConfig(settings, logger)
                : GetHttpConfig(settings, logger);
            return new[]
            {
                new ConfigurationSection("Configuration", true, new[]
                {
                    ConfigurationItem.Description($"Use the Configure button to write the MCP entry into {AgentName}'s config file."),
                    ConfigurationItem.ReadOnlyField(config.ExpectedFileContent)
                })
            };
        }

        /// <summary>
        /// Resolves a (possibly project-relative) skills path to an absolute filesystem path,
        /// mirroring the Unity editor's resolution but operating on the supplied value.
        /// </summary>
        protected static string ResolveAbsoluteSkillsPath(string projectRootPath, string folder)
        {
            if (string.IsNullOrEmpty(folder))
                return folder;
            return Path.IsPathRooted(folder)
                ? folder
                : Path.GetFullPath(Path.Combine(projectRootPath, folder));
        }
    }
}
