#requires -Version 7.0
<#
.SYNOPSIS
    Production launcher (Windows). Runs a previously published binary.
.DESCRIPTION
    Expects FoundryWebUI-X to have been published to .\publish (or $env:PUBLISH_DIR).
    Forwards any extra arguments to FoundryWebUI-X.
#>
$ErrorActionPreference = 'Stop'

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$PublishDir = if ($env:PUBLISH_DIR) { $env:PUBLISH_DIR } else { Join-Path $ProjectRoot 'publish' }
$Binary = Join-Path $PublishDir 'FoundryWebUI-X.exe'

if (-not (Test-Path $Binary)) {
    Write-Error @"
$Binary not found.
Run the following to publish first:
    dotnet publish FoundryWebUI-X.csproj -c Release -o publish
"@
}

Set-Location $PublishDir
& $Binary @args
exit $LASTEXITCODE
