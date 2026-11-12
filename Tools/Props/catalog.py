# One catalog of every prop candidate across the downloads: Quixel pack folders and zips (name and size from each
# asset's json), the Unreal exports (manifest), the Blender reports and the Dekogon packages, with triangle counts and
# FBX material names. Usage: python -I catalog.py <scratch> <out.json>
import io
import json
import os
import struct
import sys
import tarfile
import zipfile
import zlib

SCRATCH, OUT = sys.argv[1], sys.argv[2]
sys.path.insert(0, SCRATCH)
argv, sys.argv = sys.argv, ["fbx_stats"]
import fbx_stats  # noqa: E402
sys.argv = argv
FAB = "F:/FabAssets"


def materials_of(data):
    """Material names in a binary FBX (Objects > Material 'Name\\x00\\x01Material')."""
    if not data.startswith(b"Kaydara FBX Binary"):
        return []
    version = struct.unpack_from("<I", data, 23)[0]
    wide = version >= 7500
    head, size = ("<QQQ", 24) if wide else ("<III", 12)
    names = []

    def walk(pos, end):
        while pos + size < end:
            end_offset, count, plen = struct.unpack_from(head, data, pos)
            if end_offset == 0:
                return
            name_len = data[pos + size]
            name = data[pos + size + 1:pos + size + 1 + name_len]
            start = pos + size + 1 + name_len
            if name == b"Material" and count >= 2 and data[start] == ord("L"):
                p = start + 9
                if data[p] == ord("S"):
                    n = struct.unpack_from("<I", data, p + 1)[0]
                    names.append(data[p + 5:p + 5 + n].split(b"\x00")[0].decode("utf-8", "replace"))
            if name in (b"Objects",) or name == b"":
                walk(start + plen, end_offset)
            elif start + plen < end_offset and name not in (b"Material", b"Geometry", b"Model", b"Video", b"Texture"):
                walk(start + plen, end_offset)
            pos = end_offset

    walk(27, len(data))
    return names


def quixel_json(read_json):
    try:
        info = json.loads(read_json())
    except Exception:
        return {}
    dims = {m.get("key"): m.get("value") for m in info.get("meta", []) if isinstance(m, dict)}
    return {"name": info.get("name"), "dims": [dims.get("length"), dims.get("width"), dims.get("height")],
            "tags": (info.get("tags") or [])[:6]}


catalog = []


def add(source, ident, fbx_bytes, extra):
    stats = fbx_stats.parse(fbx_bytes) if fbx_bytes else {}
    entry = {"source": source, "id": ident, "tris": stats.get("tris"), "meshes": stats.get("meshes"),
             "materials": materials_of(fbx_bytes) if fbx_bytes else []}
    entry.update(extra)
    catalog.append(entry)


# Quixel pack folders: fbx/mid/<pack>_mid_extracted/<id>/<id>.fbx (+ <id>.json)
for pack in sorted(os.listdir(FAB)):
    base = os.path.join(FAB, pack, "fbx", "mid")
    if not os.path.isdir(base):
        continue
    for root, _, files in os.walk(base):
        fbxs = [f for f in files if f.lower().endswith(".fbx")]
        if not fbxs:
            continue
        ident = os.path.basename(root)
        jsons = [f for f in files if f.lower().endswith(".json")]
        meta = quixel_json(lambda: open(os.path.join(root, jsons[0]), "rb").read()) if jsons else {}
        with open(os.path.join(root, fbxs[0]), "rb") as f:
            data = f.read()
        meta["path"] = os.path.join(root, fbxs[0]).replace("\\", "/")
        meta["textures"] = sorted(f for f in files if f.lower().endswith((".jpg", ".png", ".exr")))
        add(pack.split("-")[0], ident, data, meta)

# Quixel zips: junkyard_mid.zip (<id>/<id>.fbx) and single assets (<Name>_<id>_<q>.zip)
for zname in sorted(os.listdir(FAB)):
    if not zname.lower().endswith(".zip"):
        continue
    with zipfile.ZipFile(os.path.join(FAB, zname)) as archive:
        names = archive.namelist()
        fbxs = [n for n in names if n.lower().endswith(".fbx")]
        jsons = [n for n in names if n.lower().endswith(".json")]
        for fbx in fbxs:
            folder = fbx.rsplit("/", 1)[0] if "/" in fbx else ""
            js = [j for j in jsons if (j.rsplit("/", 1)[0] if "/" in j else "") == folder]
            meta = quixel_json(lambda: archive.read(js[0])) if js else {}
            meta["path"] = zname + "!" + fbx
            meta["textures"] = sorted(n for n in names if (n.rsplit("/", 1)[0] if "/" in n else "") == folder
                                      and n.lower().endswith((".jpg", ".png", ".tif")))
            add(zname, folder or os.path.splitext(os.path.basename(fbx))[0], archive.read(fbx), meta)

# Unreal exports
for pack in ("Vigilante", "MOUT", "MilitaryTrainingFacility"):
    path = os.path.join(FAB, "FromUnreal", pack, "manifest.json")
    if not os.path.exists(path):
        continue
    manifest = json.load(open(path, encoding="utf-8"))
    for package, e in manifest["assets"].items():
        if e.get("class") not in ("StaticMesh", "SkeletalMesh") or not e.get("ok"):
            continue
        size = None
        if "min" in e:
            size = [round((e["max"][i] - e["min"][i]) / 100, 2) for i in range(3)]
        catalog.append({"source": "UE:" + pack, "id": package.split("/")[-1], "path": package,
                        "tris": (e.get("tris") or [None])[0], "lods": e.get("lods"), "nanite": e.get("nanite"),
                        "dims": size, "materials": [s.get("material") for s in e.get("slots", [])]})

# Blender files
for report in sorted(os.listdir(os.path.join(SCRATCH, "blend_reports"))):
    if not report.endswith(".json"):
        continue
    r = json.load(open(os.path.join(SCRATCH, "blend_reports", report), encoding="utf-8"))
    for o in r["objects"]:
        catalog.append({"source": "blend:" + report[:-5], "id": o["name"], "tris": o["tris"], "dims": o["size"],
                        "materials": o["materials"], "parent": o.get("parent")})

# Dekogon unity packages: prefab names
for pack in sorted(os.listdir(FAB)):
    unity = os.path.join(FAB, pack, "unity")
    if not os.path.isdir(unity):
        continue
    for f in os.listdir(unity):
        if f.endswith(".unitypackage"):
            with tarfile.open(os.path.join(unity, f), "r:gz") as tar:
                for member in tar:
                    if member.name.endswith("/pathname"):
                        p = tar.extractfile(member).read().decode("utf-8", "replace").splitlines()[0]
                        if p.endswith(".prefab"):
                            catalog.append({"source": "Dekogon:" + pack.split("_-_")[0], "id": p.rsplit("/", 1)[-1][:-7], "path": p})

with open(OUT, "w", encoding="utf-8") as f:
    json.dump(catalog, f, ensure_ascii=False, indent=1)
print("catalog entries:", len(catalog))
