#!/usr/bin/env bash
# Publish Pos.Client.UI cho macOS. Mặc định osx-x64 (máy Intel hiện tại).
# Dùng: ./build/build-macos.sh [osx-x64|osx-arm64]
set -euo pipefail
RID="${1:-osx-x64}"
OUT="dist/$RID"
echo "Publishing Pos.Client.UI ($RID) → $OUT"
dotnet publish src/Pos.Client.UI -c Release -r "$RID" --self-contained true -o "$OUT"
echo "Done: $OUT"
