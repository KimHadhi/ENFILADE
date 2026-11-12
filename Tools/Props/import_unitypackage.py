"""Bring chosen prefabs of a .unitypackage into the project with everything they need, the way Unity's import dialog
would with only those ticked, but scriptable: GUIDs and import settings kept, paths moved under a new root.

Pass 1 streams the package (and an optional render-pipeline sub-package whose assets replace the main ones by GUID,
e.g. URP materials) and reads every text asset and .meta for `guid:` references. The closure of the chosen roots over
those references is what comes in; scripts are left out unless --scripts. Pass 2 streams again and writes the closure.
Texture metas get their maxTextureSize capped. Folder metas of the package come along for every folder written.

usage: python -I import_unitypackage.py --package P [--overlay URP.unitypackage] --select SELECT.json --from PREFIX
       --to PREFIX --project ROOT [--cap 2048] [--dry]
SELECT.json: a list of regular expressions matched against package paths; matching .prefab assets are the roots.
"""
import argparse
import json
import os
import re
import tarfile

TEXT = (".prefab", ".mat", ".asset", ".controller", ".overrideController", ".anim", ".physicMaterial", ".physicsMaterial",
        ".mask", ".playable", ".unity", ".lighting", ".terrainlayer", ".shadergraph", ".shadersubgraph", ".mixer")
IMAGE = (".png", ".tga", ".tif", ".tiff", ".psd", ".jpg", ".jpeg", ".exr", ".hdr", ".bmp")
GUID = re.compile(rb"guid: ([0-9a-f]{32})")

ap = argparse.ArgumentParser()
ap.add_argument("--package", required=True)
ap.add_argument("--overlay")
ap.add_argument("--select", required=True)
ap.add_argument("--from", dest="src", required=True)
ap.add_argument("--to", dest="dst", required=True)
ap.add_argument("--project", required=True)
ap.add_argument("--cap", type=int, default=2048)
ap.add_argument("--scripts", action="store_true")
ap.add_argument("--dry", action="store_true")
args = ap.parse_args()
patterns = [re.compile(p) for p in json.load(open(args.select, encoding="utf-8"))]


def scan(package):
    """guid -> {path, size, refs}: one streaming pass."""
    info = {}
    with tarfile.open(package, "r|gz") as tar:
        for member in tar:
            parts = member.name.strip("./").split("/")
            if len(parts) != 2:
                continue
            guid, leaf = parts
            entry = info.setdefault(guid, {"path": "", "size": 0, "refs": set()})
            if leaf == "pathname":
                entry["path"] = tar.extractfile(member).read().decode("utf-8", "replace").splitlines()[0].strip()
            elif leaf == "asset":
                entry["size"] = member.size
                # Text assets carry references; read small files only (binaries never match TEXT by name, but the
                # pathname may come after the asset, so read anything small and test it later).
                if member.size < 8_000_000:
                    data = tar.extractfile(member).read()
                    if not data[:4] in (b"\x89PNG", b"Kayd") and b"guid:" in data:
                        entry["refs"].update(g.decode() for g in GUID.findall(data))
            elif leaf == "asset.meta":
                data = tar.extractfile(member).read()
                entry["refs"].update(g.decode() for g in GUID.findall(data))
    for guid, entry in info.items():
        entry["refs"].discard(guid)
    return info


main = scan(args.package)
over = scan(args.overlay) if args.overlay else {}
for guid, entry in over.items():
    if guid in main:
        main[guid]["refs"] = entry["refs"] | main[guid]["refs"]
    else:
        main[guid] = dict(entry)

roots = [g for g, e in main.items() if e["path"].endswith(".prefab") and any(p.search(e["path"]) for p in patterns)]
take, queue = set(), list(roots)
while queue:
    g = queue.pop()
    if g in take or g not in main:
        continue
    path = main[g]["path"]
    if path.endswith(".cs") and not args.scripts:
        continue
    take.add(g)
    queue.extend(main[g]["refs"])
# Ancestor folders that the package itself carries (their metas keep the package's folder GUIDs).
folders = {e["path"]: g for g, e in main.items() if e["size"] == 0 and "." not in e["path"].split("/")[-1]}
for g in list(take):
    parts = main[g]["path"].split("/")
    for i in range(2, len(parts)):
        f = "/".join(parts[:i])
        if f in folders:
            take.add(folders[f])

total = sum(main[g]["size"] for g in take)
kinds = {}
for g in take:
    ext = os.path.splitext(main[g]["path"])[1].lower() or "(folder)"
    kinds[ext] = kinds.get(ext, 0) + 1
print(f"roots {len(roots)}, assets {len(take)}, {total / 1e6:.0f} MB; by type {sorted(kinds.items(), key=lambda kv: -kv[1])}")
if args.dry:
    raise SystemExit(0)


def target(path):
    if not path.startswith(args.src):
        return None
    return os.path.join(args.project, args.dst + path[len(args.src):])


def cap_meta(data, path):
    if not path.lower().endswith(IMAGE):
        return data
    return re.sub(rb"maxTextureSize: (\d+)", lambda m: b"maxTextureSize: " + str(min(int(m.group(1)), args.cap)).encode(), data)


def write(package, only):
    written = 0
    pending = {}
    with tarfile.open(package, "r|gz") as tar:
        for member in tar:
            parts = member.name.strip("./").split("/")
            if len(parts) != 2 or parts[0] not in only:
                continue
            guid, leaf = parts
            if leaf not in ("asset", "asset.meta"):
                continue
            path = main[guid]["path"]
            dest = target(path)
            if dest is None:
                continue
            data = tar.extractfile(member).read()
            if leaf == "asset.meta":
                dest += ".meta"
                data = cap_meta(data, path)
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            with open(dest, "wb") as f:
                f.write(data)
            written += 1
    return written


overridden = {g for g in take if g in over}
n = write(args.package, take - overridden if args.overlay else take)
# Overlay assets replace the main ones (asset and meta); main-only metas of overridden assets come from the overlay too.
if args.overlay:
    n += write(args.overlay, overridden)
print("files written:", n)
