#!/usr/bin/env python3
"""Build the shared palette (32-art-and-audio-production §4): art/palettes/palette.png + palette.json.

8 x 8 swatches of 32 x 32 px with a subtle vertical gradient (top +8%, bottom -8%). Models map their
faces' UVs onto swatch centres, so one 256 x 256 texture colours almost every asset in the game.
Pure standard library (zlib PNG writer) so it runs anywhere. Re-run after editing ROWS.
"""
import json, struct, zlib, pathlib

ROWS = [
    ("earth", [("soil_dark", "3b2a1e"), ("soil", "5a4130"), ("sand", "c8b48a"), ("clay", "9c6a4a"),
               ("bark_dark", "3f2f24"), ("bark", "6b4f3a"), ("wood_light", "b08a5e"), ("straw", "d4b86a")]),
    ("foliage", [("pine_dark", "1f3a2a"), ("pine", "2f5a3a"), ("leaf_dark", "2e4f22"), ("leaf", "4f7a2e"),
                 ("leaf_light", "7fa24a"), ("grass", "6f8f3a"), ("moss", "5a6b2e"), ("autumn", "b8642a")]),
    ("stone_metal", [("granite_dark", "4a4a4e"), ("granite", "7a7a80"), ("limestone", "c2bca8"), ("flint", "3a3a42"),
                     ("iron", "5a5c62"), ("steel", "9aa0a8"), ("copper", "b06a3a"), ("bronze", "a07a3a")]),
    ("skin_hair", [("skin_1", "f1d1b8"), ("skin_2", "e0b48f"), ("skin_3", "c48a62"), ("skin_4", "8e5a3c"),
                   ("skin_5", "5a3826"), ("hair_dark", "2a201a"), ("hair_brown", "6a4628"), ("hair_fair", "c8a46a")]),
    ("cloth", [("linen", "d8cfb8"), ("wool_grey", "8a8680"), ("wool_brown", "6a5440"), ("russet", "8a4a2e"),
               ("ochre", "c0902e"), ("woad", "3a5a8a"), ("madder", "9a2e2a"), ("weld_green", "5a7a3a")]),
    ("culture", [("varrow_blue", "24386a"), ("varrow_gold", "c8a02e"), ("osmeri_teal", "2a7a7a"), ("osmeri_cream", "e8dcc0"),
                 ("brannoch_green", "2e5a3a"), ("brannoch_rust", "9a4a22"), ("ashen_grey", "6e6a66"), ("ashen_ember", "d2622a")]),
    ("water_sky", [("water_deep", "1e3a52"), ("water", "2e5a72"), ("foam", "e6eef0"), ("ice", "bcd8e4"),
                   ("snow", "f2f4f6"), ("sky", "9ec4e0"), ("fog", "c8ccd0"), ("night", "16202e")]),
    ("special", [("fire_core", "fff0b0"), ("fire", "f28a2a"), ("ember", "c03a1a"), ("blood", "6a1414"),
                 ("ink", "1a1612"), ("parchment", "e8d8b0"), ("highlight", "ffffff"), ("debug", "ff00ff")]),
]
CELL, GRID = 32, 8


def png(width, height, rows):
    raw = b"".join(b"\x00" + bytes(r) for r in rows)
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def main():
    root = pathlib.Path(__file__).resolve().parents[2]
    size = CELL * GRID
    pixels = [[0] * (size * 3) for _ in range(size)]
    swatches = []
    for r, (row_name, cells) in enumerate(ROWS):
        for c, (name, hexcode) in enumerate(cells):
            base = [int(hexcode[i:i + 2], 16) for i in (0, 2, 4)]
            for y in range(CELL):
                shade = 1.08 - 0.16 * (y / (CELL - 1))
                rgb = [max(0, min(255, round(v * shade))) for v in base]
                for x in range(CELL):
                    px, py = c * CELL + x, r * CELL + y
                    pixels[py][px * 3:px * 3 + 3] = rgb
            swatches.append({"name": name, "row": row_name, "r": r, "c": c, "hex": "#" + hexcode,
                             "u": (c + 0.5) / GRID, "v": 1 - (r + 0.5) / GRID})   # v: Blender/glTF UV origin bottom-left
    out = root / "art" / "palettes"
    out.mkdir(parents=True, exist_ok=True)
    (out / "palette.png").write_bytes(png(size, size, pixels))
    (out / "palette.json").write_text(json.dumps({"size": size, "grid": GRID, "swatches": swatches}, indent=1) + "\n")
    print(f"RESULT palette swatches={len(swatches)} png={out / 'palette.png'}")


if __name__ == "__main__":
    main()
