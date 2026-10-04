#!/usr/bin/env bash
# M2-FP5: build and launch the first playable (the island, first person). Extra arguments go to the game after `--`,
# e.g. `tools/play.sh --template` (no AI calls), `tools/play.sh --third-person`, `tools/play.sh --scenario m1_view`.
set -euo pipefail
cd "$(dirname "$0")/.."
GODOT="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
[ -x "$GODOT" ] || { echo "Godot 4.7.2 .NET not found at $GODOT (set GODOT=...)"; exit 1; }
git lfs pull --include "game/assets/**" >/dev/null 2>&1 || echo "note: git lfs pull failed; using the assets already checked out"
[ -d game/addons/terrain_3d/bin ] || tools/godot/fetch_addons.sh
echo "building the game…"
dotnet build game/FeudalSim.Game.csproj -v q -nologo | grep -E "error|Error\(s\)" || true
echo "importing assets (first run after new art takes a minute)…"
"$GODOT" --headless --path game --import >/dev/null 2>&1 || "$GODOT" --headless --path game --import >/dev/null 2>&1 || true
echo "launching — the first launch generates the island (≈ 100 s), later launches load it from the cache"
exec "$GODOT" --path game -- "$@"
