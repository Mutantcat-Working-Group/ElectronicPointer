<#
.SYNOPSIS
    Publishes ElectronicPointer for one runtime identifier.
.DESCRIPTION
    Thin wrapper around "dotnet publish" so every platform script, and every CI job, uses
    the same build configuration. The version is stamped into the assemblies and read back
    at runtime by AppIdentity, so the binary, the package names and the about box agree.

    Compatible with Windows PowerShell 5.1 so a Windows user can run this without installing
    anything extra; on Linux and macOS invoke it with pwsh.

.PARAMETER Rid
    One of win-x64, win-x86, win-arm64, osx-x64, osx-arm64, linux-x64, linux-arm64.
.PARAMETER DateStamp
    yyyyMMdd. Defaults to today, which is what makes local builds work; pass an explicit
    stamp to reproduce a release.
.PARAMETER OutputRoot
    Where the publish output lands. Defaults to <repo>/artifacts/out.
>
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'win-x86', 'win-arm64', 'osx-x64', 'osx-arm64', 'linux-x64', 'linux-arm64')]
    [string]$Rid,

    [string]$DateStamp,

    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($DateStamp)) {
    $DateStamp = (Get-Date).ToString('yyyyMMdd')
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $root 'artifacts\out'
}

$project = Join-Path $root 'src\ElectronicPointer.App\ElectronicPointer.App.csproj'
$output = Join-Path $OutputRoot $Rid

if (-not (Test-Path $project)) {
    throw "Project not found: $project"
}

Write-Host "publishing ElectronicPointer $Rid version=1.0.$DateStamp"

# The SDK stamps AssemblyVersion as 1.0.0.0 and the informational version as 1.0.<stamp>;
# AppIdentity reads both back at runtime. -p:DateStamp keeps a release byte reproducible.
dotnet publish $project `
    --configuration Release `
    --runtime $Rid `
    --self-contained true `
    --output $output `
    -p:DateStamp=$DateStamp `
    --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# Handed to the platform scripts so the deb control file, the AppImage name, the .app bundle
# and the installer name all carry the same stamp without re-deriving it.
$stampFile = Join-Path $output 'buildstamp.txt'
Set-Content -LiteralPath $stampFile -Value "1.0.$DateStamp" -Encoding utf8 -NoNewline

Write-Host "published to $output (build stamp 1.0.$DateStamp)"
