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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tools;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Tests for the server-native selection + enrollment tools (mcp-authorize b4, design 04):
    /// <c>list_engine_instances</c>, <c>select_engine_instance</c> (sticky, pin-narrowing),
    /// <c>enroll_engine_plugin</c> (mocked AS). Also verifies the descriptors that
    /// <see cref="ToolRouter"/> merges into <c>tools/list</c>.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public class ServerNativeToolsTests
    {
        const string Account = "acc-1";
        const string Session = "session-1";
        const string HashA = "aabbccdd11223344556677889900aabbccddeeff00112233445566778899aabb";
        const string PinA = "aabbccdd";
        const string HashB = "99887766554433221100ffeeddccbbaa99887766554433221100ffeeddccbbaa";
        const string PinB = "99887766";

        sealed class Clock
        {
            public DateTimeOffset Now;
            public Clock(DateTimeOffset start) => Now = start;
            public void Advance(TimeSpan by) => Now += by;
        }

        sealed class FakeEnrollment : IEnrollmentClient
        {
            public EnrollmentResult Result = EnrollmentResult.Ok("CODE123");
            public string? SeenEngine;
            public string? SeenBearer;
            public Task<EnrollmentResult> CreateAsync(string engine, string bearer, CancellationToken cancellationToken = default)
            {
                SeenEngine = engine;
                SeenBearer = bearer;
                return Task.FromResult(Result);
            }
        }

        static PluginInstanceMetadata Meta(string instanceId, string engine = "unity", string project = "MyGame", string pathHash = HashA, string machine = "PC-1")
            => new PluginInstanceMetadata(instanceId, engine, project, pathHash, machine);

        static IReadOnlyDictionary<string, JsonElement> Args(params (string key, string value)[] kv)
            => kv.ToDictionary(p => p.key, p => JsonSerializer.SerializeToElement(p.value));

        static readonly IReadOnlyDictionary<string, JsonElement> NoArgs = new Dictionary<string, JsonElement>();

        static SelectionToolContext Ctx(string? account = Account, string? session = Session, string? pin = null, string? bearer = "bearer-token")
            => new SelectionToolContext(account, session, pin, bearer);

        static string Text(ResponseCallTool r) => r.GetMessage() ?? string.Empty;

        static (ServerNativeTools tools, AccountInstances registry, SessionSelectionStore selections, FakeEnrollment enroll, Clock clock) NewTools()
        {
            var clock = new Clock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var registry = new AccountInstances(() => clock.Now);
            var selections = new SessionSelectionStore();
            var enroll = new FakeEnrollment();
            var tools = new ServerNativeTools(registry, selections, enroll);
            return (tools, registry, selections, enroll, clock);
        }

        // Reset the ambient AsyncLocal that HandleSelect writes for immediate-request effect.
        sealed class Reset : IDisposable
        {
            public void Dispose() => McpSessionTokenContext.CurrentSelectedInstanceId = null;
        }

        // ─────────────────────────────── Descriptors (tools/list merge) ───────────────────────────────

        [Fact]
        public void Descriptors_AreTheThreeServerNativeTools_WithObjectSchema()
        {
            var (tools, _, _, _, _) = NewTools();
            var names = tools.Descriptors.Select(t => t.Name).ToList();

            names.ShouldContain(ServerNativeTools.ListInstances);
            names.ShouldContain(ServerNativeTools.SelectInstance);
            names.ShouldContain(ServerNativeTools.EnrollPlugin);
            names.Count.ShouldBe(3);

            foreach (var descriptor in tools.Descriptors)
            {
                descriptor.Description.ShouldNotBeNullOrEmpty();
                descriptor.InputSchema.GetProperty("type").GetString().ShouldBe("object");
            }
        }

        [Theory]
        [InlineData("unity", "unity-mcp-cli")]
        [InlineData("godot", "godot-cli")]
        [InlineData("unreal", "unreal-mcp-cli")]
        public void CliPackage_MapsEngineToPublishedCli(string engine, string expected)
            => ServerNativeTools.CliPackage(engine).ShouldBe(expected);

        [Fact]
        public void IsServerNativeTool_RecognizesTheThreeNames()
        {
            ServerNativeTools.IsServerNativeTool(ServerNativeTools.ListInstances).ShouldBeTrue();
            ServerNativeTools.IsServerNativeTool(ServerNativeTools.SelectInstance).ShouldBeTrue();
            ServerNativeTools.IsServerNativeTool(ServerNativeTools.EnrollPlugin).ShouldBeTrue();
            ServerNativeTools.IsServerNativeTool("some_plugin_tool").ShouldBeFalse();
            ServerNativeTools.IsServerNativeTool(null).ShouldBeFalse();
        }

        // ─────────────────────────────── list_engine_instances ───────────────────────────────

        [Fact]
        public async Task List_EmptyAccount_ReturnsEnrollGuidance()
        {
            var (tools, _, _, _, _) = NewTools();
            var response = await tools.HandleAsync(ServerNativeTools.ListInstances, NoArgs, Ctx());
            response.Status.ShouldBe(ResponseStatus.Success);
            Text(response).ShouldContain("enroll_engine_plugin");
        }

        [Fact]
        public async Task List_ReturnsInstances_MarksSelected()
        {
            var (tools, registry, selections, _, _) = NewTools();
            registry.Register(Account, Meta("inst-A", project: "GameA"), "conn-A");
            registry.Register(Account, Meta("inst-B", project: "GameB", pathHash: HashB, machine: "PC-2"), "conn-B");
            selections.Set(Session, "inst-B");

            var response = await tools.HandleAsync(ServerNativeTools.ListInstances, NoArgs, Ctx());

            response.Status.ShouldBe(ResponseStatus.Success);
            var text = Text(response);
            text.ShouldContain("inst-A");
            text.ShouldContain("inst-B");
            // The selected instance line carries the marker; the unselected one does not.
            text.ShouldContain("inst-B");
            text.Split('\n').Single(l => l.Contains("inst-B")).ShouldContain("(selected)");
            text.Split('\n').Single(l => l.Contains("inst-A")).ShouldNotContain("(selected)");
        }

        [Fact]
        public async Task List_IsAccountScoped()
        {
            var (tools, registry, _, _, _) = NewTools();
            registry.Register("other-acc", Meta("inst-X"), "conn-X");

            var response = await tools.HandleAsync(ServerNativeTools.ListInstances, NoArgs, Ctx());
            Text(response).ShouldNotContain("inst-X");
        }

        // ─────────────────────────── Routing diagnostics (auth-fixes T6 / B6 / B7) ───────────────────────────

        [Fact]
        public async Task List_ShowsPerInstancePin_AndProjectBasename()
        {
            var (tools, registry, _, _, _) = NewTools();
            registry.Register(Account, Meta("inst-A", project: "GameA"), "conn-A");

            var text = Text(await tools.HandleAsync(ServerNativeTools.ListInstances, NoArgs, Ctx()));

            // The instance line now carries the 8-hex routing pin and the project basename (B7).
            text.ShouldContain($"pin {PinA}");
            text.ShouldContain("unity:GameA");
        }

        [Fact]
        public async Task List_PinnedSession_WithMatch_ShowsMatchedInstance()
        {
            var (tools, registry, _, _, _) = NewTools();
            registry.Register(Account, Meta("inst-A", pathHash: HashA), "conn-A");

            var text = Text(await tools.HandleAsync(ServerNativeTools.ListInstances, NoArgs, Ctx(pin: PinA)));

            text.ShouldContain($"session pin: {PinA}");
            text.ShouldContain("matched: inst-A");
        }

        [Fact]
        public async Task List_PinnedSession_NoMatch_ShowsNoneWithActionableHint()
        {
            var (tools, registry, _, _, _) = NewTools();
            // A live instance exists, but not for the pinned project (B6 NoMatchPinned diagnosis).
            registry.Register(Account, Meta("inst-B", project: "Beta", pathHash: HashB, machine: "PC-2"), "conn-B");

            var text = Text(await tools.HandleAsync(ServerNativeTools.ListInstances, NoArgs, Ctx(pin: PinA)));

            text.ShouldContain($"session pin: {PinA}");
            text.ShouldContain("matched: none");
            text.ShouldContain("select_engine_instance");
        }

        [Fact]
        public async Task List_UnpinnedSession_ReportsNoPin()
        {
            var (tools, registry, _, _, _) = NewTools();
            registry.Register(Account, Meta("inst-A"), "conn-A");

            var text = Text(await tools.HandleAsync(ServerNativeTools.ListInstances, NoArgs, Ctx(pin: null)));

            text.ShouldContain("session pin: none");
        }

        [Fact]
        public async Task List_EmptyAccount_PinnedSession_ShowsNoneWithEnrollHint()
        {
            var (tools, _, _, _, _) = NewTools();

            var text = Text(await tools.HandleAsync(ServerNativeTools.ListInstances, NoArgs, Ctx(pin: PinA)));

            text.ShouldContain($"session pin: {PinA}");
            text.ShouldContain("matched: none");
            text.ShouldContain("enroll_engine_plugin");
        }

        [Fact]
        public async Task List_ProjectDiagnostic_ShowsBasenameOnly_NeverFullPath()
        {
            var (tools, registry, _, _, _) = NewTools();
            // A plugin self-reports a path-like project name; diagnostics must leak only the basename
            // (06 threat table — never a full filesystem path).
            registry.Register(Account, Meta("inst-A", project: @"C:\Users\secret\Projects\MyGame"), "conn-A");

            var text = Text(await tools.HandleAsync(ServerNativeTools.ListInstances, NoArgs, Ctx()));

            text.ShouldContain("unity:MyGame");
            text.ShouldNotContain("secret");
            text.ShouldNotContain(@"C:\Users");
        }

        // ─────────────────────────────── select_engine_instance ───────────────────────────────

        [Fact]
        public async Task Select_ByInstanceId_StoresSelection_AndSucceeds()
        {
            var (tools, registry, selections, _, _) = NewTools();
            registry.Register(Account, Meta("inst-A"), "conn-A");

            using (new Reset())
            {
                var response = await tools.HandleAsync(ServerNativeTools.SelectInstance, Args(("instance_id", "inst-A")), Ctx());

                response.Status.ShouldBe(ResponseStatus.Success);
                selections.Get(Session).ShouldBe("inst-A");
                // Immediate within-request effect for routing.
                McpSessionTokenContext.CurrentSelectedInstanceId.ShouldBe("inst-A");
            }
        }

        [Fact]
        public async Task Select_UnknownInstanceId_ReturnsError()
        {
            var (tools, registry, selections, _, _) = NewTools();
            registry.Register(Account, Meta("inst-A"), "conn-A");

            var response = await tools.HandleAsync(ServerNativeTools.SelectInstance, Args(("instance_id", "nope")), Ctx());

            response.Status.ShouldBe(ResponseStatus.Error);
            selections.Get(Session).ShouldBeNull();
        }

        [Fact]
        public async Task Select_ByUniqueProjectName_Succeeds()
        {
            var (tools, registry, selections, _, _) = NewTools();
            registry.Register(Account, Meta("inst-A", project: "Alpha"), "conn-A");
            registry.Register(Account, Meta("inst-B", project: "Beta", pathHash: HashB, machine: "PC-2"), "conn-B");

            var response = await tools.HandleAsync(ServerNativeTools.SelectInstance, Args(("project_name", "Beta")), Ctx());

            response.Status.ShouldBe(ResponseStatus.Success);
            selections.Get(Session).ShouldBe("inst-B");
        }

        [Fact]
        public async Task Select_AmbiguousProjectName_ReturnsError()
        {
            var (tools, registry, _, _, clock) = NewTools();
            registry.Register(Account, Meta("inst-A", project: "Same", machine: "PC-1"), "conn-A");
            clock.Advance(TimeSpan.FromSeconds(1));
            registry.Register(Account, Meta("inst-B", project: "Same", pathHash: HashB, machine: "PC-2"), "conn-B");

            var response = await tools.HandleAsync(ServerNativeTools.SelectInstance, Args(("project_name", "Same")), Ctx());
            response.Status.ShouldBe(ResponseStatus.Error);
        }

        [Fact]
        public async Task Select_NoArguments_ReturnsError()
        {
            var (tools, registry, _, _, _) = NewTools();
            registry.Register(Account, Meta("inst-A"), "conn-A");

            var response = await tools.HandleAsync(ServerNativeTools.SelectInstance, NoArgs, Ctx());
            response.Status.ShouldBe(ResponseStatus.Error);
        }

        [Fact]
        public async Task Select_NoSessionId_ReturnsError()
        {
            var (tools, registry, _, _, _) = NewTools();
            registry.Register(Account, Meta("inst-A"), "conn-A");

            var response = await tools.HandleAsync(ServerNativeTools.SelectInstance, Args(("instance_id", "inst-A")), Ctx(session: null));
            response.Status.ShouldBe(ResponseStatus.Error);
        }

        [Fact]
        public async Task Select_PinnedSession_CannotSelectDifferentProject()
        {
            var (tools, registry, selections, _, _) = NewTools();
            // Live instance is for HashB (project Beta); session is pinned to HashA (project Alpha).
            registry.Register(Account, Meta("inst-B", project: "Beta", pathHash: HashB, machine: "PC-2"), "conn-B");

            var response = await tools.HandleAsync(ServerNativeTools.SelectInstance, Args(("instance_id", "inst-B")), Ctx(pin: PinA));

            response.Status.ShouldBe(ResponseStatus.Error);
            selections.Get(Session).ShouldBeNull();
        }

        [Fact]
        public async Task Select_PinnedSession_CanNarrowWithinPinnedProject()
        {
            var (tools, registry, selections, _, _) = NewTools();
            // Two instances of the SAME pinned project (e.g. two editors) — selection may narrow.
            registry.Register(Account, Meta("inst-A", machine: "PC-1"), "conn-A");
            registry.Register(Account, Meta("inst-A2", machine: "PC-2"), "conn-A2");

            using (new Reset())
            {
                var response = await tools.HandleAsync(ServerNativeTools.SelectInstance, Args(("instance_id", "inst-A2")), Ctx(pin: PinA));

                response.Status.ShouldBe(ResponseStatus.Success);
                selections.Get(Session).ShouldBe("inst-A2");
            }
        }

        // ─────── Stickiness end-to-end: select persists across the store reload and beats MRU ───────

        [Fact]
        public async Task Select_Stickiness_SurvivesStoreReload_AndBeatsMru()
        {
            var (tools, registry, selections, _, clock) = NewTools();
            // Two unpinned instances; inst-A is the most-recently-active (default MRU pick).
            registry.Register(Account, Meta("inst-B", project: "GameB", pathHash: HashB, machine: "PC-B"), "conn-B");
            clock.Advance(TimeSpan.FromSeconds(10));
            registry.Register(Account, Meta("inst-A", project: "GameA", pathHash: HashA, machine: "PC-A"), "conn-A");

            // Baseline: with no selection, resolution picks MRU inst-A.
            registry.Resolve(Account, null, null).Instance!.InstanceId.ShouldBe("inst-A");

            using (new Reset())
            {
                // Agent selects the NON-MRU instance-B in one request.
                var select = await tools.HandleAsync(ServerNativeTools.SelectInstance, Args(("instance_id", "inst-B")), Ctx());
                select.Status.ShouldBe(ResponseStatus.Success);
            }

            // Next request: the middleware reloads the stored selection into the ambient context.
            var reloaded = selections.Get(Session);
            reloaded.ShouldBe("inst-B");

            // Routing now honors the sticky selection over MRU.
            registry.Resolve(Account, null, reloaded).Instance!.InstanceId.ShouldBe("inst-B");
        }

        // ─────────────────────────────── enroll_engine_plugin ───────────────────────────────

        [Fact]
        public async Task Enroll_ValidEngine_ReturnsReadyToRunInstallCommand()
        {
            var (tools, _, _, enroll, _) = NewTools();
            enroll.Result = EnrollmentResult.Ok("ONE-TIME-CODE");

            var response = await tools.HandleAsync(ServerNativeTools.EnrollPlugin, Args(("engine", "godot")), Ctx());

            response.Status.ShouldBe(ResponseStatus.Success);
            Text(response).ShouldContain("npx godot-cli install-plugin --enroll ONE-TIME-CODE");
            // The session credential was forwarded verbatim to the enroll proxy.
            enroll.SeenBearer.ShouldBe("bearer-token");
            enroll.SeenEngine.ShouldBe("godot");
        }

        [Theory]
        [InlineData("unity", "unity-mcp-cli")]
        [InlineData("godot", "godot-cli")]
        [InlineData("unreal", "unreal-mcp-cli")]
        public async Task Enroll_EmitsCorrectCliPerEngine(string engine, string cli)
        {
            var (tools, _, _, enroll, _) = NewTools();
            enroll.Result = EnrollmentResult.Ok("C0DE");
            var response = await tools.HandleAsync(ServerNativeTools.EnrollPlugin, Args(("engine", engine)), Ctx());
            response.Status.ShouldBe(ResponseStatus.Success);
            Text(response).ShouldContain($"npx {cli} install-plugin --enroll C0DE");
        }

        [Fact]
        public async Task Enroll_InvalidEngine_ReturnsError()
        {
            var (tools, _, _, _, _) = NewTools();
            var response = await tools.HandleAsync(ServerNativeTools.EnrollPlugin, Args(("engine", "cryengine")), Ctx());
            response.Status.ShouldBe(ResponseStatus.Error);
        }

        [Fact]
        public async Task Enroll_MissingEngine_ReturnsError()
        {
            var (tools, _, _, _, _) = NewTools();
            var response = await tools.HandleAsync(ServerNativeTools.EnrollPlugin, NoArgs, Ctx());
            response.Status.ShouldBe(ResponseStatus.Error);
        }

        [Fact]
        public async Task Enroll_NoCredential_ReturnsError_WithoutCallingTheAs()
        {
            var (tools, _, _, enroll, _) = NewTools();
            var response = await tools.HandleAsync(ServerNativeTools.EnrollPlugin, Args(("engine", "unity")), Ctx(bearer: null));

            response.Status.ShouldBe(ResponseStatus.Error);
            enroll.SeenEngine.ShouldBeNull(); // proxy not called
        }

        [Fact]
        public async Task Enroll_AsFailure_SurfacesError()
        {
            var (tools, _, _, enroll, _) = NewTools();
            enroll.Result = EnrollmentResult.Fail("The authorization server rejected the enrollment request.");

            var response = await tools.HandleAsync(ServerNativeTools.EnrollPlugin, Args(("engine", "unity")), Ctx());

            response.Status.ShouldBe(ResponseStatus.Error);
            Text(response).ShouldContain("rejected");
        }

        [Fact]
        public async Task Enroll_PatSession_ForwardsPatVerbatim()
        {
            var (tools, _, _, enroll, _) = NewTools();
            // A PAT-authenticated agent session presents an opaque token; it is forwarded like a JWT.
            var response = await tools.HandleAsync(ServerNativeTools.EnrollPlugin, Args(("engine", "unreal")), Ctx(bearer: "pat_opaque_value"));

            response.Status.ShouldBe(ResponseStatus.Success);
            enroll.SeenBearer.ShouldBe("pat_opaque_value");
        }
    }
}
