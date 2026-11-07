#!/usr/bin/env bash
#
# Builds the Windows desktop program: ONE .exe file, nothing to install.
#
# The result is self-contained - the .NET runtime, the database engine and the whole
# user interface are inside that single file. Copy it to any 64-bit Windows PC and
# double-click it.
#
# Usage:  ./build/build-windows.sh
# Output: dist/TenPercentStockManager.exe

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/PharmacyStock.Desktop/PharmacyStock.Desktop.csproj"
OUT="$ROOT/dist"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

echo "Checking the engine before building..."
dotnet run --project "$ROOT/tests/PharmacyStock.SelfCheck" -c Release

echo
echo "Building the Windows program..."
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
  --output "$OUT"

# Nothing but the single .exe should be handed to the pharmacy.
find "$OUT" -type f ! -name 'TenPercentStockManager.exe' -delete

EXE="$OUT/TenPercentStockManager.exe"
if [[ ! -f "$EXE" ]]; then
  echo "FAILED: $EXE was not produced." >&2
  exit 1
fi

echo
echo "Done."
echo "  File: $EXE"
echo "  Size: $(du -h "$EXE" | cut -f1)"
echo "  Type: $(file -b "$EXE")"
echo
echo "Copy that single file to the Windows PC and double-click it."
