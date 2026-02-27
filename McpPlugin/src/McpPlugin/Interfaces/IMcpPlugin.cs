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
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    public interface IMcpPlugin : IConnection, IDisposable
    {
        ILogger Logger { get; }
        IMcpManager McpManager { get; }
        IMcpManagerHub? McpManagerHub { get; }
        /// <summary>
        /// Gets the version of the MCP plugin.
        /// </summary>
        Common.Version Version { get; }
        /// <summary>
        /// Gets the version handshake response status if a handshake has been performed; otherwise, null.
        /// </summary>
        VersionHandshakeResponse? VersionHandshakeStatus { get; }
        /// <summary>
        /// Gets the total number of tool calls made.
        /// </summary>
        ulong ToolCallsCount => 0;

        /// <summary>
        /// Unconditionally generates skill markdown files for all registered tools, regardless of
        /// <see cref="ConnectionConfig.GenerateSkillFiles"/>. The resolved output path is determined as
        /// follows: if <see cref="ConnectionConfig.SkillsPath"/> is an absolute path it is used as-is
        /// and <paramref name="path"/> is ignored; otherwise <paramref name="path"/> (when provided) is
        /// used as the base directory, falling back to the application base directory.
        /// Each tool produces one <c>.md</c> file describing its name, description, and parameters so
        /// that AI clients can discover and invoke it.
        /// </summary>
        /// <param name="path">
        /// Optional base directory prepended to <see cref="ConnectionConfig.SkillsPath"/> when that
        /// value is a relative path. Ignored when <see cref="ConnectionConfig.SkillsPath"/> is rooted.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if files were generated successfully; <see langword="false"/> if the
        /// tool list could not be retrieved or generation failed.
        /// </returns>
        bool GenerateSkillFiles(string? path = null);

        /// <summary>
        /// Generates skill markdown files only when <see cref="ConnectionConfig.GenerateSkillFiles"/> is
        /// <see langword="true"/>; otherwise does nothing. Called automatically on plugin build and
        /// whenever the registered tool set changes. Delegates to <see cref="GenerateSkillFiles"/> when
        /// the condition is met.
        /// </summary>
        /// <param name="path">
        /// Optional base directory forwarded to <see cref="GenerateSkillFiles"/>. Ignored when
        /// <see cref="ConnectionConfig.SkillsPath"/> is rooted.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if files were generated successfully; <see langword="false"/> if
        /// generation is disabled or the underlying call to <see cref="GenerateSkillFiles"/> failed.
        /// </returns>
        bool GenerateSkillFilesIfNeeded(string? path = null);

        /// <summary>
        /// Deletes the skill markdown subdirectory for each currently registered tool from the resolved
        /// skills path. If <see cref="ConnectionConfig.SkillsPath"/> is an absolute path it is used
        /// as-is and <paramref name="path"/> is ignored; otherwise <paramref name="path"/> (when
        /// provided) is used as the base directory, falling back to the application base directory.
        /// Only the subdirectories that correspond to registered tools are removed; any other content
        /// inside the skills folder is left intact.
        /// </summary>
        /// <param name="path">
        /// Optional base directory prepended to <see cref="ConnectionConfig.SkillsPath"/> when that
        /// value is a relative path. Ignored when <see cref="ConnectionConfig.SkillsPath"/> is rooted.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if all matching skill directories were deleted successfully (or did
        /// not exist); <see langword="false"/> if the tool list could not be retrieved or any
        /// deletion failed.
        /// </returns>
        bool DeleteSkillFiles(string? path = null);
    }
}
