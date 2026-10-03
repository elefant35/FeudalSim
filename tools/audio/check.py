#!/usr/bin/env python3
"""Audio checks (32 §12, §15). Usage: python3 tools/audio/check.py <file.wav>...

Checks naming (<bus>_<category>_<name>_<variant>.wav with bus sfx|amb|mus|vox|ui), 48 kHz, 16-bit,
mono for 3D buses (UI may be stereo), sample peak <= -1 dBFS, and reports integrated loudness (LUFS)
with bus targets for music (-18) and ambience (-24), +/- 1 LU. Prints RESULT {...}; exit 1 on failure.
"""
import json, pathlib, re, sys, wave
import numpy as np

sys.path.insert(0, str(pathlib.Path(__file__).parent))
from loudness import integrated_lufs  # noqa: E402

NAME = re.compile(r"^(sfx|amb|mus|vox|ui)_[a-z0-9]+_[a-z0-9_]+_\d{2}\.wav$")
TARGET_LUFS = {"mus": -18.0, "amb": -24.0}


def check(path):
    p = pathlib.Path(path)
    problems = []
    m = NAME.match(p.name)
    bus = m.group(1) if m else None
    if not m:
        problems.append("name must be <bus>_<category>_<name>_<NN>.wav (bus: sfx|amb|mus|vox|ui)")
    with wave.open(str(p), "rb") as w:
        fs, width, ch, n = w.getframerate(), w.getsampwidth(), w.getnchannels(), w.getnframes()
        raw = w.readframes(n)
    if fs != 48_000:
        problems.append(f"sample rate {fs} (need 48000)")
    if width != 2:
        problems.append(f"{width * 8}-bit (need 16-bit)")
    if ch != 1 and bus != "ui":
        problems.append(f"{ch} channels (3D buses must be mono)")
    data = np.frombuffer(raw, dtype="<i2").astype(np.float64) / 32768.0
    chans = [data[i::ch] for i in range(ch)]
    peak = float(np.abs(data).max()) if data.size else 0.0
    peak_db = 20 * np.log10(peak) if peak > 0 else float("-inf")
    if peak_db > -0.9:
        problems.append(f"peak {peak_db:.2f} dBFS (max -1 dBFS)")
    lufs = integrated_lufs(chans, fs) if fs == 48_000 and data.size else None
    if bus in TARGET_LUFS and lufs is not None and abs(lufs - TARGET_LUFS[bus]) > 1.0:
        problems.append(f"loudness {lufs:.1f} LUFS (target {TARGET_LUFS[bus]} +/- 1)")
    return {"file": str(p), "ok": not problems, "duration_s": round(n / fs, 3) if fs else None, "channels": ch,
            "peak_dbfs": round(peak_db, 2), "loudness_lufs": round(lufs, 1) if lufs not in (None, float("-inf")) else None,
            "problems": problems}


def main():
    results = [check(f) for f in sys.argv[1:]]
    ok = all(r["ok"] for r in results)
    print("RESULT " + json.dumps({"ok": ok, "files": results}, sort_keys=True))
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
