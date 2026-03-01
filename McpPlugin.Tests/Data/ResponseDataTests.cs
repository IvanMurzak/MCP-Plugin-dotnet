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
using com.IvanMurzak.McpPlugin.Common.Model;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Data
{
    public class ResponseDataTests
    {
        [Fact]
        public void ResponseData_DefaultConstructor_InitializesWithEmptyValues()
        {
            // Act
            var response = new ResponseData();

            // Assert
            response.RequestID.ShouldBeEmpty();
            response.Status.ShouldBe(ResponseStatus.Error);
            response.Message.ShouldBeNull();
        }

        [Fact]
        public void ResponseData_ParameterizedConstructor_SetsValues()
        {
            // Arrange
            var requestId = "test-request-123";
            var status = ResponseStatus.Success;

            // Act
            var response = new ResponseData(requestId, status);

            // Assert
            response.RequestID.ShouldBe(requestId);
            response.Status.ShouldBe(status);
        }

        [Fact]
        public void ResponseData_Constructor_ThrowsOnNullRequestId()
        {
            // Act
            Action act = () => new ResponseData(null!, ResponseStatus.Success);

            // Assert
            Should.Throw<ArgumentNullException>(act)
                .ParamName.ShouldBe("requestId");
        }

        [Fact]
        public void ResponseData_Success_CreatesSuccessResponse()
        {
            // Arrange
            var requestId = "success-request";
            var message = "Operation completed successfully";

            // Act
            var response = ResponseData.Success(requestId, message);

            // Assert
            response.RequestID.ShouldBe(requestId);
            response.Status.ShouldBe(ResponseStatus.Success);
            response.Message.ShouldBe(message);
        }

        [Fact]
        public void ResponseData_Error_CreatesErrorResponse()
        {
            // Arrange
            var requestId = "error-request";
            var message = "Operation failed";

            // Act
            var response = ResponseData.Error(requestId, message);

            // Assert
            response.RequestID.ShouldBe(requestId);
            response.Status.ShouldBe(ResponseStatus.Error);
            response.Message.ShouldBe(message);
        }

        [Fact]
        public void ResponseData_Processing_CreatesProcessingResponse()
        {
            // Arrange
            var requestId = "processing-request";
            var message = "Operation in progress";

            // Act
            var response = ResponseData.Processing(requestId, message);

            // Assert
            response.RequestID.ShouldBe(requestId);
            response.Status.ShouldBe(ResponseStatus.Processing);
            response.Message.ShouldBe(message);
        }

        [Fact]
        public void ResponseData_SetRequestID_UpdatesRequestId()
        {
            // Arrange
            var response = new ResponseData();
            var newRequestId = "new-request-id";

            // Act
            var result = response.SetRequestID(newRequestId);

            // Assert
            result.RequestID.ShouldBe(newRequestId);
            result.ShouldBeSameAs(response); // Fluent API should return same instance
        }
    }

    public class ResponseDataGenericTests
    {
        [Fact]
        public void ResponseDataGeneric_DefaultConstructor_InitializesWithEmptyValues()
        {
            // Act
            var response = new ResponseData<string>();

            // Assert
            response.RequestID.ShouldBeEmpty();
            response.Status.ShouldBe(ResponseStatus.Error);
            response.Message.ShouldBeNull();
            response.Value.ShouldBeNull();
        }

        [Fact]
        public void ResponseDataGeneric_CanStoreValue()
        {
            // Arrange
            var response = new ResponseData<int>();
            var value = 42;

            // Act
            response.Value = value;

            // Assert
            response.Value.ShouldBe(value);
        }

        [Fact]
        public void ResponseDataGeneric_Success_CreatesSuccessResponseWithValue()
        {
            // Arrange
            var requestId = "test-request";
            var message = "Success";
            var value = "test-value";

            // Act
            var response = ResponseData<string>.Success(requestId, message);
            response.Value = value;

            // Assert
            response.RequestID.ShouldBe(requestId);
            response.Status.ShouldBe(ResponseStatus.Success);
            response.Message.ShouldBe(message);
            response.Value.ShouldBe(value);
        }

        [Fact]
        public void ResponseDataGeneric_Error_CreatesErrorResponse()
        {
            // Arrange
            var requestId = "error-request";
            var message = "Error occurred";

            // Act
            var response = ResponseData<string>.Error(requestId, message);

            // Assert
            response.RequestID.ShouldBe(requestId);
            response.Status.ShouldBe(ResponseStatus.Error);
            response.Message.ShouldBe(message);
            response.Value.ShouldBeNull();
        }

        [Fact]
        public void ResponseDataGeneric_SetRequestID_ReturnsTypedInstance()
        {
            // Arrange
            var response = new ResponseData<string>();
            var newRequestId = "new-request-id";

            // Act
            var result = response.SetRequestID(newRequestId);

            // Assert
            result.ShouldBeOfType<ResponseData<string>>();
            result.RequestID.ShouldBe(newRequestId);
        }

        [Fact]
        public void ResponseDataGeneric_WithComplexType_StoresComplexValue()
        {
            // Arrange
            var complexValue = new { Name = "Test", Value = 123 };
            var response = ResponseData<object>.Success("request-id");

            // Act
            response.Value = complexValue;

            // Assert
            response.Value.ShouldBeSameAs(complexValue);
        }
    }
}
