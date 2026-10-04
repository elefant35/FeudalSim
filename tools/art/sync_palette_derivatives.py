"""Verify and manifest Godot's extracted M2 palette textures.

Run after Godot import using a Python runtime with Pillow and PyYAML. These
textures have identical pixels to the owned shared palette; no new art is made.
Existing different images fail rather than silently being replaced.
"""
import json
from pathlib import Path

from PIL import Image
import yaml

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / "content/assets/palette_engine_m2.yaml"


def main():
    palette = ROOT / "art/palettes/palette.png"
    reference = Image.open(palette).convert("RGBA")
    outputs = set()
    missing = []
    for manifest in sorted((ROOT / "content/assets").glob("*.yaml")):
        if manifest == MANIFEST:
            continue
        for entry in yaml.safe_load(manifest.read_text()) or []:
            if entry.get("milestone") != "M2" or entry.get("kind") != "model":
                continue
            for output in entry["outputs"]:
                model = ROOT / output
                if model.suffix != ".glb":
                    continue
                texture = model.with_name(model.stem + "_palette.png")
                if not texture.exists():
                    missing.append(str(texture.relative_to(ROOT)))
                    continue
                actual = Image.open(texture).convert("RGBA")
                if actual.size != reference.size or actual.tobytes() != reference.tobytes():
                    raise ValueError(f"Extracted palette differs: {texture}")
                outputs.add(str(texture.relative_to(ROOT)))
    if missing:
        raise ValueError("Run Godot import first; missing textures: " + ", ".join(missing))
    entry = dict(
        id="asset.palette.engine_copies_m2", kind="texture", status="review", milestone="M2",
        source=dict(type="generator", generator="tools/art/sync_palette_derivatives.py",
                    params=dict(palette="art/palettes/palette.png"), tool="Godot 4.7.2 glTF import"),
        outputs=sorted(outputs), measured=dict(tris_lod0=0, materials=0),
        license=dict(name="Proprietary-own-work", author="FeudalSim", attribution_required=False),
        notes="Godot-extracted 256² RGBA palette copies. Every decoded pixel verified identical to the shared owned palette; retained beside GLBs for clean-checkout material imports.")
    MANIFEST.write_text(json.dumps([entry], indent=2) + "\n")
    print(f"Verified and manifested {len(outputs)} exact palette copies")


if __name__ == "__main__":
    main()
