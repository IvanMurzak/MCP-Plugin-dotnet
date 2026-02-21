/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Kieran Hannigan (https://github.com/KaiStarkk)                │
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
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Data
{
    public class ContentBlockFactoryTests
    {
        private readonly byte[] _testData = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        [Fact]
        public void CreateImage_EncodesDataAsBase64()
        {
            var block = ContentBlock.CreateImage(_testData, Consts.MimeType.ImagePng);

            block.Type.Should().Be("image");
            block.MimeType.Should().Be(Consts.MimeType.ImagePng);
            Convert.FromBase64String(block.Data!).Should().BeEquivalentTo(_testData);
        }

        [Fact]
        public void CreateImageBase64_PassesDataThrough()
        {
            var base64 = "iVBORw0KGgo=";

            var block = ContentBlock.CreateImageBase64(base64, Consts.MimeType.ImagePng);

            block.Data.Should().Be(base64);
        }
    }

    public class ResponseCallToolImageAudioTests
    {
        private readonly byte[] _testData = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        [Fact]
        public void Image_WithMessage_PrependsTextBlock()
        {
            var response = ResponseCallTool.Image(_testData, Consts.MimeType.ImagePng, "caption");

            response.Status.Should().Be(ResponseStatus.Success);
            response.Content.Should().HaveCount(2);
            response.Content[0].Type.Should().Be("text");
            response.Content[0].Text.Should().Be("caption");
            response.Content[1].Type.Should().Be("image");
        }

        [Fact]
        public void Image_WithoutMessage_HasSingleBlock()
        {
            var response = ResponseCallTool.Image(_testData, Consts.MimeType.ImagePng);

            response.Content.Should().HaveCount(1);
            response.Content[0].Type.Should().Be("image");
        }

        [Fact]
        public void Image_DataRoundTrips()
        {
            var response = ResponseCallTool.Image(_testData, Consts.MimeType.ImagePng);

            var decoded = Convert.FromBase64String(response.Content[0].Data!);
            decoded.Should().BeEquivalentTo(_testData);
        }

        [Fact]
        public void WithContent_AcceptsMultipleBlocks()
        {
            var text = ContentBlock.CreateText("description");
            var image = ContentBlock.CreateImage(_testData, Consts.MimeType.ImagePng);

            var response = ResponseCallTool.WithContent(ResponseStatus.Success, text, image);

            response.Status.Should().Be(ResponseStatus.Success);
            response.Content.Should().HaveCount(2);
            response.Content[0].Should().BeSameAs(text);
            response.Content[1].Should().BeSameAs(image);
        }
    }
}
