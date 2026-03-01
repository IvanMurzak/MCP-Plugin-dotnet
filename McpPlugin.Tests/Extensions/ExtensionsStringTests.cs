/*
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚  Author: Ivan Murzak (https://github.com/IvanMurzak)                   â”‚
â”‚  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  â”‚
â”‚  Copyright (c) 2025 Ivan Murzak                                        â”‚
â”‚  Licensed under the Apache License, Version 2.0.                       â”‚
â”‚  See the LICENSE file in the project root for more information.        â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
*/

using System.Collections.Generic;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Extensions
{
    public class ExtensionsStringTests
    {
        [Fact]
        public void Join_WithDefaultSeparator_JoinsStringsWithComma()
        {
            // Arrange
            var strings = new List<string> { "apple", "banana", "cherry" };

            // Act
            var result = strings.Join();

            // Assert
            result.ShouldBe("apple, banana, cherry");
        }

        [Fact]
        public void Join_WithCustomSeparator_JoinsStringsWithCustomSeparator()
        {
            // Arrange
            var strings = new List<string> { "apple", "banana", "cherry" };

            // Act
            var result = strings.Join(" | ");

            // Assert
            result.ShouldBe("apple | banana | cherry");
        }

        [Fact]
        public void Join_WithEmptyList_ReturnsEmptyString()
        {
            // Arrange
            var strings = new List<string>();

            // Act
            var result = strings.Join();

            // Assert
            result.ShouldBeEmpty();
        }

        [Fact]
        public void JoinExcept_ExcludesSpecifiedString()
        {
            // Arrange
            var strings = new List<string> { "apple", "banana", "cherry" };

            // Act
            var result = strings.JoinExcept("banana");

            // Assert
            result.ShouldBe("apple, cherry");
        }

        [Fact]
        public void JoinExcept_WithNonExistentString_ReturnsAllStrings()
        {
            // Arrange
            var strings = new List<string> { "apple", "banana", "cherry" };

            // Act
            var result = strings.JoinExcept("orange");

            // Assert
            result.ShouldBe("apple, banana, cherry");
        }

        [Fact]
        public void JoinEnclose_EnclosesStringsWithDefaultSingleQuotes()
        {
            // Arrange
            var strings = new List<string> { "apple", "banana", "cherry" };

            // Act
            var result = strings.JoinEnclose();

            // Assert
            result.ShouldBe("'apple', 'banana', 'cherry'");
        }

        [Fact]
        public void JoinEnclose_WithCustomEnclose_UsesCustomEnclosure()
        {
            // Arrange
            var strings = new List<string> { "apple", "banana", "cherry" };

            // Act
            var result = strings.JoinEnclose(enclose: "\"");

            // Assert
            result.ShouldBe("\"apple\", \"banana\", \"cherry\"");
        }

        [Fact]
        public void JoinEnclose_WithCustomSeparatorAndEnclose_UsesCustomValues()
        {
            // Arrange
            var strings = new List<string> { "apple", "banana", "cherry" };

            // Act
            var result = strings.JoinEnclose(" | ", "[");

            // Assert
            result.ShouldBe("[apple[ | [banana[ | [cherry[");
        }

        [Fact]
        public void JoinEncloseExcept_ExcludesAndEncloses()
        {
            // Arrange
            var strings = new List<string> { "apple", "banana", "cherry" };

            // Act
            var result = strings.JoinEncloseExcept("banana");

            // Assert
            result.ShouldBe("'apple', 'cherry'");
        }

        [Fact]
        public void JoinEncloseExcept_WithCustomValues_UsesCustomSeparatorAndEnclosure()
        {
            // Arrange
            var strings = new List<string> { "apple", "banana", "cherry" };

            // Act
            var result = strings.JoinEncloseExcept("banana", " - ", "<<");

            // Assert
            result.ShouldBe("<<apple<< - <<cherry<<");
        }
    }
}
