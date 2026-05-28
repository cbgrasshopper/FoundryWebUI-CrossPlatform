#!/usr/bin/env bash
# Production launcher (macOS / Linux). Runs a previously published binary.
# Expects FoundryWebUI-X to have been published to ./publish (or PUBLISH_DIR).
# Forwards any extra arguments to FoundryWebUI-X.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PUBLISH_DIR="${PUBLISH_DIR:-$PROJECT_ROOT/publish}"
BINARY="$PUBLISH_DIR/FoundryWebUI-X"

if [[ ! -x "$BINARY" ]]; then
    echo "error: $BINARY not found or not executable."
    echo "Run the following to publish first:"
    echo "  dotnet publish FoundryWebUI-X.csproj -c Release -o publish"
    exit 1
fi

cd "$PUBLISH_DIR"
exec "./FoundryWebUI-X" "$@"
