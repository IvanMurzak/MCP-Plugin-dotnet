/*
┌──────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)             │
│  Repository: GitHub (https://github.com/IvanMurzak/Unity-MCP)    │
│  Copyright (c) 2025 Ivan Murzak                                  │
│  Licensed under the Apache License, Version 2.0.                 │
│  See the LICENSE file in the project root for more information.  │
└──────────────────────────────────────────────────────────────────┘
*/

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Json;
using com.IvanMurzak.McpPlugin.Common.Model;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin.Server
{
    public interface IRequestTrackingService
    {
        Task<ResponseCallTool> TrackRequestAsync(
            string requestId,
            Func<Task<ResponseCallTool>> executeRequest,
            TimeSpan timeout,
            CancellationToken cancellationToken = default);
        void CompleteRequest(ResponseCallTool response);
    }

    public class RequestTrackingService : IRequestTrackingService, IDisposable
    {
        readonly ILogger<RequestTrackingService> _logger;
        readonly ConcurrentDictionary<string, PendingRequest> _pendingRequests = new();
        readonly CompositeDisposable _disposables = new();

        public RequestTrackingService(ILogger<RequestTrackingService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("RequestTrackingService initialized");
        }

        public async Task<ResponseCallTool> TrackRequestAsync(
            string requestId,
            Func<Task<ResponseCallTool>> executeRequest,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(requestId))
                throw new ArgumentException("RequestId cannot be null or empty", nameof(requestId));

            _logger.LogTrace("Tracking request: {RequestId} with timeout: {timeout}", requestId, timeout);

            var pendingRequest = new PendingRequest(requestId, timeout);
            _pendingRequests[requestId] = pendingRequest;

            try
            {
                var initialResponse = await executeRequest();
                if (initialResponse.Status != ResponseStatus.Processing)
                {
                    _logger.LogTrace("Request {RequestId} completed immediately", requestId);
                    return initialResponse;
                }

                _logger.LogTrace("Request {RequestId} processing: {Message}", requestId, initialResponse);

                var finalResult = await pendingRequest.WaitForCompletion(cancellationToken);
                return finalResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing request {RequestId}", requestId);
                throw;
            }
            finally
            {
                _pendingRequests.TryRemove(requestId, out _);
            }
        }

        public void CompleteRequest(ResponseCallTool response)
        {
            if (string.IsNullOrEmpty(response?.RequestID))
            {
                _logger.LogError("Attempted to complete request with null or empty RequestID\n{Json}",
                    System.Text.Json.JsonSerializer.Serialize(response, JsonOptions.Pretty));
                return;
            }

            if (_pendingRequests.TryRemove(response.RequestID, out var pendingRequest))
            {
                _logger.LogTrace("Completing tracked request: {RequestID}", response.RequestID);
                if (pendingRequest is PendingRequest typedPendingRequest)
                {
                    typedPendingRequest.Complete(response);
                }
                else
                {
                    _logger.LogWarning("Type mismatch when completing request {RequestID}. Expected {ExpectedType}, got {ActualType}",
                        response.RequestID, nameof(ResponseData), pendingRequest.GetType().Name);
                }
            }
            else
            {
                _logger.LogWarning("Attempted to complete request {RequestID} that is not being tracked", response.RequestID);
            }
        }

        public void Dispose()
        {
            _logger.LogTrace("RequestTrackingService disposing");

            foreach (var kvp in _pendingRequests.ToArray())
            {
                try
                {
                    kvp.Value.Cancel();
                    kvp.Value.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error canceling pending request {RequestId} during disposal", kvp.Key);
                }
            }

            _pendingRequests.Clear();
            _disposables.Dispose();
        }

        class PendingRequest : IDisposable
        {
            public string RequestId { get; }
            public bool IsCompleted { get; protected set; }
            protected readonly CancellationTokenSource TimeoutCts;

            readonly TaskCompletionSource<ResponseCallTool> _completionSource = new();

            public PendingRequest(string requestId, TimeSpan timeout)
            {
                RequestId = requestId;
                TimeoutCts = new CancellationTokenSource(timeout);
                IsCompleted = false;

                TimeoutCts.Token.Register(() =>
                {
                    if (!IsCompleted)
                    {
                        IsCompleted = true;
                        _completionSource.TrySetResult(
                            ResponseCallTool.Error($"Request {RequestId} timed out after {timeout}").SetRequestID(RequestId));
                    }
                });
            }

            public void Complete(ResponseCallTool response)
            {
                if (!IsCompleted)
                {
                    IsCompleted = true;
                    _completionSource.TrySetResult(response.SetRequestID(RequestId));
                    TimeoutCts.Cancel();
                }
            }

            public async Task<ResponseCallTool> WaitForCompletion(CancellationToken cancellationToken = default)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(TimeoutCts.Token, cancellationToken);

                try
                {
                    var response = await _completionSource.Task.WaitAsync(linkedCts.Token);
                    return response;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return ResponseCallTool.Error("Request was canceled").SetRequestID(RequestId);
                }
                catch (OperationCanceledException) when (TimeoutCts.Token.IsCancellationRequested)
                {
                    return ResponseCallTool.Error("Request timed out").SetRequestID(RequestId);
                }
            }

            public void Cancel()
            {
                if (!IsCompleted)
                {
                    IsCompleted = true;
                    _completionSource.TrySetCanceled();
                    TimeoutCts.Cancel();
                }
            }

            public void Dispose()
            {
                TimeoutCts?.Dispose();
            }
        }
    }
}