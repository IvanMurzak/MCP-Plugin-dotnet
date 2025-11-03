/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System.Threading;
using R3;

namespace com.IvanMurzak.McpPlugin.Common
{
    public static class ExtensionsCompositeDisposable
    {
        public static CancellationTokenSource ToCancellationTokenSource(this CompositeDisposable disposables)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            disposables.Add(cancellationTokenSource);
            return cancellationTokenSource;
        }
        public static CancellationToken ToCancellationToken(this CompositeDisposable disposables)
        {
            return ToCancellationTokenSource(disposables).Token;
        }
    }
}
