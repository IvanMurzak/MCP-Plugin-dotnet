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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using com.IvanMurzak.McpPlugin.Server;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Bind-address resolution (mcp-authorize b2, decision D8): the default is loopback-only, and
    /// <c>--bind</c> opts into all-interfaces / a specific address.
    /// </summary>
    public class ExtensionsWebHostBindTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("loopback")]
        [InlineData("localhost")]
        public void Default_BindsLoopbackOnly(string? bind)
        {
            var addresses = ExtensionsWebHost.ResolveBindAddresses(bind);

            addresses.ShouldContain(IPAddress.Loopback);
            addresses.ShouldNotContain(IPAddress.Any);
            addresses.ShouldNotContain(IPAddress.IPv6Any);
            if (Socket.OSSupportsIPv6)
                addresses.ShouldContain(IPAddress.IPv6Loopback);
        }

        [Fact]
        public void BindAny_BindsAllInterfaces()
        {
            var addresses = ExtensionsWebHost.ResolveBindAddresses("any");

            addresses.ShouldContain(IPAddress.Any);
            addresses.ShouldNotContain(IPAddress.Loopback);
            if (Socket.OSSupportsIPv6)
                addresses.ShouldContain(IPAddress.IPv6Any);
        }

        [Fact]
        public void BindZeros_BindsIPv4Any()
        {
            var addresses = ExtensionsWebHost.ResolveBindAddresses("0.0.0.0");
            addresses.ShouldBe(new[] { IPAddress.Any });
        }

        [Fact]
        public void BindSpecificIPv4_BindsThatAddressOnly()
        {
            var addresses = ExtensionsWebHost.ResolveBindAddresses("192.168.1.50");
            addresses.ShouldBe(new[] { IPAddress.Parse("192.168.1.50") });
        }

        [Fact]
        public void BindInvalid_Throws()
        {
            Should.Throw<System.ArgumentException>(() => ExtensionsWebHost.ResolveBindAddresses("not-an-ip"));
        }
    }
}
