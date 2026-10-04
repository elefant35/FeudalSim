#!/usr/bin/env bash
# Fetch the pinned third-party Godot addons listed in tools/godot/addons.json into game/addons/.
# Each zip is checked against its SHA-256 before anything is extracted. Idempotent: an addon whose
# installed version marker matches is skipped. Usage: tools/godot/fetch_addons.sh [cache-dir]
set -euo pipefail
cd "$(dirname "$0")/../.."
cache="${1:-${XDG_CACHE_HOME:-$HOME/.cache}/feudalsim-addons}"
mkdir -p "$cache"

python3 - "$cache" <<'PY'
import hashlib, json, os, shutil, sys, tempfile, urllib.request, subprocess, zipfile
cache = sys.argv[1]
spec = json.load(open("tools/godot/addons.json"))
for name, a in spec.items():
    if name.startswith("_"):
        continue
    dest = os.path.join("game", a["extract"])
    marker = os.path.join(dest, ".fetched-version")
    if os.path.exists(marker) and open(marker).read().strip() == a["version"]:
        print(f"{name}: {a['version']} already installed")
        continue
    zpath = os.path.join(cache, os.path.basename(a["url"]))
    if not os.path.exists(zpath):
        print(f"{name}: downloading {a['url']}")
        # curl: the system Python on macOS often lacks CA certificates for urllib.
        subprocess.run(["curl", "-fsSL", "-o", zpath + ".part", a["url"]], check=True)
        os.replace(zpath + ".part", zpath)
    digest = hashlib.sha256(open(zpath, "rb").read()).hexdigest()
    if digest != a["sha256"]:
        os.remove(zpath)
        sys.exit(f"{name}: SHA-256 mismatch ({digest} != {a['sha256']}); download removed")
    prefix = a["extract"].rstrip("/") + "/"
    if os.path.exists(dest):
        shutil.rmtree(dest)
    with zipfile.ZipFile(zpath) as z:
        members = [m for m in z.infolist() if m.filename.startswith(prefix)]
        for m in members:
            target = os.path.join("game", m.filename)
            if m.is_dir():
                os.makedirs(target, exist_ok=True)
                continue
            os.makedirs(os.path.dirname(target), exist_ok=True)
            with z.open(m) as src, open(target, "wb") as out:
                shutil.copyfileobj(src, out)
            mode = (m.external_attr >> 16) & 0o777
            if mode:
                os.chmod(target, mode)
    open(marker, "w").write(a["version"] + "\n")
    print(f"{name}: {a['version']} installed into {dest} ({len(members)} entries, sha256 ok, license {a['license']})")
PY
