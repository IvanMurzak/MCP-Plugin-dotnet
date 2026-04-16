/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Text.Json;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Extensions
{
    public class ExtensionsJsonElementTests
    {
        private static JsonElement? ParseElement(string json)
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        // ── int ──────────────────────────────────────────────────────────────

        [Fact]
        public void SetProperty_Int_SetsValue()
        {
            JsonElement? element = null;
            var result = element.SetProperty("count", 42);
            result.GetProperty("count").GetInt32().ShouldBe(42);
        }

        [Fact]
        public void SetProperty_Int_SkipsWhenUnchanged()
        {
            JsonElement? element = ParseElement("{\"count\":42}");
            var original = element!.Value;
            var result = element.SetProperty("count", 42);
            result.GetRawText().ShouldBe(original.GetRawText());
        }

        // ── uint ─────────────────────────────────────────────────────────────

        [Fact]
        public void SetProperty_UInt_SetsValue()
        {
            JsonElement? element = null;
            var result = element.SetProperty("count", 42u);
            result.GetProperty("count").GetUInt32().ShouldBe(42u);
        }

        [Fact]
        public void SetProperty_UInt_SkipsWhenUnchanged()
        {
            JsonElement? element = ParseElement("{\"count\":42}");
            var original = element!.Value;
            var result = element.SetProperty("count", 42u);
            result.GetRawText().ShouldBe(original.GetRawText());
        }

        [Fact]
        public void SetProperty_UInt_ReplacesExistingValue()
        {
            JsonElement? element = ParseElement("{\"count\":10}");
            var result = element.SetProperty("count", 99u);
            result.GetProperty("count").GetUInt32().ShouldBe(99u);
        }

        // ── long ─────────────────────────────────────────────────────────────

        [Fact]
        public void SetProperty_Long_SetsValue()
        {
            JsonElement? element = null;
            var result = element.SetProperty("big", 9999999999L);
            result.GetProperty("big").GetInt64().ShouldBe(9999999999L);
        }

        // ── ulong ────────────────────────────────────────────────────────────

        [Fact]
        public void SetProperty_ULong_SetsValue()
        {
            JsonElement? element = null;
            var result = element.SetProperty("big", 18446744073709551615UL);
            result.GetProperty("big").GetUInt64().ShouldBe(18446744073709551615UL);
        }

        // ── float ────────────────────────────────────────────────────────────

        [Fact]
        public void SetProperty_Float_SetsValue()
        {
            JsonElement? element = null;
            var result = element.SetProperty("ratio", 3.14f);
            result.GetProperty("ratio").GetSingle().ShouldBe(3.14f);
        }

        [Fact]
        public void SetProperty_Float_SkipsWhenUnchanged()
        {
            JsonElement? element = ParseElement("{\"ratio\":3.14}");
            var original = element!.Value;
            var result = element.SetProperty("ratio", 3.14f);
            result.GetRawText().ShouldBe(original.GetRawText());
        }

        // ── double ───────────────────────────────────────────────────────────

        [Fact]
        public void SetProperty_Double_SetsValue()
        {
            JsonElement? element = null;
            var result = element.SetProperty("pi", 3.141592653589793);
            result.GetProperty("pi").GetDouble().ShouldBe(3.141592653589793);
        }

        [Fact]
        public void SetProperty_Double_SkipsWhenUnchanged()
        {
            JsonElement? element = ParseElement("{\"pi\":3.141592653589793}");
            var original = element!.Value;
            var result = element.SetProperty("pi", 3.141592653589793);
            result.GetRawText().ShouldBe(original.GetRawText());
        }

        [Fact]
        public void SetProperty_Double_ReplacesExistingValue()
        {
            JsonElement? element = ParseElement("{\"pi\":0.0}");
            var result = element.SetProperty("pi", 3.14);
            result.GetProperty("pi").GetDouble().ShouldBe(3.14);
        }

        // ── decimal ──────────────────────────────────────────────────────────

        [Fact]
        public void SetProperty_Decimal_SetsValue()
        {
            JsonElement? element = null;
            var result = element.SetProperty("price", 19.99m);
            result.GetProperty("price").GetDecimal().ShouldBe(19.99m);
        }

        [Fact]
        public void SetProperty_Decimal_SkipsWhenUnchanged()
        {
            JsonElement? element = ParseElement("{\"price\":19.99}");
            var original = element!.Value;
            var result = element.SetProperty("price", 19.99m);
            result.GetRawText().ShouldBe(original.GetRawText());
        }

        [Fact]
        public void SetProperty_Decimal_ReplacesExistingValue()
        {
            JsonElement? element = ParseElement("{\"price\":10.00}");
            var result = element.SetProperty("price", 29.99m);
            result.GetProperty("price").GetDecimal().ShouldBe(29.99m);
        }

        // ── string ───────────────────────────────────────────────────────────

        [Fact]
        public void SetProperty_String_SetsValue()
        {
            JsonElement? element = null;
            var result = element.SetProperty("name", "hello");
            result.GetProperty("name").GetString().ShouldBe("hello");
        }

        [Fact]
        public void SetProperty_String_SkipsWhenUnchanged()
        {
            JsonElement? element = ParseElement("{\"name\":\"hello\"}");
            var original = element!.Value;
            var result = element.SetProperty("name", "hello");
            result.GetRawText().ShouldBe(original.GetRawText());
        }

        // ── bool ─────────────────────────────────────────────────────────────

        [Fact]
        public void SetProperty_Bool_SetsValue()
        {
            JsonElement? element = null;
            var result = element.SetProperty("active", true);
            result.GetProperty("active").GetBoolean().ShouldBe(true);
        }

        [Fact]
        public void SetProperty_Bool_SkipsWhenUnchanged()
        {
            JsonElement? element = ParseElement("{\"active\":true}");
            var original = element!.Value;
            var result = element.SetProperty("active", true);
            result.GetRawText().ShouldBe(original.GetRawText());
        }

        // ── shared behavior ──────────────────────────────────────────────────

        [Fact]
        public void SetProperty_PreservesOtherProperties()
        {
            JsonElement? element = ParseElement("{\"a\":1,\"b\":\"two\",\"c\":true}");
            var result = element.SetProperty("b", "updated");
            result.GetProperty("a").GetInt32().ShouldBe(1);
            result.GetProperty("b").GetString().ShouldBe("updated");
            result.GetProperty("c").GetBoolean().ShouldBe(true);
        }

        [Fact]
        public void SetProperty_AddsNewPropertyToExistingObject()
        {
            JsonElement? element = ParseElement("{\"existing\":1}");
            var result = element.SetProperty("newProp", 42u);
            result.GetProperty("existing").GetInt32().ShouldBe(1);
            result.GetProperty("newProp").GetUInt32().ShouldBe(42u);
        }
    }
}
