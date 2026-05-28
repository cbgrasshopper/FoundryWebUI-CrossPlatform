#!/usr/bin/env bash
# Development launcher (macOS / Linux). Runs the app from source via `dotnet run`.
# Forwards any extra arguments to FoundryWebUI-X (e.g. --port 8080, --no-browser).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$PROJECT_ROOT"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "error: 'dotnet' not found on PATH."
    echo "Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0"
    exit 1
fi

exec dotnet run --project FoundryWebUI-X.csproj -- "$@"
