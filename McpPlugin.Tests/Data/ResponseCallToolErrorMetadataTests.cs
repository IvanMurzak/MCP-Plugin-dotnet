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
using System.Text.Json.Nodes;
using com.IvanMurzak.McpPlugin.Common.Model;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Data
{
    public class ResponseCallToolErrorMetadataTests
    {
        [Fact]
        public void Error_WithMessage_DefaultsToInternal()
        {
            var response = ResponseCallTool.Error("Invalid input.");

            response.Status.ShouldBe(ResponseStatus.Error);
            response.ErrorKind.ShouldBe(ResponseErrorKind.Internal);
            response.HttpStatusCode.ShouldBeNull();
        }

        [Fact]
        public void Error_WithExplicitBadRequest_PreservesBadRequest()
        {
            var response = ResponseCallTool.Error("Invalid input.", ResponseErrorKind.BadRequest);

            response.Status.ShouldBe(ResponseStatus.Error);
            response.ErrorKind.ShouldBe(ResponseErrorKind.BadRequest);
            response.HttpStatusCode.ShouldBeNull();
        }

        [Fact]
        public void Error_WithException_DefaultsToInternal()
        {
            var response = ResponseCallTool.Error(new InvalidOperationException("boom"));

            response.Status.ShouldBe(ResponseStatus.Error);
            response.ErrorKind.ShouldBe(ResponseErrorKind.Internal);
            response.HttpStatusCode.ShouldBeNull();
        }

        [Fact]
        public void Error_WithExplicitKindAndStatus_PreservesMetadata()
        {
            var response = ResponseCallTool.Error(
                "Request timed out.",
                ResponseErrorKind.Timeout,
                504);

            response.ErrorKind.ShouldBe(ResponseErrorKind.Timeout);
            response.HttpStatusCode.ShouldBe(504);
        }

        [Fact]
        public void ErrorStructured_WithExplicitKindAndStatus_PreservesMetadata()
        {
            var structured = new JsonObject { ["message"] = "missing" };

            var response = ResponseCallTool.ErrorStructured(
                structured,
                ResponseErrorKind.NotFound,
                404);

            response.ErrorKind.ShouldBe(ResponseErrorKind.NotFound);
            response.HttpStatusCode.ShouldBe(404);
        }

        [Fact]
        public void Pack_CopiesErrorMetadataToResponseData()
        {
            var toolResponse = ResponseCallTool.Error(
                "Current state does not allow this operation.",
                ResponseErrorKind.Conflict,
                409);

            var packed = toolResponse.Pack("request-1");

            packed.Status.ShouldBe(ResponseStatus.Error);
            packed.ErrorKind.ShouldBe(ResponseErrorKind.Conflict);
            packed.HttpStatusCode.ShouldBe(409);
            packed.Value.ShouldBeSameAs(toolResponse);
        }
    }
}
