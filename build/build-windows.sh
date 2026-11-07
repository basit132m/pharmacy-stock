#!/usr/bin/env bash
#
# Builds the Windows program: ONE .exe file, nothing to install.
#
# The result is self-contained - the .NET runtime, the web server, the database
# engine and the whole user interface are all inside that single file. Copy it to
# any 64-bit Windows PC and double-click it.
#
# Usage:  ./build/build-windows.sh
# Output: dist/TenPercentStockManager.exe

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/PharmacyStock.App/PharmacyStock.App.csproj"
OUT="$ROOT/dist"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

echo "Building Windows executable..."
rm -rf "$OUT"
mkdir -p "$OUT"

dotnet publish "$PROJECT" \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none \
  -p:GenerateDocumentationFile=false \
  --output "$OUT"

# Nothing but the single .exe should be handed to the pharmacy.
rm -f "$OUT"/*.pdb "$OUT"/web.config

EXE="$OUT/TenPercentStockManager.exe"
if [[ ! -f "$EXE" ]]; then
  echo "FAILED: $EXE was not produced." >&2
  exit 1
fi

SIZE="$(du -h "$EXE" | cut -f1)"
echo
echo "Done."
echo "  File: $EXE"
echo "  Size: $SIZE"
echo
echo "Copy that single file to the Windows PC and double-click it."
