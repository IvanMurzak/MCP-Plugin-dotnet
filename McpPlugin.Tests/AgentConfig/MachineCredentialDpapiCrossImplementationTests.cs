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
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using com.IvanMurzak.McpPlugin.AgentConfig;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// Cross-implementation DPAPI parity (unified-machine-auth 04 §1, task x1, Windows CI leg only):
    /// proves the C# store's <c>CryptProtectData</c>/<c>CryptUnprotectData</c> P/Invoke codec
    /// (<see cref="MachineCredentialStore"/>) and cli-core's TS codec — which shells out to
    /// <c>powershell.exe</c> running <c>[System.Security.Cryptography.ProtectedData]</c> (see
    /// <c>machine-credentials.ts</c> <c>dpapiTransform</c>) — are byte-interoperable: a blob written
    /// by one is readable by the other, on the SAME machine/user (DPAPI's CurrentUser scope makes a
    /// cross-machine committed ciphertext vector meaningless — this is why the proof is a live
    /// round-trip, not a static golden file).
    ///
    /// <para>This test file reproduces the TS side's exact mechanism verbatim (the same PowerShell
    /// script <c>machine-credentials.ts</c> spawns) rather than shelling out to Node, so each repo's
    /// CI can prove interop hermetically without installing the sibling language's toolchain.</para>
    ///
    /// <para><b>Entropy plant (mandated, DoD):</b> DPAPI's optional entropy parameter must be
    /// <c>null</c> on BOTH sides — <see cref="MachineCredentialStore"/>'s <c>Protect</c>/<c>Unprotect</c>
    /// pass <c>IntPtr.Zero</c>, and the TS <c>dpapiTransform</c> script passes <c>$null</c>.
    /// <see cref="EntropyMismatch_TurnsTheCrossDecryptRed_BothDirections"/> proves a non-null entropy
    /// on EITHER side breaks the round trip — verified RED locally before this suite shipped (see the
    /// x1 task report for the exact mutation and failure output).</para>
    /// </summary>
    public sealed class MachineCredentialDpapiCrossImplementationTests
    {
        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private static readonly byte[] SamplePlaintext =
            Encoding.UTF8.GetBytes("{\"accessToken\":\"dpapi-cross-impl-AT\",\"refreshToken\":\"dpapi-cross-impl-RT\"}");

        /// <summary>
        /// Verbatim transcription of cli-core's <c>dpapiTransform</c> (machine-credentials.ts) — the
        /// REAL script the TS store spawns on Windows. Not a re-implementation of DPAPI: it is the
        /// TS codec's actual mechanism, run here so the C# repo can prove interop without a Node
        /// toolchain in CI.
        /// </summary>
        private static byte[] RunTsShapePowerShellDpapi(string action, byte[] input, byte[]? entropy)
        {
            var entropyExpr = entropy == null ? "$null" : "[Convert]::FromBase64String($env:AIGD_ENTROPY)";
            var script =
                "$ErrorActionPreference='Stop';" +
                "Add-Type -AssemblyName System.Security;" +
                "$in=[Convert]::FromBase64String($env:AIGD_IN);" +
                $"$out=[System.Security.Cryptography.ProtectedData]::{action}($in,{entropyExpr},[System.Security.Cryptography.DataProtectionScope]::CurrentUser);" +
                "[Convert]::ToBase64String($out)";

            var psi = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);
            psi.Environment["AIGD_IN"] = Convert.ToBase64String(input);
            if (entropy != null)
                psi.Environment["AIGD_ENTROPY"] = Convert.ToBase64String(entropy);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start powershell.exe");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(20_000).ShouldBeTrue("powershell.exe DPAPI helper timed out");

            if (process.ExitCode != 0)
                throw new CryptographicException($"powershell.exe DPAPI helper ({action}) failed (exit {process.ExitCode}): {stderr}");

            return Convert.FromBase64String(stdout.Trim());
        }

        [Fact]
        public void RoundTrip_CSharpProtect_ThenTsShapePowerShellUnprotect_RecoversPlaintext()
        {
            if (!IsWindows)
                return; // Windows-only assertion; exercised on the windows-latest CI leg.

            // Encrypt via the REAL C# codec path (CryptProtectData P/Invoke).
            var ciphertext = MachineCredentialStore.ProtectBytes(SamplePlaintext);

            // Decrypt via the TS codec's real mechanism (ProtectedData via powershell.exe).
            var recovered = RunTsShapePowerShellDpapi("Unprotect", ciphertext, entropy: null);

            recovered.ShouldBe(SamplePlaintext);
        }

        [Fact]
        public void RoundTrip_TsShapePowerShellProtect_ThenCSharpUnprotect_RecoversPlaintext()
        {
            if (!IsWindows)
                return; // Windows-only assertion; exercised on the windows-latest CI leg.

            // Encrypt via the TS codec's real mechanism (ProtectedData via powershell.exe).
            var ciphertext = RunTsShapePowerShellDpapi("Protect", SamplePlaintext, entropy: null);

            // Decrypt via the REAL C# codec path (CryptUnprotectData P/Invoke).
            var recovered = MachineCredentialStore.UnprotectBytes(ciphertext);

            recovered.ShouldBe(SamplePlaintext);
        }

        [Fact]
        public void EntropyMismatch_TurnsTheCrossDecryptRed_BothDirections()
        {
            if (!IsWindows)
                return; // Windows-only assertion; exercised on the windows-latest CI leg.

            var entropy = Encoding.UTF8.GetBytes("non-null-entropy-must-break-interop");

            // Direction 1: TS-shape encrypts WITH entropy; C#'s real Unprotect always passes null
            // entropy (matches production) — must fail, not silently recover different bytes.
            var tsCipherWithEntropy = RunTsShapePowerShellDpapi("Protect", SamplePlaintext, entropy);
            Should.Throw<CryptographicException>(() => MachineCredentialStore.UnprotectBytes(tsCipherWithEntropy));

            // Direction 2: C# encrypts with real (null-entropy) Protect; TS-shape decrypt WITH
            // entropy — must fail.
            var csharpCipher = MachineCredentialStore.ProtectBytes(SamplePlaintext);
            Should.Throw<CryptographicException>(() => RunTsShapePowerShellDpapi("Unprotect", csharpCipher, entropy));
        }
    }
}
