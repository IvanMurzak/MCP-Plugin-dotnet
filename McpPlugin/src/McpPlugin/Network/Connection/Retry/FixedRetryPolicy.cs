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
using Microsoft.AspNetCore.SignalR.Client;

namespace com.IvanMurzak.McpPlugin
{
    public class FixedRetryPolicy : IRetryPolicy
    {
        private readonly TimeSpan _delay;
        private readonly int? _maxRetries;

        public FixedRetryPolicy(TimeSpan delay, int? maxRetries = null)
        {
            _delay = delay;
            _maxRetries = maxRetries;
        }

        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            if (_maxRetries.HasValue && retryContext.PreviousRetryCount >= _maxRetries.Value)
                return null;
            return _delay;
        }
    }
}
