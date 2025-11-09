/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

namespace com.IvanMurzak.McpPlugin.Common
{
    /// <summary>
    /// Thread-safe boolean variable using a simple lock-based approach.
    /// Provides atomic read and write operations for boolean values.
    /// </summary>
    public class ThreadSafeBool
    {
        private bool _value;
        private readonly object _lock = new object();

        public ThreadSafeBool(bool initialValue = false)
        {
            _value = initialValue;
        }

        /// <summary>
        /// Gets or sets the value in a thread-safe manner.
        /// </summary>
        public bool Value
        {
            get
            {
                lock (_lock)
                {
                    return _value;
                }
            }
        }

        /// <summary>
        /// Attempts to set the value from false to true atomically.
        /// </summary>
        /// <returns>True if the value was changed from false to true, false if it was already true.</returns>
        public bool TrySetTrue()
        {
            lock (_lock)
            {
                if (_value)
                    return false;

                _value = true;
                return true;
            }
        }

        /// <summary>
        /// Attempts to set the value from true to false atomically.
        /// </summary>
        /// <returns>True if the value was changed from true to false, false if it was already false.</returns>
        public bool TrySetFalse()
        {
            lock (_lock)
            {
                if (!_value)
                    return false;

                _value = false;
                return true;
            }
        }

        /// <summary>
        /// Implicitly converts ThreadSafeBool to bool for easier usage.
        /// </summary>
        public static implicit operator bool(ThreadSafeBool threadSafeBool)
        {
            return threadSafeBool.Value;
        }
    }
}
