#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Updates ReflectorNet package to the latest version from NuGet

.DESCRIPTION
    Updates com.IvanMurzak.ReflectorNet package across all project files
    to the latest available version on NuGet.

.PARAMETER WhatIf
    Preview which projects would be updated without applying changes

.EXAMPLE
    .\update-reflectornet.ps1

.EXAMPLE
    .\update-reflectornet.ps1 -WhatIf
#>

param(
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

$PackageName = "com.IvanMurzak.ReflectorNet"

# Project files to update (relative to script root)
$ProjectFiles = @(
    "/../McpPlugin.Common/McpPlugin.Common.csproj",
    "/../McpPlugin/McpPlugin.csproj",
    "/../McpPlugin.Server/McpPlugin.Server.csproj"
)

function Write-ColorText {
    param([string]$Text, [string]$Color = "White")
    Write-Host $Text -ForegroundColor $Color
}

function Get-CurrentPackageVersion {
    param([string]$ProjectPath)

    if (-not (Test-Path $ProjectPath)) {
        return $null
    }

    $content = Get-Content $ProjectPath -Raw
    if ($content -match "PackageReference Include=`"$PackageName`" Version=`"([^`"]+)`"") {
        return $Matches[1]
    }
    return $null
}

# Main execution
try {
    Write-ColorText "🔄 ReflectorNet Package Update Script" "Cyan"
    Write-ColorText "======================================" "Cyan"
    Write-ColorText "Package: $PackageName`n" "White"

    # Get current versions
    Write-ColorText "📋 Current versions:" "Cyan"
    foreach ($project in $ProjectFiles) {
        $fullPath = Join-Path $PSScriptRoot $project
        $version = Get-CurrentPackageVersion -ProjectPath $fullPath
        $projectName = Split-Path $project -Leaf
        if ($version) {
            Write-ColorText "   $projectName : $version" "Gray"
        } else {
            Write-ColorText "   $projectName : not found" "Yellow"
        }
    }

    if ($WhatIf) {
        Write-ColorText "`n📋 Preview mode - would update the following projects:" "Cyan"
        foreach ($project in $ProjectFiles) {
            $fullPath = Join-Path $PSScriptRoot $project
            if (Test-Path $fullPath) {
                Write-ColorText "   $project" "Gray"
            }
        }
        Write-ColorText "`n✅ Preview completed. Run without -WhatIf to apply changes." "Green"
        exit 0
    }

    # Clear local NuGet cache
    Write-ColorText "`n🧹 Clearing local NuGet cache..." "Cyan"
    $cacheResult = dotnet nuget locals all --clear 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-ColorText "   ✅ Cache cleared successfully" "Green"
    } else {
        Write-ColorText "   ⚠️  Cache clear warning: $cacheResult" "Yellow"
    }

    Write-ColorText "`n🚀 Updating to latest version..." "Cyan"

    foreach ($project in $ProjectFiles) {
        $fullPath = Join-Path $PSScriptRoot $project
        $projectName = Split-Path $project -Leaf

        if (-not (Test-Path $fullPath)) {
            Write-ColorText "⚠️  Project not found: $project" "Yellow"
            continue
        }

        Write-ColorText "📦 Updating $projectName..." "White"

        $result = dotnet add $fullPath package $PackageName 2>&1

        if ($LASTEXITCODE -eq 0) {
            $newVersion = Get-CurrentPackageVersion -ProjectPath $fullPath
            Write-ColorText "   ✅ Updated to $newVersion" "Green"
        } else {
            Write-ColorText "   ❌ Failed to update: $result" "Red"
        }
    }

    Write-ColorText "`n🎉 ReflectorNet update completed!" "Green"
    Write-ColorText "💡 Remember to commit these changes to git" "Cyan"
}
catch {
    Write-ColorText "`n❌ Script failed: $($_.Exception.Message)" "Red"
    exit 1
}
