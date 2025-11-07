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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    public partial class McpPlugin : IMcpPlugin
    {
        readonly static ReactiveProperty<McpPlugin?> _instance = new(null);

        public static bool HasInstance => _instance.CurrentValue != null;
        public static IMcpPlugin? Instance => _instance.CurrentValue;

        public static IDisposable DoOnce(Action<IMcpPlugin> func) => _instance
            .Where(x => x != null)
            .Take(1)
            .ObserveOnCurrentSynchronizationContext()
            .SubscribeOnCurrentSynchronizationContext()
            .Subscribe(instance =>
            {
                if (instance == null)
                    return;
                if (func == null)
                {
                    instance._logger.LogWarning("{method} called with null func",
                        nameof(DoOnce));
                    return;
                }
                try
                {
                    func(instance);
                }
                catch (Exception e)
                {
                    instance._logger.LogError(e, "Error in {method}",
                        nameof(DoOnce));
                }
            });

        public static IDisposable DoAlways(Action<IMcpPlugin> func) => _instance
            .Where(x => x != null)
            .ObserveOnCurrentSynchronizationContext()
            .SubscribeOnCurrentSynchronizationContext()
            .Subscribe(instance =>
            {
                if (instance == null)
                    return;
                if (func == null)
                {
                    instance._logger.LogWarning("{method} called with null func",
                        nameof(DoAlways));
                    return;
                }
                try
                {
                    func(instance);
                }
                catch (Exception e)
                {
                    instance._logger.LogError(e, "Error in {method}",
                        nameof(DoAlways));
                }
            });

        public static void StaticDispose()
        {
            var instance = _instance.CurrentValue;
            if (instance == null)
                return;

            if (!_instance.IsDisposed)
            {
                _instance.Value = null;
                _instance.Dispose();
            }

            instance.DisconnectImmediate();
        }
    }
}
