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

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// The outcome of <see cref="MachineCredentialStore.TryRead"/>. Distinguishes the three states
    /// the store contract requires (design 04 §1): no store at all (<see cref="MachineCredentialStoreStatus.Missing"/>),
    /// a healthy credential (<see cref="MachineCredentialStoreStatus.Ok"/>), and — critically — a
    /// store that <b>exists but cannot be read</b> (<see cref="MachineCredentialStoreStatus.Unreadable"/>:
    /// DPAPI unprotect failure after a password reset / roaming profile / service account, or a
    /// corrupted document). An unreadable store is <b>not</b> an empty store: callers surface
    /// "sign in required" and must never crash, delete, or overwrite it until the user explicitly
    /// re-authorizes.
    /// </summary>
    public sealed class MachineCredentialReadResult
    {
        MachineCredentialReadResult(MachineCredentialStoreStatus status, MachineCredentials? credentials, string? error)
        {
            Status = status;
            Credentials = credentials;
            Error = error;
        }

        /// <summary>The read outcome.</summary>
        public MachineCredentialStoreStatus Status { get; }

        /// <summary>The credentials; non-null exactly when <see cref="Status"/> is <see cref="MachineCredentialStoreStatus.Ok"/>.</summary>
        public MachineCredentials? Credentials { get; }

        /// <summary>
        /// Diagnostic detail when <see cref="Status"/> is <see cref="MachineCredentialStoreStatus.Unreadable"/>.
        /// Never contains token material.
        /// </summary>
        public string? Error { get; }

        /// <summary>No credential file exists.</summary>
        public static MachineCredentialReadResult Missing()
            => new MachineCredentialReadResult(MachineCredentialStoreStatus.Missing, null, null);

        /// <summary>The store was read and decoded successfully.</summary>
        public static MachineCredentialReadResult Ok(MachineCredentials credentials)
            => new MachineCredentialReadResult(
                MachineCredentialStoreStatus.Ok,
                credentials ?? throw new ArgumentNullException(nameof(credentials)),
                null);

        /// <summary>The store exists but could not be read (see <paramref name="error"/>).</summary>
        public static MachineCredentialReadResult Unreadable(string error)
            => new MachineCredentialReadResult(MachineCredentialStoreStatus.Unreadable, null, error);
    }

    /// <summary>The three states of a machine credential store read (see <see cref="MachineCredentialReadResult"/>).</summary>
    public enum MachineCredentialStoreStatus
    {
        /// <summary>No credential file exists (signed out / never signed in).</summary>
        Missing = 0,

        /// <summary>The store was read and decoded successfully.</summary>
        Ok = 1,

        /// <summary>
        /// The store exists but cannot be read (DPAPI unprotect failure, corrupted or truncated
        /// document). Surface "sign in required"; never crash, never delete, never overwrite until
        /// the user explicitly re-authorizes.
        /// </summary>
        Unreadable = 2,
    }
}
