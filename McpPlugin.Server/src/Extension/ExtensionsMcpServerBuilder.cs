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
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;
using com.IvanMurzak.McpPlugin.Server.Security;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tools;
using com.IvanMurzak.McpPlugin.Server.Webhooks;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ExtensionsMcpServerBuilder
    {
        private static McpServerSetup? GetMcpServerSetup(IServiceCollection services)
        {
            foreach (var descriptor in services)
            {
                if (descriptor.ServiceType == typeof(McpServerSetup)
                    && descriptor.ImplementationInstance is McpServerSetup setup)
                    return setup;
            }
            return null;
        }

        public static IMcpServerBuilder WithMcpPluginServer(
            this IMcpServerBuilder mcpServerBuilder,
            DataArguments dataArguments,
            Action<Microsoft.AspNetCore.SignalR.HubOptions>? signalRConfigure = null,
            Common.Version? version = null)
        {
            if (mcpServerBuilder == null)
                throw new ArgumentNullException(nameof(mcpServerBuilder));

            if (dataArguments == null)
                throw new ArgumentNullException(nameof(dataArguments));

            var reflector = new Reflector();

            signalRConfigure ??= configure =>
            {
                configure.EnableDetailedErrors = false;
                // 128 MB per-message cap. This is an upper bound, not a pre-allocation, so it is
                // cheap to keep generous; it must comfortably exceed a screenshot tool result
                // (image base64 inside a JSON envelope), which a 4 MB cap silently dropped for
                // high-resolution Game View captures. Plugin-side capture tools clamp their own
                // resolution to keep real payloads far below this ceiling.
                configure.MaximumReceiveMessageSize = 1024 * 1024 * 128; // 128 MB
                configure.ClientTimeoutInterval = TimeSpan.FromMinutes(5);
                configure.KeepAliveInterval = TimeSpan.FromSeconds(30);
                configure.HandshakeTimeout = TimeSpan.FromMinutes(2);
            };

            version ??= new Common.Version
            {
                Api = Consts.ApiVersion,
                Plugin = Consts.PluginVersion
            };

            mcpServerBuilder.Services
                .AddSignalR(signalRConfigure)
                .AddJsonProtocol(options => SignalR_JsonConfiguration.ConfigureJsonSerializer(reflector, options));

            mcpServerBuilder.Services.AddSingleton<IDataArguments>(dataArguments);
            mcpServerBuilder.Services.AddSingleton(version);

            var setup = GetMcpServerSetup(mcpServerBuilder.Services);

            // Configure authentication using the connection strategy
            mcpServerBuilder.Services.AddAuthentication(TokenAuthenticationHandler.SchemeName)
                .AddScheme<TokenAuthenticationOptions, TokenAuthenticationHandler>(
                    authenticationScheme: TokenAuthenticationHandler.SchemeName,
                    options =>
                    {
                        // The resolved strategy owns auth configuration (none → anonymous,
                        // oauth → OAuthMode). setup is always registered by WithMcpServer; the
                        // fallback stays anonymous/fail-closed (the legacy shared-token default was
                        // removed in mcp-authorize b5 — a token never gates the endpoint on its own).
                        if (setup != null)
                            setup.Strategy.ConfigureAuthentication(options, dataArguments);
                        else
                            options.OAuthMode = false;
                    });
            mcpServerBuilder.Services.AddAuthorization();

            // OAuth resource-server services (mcp-authorize b2) — registered only in oauth mode.
            // The TokenAuthenticationHandler's optional IOAuthTokenValidator/OAuthResourceServerConfig
            // ctor params resolve from these; in legacy modes they stay null (unchanged behavior).
            if (dataArguments.Authorization == Consts.MCP.Server.AuthOption.oauth)
            {
                mcpServerBuilder.Services.AddHttpClient();

                var oauthConfig = new OAuthResourceServerConfig(dataArguments.AuthIssuer!, dataArguments.PublicUrl!, metadataUrl: dataArguments.AuthMetadataUrl);
                mcpServerBuilder.Services.AddSingleton(oauthConfig);
                mcpServerBuilder.Services.AddSingleton<IJwksDiskCache>(_ => new FileJwksDiskCache());

                mcpServerBuilder.Services.AddSingleton<IJwksKeyProvider>(sp =>
                {
                    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                    var cache = sp.GetRequiredService<IJwksDiskCache>();
                    JwksFetch fetch = async ct =>
                    {
                        var client = httpFactory.CreateClient();
                        client.Timeout = TimeSpan.FromSeconds(10);
                        using var response = await client.GetAsync(oauthConfig.JwksUri, ct);
                        if (!response.IsSuccessStatusCode)
                            return null;
                        return await response.Content.ReadAsStringAsync(ct);
                    };
                    return new JwksKeyProvider(fetch, cache, logger: sp.GetService<ILogger<JwksKeyProvider>>());
                });

                mcpServerBuilder.Services.AddSingleton<IIntrospectionClient>(sp =>
                {
                    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                    IntrospectionPost post = async (token, ct) =>
                    {
                        var client = httpFactory.CreateClient();
                        client.Timeout = TimeSpan.FromSeconds(10);
                        var body = new FormUrlEncodedContent(new[]
                        {
                            new KeyValuePair<string, string>("token", token)
                        });
                        using var response = await client.PostAsync(oauthConfig.IntrospectionEndpoint, body, ct);
                        if (!response.IsSuccessStatusCode)
                            return null;
                        return await response.Content.ReadAsStringAsync(ct);
                    };
                    return new IntrospectionClient(post, logger: sp.GetService<ILogger<IntrospectionClient>>());
                });

                mcpServerBuilder.Services.AddSingleton<IOAuthTokenValidator>(sp => new AccessTokenValidator(
                    sp.GetRequiredService<OAuthResourceServerConfig>(),
                    sp.GetRequiredService<IJwksKeyProvider>(),
                    sp.GetRequiredService<IIntrospectionClient>(),
                    logger: sp.GetService<ILogger<AccessTokenValidator>>()));

                // Server-native tools (mcp-authorize b4) — the account+instance selection + enrollment
                // surface. Only meaningful in oauth mode (the pairing plane), so registered here.
                mcpServerBuilder.Services.AddSingleton<ISessionSelectionStore, SessionSelectionStore>();

                mcpServerBuilder.Services.AddSingleton<IEnrollmentClient>(sp =>
                {
                    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                    var config = sp.GetRequiredService<OAuthResourceServerConfig>();
                    // Enroll (like JWKS + introspection) uses the server-side fetch base, which honors
                    // the optional --auth-metadata-url override; unset → derived from Issuer (auth-fixes L2a).
                    var enrollEndpoint = config.EnrollmentEndpoint;
                    EnrollCreatePost post = async (bearer, engine, publicUrl, ct) =>
                    {
                        var client = httpFactory.CreateClient();
                        client.Timeout = TimeSpan.FromSeconds(10);
                        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, enrollEndpoint);
                        // Forward the session credential verbatim; NEVER log it.
                        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                        var payload = JsonSerializer.Serialize(new Dictionary<string, string>
                        {
                            ["engine"] = engine,
                            ["public_url"] = publicUrl
                        });
                        httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                        using var httpResponse = await client.SendAsync(httpRequest, ct);
                        if (!httpResponse.IsSuccessStatusCode)
                            return null;
                        return await httpResponse.Content.ReadAsStringAsync(ct);
                    };
                    return new EnrollmentClient(post, dataArguments.PublicUrl!, logger: sp.GetService<ILogger<EnrollmentClient>>());
                });

                mcpServerBuilder.Services.AddSingleton<ServerNativeTools>(sp =>
                {
                    // In oauth mode the resolved IMcpConnectionStrategy is always the AccountMcpStrategy,
                    // whose registry backs the selection tools.
                    var strategy = sp.GetRequiredService<IMcpConnectionStrategy>() as AccountMcpStrategy
                        ?? throw new InvalidOperationException("ServerNativeTools requires the oauth AccountMcpStrategy.");
                    return new ServerNativeTools(
                        strategy.Instances,
                        sp.GetRequiredService<ISessionSelectionStore>(),
                        sp.GetRequiredService<IEnrollmentClient>(),
                        logger: sp.GetService<ILogger<ServerNativeTools>>());
                });
            }

            // Origin validation options (mcp-authorize b2) — registered in ALL modes; consumed by
            // OriginValidationMiddleware (wired in ExtensionsWebApplication.UseMcpPluginServer).
            mcpServerBuilder.Services.AddSingleton(OriginValidationOptions.FromArguments(dataArguments));

            mcpServerBuilder.Services.AddRouting();

            // Configure transport-specific services
            if (setup != null)
                setup.Transport.ConfigureServices(mcpServerBuilder.Services, dataArguments);
            else if (dataArguments.ClientTransport == Consts.MCP.Server.TransportMethod.stdio)
                mcpServerBuilder.Services.AddHostedService<McpServerService>();

            mcpServerBuilder.Services.AddSingleton<HubEventToolsChange>();
            mcpServerBuilder.Services.AddSingleton<HubEventPromptsChange>();
            mcpServerBuilder.Services.AddSingleton<HubEventResourcesChange>();
            mcpServerBuilder.Services.AddSingleton<IRequestTrackingService, RequestTrackingService>();
            mcpServerBuilder.Services.AddSingleton<IMcpSessionTracker, McpSessionTracker>();
            mcpServerBuilder.Services.AddSingleton<McpGracefulShutdownService>();
            mcpServerBuilder.Services.AddHostedService(sp => sp.GetRequiredService<McpGracefulShutdownService>());

            mcpServerBuilder.Services.AddWebhooks(dataArguments);

            mcpServerBuilder.Services.AddSingleton<RemoteToolRunner>();
            mcpServerBuilder.Services.AddSingleton<IClientToolHub>(sp => sp.GetRequiredService<RemoteToolRunner>());

            mcpServerBuilder.Services.AddSingleton<RemoteSystemToolRunner>();
            mcpServerBuilder.Services.AddSingleton<IClientSystemToolHub>(sp => sp.GetRequiredService<RemoteSystemToolRunner>());

            mcpServerBuilder.Services.AddSingleton<RemotePromptRunner>();
            mcpServerBuilder.Services.AddSingleton<IClientPromptHub>(sp => sp.GetRequiredService<RemotePromptRunner>());

            mcpServerBuilder.Services.AddSingleton<RemoteResourceRunner>();
            mcpServerBuilder.Services.AddSingleton<IClientResourceHub>(sp => sp.GetRequiredService<RemoteResourceRunner>());

            return mcpServerBuilder;
        }
    }
}
