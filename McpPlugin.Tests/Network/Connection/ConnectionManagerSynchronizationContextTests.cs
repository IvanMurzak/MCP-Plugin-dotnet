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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection
{
    /// <summary>
    /// DEV-240: an engine editor (Godot, Unity, Unreal) calls Connect/Disconnect from its main thread, which
    /// carries a single-threaded <see cref="SynchronizationContext"/>. If the library captured that context, its
    /// continuations (including the one that releases <c>_gate</c>) would queue on the main thread; an assembly
    /// reload then blocks the main thread while it waits for the gate, and the two deadlock
    /// ("Could not acquire gate within timeout", "Failed to unload assemblies").
    /// These tests start the calls on a thread whose context is blocked and never runs a posted callback, and
    /// require that nothing is posted to it and that every call still completes.
    /// </summary>
    public class ConnectionManagerSynchronizationContextTests
    {
        static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(15);

        readonly ILogger _logger;
        readonly Mock<IHubConnectionProvider> _provider = new Mock<IHubConnectionProvider>();
        readonly Common.Version _version = new Common.Version { Api = "1.0.0", Plugin = "1.0.0", Environment = "test" };
        const string Endpoint = "http://localhost:5000/hub";

        /// <summary>Completes when the connect flow, already holding _gate, calls the provider.</summary>
        readonly TaskCompletionSource<bool> _providerCalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConnectionManagerSynchronizationContextTests(ITestOutputHelper output)
        {
            _logger = TestLoggerFactory.Create(output, LogLevel.Debug).CreateLogger<ConnectionManagerSynchronizationContextTests>();

            // The provider completes asynchronously (as a real one does), so the connect flow genuinely yields
            // while it holds _gate. ConfigureAwait(false): the provider is test code, not the code under test.
            _provider
                .Setup(x => x.CreateConnectionAsync(Endpoint))
                .Returns(async () =>
                {
                    _providerCalled.TrySetResult(true);
                    await Task.Delay(50).ConfigureAwait(false);
                    return CreateUnreachableHubConnection();
                });
        }

        [Fact]
        public async Task Connect_StartedOnBlockedMainThread_PostsNothingAndDisconnectAcquiresGate()
        {
            await using var connectionManager = new ConnectionManager(_logger, _version, Endpoint, _provider.Object);
            using var mainThread = new BlockedMainThread();

            var connectTask = mainThread.Start(() => connectionManager.Connect());

            // The reload path: tear the connection down while the main thread stays blocked.
            using var disconnectCts = new CancellationTokenSource(CompletionTimeout);
            var disconnectTask = connectionManager.Disconnect(disconnectCts.Token);

            await WaitOrFail(disconnectTask, "Disconnect");
            disconnectCts.IsCancellationRequested.ShouldBeFalse("Disconnect must acquire _gate, not time out waiting for it");
            (await WaitOrFail(connectTask, "Connect")).ShouldBeFalse("Disconnect cancels the in-flight connect");

            mainThread.Context.PostCount.ShouldBe(0, "library continuations must not be posted to the caller's context");
            mainThread.Context.SendCount.ShouldBe(0);
        }

        [Fact]
        public async Task Disconnect_StartedOnBlockedMainThread_WhileConnecting_PostsNothingAndCompletes()
        {
            await using var connectionManager = new ConnectionManager(_logger, _version, Endpoint, _provider.Object);
            using var mainThread = new BlockedMainThread();

            var connectTask = Task.Run(() => connectionManager.Connect());
            await WaitOrFail(_providerCalled.Task, "Connect reaching the provider"); // the connect flow now holds _gate

            var disconnectTask = mainThread.Start(() => connectionManager.Disconnect());

            await WaitOrFail(disconnectTask, "Disconnect");
            await WaitOrFail(connectTask, "Connect");

            mainThread.Context.PostCount.ShouldBe(0, "library continuations must not be posted to the caller's context");
            mainThread.Context.SendCount.ShouldBe(0);
        }

        [Fact]
        public async Task ConnectThenDispose_StartedOnBlockedMainThread_PostsNothingAndCompletes()
        {
            var connectionManager = new ConnectionManager(_logger, _version, Endpoint, _provider.Object);
            using var mainThread = new BlockedMainThread();

            // Start returns once Connect yields inside the provider, i.e. while the connect flow holds _gate.
            var connectTask = mainThread.Start(() => connectionManager.Connect());
            var disposeTask = mainThread.Start(() => connectionManager.DisposeAsync().AsTask());

            await WaitOrFail(disposeTask, "DisposeAsync");
            await WaitOrFail(connectTask, "Connect");

            mainThread.Context.PostCount.ShouldBe(0, "library continuations must not be posted to the caller's context");
            mainThread.Context.SendCount.ShouldBe(0);
        }

        [Fact]
        public async Task SyncDispose_OnTheBlockedMainThread_WhileItsConnectHoldsTheGate_AcquiresTheGate()
        {
            // The exact reload shape: the main thread started Connect, then blocks inside a synchronous teardown that
            // waits for _gate. The connect flow must release the gate without needing the main thread.
            var logger = new CapturingLogger();
            var connectionManager = new ConnectionManager(logger, _version, Endpoint, _provider.Object);
            using var mainThread = new BlockedMainThread();

            var connectTask = mainThread.Start(() => connectionManager.Connect()); // returns while the connect flow holds _gate
            await WaitOrFail(mainThread.Start(() => { connectionManager.Dispose(); return Task.CompletedTask; }), "Dispose");
            await WaitOrFail(connectTask, "Connect");

            logger.Warnings.ShouldNotContain(w => w.Contains("Could not acquire gate"));
            mainThread.Context.PostCount.ShouldBe(0, "library continuations must not be posted to the caller's context");
        }

        static async Task WaitOrFail(Task task, string what)
        {
            var completed = await Task.WhenAny(task, Task.Delay(CompletionTimeout));
            if (completed != task)
                throw new TimeoutException($"{what} did not complete: a continuation is stuck on the blocked main-thread context");
            await task;
        }

        static async Task<T> WaitOrFail<T>(Task<T> task, string what)
        {
            await WaitOrFail((Task)task, what);
            return await task;
        }

        static HubConnection CreateUnreachableHubConnection() => new HubConnectionBuilder()
            .WithUrl("http://localhost:9999/test", options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
            })
            .Build();

        /// <summary>A single-threaded context whose thread is blocked (as during a reload): it records and drops every callback.</summary>
        sealed class BlockedContext : SynchronizationContext
        {
            int _postCount;
            int _sendCount;
            public int PostCount => Volatile.Read(ref _postCount);
            public int SendCount => Volatile.Read(ref _sendCount);
            public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _postCount);
            public override void Send(SendOrPostCallback d, object? state)
            {
                Interlocked.Increment(ref _sendCount);
                throw new InvalidOperationException("The main thread is blocked.");
            }
            public override SynchronizationContext CreateCopy() => this;
        }

        /// <summary>
        /// A dedicated "engine main thread": runs each start delegate with <see cref="BlockedContext"/> installed,
        /// then stays blocked (never pumps the context) until disposed.
        /// </summary>
        sealed class BlockedMainThread : IDisposable
        {
            readonly Thread _thread;
            readonly BlockingCollection<Action?> _work = new BlockingCollection<Action?>();
            public BlockedContext Context { get; } = new BlockedContext();

            public BlockedMainThread()
            {
                _thread = new Thread(() =>
                {
                    SynchronizationContext.SetSynchronizationContext(Context);
                    while (_work.Take() is Action action)
                        action();
                }) { IsBackground = true, Name = "blocked-main-thread" };
                _thread.Start();
            }

            /// <summary>Calls <paramref name="start"/> on the main thread and returns the task it started.</summary>
            public Task Start(Func<Task> start) => Start(async () => { await start().ConfigureAwait(false); return true; });

            public Task<T> Start<T>(Func<Task<T>> start)
            {
                var started = new TaskCompletionSource<Task<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
                _work.Add(() =>
                {
                    try { started.SetResult(start()); }
                    catch (Exception ex) { started.SetException(ex); }
                });
                // The start delegate runs synchronously up to its first yielding await, so this returns quickly.
                return started.Task.Wait(CompletionTimeout)
                    ? started.Task.Result
                    : throw new TimeoutException("The main-thread start delegate did not return");
            }

            public void Dispose()
            {
                _work.Add(null);
                _thread.Join(CompletionTimeout);
                _work.Dispose();
            }
        }
    }
}
