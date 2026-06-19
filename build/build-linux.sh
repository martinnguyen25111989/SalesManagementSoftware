#!/usr/bin/env bash
# Publish Pos.Client.UI cho Linux (linux-x64), self-contained.
set -euo pipefail
RID="${1:-linux-x64}"
OUT="dist/$RID"
echo "Publishing Pos.Client.UI ($RID) → $OUT"
dotnet publish src/Pos.Client.UI -c Release -r "$RID" --self-contained true -o "$OUT"
echo "Done: $OUT"
