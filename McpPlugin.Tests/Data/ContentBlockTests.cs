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
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using FluentAssertions;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Data
{
    public class ContentBlockTests
    {
        private readonly byte[] _testImageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes
        private readonly byte[] _testAudioData = new byte[] { 0x52, 0x49, 0x46, 0x46 }; // WAV RIFF header

        [Fact]
        public void CreateText_CreatesTextContentBlock()
        {
            // Arrange
            var text = "Hello, World!";

            // Act
            var block = ContentBlock.CreateText(text);

            // Assert
            block.Type.Should().Be("text");
            block.Text.Should().Be(text);
            block.MimeType.Should().Be(Consts.MimeType.TextPlain);
            block.Data.Should().BeNull();
        }

        [Fact]
        public void CreateText_WithCustomMimeType_CreatesTextContentBlock()
        {
            // Arrange
            var text = "{\"key\": \"value\"}";

            // Act
            var block = ContentBlock.CreateText(text, Consts.MimeType.TextJson);

            // Assert
            block.Type.Should().Be("text");
            block.Text.Should().Be(text);
            block.MimeType.Should().Be(Consts.MimeType.TextJson);
        }

        [Fact]
        public void CreateImage_CreatesImageContentBlockWithBase64()
        {
            // Arrange
            var expectedBase64 = Convert.ToBase64String(_testImageData);

            // Act
            var block = ContentBlock.CreateImage(_testImageData, Consts.MimeType.ImagePng);

            // Assert
            block.Type.Should().Be("image");
            block.Data.Should().Be(expectedBase64);
            block.MimeType.Should().Be(Consts.MimeType.ImagePng);
            block.Text.Should().BeNull();
        }

        [Fact]
        public void CreateImageBase64_CreatesImageContentBlockFromBase64()
        {
            // Arrange
            var base64Data = "iVBORw0KGgo=";

            // Act
            var block = ContentBlock.CreateImageBase64(base64Data, Consts.MimeType.ImageJpeg);

            // Assert
            block.Type.Should().Be("image");
            block.Data.Should().Be(base64Data);
            block.MimeType.Should().Be(Consts.MimeType.ImageJpeg);
        }

        [Fact]
        public void CreateAudio_CreatesAudioContentBlockWithBase64()
        {
            // Arrange
            var expectedBase64 = Convert.ToBase64String(_testAudioData);

            // Act
            var block = ContentBlock.CreateAudio(_testAudioData, Consts.MimeType.AudioWav);

            // Assert
            block.Type.Should().Be("audio");
            block.Data.Should().Be(expectedBase64);
            block.MimeType.Should().Be(Consts.MimeType.AudioWav);
            block.Text.Should().BeNull();
        }

        [Fact]
        public void CreateAudioBase64_CreatesAudioContentBlockFromBase64()
        {
            // Arrange
            var base64Data = "UklGRg==";

            // Act
            var block = ContentBlock.CreateAudioBase64(base64Data, Consts.MimeType.AudioMpeg);

            // Assert
            block.Type.Should().Be("audio");
            block.Data.Should().Be(base64Data);
            block.MimeType.Should().Be(Consts.MimeType.AudioMpeg);
        }
    }

    public class ResponseCallToolImageAudioTests
    {
        private readonly byte[] _testImageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        private readonly byte[] _testAudioData = new byte[] { 0x52, 0x49, 0x46, 0x46 };

        [Fact]
        public void Image_CreatesSuccessResponseWithImageContent()
        {
            // Arrange
            var expectedBase64 = Convert.ToBase64String(_testImageData);

            // Act
            var response = ResponseCallTool.Image(_testImageData, Consts.MimeType.ImagePng);

            // Assert
            response.Status.Should().Be(ResponseStatus.Success);
            response.Content.Should().HaveCount(1);
            response.Content[0].Type.Should().Be("image");
            response.Content[0].Data.Should().Be(expectedBase64);
            response.Content[0].MimeType.Should().Be(Consts.MimeType.ImagePng);
        }

        [Fact]
        public void Image_WithMessage_IncludesTextContentBeforeImage()
        {
            // Arrange
            var message = "Screenshot captured";

            // Act
            var response = ResponseCallTool.Image(_testImageData, Consts.MimeType.ImagePng, message);

            // Assert
            response.Status.Should().Be(ResponseStatus.Success);
            response.Content.Should().HaveCount(2);
            response.Content[0].Type.Should().Be("text");
            response.Content[0].Text.Should().Be(message);
            response.Content[1].Type.Should().Be("image");
        }

        [Fact]
        public void Audio_CreatesSuccessResponseWithAudioContent()
        {
            // Arrange
            var expectedBase64 = Convert.ToBase64String(_testAudioData);

            // Act
            var response = ResponseCallTool.Audio(_testAudioData, Consts.MimeType.AudioWav);

            // Assert
            response.Status.Should().Be(ResponseStatus.Success);
            response.Content.Should().HaveCount(1);
            response.Content[0].Type.Should().Be("audio");
            response.Content[0].Data.Should().Be(expectedBase64);
            response.Content[0].MimeType.Should().Be(Consts.MimeType.AudioWav);
        }

        [Fact]
        public void Audio_WithMessage_IncludesTextContentBeforeAudio()
        {
            // Arrange
            var message = "Audio recorded";

            // Act
            var response = ResponseCallTool.Audio(_testAudioData, Consts.MimeType.AudioMpeg, message);

            // Assert
            response.Status.Should().Be(ResponseStatus.Success);
            response.Content.Should().HaveCount(2);
            response.Content[0].Type.Should().Be("text");
            response.Content[0].Text.Should().Be(message);
            response.Content[1].Type.Should().Be("audio");
        }

        [Fact]
        public void WithContent_CreatesResponseWithMultipleBlocks()
        {
            // Arrange
            var textBlock = ContentBlock.CreateText("Description");
            var imageBlock = ContentBlock.CreateImage(_testImageData, Consts.MimeType.ImagePng);

            // Act
            var response = ResponseCallTool.WithContent(textBlock, imageBlock);

            // Assert
            response.Status.Should().Be(ResponseStatus.Success);
            response.Content.Should().HaveCount(2);
            response.Content[0].Should().BeSameAs(textBlock);
            response.Content[1].Should().BeSameAs(imageBlock);
        }

        [Fact]
        public void WithContent_WithStatus_CreatesResponseWithSpecifiedStatus()
        {
            // Arrange
            var textBlock = ContentBlock.CreateText("Error details");

            // Act
            var response = ResponseCallTool.WithContent(ResponseStatus.Error, textBlock);

            // Assert
            response.Status.Should().Be(ResponseStatus.Error);
            response.Content.Should().HaveCount(1);
        }

        [Fact]
        public void Image_DataCanBeDecodedBackToOriginal()
        {
            // Act
            var response = ResponseCallTool.Image(_testImageData, Consts.MimeType.ImagePng);
            var decodedData = Convert.FromBase64String(response.Content[0].Data!);

            // Assert
            decodedData.Should().BeEquivalentTo(_testImageData);
        }
    }

    public class MimeTypeConstantsTests
    {
        [Fact]
        public void ImageMimeTypes_AreCorrectlyDefined()
        {
            Consts.MimeType.ImagePng.Should().Be("image/png");
            Consts.MimeType.ImageJpeg.Should().Be("image/jpeg");
            Consts.MimeType.ImageGif.Should().Be("image/gif");
            Consts.MimeType.ImageWebp.Should().Be("image/webp");
            Consts.MimeType.ImageSvg.Should().Be("image/svg+xml");
        }

        [Fact]
        public void AudioMimeTypes_AreCorrectlyDefined()
        {
            Consts.MimeType.AudioMpeg.Should().Be("audio/mpeg");
            Consts.MimeType.AudioWav.Should().Be("audio/wav");
            Consts.MimeType.AudioOgg.Should().Be("audio/ogg");
            Consts.MimeType.AudioWebm.Should().Be("audio/webm");
        }
    }
}
