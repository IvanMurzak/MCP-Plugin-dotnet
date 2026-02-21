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
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using FluentAssertions;
using ModelContextProtocol.Protocol;
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
            result.Should().BeOfType<TextContentBlock>();
            ((TextContentBlock)result).Text.Should().Be("hello world");
        }

        [Fact]
        public void ToContent_TextBlock_EmptyText_ReturnsEmptyTextContentBlock()
        {
            // Arrange
            var block = new Common.Model.ContentBlock { Type = "text", Text = null };

            // Act
            var result = block.ToContent();

            // Assert
            result.Should().BeOfType<TextContentBlock>();
            ((TextContentBlock)result).Text.Should().Be(string.Empty);
        }

        [Fact]
        public void ToContent_ImageBlock_ReturnsImageContentBlock()
        {
            // Arrange
            var block = CommonContentBlock.CreateImage(_testImageBytes, Consts.MimeType.ImagePng);

            // Act
            var result = block.ToContent();

            // Assert
            result.Should().BeOfType<ImageContentBlock>();
            var imageBlock = (ImageContentBlock)result;
            imageBlock.MimeType.Should().Be(Consts.MimeType.ImagePng);
        }

        [Fact]
        public void ToContent_ImageBlock_DataRoundTrips()
        {
            // Arrange
            var block = CommonContentBlock.CreateImage(_testImageBytes, Consts.MimeType.ImagePng);

            // Act
            var result = (ImageContentBlock)block.ToContent();

            // Assert
            result.DecodedData.ToArray().Should().BeEquivalentTo(_testImageBytes);
        }

        [Fact]
        public void ToContent_ImageBlock_NullData_Throws()
        {
            // Arrange
            var block = new Common.Model.ContentBlock { Type = "image", Data = null, MimeType = Consts.MimeType.ImagePng };

            // Act
            var act = () => block.ToContent();

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Image content block is missing Data*");
        }

        [Fact]
        public void ToContent_AudioBlock_ReturnsAudioContentBlock()
        {
            // Arrange
            var block = CommonContentBlock.CreateAudio(_testAudioBytes, Consts.MimeType.AudioWav);

            // Act
            var result = block.ToContent();

            // Assert
            result.Should().BeOfType<AudioContentBlock>();
            var audioBlock = (AudioContentBlock)result;
            audioBlock.MimeType.Should().Be(Consts.MimeType.AudioWav);
        }

        [Fact]
        public void ToContent_AudioBlock_DataRoundTrips()
        {
            // Arrange
            var block = CommonContentBlock.CreateAudio(_testAudioBytes, Consts.MimeType.AudioWav);

            // Act
            var result = (AudioContentBlock)block.ToContent();

            // Assert
            result.DecodedData.ToArray().Should().BeEquivalentTo(_testAudioBytes);
        }

        [Fact]
        public void ToContent_AudioBlock_NullData_Throws()
        {
            // Arrange
            var block = new Common.Model.ContentBlock { Type = "audio", Data = null, MimeType = Consts.MimeType.AudioWav };

            // Act
            var act = () => block.ToContent();

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Audio content block is missing Data*");
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
            result.Should().BeOfType<EmbeddedResourceBlock>();
            var resourceBlock = (EmbeddedResourceBlock)result;
            resourceBlock.Resource.Should().BeOfType<TextResourceContents>();
            ((TextResourceContents)resourceBlock.Resource).Text.Should().Be("hello");
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
            result.Should().BeOfType<EmbeddedResourceBlock>();
            var resourceBlock = (EmbeddedResourceBlock)result;
            resourceBlock.Resource.Should().BeOfType<BlobResourceContents>();
        }

        [Fact]
        public void ToContent_ResourceBlock_NullResource_Throws()
        {
            // Arrange
            var block = new Common.Model.ContentBlock { Type = "resource", Resource = null };

            // Act
            var act = () => block.ToContent();

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Resource content block is missing Resource*");
        }

        [Fact]
        public void ToContent_UnknownType_FallsBackToTextContentBlock()
        {
            // Arrange
            var block = new Common.Model.ContentBlock { Type = "unknown", Text = "fallback" };

            // Act
            var result = block.ToContent();

            // Assert
            result.Should().BeOfType<TextContentBlock>();
            ((TextContentBlock)result).Text.Should().Be("fallback");
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
            result.IsError.Should().BeFalse();
            result.Content.Should().HaveCount(1);
            result.Content[0].Should().BeOfType<ImageContentBlock>();
            ((ImageContentBlock)result.Content[0]).MimeType.Should().Be(Consts.MimeType.ImagePng);
        }

        [Fact]
        public void ToCallToolResult_ImageWithMessage_PreservesBothBlocks()
        {
            // Arrange
            var response = ResponseCallTool.Image(_testImageBytes, Consts.MimeType.ImagePng, "caption");

            // Act
            var result = response.ToCallToolResult();

            // Assert
            result.Content.Should().HaveCount(2);
            result.Content[0].Should().BeOfType<TextContentBlock>();
            result.Content[1].Should().BeOfType<ImageContentBlock>();
        }

        [Fact]
        public void ToCallToolResult_AudioContent_PreservesAudioBlock()
        {
            // Arrange
            var response = ResponseCallTool.Audio(_testImageBytes, Consts.MimeType.AudioWav);

            // Act
            var result = response.ToCallToolResult();

            // Assert
            result.IsError.Should().BeFalse();
            result.Content.Should().HaveCount(1);
            result.Content[0].Should().BeOfType<AudioContentBlock>();
            ((AudioContentBlock)result.Content[0]).MimeType.Should().Be(Consts.MimeType.AudioWav);
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
            result.Content.Should().HaveCount(2);
            result.Content[0].Should().BeOfType<TextContentBlock>();
            result.Content[1].Should().BeOfType<ImageContentBlock>();
        }

        [Fact]
        public void ToCallToolResult_ErrorStatus_SetsIsError()
        {
            // Arrange
            var response = ResponseCallTool.Error("something went wrong");

            // Act
            var result = response.ToCallToolResult();

            // Assert
            result.IsError.Should().BeTrue();
        }
    }
}
