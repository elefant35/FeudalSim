#!/usr/bin/env bash
# M0-13 acceptance check (20 §20 step 13): run the Godot embodiment autotest headless, then replay its input log
# with the headless sim and require the same final state hash, sub-metre snaps, and a clean log (no ERROR/WARNING).
# Usage: tools/godot/embodiment_check.sh   (GODOT=/path/to/godot to override the binary)
set -euo pipefail
cd "$(dirname "$0")/../.."
GODOT="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
out="$(mktemp)"
trap 'rm -f "$out"' EXIT

"$GODOT" --headless --path game res://scenes/dev/EmbodimentSpike.tscn -- --autotest >"$out" 2>&1 || true
grep '^Embodiment:' "$out"

if grep -qE '^(ERROR|WARNING|SCRIPT ERROR)' "$out"; then
  echo "FAIL: Godot reported errors or warnings:"; grep -E '^(ERROR|WARNING|SCRIPT ERROR)' -A2 "$out"; exit 1
fi

final="$(sed -nE 's/^Embodiment: FINAL step ([0-9]+) hash ([0-9a-f]{16})$/\1 \2/p' "$out")"
log="$(sed -nE 's/^Embodiment: LOG (.+)$/\1/p' "$out")"
[[ -n "$final" && -n "$log" ]] || { echo "FAIL: autotest did not finish"; exit 1; }
read -r step hash <<<"$final"

embodied="$(grep -c 'Embodied {' "$out" || true)"
demoted="$(grep -c 'From = Lod0, To = Lod1' "$out" || true)"
maxsnap="$(sed -nE 's/.*SnapDistance = ([0-9.E+-]+).*/\1/p' "$out" | sort -g | tail -1)"
(( embodied >= 2 && demoted >= 1 )) || { echo "FAIL: expected embodiment, demotion and re-embodiment (embodied=$embodied demoted=$demoted)"; exit 1; }
awk -v s="$maxsnap" 'BEGIN { exit !(s < 1.0) }' || { echo "FAIL: max snap ${maxsnap} m ≥ 1 m"; exit 1; }

replay="$(dotnet run --project src/FeudalSim.Headless -c Release -v q -- replay --scenario content/scenarios/m0_embodiment.yaml --log "$log" --until-step "$step")"
echo "$replay"
[[ "$replay" == *"final hash $hash"* ]] || { echo "FAIL: replay hash differs from the Godot session ($hash)"; exit 1; }
echo "PASS: Godot session and headless replay agree at step $step (hash $hash); $embodied embodiments, $demoted demotions, max snap ${maxsnap} m"
