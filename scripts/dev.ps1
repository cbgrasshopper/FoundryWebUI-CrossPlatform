#requires -Version 7.0
<#
.SYNOPSIS
    Development launcher (Windows). Runs the app from source via `dotnet run`.
.DESCRIPTION
    Forwards any extra arguments to FoundryWebUI-X (e.g. --port 8080, --no-browser).
#>
$ErrorActionPreference = 'Stop'

$ProjectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $ProjectRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "'dotnet' not found on PATH. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0"
}

& dotnet run --project FoundryWebUI-X.csproj -- @args
exit $LASTEXITCODE
