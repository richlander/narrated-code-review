#!/bin/bash
# Packs and installs agent-trace as a global tool from local source.
# Usage: ./install.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$SCRIPT_DIR/src/AgentTrace/AgentTrace.csproj"
PACK_OUTPUT="$SCRIPT_DIR/artifacts/packages"
PACKAGE_ID="AgentTrace"

echo "=== Installing agent-trace from source ==="

# Uninstall if already installed
if dotnet tool list -g | grep -q agent-trace; then
    echo "Uninstalling existing agent-trace..."
    dotnet tool uninstall -g "$PACKAGE_ID"
fi

# Clean previous packages
rm -rf "$PACK_OUTPUT"

# Pack
echo "Packing..."
dotnet pack "$PROJECT" -o "$PACK_OUTPUT"
dotnet pack "$PROJECT" -o "$PACK_OUTPUT" --ucr

# Install from local packages
echo "Installing..."
dotnet tool install -g "$PACKAGE_ID" --add-source "$PACK_OUTPUT"

echo ""
echo "Done. Run 'agent-trace --version' to verify."
