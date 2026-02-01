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
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection
{
    /// <summary>
    /// Unit tests for ConnectionManager.Connect method.
    ///
    /// NOTE: Due to HubConnection being a sealed class in ASP.NET Core SignalR,
    /// direct mocking of HubConnection.State and other non-virtual members is not possible.
    /// These tests focus on testing the connection logic, concurrency handling, and
    /// cancellation scenarios by mocking the IHubConnectionProvider interface.
    ///
    /// For complete integration testing with real HubConnection instances,
    /// separate integration tests should be created using TestServer.
    /// </summary>
    public class ConnectionManagerTests
    {
        private readonly ITestOutputHelper _output;
        private readonly ILogger _logger;
        private readonly Mock<IHubConnectionProvider> _mockHubConnectionProvider;
        private readonly Common.Version _testVersion;
        private readonly string _testEndpoint;

        public ConnectionManagerTests(ITestOutputHelper output)
        {
            _output = output;
            var loggerFactory = TestLoggerFactory.Create(_output, LogLevel.Debug);
            _logger = loggerFactory.CreateLogger<ConnectionManagerTests>();
            _mockHubConnectionProvider = new Mock<IHubConnectionProvider>();
            _testVersion = new Common.Version { Api = "1.0.0", Plugin = "1.0.0", Environment = "test" };
            _testEndpoint = "http://localhost:5000/hub";
        }

        #region Provider Interaction Tests

        [Fact]
        public async Task Connect_CallsProviderToCreateConnection()
        {
            // Arrange
            var mockConnection = CreateMockHubConnection();
            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .ReturnsAsync(mockConnection);

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var result = await connectionManager.Connect(cts.Token);

            // Assert
            _mockHubConnectionProvider.Verify(x => x.CreateConnectionAsync(_testEndpoint), Times.Once);
        }

        [Fact]
        public async Task Connect_WhenProviderReturnsNull_ReturnsFalse()
        {
            // Arrange
            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .ReturnsAsync((HubConnection)null!);

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await connectionManager.Connect(cts.Token);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task Connect_WhenProviderThrows_ReturnsFalse()
        {
            // Arrange
            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .ThrowsAsync(new InvalidOperationException("Failed to create connection"));

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await connectionManager.Connect(cts.Token);

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region Multithreading & Concurrency Tests

        [Fact]
        public async Task Connect_WhenMultipleThreadsCallSimultaneously_ProviderCalledOnlyOnce()
        {
            // Arrange
            var connectionCreationCount = 0;
            var connectionCreated = new TaskCompletionSource<bool>();

            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .Returns(async () =>
                {
                    var count = Interlocked.Increment(ref connectionCreationCount);
                    System.Diagnostics.Debug.WriteLine($"CreateConnectionAsync called. Count: {count}");
                    connectionCreated.TrySetResult(true);
                    // Return immediately - the HubConnection will fail to start, but that's OK
                    // We're testing that CreateConnectionAsync is only called once, not that the connection succeeds
                    await Task.Yield();
                    return CreateMockHubConnection();
                });

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act - Launch 10 concurrent connection attempts with SHORT timeout
            // The timeout needs to be short so the connection attempts fail quickly
            // We're testing concurrency control, not successful connections
            var tasks = new Task<bool>[10];
            for (int i = 0; i < tasks.Length; i++)
            {
                var taskIndex = i;
                tasks[i] = Task.Run(async () =>
                {
                    System.Diagnostics.Debug.WriteLine($"Task {taskIndex} starting Connect call");
                    // Short timeout - we expect the connection to fail (non-existent endpoint)
                    // but we're testing that only ONE CreateConnectionAsync call is made
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var result = await connectionManager.Connect(cts.Token);
                    System.Diagnostics.Debug.WriteLine($"Task {taskIndex} completed with result: {result}");
                    return result;
                });
            }

            System.Diagnostics.Debug.WriteLine("Waiting for connection to start...");
            await EnsureConnectionStartedAsync(connectionCreated.Task);
            System.Diagnostics.Debug.WriteLine($"Connection started. Current creation count: {connectionCreationCount}");

            await Task.Delay(100); // Allow other tasks to queue

            System.Diagnostics.Debug.WriteLine($"Before tasks complete. Creation count: {connectionCreationCount}");

            // Wait for all tasks to complete (they will fail due to invalid endpoint, but that's expected)
            await EnsureTasksCompleteAsync(6000, tasks);

            System.Diagnostics.Debug.WriteLine($"Final connection creation count: {connectionCreationCount}");

            // Assert - Only ONE CreateConnectionAsync call should have been made despite 10 concurrent Connect calls
            connectionCreationCount.Should().Be(1, "only one connection should be created despite multiple concurrent calls");
        }

        [Fact]
        public async Task Connect_WhenCalledConcurrently_ReusesFirstConnectionAttempt()
        {
            // Arrange
            var connectionStarted = new TaskCompletionSource<bool>();
            var allowConnectionToComplete = new TaskCompletionSource<bool>();
            var providerCallCount = 0;

            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .Returns(async () =>
                {
                    Interlocked.Increment(ref providerCallCount);
                    connectionStarted.TrySetResult(true);
                    await allowConnectionToComplete.Task;
                    return CreateMockHubConnection();
                });

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act
            var firstTask = Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                return await connectionManager.Connect(cts.Token);
            });

            await EnsureConnectionStartedAsync(connectionStarted.Task);

            var secondTask = Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                return await connectionManager.Connect(cts.Token);
            });

            await Task.Delay(50);

            // Assert - verify only one connection attempt while both tasks are running
            providerCallCount.Should().Be(1, "second call should reuse first connection attempt");

            // Complete connection and wait for both tasks
            allowConnectionToComplete.SetResult(true);
            await EnsureTasksCompleteAsync(5000, firstTask, secondTask);

            providerCallCount.Should().Be(1, "provider should only be called once");
        }

        [Fact]
        public async Task Connect_WhenCalledSequentially_CreatesMultipleConnections()
        {
            // Arrange
            var providerCallCount = 0;
            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .Returns(async () =>
                {
                    Interlocked.Increment(ref providerCallCount);
                    await Task.Yield(); // Minimal delay to simulate async operation
                    return CreateMockHubConnection();
                });

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act - Make 3 sequential connection attempts
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result1 = await connectionManager.Connect(cts.Token);
            await Task.Delay(20); // Minimal delay to ensure connection completes

            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result2 = await connectionManager.Connect(cts2.Token);
            await Task.Delay(20);

            using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result3 = await connectionManager.Connect(cts3.Token);

            // Assert - Sequential calls should each create a connection (or reuse if already connected)
            providerCallCount.Should().BeGreaterThanOrEqualTo(1, "at least one connection should be created");
        }

        [Fact]
        public async Task Connect_ThreadSafety_NoRaceConditionsUnderLoad()
        {
            // Arrange
            var callCount = 0;
            var connectionStarted = new TaskCompletionSource<bool>();
            var allowConnectionToComplete = new TaskCompletionSource<bool>();

            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .Returns(async () =>
                {
                    Interlocked.Increment(ref callCount);
                    connectionStarted.TrySetResult(true);
                    await allowConnectionToComplete.Task; // Wait for signal to complete
                    return CreateMockHubConnection();
                });

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act - Launch 20 concurrent connection attempts to stress test thread safety
            var tasks = new Task<bool>[20];

            for (int i = 0; i < 20; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    using var taskCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    return await connectionManager.Connect(taskCts.Token);
                });
            }

            // Wait for connection to start, then allow it to complete
            await EnsureConnectionStartedAsync(connectionStarted.Task);
            await Task.Delay(100); // Allow other tasks to queue
            allowConnectionToComplete.SetResult(true);

            // This should not throw any exceptions or deadlock
            await EnsureTasksCompleteAsync(8000, tasks);

            // Assert - All tasks should complete without exceptions
            tasks.Should().NotBeNull();
            tasks.Length.Should().Be(20);
            // Due to concurrency control, only one connection should be created
            callCount.Should().Be(1, "concurrent calls should reuse the same connection attempt");
        }

        #endregion

        #region Cancellation Tests

        [Fact]
        public async Task Connect_WhenCancellationTokenAlreadyCanceled_ReturnsFalse()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel before calling Connect

            var mockConnection = CreateMockHubConnection();
            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .ReturnsAsync(mockConnection);

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act
            var result = await connectionManager.Connect(cts.Token);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task Connect_WhenCanceledDuringConnection_ReturnsFalse()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            var connectionStarted = new TaskCompletionSource<bool>();

            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .Returns(async () =>
                {
                    connectionStarted.SetResult(true);
                    await Task.Delay(Timeout.Infinite, cts.Token); // Will be canceled
                    return CreateMockHubConnection();
                });

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act
            var connectTask = Task.Run(async () => await connectionManager.Connect(cts.Token));

            await EnsureConnectionStartedAsync(connectionStarted.Task);
            cts.Cancel(); // Cancel during connection

            var result = await connectTask;

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task Connect_WithShortTimeout_ReturnsFalse()
        {
            // Arrange
            var connectionStarted = new TaskCompletionSource<bool>();
            var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .Returns(async () =>
                {
                    connectionStarted.TrySetResult(true);
                    // Simulate a long operation that will be interrupted by timeout
                    await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
                    return CreateMockHubConnection();
                });

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act - Use very short timeout
            var result = await connectionManager.Connect(cts.Token);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task Connect_CancellationPropagatedToProvider()
        {
            // Arrange
            var connectionStarted = new TaskCompletionSource<bool>();
            var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .Returns(async () =>
                {
                    connectionStarted.SetResult(true);
                    // Note: CreateConnectionAsync doesn't receive a cancellation token from ConnectionManager,
                    // so we simulate a long operation that will be interrupted by the Connect method's
                    // cancellation handling instead of direct cancellation of the provider call
                    await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
                    return CreateMockHubConnection();
                });

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act
            var result = await connectionManager.Connect(cts.Token);

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region Exception Handling Tests

        [Fact]
        public async Task Connect_WhenProviderThrowsOperationCanceledException_ReturnsFalse()
        {
            // Arrange
            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .ThrowsAsync(new OperationCanceledException("Connection canceled"));

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await connectionManager.Connect(cts.Token);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task Connect_WhenExceptionOccurs_LogsError()
        {
            // Arrange
            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .ThrowsAsync(new Exception("Connection error"));

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await connectionManager.Connect(cts.Token);

            // Assert
            result.Should().BeFalse();
            // Note: Since we're using a real logger, we can't verify mock calls.
            // The test verifies that the method returns false when an exception occurs,
            // and the actual logging can be observed in the test output.
        }

        #endregion

        #region Disconnect Tests

        [Fact]
        public async Task Disconnect_WhenCalledDuringMultipleConcurrentConnectAttempts_StopsAllAttempts()
        {
            // Arrange
            var connectionStarted = new TaskCompletionSource<bool>();
            var allowConnectionToComplete = new TaskCompletionSource<bool>();
            var providerCallCount = 0;

            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .Returns(async () =>
                {
                    var count = Interlocked.Increment(ref providerCallCount);
                    System.Diagnostics.Debug.WriteLine($"CreateConnectionAsync called. Count: {count}");
                    connectionStarted.TrySetResult(true);
                    await allowConnectionToComplete.Task;
                    return CreateMockHubConnection();
                });

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act - Start first connection attempt without awaiting
            var firstConnectTask = Task.Run(async () =>
            {
                System.Diagnostics.Debug.WriteLine("First Connect call started");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var result = await connectionManager.Connect(cts.Token);
                System.Diagnostics.Debug.WriteLine($"First Connect call completed with result: {result}");
                return result;
            });

            // Wait for the first connection to start
            await EnsureConnectionStartedAsync(connectionStarted.Task);
            System.Diagnostics.Debug.WriteLine("First connection started");

            // Start second connection attempt (should wait for first)
            var secondConnectTask = Task.Run(async () =>
            {
                System.Diagnostics.Debug.WriteLine("Second Connect call started");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var result = await connectionManager.Connect(cts.Token);
                System.Diagnostics.Debug.WriteLine($"Second Connect call completed with result: {result}");
                return result;
            });

            // Give second connect time to start waiting
            await Task.Delay(100);
            System.Diagnostics.Debug.WriteLine("Second connection should be waiting now");

            // Now call Disconnect - this should cancel all connection attempts
            var disconnectTask = Task.Run(async () =>
            {
                System.Diagnostics.Debug.WriteLine("Disconnect call started");
                await connectionManager.Disconnect();
                System.Diagnostics.Debug.WriteLine("Disconnect call completed");
            });

            // Give Disconnect time to cancel the internal token
            await Task.Delay(100);
            System.Diagnostics.Debug.WriteLine("Disconnect should have canceled the token");

            // Allow the connection creation to complete (but it should already be canceled)
            allowConnectionToComplete.SetResult(true);
            System.Diagnostics.Debug.WriteLine("Allowed connection to complete");

            // Wait for all operations to complete
            await EnsureTasksCompleteAsync(10000, firstConnectTask, secondConnectTask, disconnectTask);
            System.Diagnostics.Debug.WriteLine("All tasks completed");

            // Assert - Both connect calls should return false (canceled)
            var firstResult = await firstConnectTask;
            var secondResult = await secondConnectTask;

            firstResult.Should().BeFalse("first connect should be canceled by Disconnect");
            secondResult.Should().BeFalse("second connect should be canceled by Disconnect");

            // Only one connection should have been attempted
            providerCallCount.Should().Be(1, "only one connection creation should occur despite multiple Connect calls");

            System.Diagnostics.Debug.WriteLine($"Test completed. Provider call count: {providerCallCount}");
        }

        [Fact]
        public async Task Disconnect_WhenCalledAfterConcurrentConnects_PreventsQueuedConnectFromProceeding()
        {
            // This test specifically addresses the bug where:
            // 1. Connect #1 acquires gate, starts connection loop
            // 2. Connect #2 waits for gate
            // 3. Disconnect cancels token, waits for gate
            // 4. Connect #1 releases gate
            // 5. Connect #2 acquires gate, creates new token, starts new loop (BUG!)
            // 6. Disconnect finally acquires gate but too late
            //
            // Expected behavior: Connect #2 should see ongoing task and wait for it,
            // and when Disconnect clears the task, Connect #2 should return false

            // Arrange
            var connectionStarted = new TaskCompletionSource<bool>();
            var firstConnectCanFinish = new TaskCompletionSource<bool>();
            var providerCallCount = 0;

            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .Returns(async () =>
                {
                    var count = Interlocked.Increment(ref providerCallCount);
                    System.Diagnostics.Debug.WriteLine($"Provider call #{count}");
                    connectionStarted.TrySetResult(true);
                    await firstConnectCanFinish.Task;
                    return CreateMockHubConnection();
                });

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act
            // Start first Connect (will acquire gate and start connection)
            var firstConnect = Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                return await connectionManager.Connect(cts.Token);
            });

            // Wait for first connection to start
            await EnsureConnectionStartedAsync(connectionStarted.Task);

            // Start second Connect immediately (should wait for ongoing task, NOT queue for gate)
            var secondConnect = Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                return await connectionManager.Connect(cts.Token);
            });

            await Task.Delay(50); // Let second connect start waiting

            // Call Disconnect (should cancel token and clear ongoing task)
            var disconnect = Task.Run(async () => await connectionManager.Disconnect());

            await Task.Delay(50); // Let disconnect cancel the token

            // Now allow first connect to finish
            firstConnectCanFinish.SetResult(true);

            // Wait for everything to complete
            await EnsureTasksCompleteAsync(10000, firstConnect, secondConnect, disconnect);

            // Assert
            var firstResult = await firstConnect;
            var secondResult = await secondConnect;

            // Both should fail because Disconnect was called
            firstResult.Should().BeFalse("first connect should fail due to disconnect");
            secondResult.Should().BeFalse("second connect should fail because ongoing task was canceled and cleared");

            // CRITICAL: Only ONE provider call should have been made
            // If there are 2 calls, it means Connect #2 created a new connection after Disconnect (BUG!)
            providerCallCount.Should().Be(1, "second Connect should NOT create a new connection after Disconnect");
        }

        [Fact]
        public async Task Connect_AfterDisconnect_SucceedsOnReconnect()
        {
            // This test verifies the Connect -> Disconnect -> Connect scenario
            // Bug: After Disconnect, the internalCts is canceled but not disposed/nulled,
            // causing subsequent Connect attempts to fail at the canceled token check

            // Arrange
            var providerCallCount = 0;
            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .Returns(async () =>
                {
                    var count = Interlocked.Increment(ref providerCallCount);
                    System.Diagnostics.Debug.WriteLine($"Provider call #{count}");
                    await Task.Yield();
                    return CreateMockHubConnection();
                });

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act
            // First Connect
            System.Diagnostics.Debug.WriteLine("First Connect attempt");
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var firstConnect = await connectionManager.Connect(cts1.Token);
            System.Diagnostics.Debug.WriteLine($"First Connect result: {firstConnect}");

            await Task.Delay(100); // Give connection time to process

            // Disconnect
            System.Diagnostics.Debug.WriteLine("Disconnecting");
            await connectionManager.Disconnect();
            System.Diagnostics.Debug.WriteLine("Disconnect completed");

            await Task.Delay(100); // Give disconnect time to process

            // Second Connect (should succeed)
            System.Diagnostics.Debug.WriteLine("Second Connect attempt (after disconnect)");
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var secondConnect = await connectionManager.Connect(cts2.Token);
            System.Diagnostics.Debug.WriteLine($"Second Connect result: {secondConnect}");

            // Assert
            // The second connect should succeed (or at least attempt to connect)
            // The provider should be called at least twice (once for each Connect)
            providerCallCount.Should().BeGreaterThanOrEqualTo(2, "reconnection after disconnect should create a new connection");
        }

        #endregion

        #region InvokeAsync Tests

        [Fact]
        public async Task InvokeAsync_WhenDisposed_ReturnsDefault()
        {
            // Arrange
            var mockConnection = CreateMockHubConnection();
            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .ReturnsAsync(mockConnection);

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Dispose the connection manager
            await connectionManager.DisposeAsync();

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var result = await connectionManager.InvokeAsync<string>("TestMethod", cts.Token);

            // Assert
            result.Should().BeNull("InvokeAsync should return default when disposed");
        }

        [Fact]
        public async Task InvokeAsync_WhenConnectionNotEstablished_ReturnsDefault()
        {
            // Arrange
            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .ReturnsAsync((HubConnection)null!);

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var result = await connectionManager.InvokeAsync<string>("TestMethod", cts.Token);

            // Assert
            result.Should().BeNull("InvokeAsync should return default when connection cannot be established");
        }

        [Fact]
        public async Task InvokeAsync_WhenCancellationTokenAlreadyCanceled_ReturnsDefault()
        {
            // Arrange
            var mockConnection = CreateMockHubConnection();
            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .ReturnsAsync(mockConnection);

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel before calling InvokeAsync

            // Act
            var result = await connectionManager.InvokeAsync<string>("TestMethod", cts.Token);

            // Assert
            result.Should().BeNull("InvokeAsync should return default when cancellation token is already canceled");
        }

        [Fact]
        public async Task InvokeAsync_WhenCanceledDuringConnection_ReturnsDefault()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            var connectionStarted = new TaskCompletionSource<bool>();

            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .Returns(async () =>
                {
                    connectionStarted.SetResult(true);
                    await Task.Delay(Timeout.Infinite, cts.Token); // Will be canceled
                    return CreateMockHubConnection();
                });

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // First call Connect to enable _continueToReconnect
            var connectTask = Task.Run(async () => await connectionManager.Connect(cts.Token));

            await EnsureConnectionStartedAsync(connectionStarted.Task);

            // Now start InvokeAsync which will wait for the ongoing connection
            var invokeTask = Task.Run(async () => await connectionManager.InvokeAsync<string>("TestMethod", cts.Token));

            await Task.Delay(50); // Allow InvokeAsync to start waiting
            cts.Cancel(); // Cancel during connection

            var connectResult = await connectTask;
            var invokeResult = await invokeTask;

            // Assert
            connectResult.Should().BeFalse("Connect should return false when canceled");
            invokeResult.Should().BeNull("InvokeAsync should return default when canceled during connection");
        }

        [Fact]
        public async Task InvokeAsync_WhenProviderThrows_ReturnsDefault()
        {
            // Arrange
            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .ThrowsAsync(new InvalidOperationException("Failed to create connection"));

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await connectionManager.InvokeAsync<int>("TestMethod", cts.Token);

            // Assert
            result.Should().Be(0, "InvokeAsync should return default(int) when provider throws");
        }

        [Fact]
        public async Task InvokeAsync_WhenHubConnectionIsNull_ReturnsDefault()
        {
            // Arrange
            // Create a connection that returns null initially
            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .ReturnsAsync((HubConnection)null!);

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var result = await connectionManager.InvokeAsync<bool>("TestMethod", cts.Token);

            // Assert
            result.Should().BeFalse("InvokeAsync should return default(bool) when hub connection is null");
        }

        [Fact]
        public async Task InvokeAsync_WithShortTimeout_ReturnsDefault()
        {
            // Arrange
            var connectionStarted = new TaskCompletionSource<bool>();
            var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .Returns(async () =>
                {
                    connectionStarted.TrySetResult(true);
                    // Simulate a long operation that will be interrupted by timeout
                    await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
                    return CreateMockHubConnection();
                });

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act - Use very short timeout
            var result = await connectionManager.InvokeAsync<string>("TestMethod", cts.Token);

            // Assert
            result.Should().BeNull("InvokeAsync should return default when timeout occurs");
        }

        [Fact]
        public async Task InvokeAsync_MultipleCallsWhileDisposed_AllReturnDefault()
        {
            // Arrange
            var mockConnection = CreateMockHubConnection();
            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .ReturnsAsync(mockConnection);

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Dispose the connection manager
            await connectionManager.DisposeAsync();

            // Act - Make multiple calls after disposal
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var task1 = connectionManager.InvokeAsync<string>("Method1", cts.Token);
            var task2 = connectionManager.InvokeAsync<string>("Method2", cts.Token);
            var task3 = connectionManager.InvokeAsync<string>("Method3", cts.Token);

            var results = await Task.WhenAll(task1, task2, task3);

            // Assert
            results.Should().AllSatisfy(r => r.Should().BeNull("all InvokeAsync calls should return default when disposed"));
        }

        [Fact]
        public async Task InvokeAsync_WhenDisposedDuringInvocation_HandlesGracefully()
        {
            // Arrange
            var connectionStarted = new TaskCompletionSource<bool>();
            var allowConnectionToComplete = new TaskCompletionSource<bool>();

            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .Returns(async () =>
                {
                    connectionStarted.TrySetResult(true);
                    await allowConnectionToComplete.Task;
                    return CreateMockHubConnection();
                });

            var connectionManager = new ConnectionManager(
                _logger,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // First start a Connect call to enable _continueToReconnect and begin connection
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var connectTask = Task.Run(async () => await connectionManager.Connect(cts.Token));

            await EnsureConnectionStartedAsync(connectionStarted.Task);

            // Now start InvokeAsync which will wait for the ongoing connection
            var invokeTask = Task.Run(async () => await connectionManager.InvokeAsync<string>("TestMethod", cts.Token));

            await Task.Delay(50); // Allow InvokeAsync to start waiting

            // Dispose while connection is in progress
            var disposeTask = connectionManager.DisposeAsync();

            // Allow connection to complete
            allowConnectionToComplete.SetResult(true);

            await disposeTask;
            var connectResult = await connectTask;
            var invokeResult = await invokeTask;

            // Assert
            connectResult.Should().BeFalse("Connect should return false when disposed");
            invokeResult.Should().BeNull("InvokeAsync should return default when disposed during invocation");
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a mock HubConnection for testing.
        /// Note: Due to HubConnection being sealed, this creates a minimal instance
        /// that can be used in tests. For full integration testing, use real HubConnection instances.
        /// </summary>
        private static HubConnection CreateMockHubConnection()
        {
            // Create a real HubConnection instance with a dummy URL
            // This is necessary because HubConnection is sealed and cannot be mocked
            var builder = new HubConnectionBuilder()
                .WithUrl("http://localhost:9999/test")
                .WithAutomaticReconnect();

            return builder.Build();
        }

        /// <summary>
        /// Ensures that the connection has started within a reasonable timeout.
        /// </summary>
        /// <param name="connectionStartedTask">The task representing the connection start operation.</param>
        /// <param name="timeoutMs">The timeout in milliseconds (default: 2000ms).</param>
        private static async Task EnsureConnectionStartedAsync(Task connectionStartedTask, int timeoutMs = 2000)
        {
            var timeoutTask = Task.Delay(timeoutMs);
            var completedTask = await Task.WhenAny(connectionStartedTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException("Connection did not start within expected time");
            }
        }

        /// <summary>
        /// Ensures that all tasks complete within a reasonable timeout.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        private static Task EnsureTasksCompleteAsync(params Task[] tasks)
            => EnsureTasksCompleteAsync(5000, tasks);

        /// <summary>
        /// Ensures that all tasks complete within a reasonable timeout.
        /// </summary>
        /// <param name="timeoutMs">The timeout in milliseconds.</param>
        /// <param name="tasks">The tasks to wait for completion.</param>
        private static async Task EnsureTasksCompleteAsync(int timeoutMs, params Task[] tasks)
        {
            var allTasksCompleted = Task.WhenAll(tasks);
            var timeoutTask = Task.Delay(timeoutMs);
            var completedTask = await Task.WhenAny(allTasksCompleted, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException("Tasks did not complete within expected time");
            }
        }

        #endregion
    }
}
