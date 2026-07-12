<#
.SYNOPSIS
    mcp-authorize Phase-2 gate (task b8) — runnable isolation/selection/pin/Origin/stdio matrix
    plus cross-language golden-vector parity, invoked as a single command locally or in CI.

.DESCRIPTION
    Proves McpPlugin 7.0 end-to-end before the host (c1) and engines (Phase 4) consume it:
      * live multi-tenant isolation matrix (2 accounts x 2 instances x 2 sessions) with
        leak-detection — the harness FAILS if cross-account routing/notification leakage is
        introduced (Phase2Gate* tests in McpPlugin.Server.Tests).
      * cross-language golden-vector parity — the C# ProjectIdentity reference vectors the TS
        consumers must reproduce at Phase 4 (ProjectIdentityGoldenVector* in McpPlugin.Tests).

    Hermetic: no external authorization server, no multi-process orchestration — a stable CI gate.

.PARAMETER NoBuild
    Reuse an existing Release build instead of restoring + rebuilding.

.EXAMPLE
    ./scripts/phase2-gate-matrix.ps1
.EXAMPLE
    ./scripts/phase2-gate-matrix.ps1 -NoBuild
#>
[CmdletBinding()]
param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$Configuration = 'Release'

$RepoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $RepoRoot
try
{
    if (-not $NoBuild)
    {
        Write-Host "== restore + build ($Configuration) =="
        dotnet restore
        if ($LASTEXITCODE -ne 0) { throw "restore failed" }
        dotnet build --no-restore --configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw "build failed" }
    }

    Write-Host ""
    Write-Host "== Phase-2 isolation/selection/pin/Origin/stdio matrix (McpPlugin.Server.Tests) =="
    dotnet test McpPlugin.Server.Tests/McpPlugin.Server.Tests.csproj `
        --no-build --configuration $Configuration --verbosity normal `
        --filter "FullyQualifiedName~IsolationMatrix"
    if ($LASTEXITCODE -ne 0) { throw "isolation matrix failed" }

    Write-Host ""
    Write-Host "== Golden-vector parity: C# ProjectIdentity reference (McpPlugin.Tests) =="
    dotnet test McpPlugin.Tests/McpPlugin.Tests.csproj `
        --no-build --configuration $Configuration --verbosity normal `
        --filter "FullyQualifiedName~ProjectIdentityGoldenVector"
    if ($LASTEXITCODE -ne 0) { throw "golden-vector parity failed" }

    Write-Host ""
    Write-Host "== Phase-2 gate: PASS =="
}
finally
{
    Pop-Location
}
