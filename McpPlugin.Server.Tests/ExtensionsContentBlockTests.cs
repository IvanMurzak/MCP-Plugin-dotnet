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
using System.Text.Json;
using System.Text.Json.Nodes;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;
using CommonContentBlock = com.IvanMurzak.McpPlugin.Common.Model.ContentBlock;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    public class ExtensionsContentBlockTests
    {
        private static readonly byte[] _testImageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        private static readonly byte[] _testAudioBytes = new byte[] { 0x52, 0x49, 0x46, 0x46 }; // RIFF header

        [Fact]
        public void ToContent_TextBlock_ReturnsTextContentBlock()
        {
            // Arrange
            var block = CommonContentBlock.CreateText("hello world");

            // Act
            var result = block.ToContent();

            // Assert
            result.ShouldBeOfType<TextContentBlock>();
            ((TextContentBlock)result).Text.ShouldBe("hello world");
        }

        [Fact]
        public void ToContent_TextBlock_EmptyText_ReturnsEmptyTextContentBlock()
        {
            // Arrange
            var block = new Common.Model.ContentBlock { Type = "text", Text = null };

            // Act
            var result = block.ToContent();

            // Assert
            result.ShouldBeOfType<TextContentBlock>();
            ((TextContentBlock)result).Text.ShouldBe(string.Empty);
        }

        [Fact]
        public void ToContent_ImageBlock_ReturnsImageContentBlock()
        {
            // Arrange
            var block = CommonContentBlock.CreateImage(_testImageBytes, Consts.MimeType.ImagePng);

            // Act
            var result = block.ToContent();

            // Assert
            result.ShouldBeOfType<ImageContentBlock>();
            var imageBlock = (ImageContentBlock)result;
            imageBlock.MimeType.ShouldBe(Consts.MimeType.ImagePng);
        }

        [Fact]
        public void ToContent_ImageBlock_DataRoundTrips()
        {
            // Arrange
            var block = CommonContentBlock.CreateImage(_testImageBytes, Consts.MimeType.ImagePng);

            // Act
            var result = (ImageContentBlock)block.ToContent();

            // Assert
            result.DecodedData.ToArray().ShouldBe(_testImageBytes);
        }

        [Fact]
        public void ToContent_ImageBlock_NullData_Throws()
        {
            // Arrange
            var block = new Common.Model.ContentBlock { Type = "image", Data = null, MimeType = Consts.MimeType.ImagePng };

            // Act
            var act = () => block.ToContent();

            // Assert
            Should.Throw<InvalidOperationException>(act)
                .Message.ShouldContain("Image content block is missing Data");
        }

        [Fact]
        public void ToContent_AudioBlock_ReturnsAudioContentBlock()
        {
            // Arrange
            var block = CommonContentBlock.CreateAudio(_testAudioBytes, Consts.MimeType.AudioWav);

            // Act
            var result = block.ToContent();

            // Assert
            result.ShouldBeOfType<AudioContentBlock>();
            var audioBlock = (AudioContentBlock)result;
            audioBlock.MimeType.ShouldBe(Consts.MimeType.AudioWav);
        }

        [Fact]
        public void ToContent_AudioBlock_DataRoundTrips()
        {
            // Arrange
            var block = CommonContentBlock.CreateAudio(_testAudioBytes, Consts.MimeType.AudioWav);

            // Act
            var result = (AudioContentBlock)block.ToContent();

            // Assert
            result.DecodedData.ToArray().ShouldBe(_testAudioBytes);
        }

        [Fact]
        public void ToContent_AudioBlock_NullData_Throws()
        {
            // Arrange
            var block = new Common.Model.ContentBlock { Type = "audio", Data = null, MimeType = Consts.MimeType.AudioWav };

            // Act
            var act = () => block.ToContent();

            // Assert
            Should.Throw<InvalidOperationException>(act)
                .Message.ShouldContain("Audio content block is missing Data");
        }

        [Fact]
        public void ToContent_ResourceBlock_TextResource_ReturnsEmbeddedResourceBlock()
        {
            // Arrange
            var resource = ResponseResourceContent.CreateText("file:///test.txt", "hello", Consts.MimeType.TextPlain);
            var block = CommonContentBlock.CreateResource(resource);

            // Act
            var result = block.ToContent();

            // Assert
            result.ShouldBeOfType<EmbeddedResourceBlock>();
            var resourceBlock = (EmbeddedResourceBlock)result;
            resourceBlock.Resource.ShouldBeOfType<TextResourceContents>();
            ((TextResourceContents)resourceBlock.Resource).Text.ShouldBe("hello");
        }

        [Fact]
        public void ToContent_ResourceBlock_BlobResource_ReturnsEmbeddedResourceBlock()
        {
            // Arrange
            var base64Blob = Convert.ToBase64String(_testImageBytes);
            var resource = ResponseResourceContent.CreateBlob("file:///test.png", base64Blob, Consts.MimeType.ImagePng);
            var block = CommonContentBlock.CreateResource(resource);

            // Act
            var result = block.ToContent();

            // Assert
            result.ShouldBeOfType<EmbeddedResourceBlock>();
            var resourceBlock = (EmbeddedResourceBlock)result;
            resourceBlock.Resource.ShouldBeOfType<BlobResourceContents>();
        }

        [Fact]
        public void ToContent_ResourceBlock_NullResource_Throws()
        {
            // Arrange
            var block = new Common.Model.ContentBlock { Type = "resource", Resource = null };

            // Act
            var act = () => block.ToContent();

            // Assert
            Should.Throw<InvalidOperationException>(act)
                .Message.ShouldContain("Resource content block is missing Resource");
        }

        [Fact]
        public void ToContent_UnknownType_FallsBackToTextContentBlock()
        {
            // Arrange
            var block = new Common.Model.ContentBlock { Type = "unknown", Text = "fallback" };

            // Act
            var result = block.ToContent();

            // Assert
            result.ShouldBeOfType<TextContentBlock>();
            ((TextContentBlock)result).Text.ShouldBe("fallback");
        }
    }

    public class ExtensionsToolContentTests
    {
        private static readonly byte[] _testImageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        [Fact]
        public void ToCallToolResult_ImageContent_PreservesImageBlock()
        {
            // Arrange
            var response = ResponseCallTool.Image(_testImageBytes, Consts.MimeType.ImagePng);

            // Act
            var result = response.ToCallToolResult();

            // Assert
            result.IsError.ShouldBe(false);
            result.Content.Count.ShouldBe(1);
            result.Content[0].ShouldBeOfType<ImageContentBlock>();
            ((ImageContentBlock)result.Content[0]).MimeType.ShouldBe(Consts.MimeType.ImagePng);
        }

        [Fact]
        public void ToCallToolResult_ImageWithMessage_PreservesBothBlocks()
        {
            // Arrange
            var response = ResponseCallTool.Image(_testImageBytes, Consts.MimeType.ImagePng, "caption");

            // Act
            var result = response.ToCallToolResult();

            // Assert
            result.Content.Count.ShouldBe(2);
            result.Content[0].ShouldBeOfType<TextContentBlock>();
            result.Content[1].ShouldBeOfType<ImageContentBlock>();
        }

        [Fact]
        public void ToCallToolResult_AudioContent_PreservesAudioBlock()
        {
            // Arrange
            var response = ResponseCallTool.Audio(_testImageBytes, Consts.MimeType.AudioWav);

            // Act
            var result = response.ToCallToolResult();

            // Assert
            result.IsError.ShouldBe(false);
            result.Content.Count.ShouldBe(1);
            result.Content[0].ShouldBeOfType<AudioContentBlock>();
            ((AudioContentBlock)result.Content[0]).MimeType.ShouldBe(Consts.MimeType.AudioWav);
        }

        [Fact]
        public void ToCallToolResult_MixedContent_PreservesAllBlocks()
        {
            // Arrange
            var text = CommonContentBlock.CreateText("Result:");
            var image = CommonContentBlock.CreateImage(_testImageBytes, Consts.MimeType.ImagePng);
            var response = ResponseCallTool.WithContent(ResponseStatus.Success, text, image);

            // Act
            var result = response.ToCallToolResult();

            // Assert
            result.Content.Count.ShouldBe(2);
            result.Content[0].ShouldBeOfType<TextContentBlock>();
            result.Content[1].ShouldBeOfType<ImageContentBlock>();
        }

        [Fact]
        public void ToCallToolResult_ErrorStatus_SetsIsError()
        {
            // Arrange
            var response = ResponseCallTool.Error("something went wrong");

            // Act
            var result = response.ToCallToolResult();

            // Assert
            result.IsError.ShouldBe(true);
        }

        [Fact]
        public void ToCallToolResult_WithNonNullStructuredContent_MapsToJsonElement()
        {
            // Arrange – SuccessStructured wraps the node as {"result": <value>}
            var response = ResponseCallTool.SuccessStructured(JsonValue.Create(42));

            // Act
            var result = response.ToCallToolResult();

            // Assert
            result.StructuredContent.ShouldNotBeNull();
            var element = result.StructuredContent!.Value;
            element.ValueKind.ShouldBe(JsonValueKind.Object);
            element.GetProperty("result").GetInt32().ShouldBe(42);
        }

        [Fact]
        public void ToCallToolResult_WithNullStructuredContent_RemainsNull()
        {
            // Arrange – Success() leaves StructuredContent null
            var response = ResponseCallTool.Success("hello");

            // Act
            var result = response.ToCallToolResult();

            // Assert
            result.StructuredContent.ShouldBeNull();
        }
    }
}
