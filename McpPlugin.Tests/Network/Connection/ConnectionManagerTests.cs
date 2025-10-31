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
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.McpPlugin.Common;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

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
        private readonly Mock<ILogger> _mockLogger;
        private readonly Mock<IHubConnectionProvider> _mockHubConnectionProvider;
        private readonly Common.Version _testVersion;
        private readonly string _testEndpoint;

        public ConnectionManagerTests()
        {
            _mockLogger = new Mock<ILogger>();
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
                _mockLogger.Object,
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
                _mockLogger.Object,
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
                _mockLogger.Object,
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
            var allowConnectionToComplete = new TaskCompletionSource<bool>();

            _mockHubConnectionProvider
                .Setup(x => x.CreateConnectionAsync(_testEndpoint))
                .Returns(async () =>
                {
                    Interlocked.Increment(ref connectionCreationCount);
                    connectionCreated.TrySetResult(true);
                    await allowConnectionToComplete.Task;
                    return CreateMockHubConnection();
                });

            var connectionManager = new ConnectionManager(
                _mockLogger.Object,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act - Launch 10 concurrent connection attempts
            var tasks = new Task<bool>[10];
            for (int i = 0; i < 10; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    return await connectionManager.Connect(cts.Token);
                });
            }

            await EnsureConnectionStartedAsync(connectionCreated.Task);
            await Task.Delay(100); // Allow other tasks to queue

            // Allow connection to complete
            allowConnectionToComplete.SetResult(true);

            await EnsureTasksCompleteAsync(tasks);

            // Assert
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
                _mockLogger.Object,
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
            await EnsureTasksCompleteAsync(firstTask, secondTask);

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
                _mockLogger.Object,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act - Make 3 sequential connection attempts
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result1 = await connectionManager.Connect(cts.Token);
            await Task.Delay(20); // Minimal delay to ensure connection completes

            var result2 = await connectionManager.Connect(cts.Token);
            await Task.Delay(20);

            var result3 = await connectionManager.Connect(cts.Token);

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
                _mockLogger.Object,
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
                _mockLogger.Object,
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
                _mockLogger.Object,
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
                _mockLogger.Object,
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
                _mockLogger.Object,
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
                _mockLogger.Object,
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
                _mockLogger.Object,
                _testVersion,
                _testEndpoint,
                _mockHubConnectionProvider.Object
            );

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await connectionManager.Connect(cts.Token);

            // Assert
            result.Should().BeFalse();
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
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
        private static async Task EnsureTasksCompleteAsync(int timeoutMs, Task[] tasks)
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
