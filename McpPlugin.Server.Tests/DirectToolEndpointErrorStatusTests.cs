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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Api;
using com.IvanMurzak.McpPlugin.Server.Webhooks.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    [Collection("McpPlugin.Server")]
    public sealed class DirectToolEndpointErrorStatusTests
    {
        [Theory]
        [InlineData(ResponseErrorKind.BadRequest, HttpStatusCode.BadRequest)]
        [InlineData(ResponseErrorKind.NotFound, HttpStatusCode.NotFound)]
        [InlineData(ResponseErrorKind.Conflict, HttpStatusCode.Conflict)]
        [InlineData(ResponseErrorKind.Unavailable, HttpStatusCode.ServiceUnavailable)]
        [InlineData(ResponseErrorKind.Timeout, HttpStatusCode.GatewayTimeout)]
        [InlineData(ResponseErrorKind.Internal, HttpStatusCode.InternalServerError)]
        public async Task ToolCall_MapsErrorKindToHttpStatus(ResponseErrorKind errorKind, HttpStatusCode expectedStatus)
        {
            var toolResponse = ResponseData<ResponseCallTool>.Error("request-1", "mapped error", errorKind)
                .SetData(ResponseCallTool.Error("mapped error", errorKind));
            await using var host = await StartHostAsync(toolResponse: toolResponse);
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };

            var response = await PostJsonAsync(client, "/api/tools/test");

            response.StatusCode.ShouldBe(expectedStatus);
        }

        [Fact]
        public async Task ToolCall_ExplicitHttpStatusOverridesErrorKind()
        {
            var toolResponse = ResponseData<ResponseCallTool>.Error("request-1", "legal restriction", ResponseErrorKind.BadRequest, 451)
                .SetData(ResponseCallTool.Error("legal restriction", ResponseErrorKind.BadRequest, 451));
            await using var host = await StartHostAsync(toolResponse: toolResponse);
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };

            var response = await PostJsonAsync(client, "/api/tools/test");

            response.StatusCode.ShouldBe((HttpStatusCode)451);
        }

        [Fact]
        public async Task ToolCall_NullHubResponse_ReturnsBadGateway()
        {
            await using var host = await StartHostAsync(toolHubReturnsNull: true);
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };

            var response = await PostJsonAsync(client, "/api/tools/test");

            response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        }

        [Theory]
        [InlineData(ResponseErrorKind.BadRequest, HttpStatusCode.BadRequest)]
        [InlineData(ResponseErrorKind.NotFound, HttpStatusCode.NotFound)]
        [InlineData(ResponseErrorKind.Conflict, HttpStatusCode.Conflict)]
        [InlineData(ResponseErrorKind.Unavailable, HttpStatusCode.ServiceUnavailable)]
        [InlineData(ResponseErrorKind.Timeout, HttpStatusCode.GatewayTimeout)]
        [InlineData(ResponseErrorKind.Internal, HttpStatusCode.InternalServerError)]
        public async Task SystemToolCall_MapsErrorKindToHttpStatus(ResponseErrorKind errorKind, HttpStatusCode expectedStatus)
        {
            var toolResponse = ResponseData<ResponseCallTool>.Error("request-1", "mapped error", errorKind)
                .SetData(ResponseCallTool.Error("mapped error", errorKind));
            await using var host = await StartHostAsync(systemToolResponse: toolResponse);
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };

            var response = await PostJsonAsync(client, "/api/system-tools/test");

            response.StatusCode.ShouldBe(expectedStatus);
        }

        [Fact]
        public async Task SystemToolCall_NullHubResponse_ReturnsBadGateway()
        {
            await using var host = await StartHostAsync(systemToolHubReturnsNull: true);
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };

            var response = await PostJsonAsync(client, "/api/system-tools/test");

            response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        }

        static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string route)
            => client.PostAsync(route, new StringContent("{}", Encoding.UTF8, "application/json"));

        static async Task<RunningHost> StartHostAsync(
            ResponseData<ResponseCallTool>? toolResponse = null,
            ResponseData<ResponseCallTool>? systemToolResponse = null,
            bool toolHubReturnsNull = false,
            bool systemToolHubReturnsNull = false)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();

            var dataArguments = Mock.Of<IDataArguments>(d => d.Authorization == Consts.MCP.Server.AuthOption.none);
            builder.Services.AddSingleton(dataArguments);
            builder.Services.AddSingleton<IClientToolHub>(new StubToolHub(toolResponse, toolHubReturnsNull));
            builder.Services.AddSingleton<IClientSystemToolHub>(new StubSystemToolHub(systemToolResponse, systemToolHubReturnsNull));
            builder.Services.AddSingleton(Mock.Of<IAuthorizationWebhookService>());

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            app.MapDirectToolCallApi(dataArguments);
            app.MapSystemToolApi(dataArguments);

            await app.StartAsync();
            return new RunningHost(app, app.Urls.First());
        }

        sealed class RunningHost : IAsyncDisposable
        {
            readonly WebApplication _app;
            public string BaseUrl { get; }

            public RunningHost(WebApplication app, string baseUrl)
            {
                _app = app;
                BaseUrl = baseUrl;
            }

            public async ValueTask DisposeAsync()
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }

        sealed class StubToolHub : IClientToolHub
        {
            readonly ResponseData<ResponseCallTool>? _toolResponse;

            public StubToolHub(ResponseData<ResponseCallTool>? toolResponse, bool returnsNull)
            {
                _toolResponse = returnsNull
                    ? null
                    : toolResponse ?? ResponseData<ResponseCallTool>.Success("request-1")
                        .SetData(ResponseCallTool.Success("ok"));
            }

            public Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request)
                => Task.FromResult(_toolResponse!);

            public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request, CancellationToken cancellationToken = default)
                => Task.FromResult(ResponseData<ResponseListTool[]>.Success(request.RequestID)
                    .SetData(Array.Empty<ResponseListTool>()));
        }

        sealed class StubSystemToolHub : IClientSystemToolHub
        {
            readonly ResponseData<ResponseCallTool>? _toolResponse;

            public StubSystemToolHub(ResponseData<ResponseCallTool>? toolResponse, bool returnsNull)
            {
                _toolResponse = returnsNull
                    ? null
                    : toolResponse ?? ResponseData<ResponseCallTool>.Success("request-1")
                        .SetData(ResponseCallTool.Success("ok"));
            }

            public Task<ResponseData<ResponseCallTool>> RunSystemTool(RequestCallTool request, CancellationToken cancellationToken = default)
                => Task.FromResult(_toolResponse!);

            public Task<ResponseData<ResponseListTool[]>> RunListSystemTool(RequestListTool request, CancellationToken cancellationToken = default)
                => Task.FromResult(ResponseData<ResponseListTool[]>.Success(request.RequestID)
                    .SetData(Array.Empty<ResponseListTool>()));
        }
    }
}
