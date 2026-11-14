"""Bring chosen prefabs of a .unitypackage into the project with everything they need, the way Unity's import dialog
would with only those ticked, but scriptable: GUIDs and import settings kept, paths moved under a new root.

Pass 1 streams the package (and an optional render-pipeline sub-package whose assets replace the main ones by GUID,
e.g. URP materials) and reads every text asset and .meta for `guid:` references. The closure of the chosen roots over
those references is what comes in; scripts are left out unless --scripts. Pass 2 streams again and writes the closure.
Texture metas get their maxTextureSize capped. Folder metas of the package come along for every folder written.

usage: python -I import_unitypackage.py --package P [--overlay URP.unitypackage] --select SELECT.json --from PREFIX
       --to PREFIX --project ROOT [--cap 2048] [--cap-rule REGEX=SIZE ...] [--exclude EXCLUDE.json] [--keep-existing]
       [--strip-scripts] [--dry]
SELECT.json: a list of regular expressions matched against package paths; matching .prefab and .unity assets are the
roots (a whole scene brings every prefab, material and texture it places).
EXCLUDE.json: regular expressions for package paths never taken nor followed (a scene's baked lighting, say: the map
builder bakes its own).
--keep-existing: assets whose GUID the project already has are not written again (an earlier import, maybe edited or
converted since, e.g. .tga -> .png with the same GUID), so nothing is clobbered and no GUID ends up twice.
--cap-rule: a smaller (or larger) cap for the textures whose package path matches, the first matching rule wins, e.g.
"/Textures_Props/=1024": a prop is small on screen, so its maps need not be as big as a floor's.
--strip-scripts: prefabs and scenes lose the components whose script stays out (left in, every load warns about a
missing script, and a prefab instance cannot drop a component its prefab carries).
"""
import argparse
import json
import os
import re
import struct
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
ap.add_argument("--cap-rule", action="append", default=[])
ap.add_argument("--scripts", action="store_true")
ap.add_argument("--exclude")
ap.add_argument("--keep-existing", action="store_true")
ap.add_argument("--strip-scripts", action="store_true")
ap.add_argument("--dry", action="store_true")
args = ap.parse_args()
patterns = [re.compile(p) for p in json.load(open(args.select, encoding="utf-8"))]
excludes = [re.compile(p) for p in json.load(open(args.exclude, encoding="utf-8"))] if args.exclude else []
cap_rules = [(re.compile(rule.rsplit("=", 1)[0]), int(rule.rsplit("=", 1)[1])) for rule in args.cap_rule]


def cap_for(path):
    """The texture size cap for a package path: the first matching --cap-rule, else --cap."""
    return next((size for pattern, size in cap_rules if pattern.search(path)), args.cap)


def scan(package):
    """guid -> {path, size, refs, pixels}: one streaming pass."""
    info = {}
    with tarfile.open(package, "r|gz") as tar:
        for member in tar:
            parts = member.name.strip("./").split("/")
            if len(parts) != 2:
                continue
            guid, leaf = parts
            entry = info.setdefault(guid, {"path": "", "size": 0, "refs": set(), "pixels": None})
            if leaf == "pathname":
                entry["path"] = tar.extractfile(member).read().decode("utf-8", "replace").splitlines()[0].strip()
            elif leaf == "asset":
                entry["size"] = member.size
                # Text assets carry references (the pathname may come after the asset, so the kind is told by the
                # first bytes): a scene of 10 MB and more too. Images and models are only peeked at; a PNG's size
                # is kept for the texture memory estimate.
                stream = tar.extractfile(member)
                head = stream.read(32)
                if head[:8] == b"\x89PNG\r\n\x1a\n" and len(head) >= 24:
                    entry["pixels"] = struct.unpack(">II", head[16:24])
                elif not head.startswith(b"Kayd") and member.size < 64_000_000:
                    data = head + stream.read()
                    if b"guid:" in data:
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

roots = [g for g, e in main.items() if e["path"].endswith((".prefab", ".unity")) and any(p.search(e["path"]) for p in patterns)]
take, queue = set(), list(roots)
while queue:
    g = queue.pop()
    if g in take or g not in main:
        continue
    path = main[g]["path"]
    if path.endswith(".cs") and not args.scripts:
        continue
    if any(p.search(path) for p in excludes):
        continue
    take.add(g)
    queue.extend(main[g]["refs"])
# Scripts the package carries but this import leaves out: their components are stripped with --strip-scripts.
left_out_scripts = {g for g, e in main.items() if e["path"].endswith(".cs") and g not in take}


def project_guids():
    """Every GUID the project's Assets already holds (first lines of each .meta)."""
    found = set()
    for base, _, files in os.walk(os.path.join(args.project, "Assets")):
        for name in files:
            if name.endswith(".meta"):
                try:
                    with open(os.path.join(base, name), "rb") as f:
                        m = GUID.search(f.read(400))
                except OSError:
                    continue
                if m:
                    found.add(m.group(1).decode())
    return found


existing = (project_guids() & take) if args.keep_existing else set()
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
new = take - existing
print(f"already in the project (kept as they are): {len(existing)}; new: {len(new)}, "
      f"{sum(main[g]['size'] for g in new) / 1e6:.0f} MB")
# Rough GPU memory of the new textures at the cap: block-compressed ~1 byte per texel, mips +1/3.
texels, sizes = 0, {}
for g in new:
    px = main[g]["pixels"]
    if not px:
        continue
    w, h = px
    scale = max(1.0, max(w, h) / cap_for(main[g]["path"]))
    w, h = int(w / scale), int(h / scale)
    texels += w * h
    sizes[max(w, h)] = sizes.get(max(w, h), 0) + 1
print(f"new textures by size after the caps: {sorted(sizes.items(), reverse=True)}; about {texels * 4 / 3 / 1e6:.0f} MB on the GPU")
if args.dry:
    for g in sorted(new, key=lambda g: -main[g]["size"])[:25]:
        print(f"  {main[g]['size'] / 1e6:8.1f} MB  {main[g]['path']}")
    raise SystemExit(0)


def target(path):
    if not path.startswith(args.src):
        return None
    return os.path.join(args.project, args.dst + path[len(args.src):])


def cap_meta(data, path):
    if not path.lower().endswith(IMAGE):
        return data
    cap = cap_for(path)
    return re.sub(rb"maxTextureSize: (\d+)", lambda m: b"maxTextureSize: " + str(min(int(m.group(1)), cap)).encode(), data)


# A prefab's LODGroup entries for renderers in another asset (Dekogon lists the FBX's UCX_ collision renderer): never
# drawn by an instance, but each placement warns "registered with more than one LODGroup". Same as fix_lod_renderers.py.
FOREIGN_LOD = re.compile(rb"^    - renderer: \{fileID: -?\d+, guid: [0-9a-f]{32},(?:\r?\n[ \t]+| )type: \d+\}\r?\n", re.M)
EMPTY_LOD = re.compile(rb"^(    renderers:)(\r?\n)(?!    - renderer:)", re.M)


DOC = re.compile(rb"^--- !u!(\d+) &(-?\d+)[^\r\n]*\r?\n", re.M)
SCRIPT_REF = re.compile(rb"^  m_Script: \{fileID: -?\d+, guid: ([0-9a-f]{32})", re.M)
# A scene's own baked lighting stays behind (excluded): point its references at nothing rather than a missing GUID.
SCENE_LIGHTING = re.compile(rb"^(  m_(?:LightingDataAsset|LightingSettings): )\{fileID: -?\d+, guid: [0-9a-f]{32}, type: \d+\}", re.M)


def strip_scripts(data):
    """Drop MonoBehaviour documents whose script stays out, and their entries in the GameObjects' component lists."""
    starts = [(m.start(), m.group(1), m.group(2)) for m in DOC.finditer(data)]
    if not starts:
        return data, 0
    keep, dropped_ids = [data[:starts[0][0]]], set()
    for i, (start, kind, file_id) in enumerate(starts):
        end = starts[i + 1][0] if i + 1 < len(starts) else len(data)
        doc = data[start:end]
        ref = SCRIPT_REF.search(doc) if kind == b"114" else None
        if ref and ref.group(1).decode() in left_out_scripts:
            dropped_ids.add(file_id)
            continue
        keep.append(doc)
    if not dropped_ids:
        return data, 0
    out = b"".join(keep)
    ids = b"|".join(re.escape(i) for i in dropped_ids)
    out = re.sub(rb"^  - component: \{fileID: (?:" + ids + rb")\}\r?\n", b"", out, flags=re.M)
    return out, len(dropped_ids)


stripped = [0]


def clean_prefab(data, path):
    low = path.lower()
    if not low.endswith((".prefab", ".unity")):
        return data
    if args.strip_scripts:
        data, n = strip_scripts(data)
        stripped[0] += n
    if low.endswith(".unity"):
        return SCENE_LIGHTING.sub(rb"\1{fileID: 0}", data) if excludes else data
    data, dropped = FOREIGN_LOD.subn(b"", data)
    return EMPTY_LOD.sub(rb"\1 []\2", data) if dropped else data


def free_path(dest, guid):
    """dest, or the first 'name N.ext' beside it whose .meta isn't another GUID's (folders keep their path)."""
    def other(path):
        meta = path + ".meta"
        if not os.path.exists(meta):
            return os.path.exists(path)
        with open(meta, "rb") as f:
            found = GUID.search(f.read(400))
        return found is not None and found.group(1).decode() != guid
    if os.path.isdir(dest) or not other(dest):
        return dest
    base, ext = os.path.splitext(dest)
    n = 1
    while other(f"{base} {n}{ext}"):
        n += 1
    return f"{base} {n}{ext}"


def write(package, only):
    """Writes the chosen assets. A GUID's files sit together in the package; they are held until its last one is read
    and then written .meta first, so an editor that refreshes meanwhile never meets an asset without its .meta (it
    would give it a new GUID, and the .meta written after would no longer match)."""
    written = 0
    held, held_guid = [], None
    moved = {}

    def flush():
        nonlocal written
        for dest, data in sorted(held, key=lambda item: not item[0].endswith(".meta")):
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            with open(dest, "wb") as f:
                f.write(data)
            written += 1
        held.clear()

    with tarfile.open(package, "r|gz") as tar:
        for member in tar:
            parts = member.name.strip("./").split("/")
            if len(parts) != 2 or parts[0] not in only:
                continue
            guid, leaf = parts
            if leaf not in ("asset", "asset.meta"):
                continue
            if guid != held_guid:
                flush()
                held_guid = guid
            path = main[guid]["path"]
            dest = target(path)
            if dest is None:
                continue
            # A file of another GUID already at that path (an earlier import's, or one Unity made, e.g. a material it
            # extracted from a model): never overwritten, since whatever points at its GUID would lose it. This one
            # goes beside it under the next free name, as Unity's own import does ("name 1.mat"); its GUID is what the
            # package's assets point at, so the path doesn't matter to them.
            if guid not in moved:
                moved[guid] = free_path(dest, guid)
                if moved[guid] != dest:
                    print("  kept the project's", os.path.relpath(dest, args.project), "-> this one as", os.path.basename(moved[guid]))
            dest = moved[guid]
            data = tar.extractfile(member).read()
            if leaf == "asset.meta":
                dest += ".meta"
                data = cap_meta(data, path)
            else:
                data = clean_prefab(data, path)
            held.append((dest, data))
    flush()
    return written


overridden = {g for g in new if g in over}
n = write(args.package, new - overridden if args.overlay else new)
# Overlay assets replace the main ones (asset and meta); main-only metas of overridden assets come from the overlay too.
if args.overlay:
    n += write(args.overlay, overridden)
print("files written:", n, "; script components stripped:", stripped[0])
