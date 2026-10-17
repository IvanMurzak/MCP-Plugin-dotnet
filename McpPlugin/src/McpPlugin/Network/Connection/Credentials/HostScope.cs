/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using System;
using System.Net;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Host classification for credential scoping. The stored ai-game.dev account credential may be sent to a
    /// Custom (self-hosted) server only when that server is on this machine. The decision is made by parsing the
    /// URL host — never by DNS — so a name that merely resolves to a loopback address does not qualify.
    /// </summary>
    public static class HostScope
    {
        /// <summary>
        /// True when <paramref name="url"/> is an absolute <c>http</c>/<c>https</c> URL without user info whose host
        /// is literally <c>localhost</c> (any case), an IPv4 address in <c>127.0.0.0/8</c>, or the IPv6 loopback
        /// <c>::1</c> (also as an IPv4-mapped <c>::ffff:127.x.y.z</c>). Everything else — including
        /// <c>http://localhost@evil.com</c>, <c>http://127.0.0.1.evil.com</c>, <c>http://localhost.evil.com</c>,
        /// <c>http://localhost.</c> and unparseable input — is false.
        /// </summary>
        public static bool IsLoopback(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;
            if (!Uri.TryCreate(url!.Trim(), UriKind.Absolute, out var uri))
                return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return false;
            if (!string.IsNullOrEmpty(uri.UserInfo))
                return false;

            switch (uri.HostNameType)
            {
                case UriHostNameType.Dns:
                    return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
                case UriHostNameType.IPv4:
                case UriHostNameType.IPv6:
                    if (!IPAddress.TryParse(uri.DnsSafeHost, out var address))
                        return false;
                    if (address.IsIPv4MappedToIPv6)
                        address = address.MapToIPv4();
                    return IPAddress.IsLoopback(address);
                default:
                    return false;
            }
        }
    }
}
