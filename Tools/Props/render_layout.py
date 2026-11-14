"""A built scene laid out for rendering in Blender (render_scene.py): its model copies (FBX, place, yaw, and each of
the model's materials as a texture and a tint), its boxes (cubes: place, turn, size, and their surface: side and top
textures with their tiles, or one texture), and its lights.
usage: python -I render_layout.py <project root> <scene.unity> <out.json>"""
import json
import math
import os
import re
import sys

root, scene, out = sys.argv[1:4]
GUID = re.compile(rb"guid: ([0-9a-f]{32})")
PATHS = {}
for base, _, files in os.walk(os.path.join(root, "Assets")):
    for name in files:
        if name.endswith(".meta"):
            p = os.path.join(base, name)
            try:
                m = GUID.search(open(p, "rb").read(400))
            except OSError:
                continue
            if m:
                PATHS[m.group(1).decode()] = os.path.abspath(p[:-5])
data = open(scene, "rb").read()
heads = list(re.finditer(rb"^--- !u!(\d+) &(-?\d+)([^\r\n]*)\r?\n", data, re.M))
docs = {}
for i, m in enumerate(heads):
    end = heads[i + 1].start() if i + 1 < len(heads) else len(data)
    docs[int(m.group(2))] = (int(m.group(1)), b"stripped" in m.group(3), data[m.end():end])


def vec(body, key, n):
    pat = key + rb": \{x: ([-\d.eE]+), y: ([-\d.eE]+), z: ([-\d.eE]+)" + (rb", w: ([-\d.eE]+)" if n == 4 else b"") + rb"\}"
    m = re.search(pat, body)
    return [float(v) for v in m.groups()] if m else None


def ref(body, key):
    m = re.search(key + rb": \{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}))?", body)
    return (int(m.group(1)), m.group(2).decode() if m.group(2) else None) if m else None


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return [aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz]


def qrot(q, v):
    x, y, z, w = q
    uv = (y * v[2] - z * v[1], z * v[0] - x * v[2], x * v[1] - y * v[0])
    uuv = (y * uv[2] - z * uv[1], z * uv[0] - x * uv[2], x * uv[1] - y * uv[0])
    return [v[i] + 2 * (w * uv[i] + uuv[i]) for i in range(3)]


MATERIALS = {}


def material(guid):
    """A material file read for rendering: its textures, tiles and tints."""
    if guid in MATERIALS:
        return MATERIALS[guid]
    path = PATHS.get(guid)
    result = None
    if path and path.lower().endswith(".mat"):
        body = open(path, "rb").read()

        def tex(name):
            m = re.search(rb"- " + name + rb":\s*\r?\n\s*m_Texture: \{fileID: \d+, guid: ([0-9a-f]{32})", body)
            return PATHS.get(m.group(1).decode()) if m else None

        def flt(name, default):
            m = re.search(rb"- " + name + rb": ([-\d.eE]+)", body)
            return float(m.group(1)) if m else default

        def col(name):
            m = re.search(rb"- " + name + rb": \{r: ([-\d.eE]+), g: ([-\d.eE]+), b: ([-\d.eE]+)", body)
            return [float(v) for v in m.groups()] if m else None

        surface = b"_TopMap:" in body and b"- _Tile:" in body
        result = {"name": os.path.basename(path), "surface": surface,
                  "tex": tex(rb"_BaseMap") or tex(rb"_MainTex") or tex(rb"_BaseColorMap"),
                  "top": tex(rb"_TopMap") if surface else None,
                  "tile": flt(rb"_Tile", 3.0), "topTile": flt(rb"_TopTile", 3.0),
                  "tint": col(rb"_BaseColor") or col(rb"_Color") or [1, 1, 1], "topTint": col(rb"_TopColor") or [1, 1, 1],
                  "transparent": b"_Surface: 1" in body or b"Transparent" in body[:2000]}
    MATERIALS[guid] = result
    return result


local = {}
for fid, (cls, stripped, body) in docs.items():
    if cls in (4, 224) and not stripped:
        local[fid] = {"pos": vec(body, rb"m_LocalPosition", 3) or [0, 0, 0], "rot": vec(body, rb"m_LocalRotation", 4) or [0, 0, 0, 1],
                      "scale": vec(body, rb"m_LocalScale", 3) or [1, 1, 1], "parent": (ref(body, rb"m_Father") or (0, None))[0],
                      "go": (ref(body, rb"m_GameObject") or (0, None))[0]}


def world(t):
    pos, rot, scale = t["pos"], t["rot"], t["scale"]
    p = t["parent"]
    depth = 0
    while p and p in local and depth < 64:
        up = local[p]
        pos = [up["pos"][i] + v for i, v in enumerate(qrot(up["rot"], [pos[0] * up["scale"][0], pos[1] * up["scale"][1], pos[2] * up["scale"][2]]))]
        rot = qmul(up["rot"], rot)
        scale = [scale[i] * up["scale"][i] for i in range(3)]
        p = up["parent"]
        depth += 1
    return pos, rot, scale


def model_materials(fbx):
    found = {}
    try:
        meta = open(fbx + ".meta", "rb").read()
    except OSError:
        return found
    for name, guid in re.findall(rb"name: ([^\r\n]*)\r?\n\s*second: \{fileID: -?\d+, guid: ([0-9a-f]{32})", meta):
        info = material(guid.decode())
        if info:
            found[name.decode("utf-8", "replace").strip()] = info
    return found


result = {"copies": [], "boxes": [], "lights": [], "prefabs": []}
names = {}
for fid, (cls, stripped, body) in docs.items():
    if cls == 1 and not stripped:
        m = re.search(rb"m_Name: ([^\r\n]*)", body)
        names[fid] = m.group(1).decode("utf-8", "replace") if m else ""
removed_go = set()
for fid, (cls, stripped, body) in docs.items():
    if cls != 1001:
        continue
    source = ref(body, rb"m_SourcePrefab")
    src = PATHS.get(source[1]) if source else None
    mods = {}
    for m in re.finditer(rb"propertyPath: (m_Local(?:Position|Rotation|Scale)\.[xyzw]|m_IsActive)\s*\r?\n\s*value: ([^\r\n]*)", body):
        mods[m.group(1).decode()] = m.group(2).decode().strip()
    t = {"pos": [float(mods.get("m_LocalPosition." + a, 0)) for a in "xyz"],
         "rot": [float(mods.get("m_LocalRotation." + a, 1 if a == "w" else 0)) for a in "xyzw"],
         "scale": [float(mods.get("m_LocalScale." + a, 1)) for a in "xyz"],
         "parent": (ref(body, rb"m_TransformParent") or (0, None))[0]}
    pos, rot, scale = world(t)
    # Its turn about Y: where its x axis points, flattened (a model stood on its exporter's -90 deg X keeps its x).
    ax = qrot(rot, [1, 0, 0])
    yaw = math.degrees(math.atan2(-ax[2], ax[0]))
    entry = {"pos": pos, "rot": rot, "scale": scale, "yaw": yaw, "source": src}
    if src and src.lower().endswith(".fbx"):
        entry["materials"] = model_materials(src)
        result["copies"].append(entry)
    else:
        result["prefabs"].append(entry)
filters, renderers = {}, {}
for fid, (cls, stripped, body) in docs.items():
    if stripped:
        continue
    go = ref(body, rb"m_GameObject")
    if cls == 33 and go:
        filters[go[0]] = ref(body, rb"m_Mesh")
    elif cls == 23 and go:
        mats = re.findall(rb"- \{fileID: (-?\d+), guid: ([0-9a-f]{32})", body.split(b"m_Materials:")[1][:400]) if b"m_Materials:" in body else []
        renderers[go[0]] = {"enabled": re.search(rb"m_Enabled: 1", body) is not None,
                            "material": material(mats[0][1].decode()) if mats else None}
    elif cls == 108 and go:
        lt = local.get(next((t for t, v in local.items() if v["go"] == go[0]), None))
        if lt:
            pos, rot, _ = world(lt)
            kind = re.search(rb"m_Type: (\d)", body)
            color = re.search(rb"m_Color: \{r: ([-\d.eE]+), g: ([-\d.eE]+), b: ([-\d.eE]+)", body)
            result["lights"].append({"pos": pos, "rot": rot, "type": int(kind.group(1)) if kind else 2,
                                     "intensity": float((re.search(rb"m_Intensity: ([-\d.eE]+)", body) or [0, b"1"])[1]),
                                     "range": float((re.search(rb"m_Range: ([-\d.eE]+)", body) or [0, b"10"])[1]),
                                     "color": [float(v) for v in color.groups()] if color else [1, 1, 1]})
for tf, t in local.items():
    go = t["go"]
    mesh = filters.get(go)
    if not mesh or go not in renderers or mesh[1] != "0000000000000000e000000000000000" or mesh[0] != 10202:
        continue
    pos, rot, scale = world(t)
    result["boxes"].append({"name": names.get(go, ""), "pos": pos, "rot": rot, "size": scale, **renderers[go]})
json.dump(result, open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=0)
print("copies", len(result["copies"]), "prefab copies", len(result["prefabs"]), "boxes", len(result["boxes"]), "lights", len(result["lights"]))
