<#
.SYNOPSIS
    Builds, tests and publishes FPS.LOL.
.EXAMPLE
    .\build.ps1                 # Release build + tests + self-contained win-x64 publish
    .\build.ps1 -SkipTests
    .\build.ps1 -UnitTestsOnly  # skip tests that touch real system settings
#>
param(
    [switch]$SkipTests,
    [switch]$UnitTestsOnly
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

# Use a per-user SDK installation when dotnet is not on PATH.
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet -and (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe")) {
    $dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
    $env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
}
if (-not $dotnet) { throw ".NET 8 SDK not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

Write-Host "== Build (Release) ==" -ForegroundColor Cyan
& $dotnet build FPS.LOL.sln -c Release -nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

if (-not $SkipTests) {
    Write-Host "== Tests ==" -ForegroundColor Cyan
    $filter = if ($UnitTestsOnly) { @("--filter", "Category!=System") } else { @() }
    & $dotnet test tests\FpsLol.Tests\FpsLol.Tests.csproj -c Release --no-build -nologo @filter
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

Write-Host "== Publish (self-contained win-x64) ==" -ForegroundColor Cyan
& $dotnet publish src\FpsLol\FpsLol.csproj -p:PublishProfile=win-x64 -nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Write-Host "Done: publish\win-x64\FPS.LOL.exe" -ForegroundColor Green
