#!/usr/bin/env bash
# Builds the Compass server for Linux into publish/server-linux-x64/.
#
# The plugin is not built here: it targets the Dalamud SDK, which resolves against a Windows
# XIVLauncher installation. Build that on the machine you play on, with publish.cmd.
#
# Usage:  ./publish.sh [runtime]     e.g. ./publish.sh linux-arm64

set -euo pipefail

cd "$(dirname "$0")"

RUNTIME="${1:-linux-x64}"
OUT="publish/server-$RUNTIME"

echo
echo "  Building the Compass server for $RUNTIME"
echo "  ----------------------------------------"
echo

rm -rf "$OUT"

# Framework-dependent: the target needs the ASP.NET Core 10 runtime installed. Add
# --self-contained true if you would rather ship the runtime with it.
dotnet publish src/Compass.Server \
    -c Release \
    -r "$RUNTIME" \
    --self-contained false \
    -o "$OUT" \
    --nologo

chmod +x "$OUT/Compass.Server"

echo
echo "  Done:  $(pwd)/$OUT"
echo
echo "  Read docs/DEPLOYMENT.md before running this anywhere public."
echo "  In particular: set an absolute Compass__DataDirectory, and put it behind TLS."
echo
