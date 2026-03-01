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
using Shouldly;
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

            block.Type.ShouldBe("image");
            block.MimeType.ShouldBe(Consts.MimeType.ImagePng);
            Convert.FromBase64String(block.Data!).ShouldBe(_testData);
        }

        [Fact]
        public void CreateImageBase64_PassesDataThrough()
        {
            var base64 = "iVBORw0KGgo=";

            var block = ContentBlock.CreateImageBase64(base64, Consts.MimeType.ImagePng);

            block.Data.ShouldBe(base64);
        }
    }

    public class ResponseCallToolImageAudioTests
    {
        private readonly byte[] _testData = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        [Fact]
        public void Image_WithMessage_PrependsTextBlock()
        {
            var response = ResponseCallTool.Image(_testData, Consts.MimeType.ImagePng, "caption");

            response.Status.ShouldBe(ResponseStatus.Success);
            response.Content.Count.ShouldBe(2);
            response.Content[0].Type.ShouldBe("text");
            response.Content[0].Text.ShouldBe("caption");
            response.Content[1].Type.ShouldBe("image");
        }

        [Fact]
        public void Image_WithoutMessage_HasSingleBlock()
        {
            var response = ResponseCallTool.Image(_testData, Consts.MimeType.ImagePng);

            response.Content.Count.ShouldBe(1);
            response.Content[0].Type.ShouldBe("image");
        }

        [Fact]
        public void Image_DataRoundTrips()
        {
            var response = ResponseCallTool.Image(_testData, Consts.MimeType.ImagePng);

            var decoded = Convert.FromBase64String(response.Content[0].Data!);
            decoded.ShouldBe(_testData);
        }

        [Fact]
        public void WithContent_AcceptsMultipleBlocks()
        {
            var text = ContentBlock.CreateText("description");
            var image = ContentBlock.CreateImage(_testData, Consts.MimeType.ImagePng);

            var response = ResponseCallTool.WithContent(ResponseStatus.Success, text, image);

            response.Status.ShouldBe(ResponseStatus.Success);
            response.Content.Count.ShouldBe(2);
            response.Content[0].ShouldBeSameAs(text);
            response.Content[1].ShouldBeSameAs(image);
        }
    }
}
