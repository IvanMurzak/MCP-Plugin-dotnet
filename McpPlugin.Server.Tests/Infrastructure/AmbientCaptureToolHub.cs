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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Auth;

namespace com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure
{
    /// <summary>
    /// Everything <see cref="McpSessionTokenContext"/> exposes, read at ONE instant. The point of
    /// snapshotting the whole surface rather than the one flag under test is that the ambient values
    /// share a single transport mechanism (the session's captured
    /// <see cref="System.Threading.ExecutionContext"/>), so a claim about one of them is only as
    /// good as the evidence about the mechanism — and the mechanism is what the other eight show.
    /// </summary>
    public sealed class AmbientSnapshot
    {
        public string? Token { get; private set; }
        public string? SessionId { get; private set; }
        public string? ProjectPin { get; private set; }
        public string? SelectedInstanceId { get; private set; }
        public string? ClientIp { get; private set; }
        public string? UserAgent { get; private set; }
        public string? AccountId { get; private set; }
        public bool IsTrustedInternalClient { get; private set; }
        public bool IsSkillMetaClient { get; private set; }

        public static AmbientSnapshot Capture() => new AmbientSnapshot
        {
            Token = McpSessionTokenContext.CurrentToken,
            SessionId = McpSessionTokenContext.CurrentSessionId,
            ProjectPin = McpSessionTokenContext.CurrentProjectPin,
            SelectedInstanceId = McpSessionTokenContext.CurrentSelectedInstanceId,
            ClientIp = McpSessionTokenContext.CurrentClientIp,
            UserAgent = McpSessionTokenContext.CurrentUserAgent,
            AccountId = McpSessionTokenContext.CurrentIdentity?.AccountId,
            IsTrustedInternalClient = McpSessionTokenContext.IsTrustedInternalClient,
            IsSkillMetaClient = McpSessionTokenContext.IsSkillMetaClient
        };

        public override string ToString()
            => $"UserAgent={UserAgent ?? "<null>"}, SessionId={SessionId ?? "<null>"}, " +
               $"ProjectPin={ProjectPin ?? "<null>"}, ClientIp={ClientIp ?? "<null>"}, " +
               $"AccountId={AccountId ?? "<null>"}, Token={(Token == null ? "<null>" : "<set>")}, " +
               $"SelectedInstanceId={SelectedInstanceId ?? "<null>"}, " +
               $"IsTrustedInternalClient={IsTrustedInternalClient}, IsSkillMetaClient={IsSkillMetaClient}";
    }

    /// <summary>
    /// Stands in for <c>RemoteToolRunner</c> so a real MCP session can answer <c>tools/list</c> and
    /// <c>tools/call</c> without a live SignalR plugin — and, more importantly, so a test can read
    /// the ambient session state from INSIDE the MCP request handler.
    ///
    /// <para>This is the measurement seam. <c>ToolRouter.ListAll</c> awaits
    /// <see cref="RunListTool"/> on whatever <see cref="System.Threading.ExecutionContext"/> the SDK
    /// dispatched the handler on, so a snapshot taken here is, by construction, exactly what
    /// <c>ExtensionsListMeta.BuildToolMeta</c> is about to read a few frames later. Nothing above
    /// the transport can observe that context — which is precisely why the defect this fixture
    /// family exists for was invisible to every in-process test.</para>
    /// </summary>
    public sealed class AmbientCaptureToolHub : IClientToolHub
    {
        /// <summary>A tool that declares BOTH skill values — the fixture the gate is measured on.</summary>
        public const string AttributedToolName = "gameobject-create";

        /// <summary>A tool that declares NEITHER — the in-cell control for "keys are attribute-driven".</summary>
        public const string UnattributedToolName = "skill-tool-none";

        public const string SkillDescriptionText = "Creates a primitive object in the scene";
        public const string SkillBodyText = "# GameObject create\n\nUse `path` to nest the new object.";

        readonly List<AmbientSnapshot> _listSnapshots = new List<AmbientSnapshot>();
        readonly List<AmbientSnapshot> _callSnapshots = new List<AmbientSnapshot>();
        readonly object _gate = new object();

        /// <summary>Ambient state observed inside each <c>tools/list</c> handler, in call order.</summary>
        public IReadOnlyList<AmbientSnapshot> ListSnapshots
        {
            get { lock (_gate) return _listSnapshots.ToList(); }
        }

        /// <summary>Ambient state observed inside each <c>tools/call</c> handler, in call order.</summary>
        public IReadOnlyList<AmbientSnapshot> CallSnapshots
        {
            get { lock (_gate) return _callSnapshots.ToList(); }
        }

        public AmbientSnapshot LastListSnapshot
        {
            get
            {
                lock (_gate)
                {
                    if (_listSnapshots.Count == 0)
                        throw new InvalidOperationException("tools/list never reached the plugin seam.");
                    return _listSnapshots[_listSnapshots.Count - 1];
                }
            }
        }

        public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
                _listSnapshots.Add(AmbientSnapshot.Capture());

            var tools = new[]
            {
                new ResponseListTool
                {
                    Name = AttributedToolName,
                    Enabled = true,
                    InputSchema = Consts.MCP.EmptyInputSchema,
                    SkillDescription = SkillDescriptionText,
                    SkillBody = SkillBodyText
                },
                new ResponseListTool
                {
                    Name = UnattributedToolName,
                    Enabled = true,
                    InputSchema = Consts.MCP.EmptyInputSchema
                }
            };

            return Task.FromResult(tools.Pack("req-ok"));
        }

        public Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request)
        {
            lock (_gate)
                _callSnapshots.Add(AmbientSnapshot.Capture());

            return Task.FromResult(ResponseCallTool.Success("ok").Pack("req-ok"));
        }
    }

    /// <summary>
    /// A catalog with one ENABLED and one DISABLED tool, and no skill metadata anywhere. Used to
    /// observe the <c>X-McpPlugin-Internal-Client</c> axis over the wire through catalog
    /// VISIBILITY, which is a different observable from <c>_meta</c> — so a test built on it cannot
    /// pass or fail for the skill gate's reasons.
    /// </summary>
    public sealed class DisabledToolHub : IClientToolHub
    {
        public const string EnabledToolName = "enabled-tool";
        public const string DisabledToolName = "disabled-tool";

        public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request, CancellationToken cancellationToken = default)
        {
            var tools = new[]
            {
                new ResponseListTool { Name = EnabledToolName, Enabled = true, InputSchema = Consts.MCP.EmptyInputSchema },
                new ResponseListTool { Name = DisabledToolName, Enabled = false, InputSchema = Consts.MCP.EmptyInputSchema }
            };
            return Task.FromResult(tools.Pack("req-ok"));
        }

        public Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request)
            => throw new NotSupportedException("This fixture only drives tools/list.");
    }

    /// <summary>
    /// The prompt-side twin of <see cref="DisabledToolHub"/>: one enabled and one disabled prompt,
    /// so the trusted-client visibility axis can be observed on <c>prompts/list</c>.
    /// </summary>
    public sealed class DisabledPromptHub : IClientPromptHub
    {
        public const string EnabledPromptName = "enabled-prompt";
        public const string DisabledPromptName = "disabled-prompt";

        public Task<ResponseData<ResponseListPrompts>> RunListPrompts(RequestListPrompts request, CancellationToken cancellationToken = default)
        {
            var response = new ResponseListPrompts
            {
                Prompts = new List<ResponsePrompt>
                {
                    new ResponsePrompt { Name = EnabledPromptName, Description = "enabled", Enabled = true },
                    new ResponsePrompt { Name = DisabledPromptName, Description = "disabled", Enabled = false }
                }
            };
            return Task.FromResult(response.Pack("req-ok"));
        }

        public Task<ResponseData<ResponseGetPrompt>> RunGetPrompt(RequestGetPrompt request)
            => throw new NotSupportedException("This fixture only drives prompts/list.");
    }

    /// <summary>
    /// The resource-side twin: one enabled and one disabled resource, for <c>resources/list</c>.
    /// </summary>
    public sealed class DisabledResourceHub : IClientResourceHub
    {
        public const string EnabledResourceUri = "test://enabled";
        public const string DisabledResourceUri = "test://disabled";

        public Task<ResponseData<ResponseResourceContent[]>> RunResourceContent(RequestResourceContent request)
            => throw new NotSupportedException("This fixture only drives resources/list.");

        public Task<ResponseData<ResponseListResource[]>> RunListResources(RequestListResources request, CancellationToken cancellationToken = default)
        {
            var resources = new[]
            {
                new ResponseListResource(EnabledResourceUri, "enabled-resource", enabled: true),
                new ResponseListResource(DisabledResourceUri, "disabled-resource", enabled: false)
            };
            return Task.FromResult(resources.Pack("req-ok"));
        }

        public Task<ResponseData<ResponseResourceTemplate[]>> RunResourceTemplates(RequestListResourceTemplates request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ResponseResourceTemplate[0].Pack("req-ok"));
    }
}
