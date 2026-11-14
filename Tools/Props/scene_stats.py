"""What a built map costs, read from its scene file without Unity: renderers, triangles, materials, decals, lights,
shadow casters, LOD coverage, colliders and reflection probes, with what each prefab copy brings from its prefab or
model file. For the map optimisation pass (2026-11-14, the user: "맵 최적화랑 꾸미기 좀"): measure a scene before a
change and again after the rebuild, the same way.

usage: python -I scene_stats.py <project root> <scene.unity> [<scene.unity> ...] [--json <out.json>]

Counts (only what is enabled and active; a prefab copy's parts minus what the copy removed):
- renderers: mesh and skinned mesh renderers; draws: their material slots (a draw each, before batching).
- tris: triangles of what is drawn close up (each LOD group's first level, and everything not in a group).
- materials: distinct materials on those renderers.
- casters: renderers casting shadows; small casters: those whose mesh is under 400 triangles (cheap props whose
  shadows cost a pass each and show little).
- LOD: renderers inside a LOD group (any level) out of all renderers.
- lights by mode (realtime, mixed, baked) and how many cast shadows live; decals (URP decal projectors); reflection
  probes; colliders by kind.
Approximations: a model's (FBX) renderers are its mesh nodes, LOD levels by the _LOD<n> name suffix, its triangles read
from the FBX (7.x binary, or 6.1); a copy's overrides on a model's parts are applied only when the model has one
renderer. Meshes in .asset files count their index buffers. Unknown meshes count 0 triangles and are listed.
"""
import json
import os
import re
import struct
import sys
import zlib

DOC = re.compile(rb"^--- !u!(\d+) &(-?\d+)([^\r\n]*)\r?\n", re.M)
REF = rb"\{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}))?(?:, type: \d+)?\}"
GUID = re.compile(rb"guid: ([0-9a-f]{32})")
DECAL_SCRIPT = "0777d029ed3dffa4692f417d4aba19ca"
BUILTIN = "0000000000000000e000000000000000"
# Built-in meshes' triangles (Cube, Cylinder, Sphere, Capsule, Plane, Quad).
BUILTIN_TRIS = {10202: 12, 10206: 80, 10207: 768, 10208: 832, 10209: 200, 10210: 2}
COLLIDERS = {65: "box", 64: "mesh", 135: "sphere", 136: "capsule", 143: "controller", 154: "terrain"}
SMALL_CASTER = 400
# A caster this small on its longest side (m) is a small one: its shadow costs a pass a cascade and shows little.
SMALL_SIZE = 1.0

args = [a for a in sys.argv[1:]]
out_json = None
if "--json" in args:
    i = args.index("--json")
    out_json = args[i + 1]
    del args[i:i + 2]
root, scenes = args[0], args[1:]


def guid_paths():
    found = {}
    # The project's own assets (a package's meshes are rare in a map and would cost a walk through the package cache).
    for top in ("Assets",):
        for base, _, files in os.walk(os.path.join(root, top)):
            for name in files:
                if not name.endswith(".meta"):
                    continue
                path = os.path.join(base, name)
                try:
                    with open(path, "rb") as f:
                        m = GUID.search(f.read(400))
                except OSError:
                    continue
                if m:
                    found[m.group(1).decode()] = path[:-5]
    return found


PATHS = guid_paths()


def parse_docs(data):
    docs = {}
    heads = list(DOC.finditer(data))
    for i, m in enumerate(heads):
        end = heads[i + 1].start() if i + 1 < len(heads) else len(data)
        docs[int(m.group(2))] = (int(m.group(1)), b"stripped" in m.group(3), data[m.end():end])
    return docs


def ref(body, key):
    m = re.search(key + rb": " + REF, body)
    if not m:
        return None
    return int(m.group(1)), (m.group(2).decode() if m.group(2) else None)


def refs_list(body, key):
    m = re.search(key + rb":\s*\r?\n((?:\s*- " + REF + rb"\r?\n)+)", body)
    if not m:
        return []
    return [(int(a), b.decode() if b else None) for a, b in re.findall(rb"- " + REF, m.group(1))]


def num3(body, key):
    """(x, y, z) of a vector field, or None."""
    if not body:
        return None
    m = re.search(key + rb": \{x: ([-\d.eE]+), y: ([-\d.eE]+), z: ([-\d.eE]+)\}", body)
    return tuple(float(v) for v in m.groups()) if m else None


def value(body, key, default=None):
    m = re.search(rb"^\s*" + key + rb": ([^\r\n]*)", body, re.M)
    return m.group(1).strip() if m else default


# ---- Models (FBX) ----

def fbx_records(data):
    """(name, props, children) tree of a binary FBX (7.x and 6.1)."""
    version = struct.unpack_from("<I", data, 23)[0]
    wide = version >= 7500
    head, head_size = ("<QQQ", 24) if wide else ("<III", 12)
    scalar = {"Y": ("h", 2), "C": ("?", 1), "I": ("i", 4), "F": ("f", 4), "D": ("d", 8), "L": ("q", 8)}
    array = {"f": ("f", 4), "d": ("d", 8), "l": ("q", 8), "i": ("i", 4), "b": ("b", 1)}

    def props_at(pos, count):
        out = []
        for _ in range(count):
            t = chr(data[pos])
            pos += 1
            if t in scalar:
                fmt, size = scalar[t]
                out.append(struct.unpack_from("<" + fmt, data, pos)[0])
                pos += size
            elif t in array:
                length, encoding, size = struct.unpack_from("<III", data, pos)
                pos += 12
                raw = data[pos:pos + size]
                pos += size
                out.append(("array", t, length, encoding, raw))
            elif t in ("S", "R"):
                size = struct.unpack_from("<I", data, pos)[0]
                pos += 4
                out.append(data[pos:pos + size])
                pos += size
            else:
                raise ValueError("FBX property type " + t)
        return out, pos

    def node_at(pos):
        end, count, _ = struct.unpack_from(head, data, pos)
        if end == 0:
            return None, pos + head_size + 1
        name_len = data[pos + head_size]
        name = data[pos + head_size + 1:pos + head_size + 1 + name_len].decode("latin-1")
        props, p = props_at(pos + head_size + 1 + name_len, count)
        children = []
        while p < end:
            child, p = node_at(p)
            if child is None:
                break
            children.append(child)
        return (name, props, children), end

    nodes, pos = [], 27
    while pos < len(data) - head_size:
        node, pos = node_at(pos)
        if node is None:
            break
        nodes.append(node)
    return version, nodes


def tri_count(props):
    """Triangles of a PolygonVertexIndex record (7.x: one array; 6.1: one number per property)."""
    if props and isinstance(props[0], tuple) and props[0][0] == "array":
        _, t, length, encoding, raw = props[0]
        values = struct.unpack("<%di" % length, zlib.decompress(raw) if encoding == 1 else raw)
    else:
        values = [v for v in props if isinstance(v, int)]
    tris = corners = 0
    for v in values:
        corners += 1
        if v < 0:
            tris += max(0, corners - 2)
            corners = 0
    return tris


def text(v):
    return v.split(b"\x00")[0].decode("utf-8", "replace") if isinstance(v, bytes) else str(v)


def extent(props):
    """The longest side of a Vertices record's box (file units)."""
    if props and isinstance(props[0], tuple) and props[0][0] == "array":
        _, t, length, encoding, raw = props[0]
        fmt = "d" if t == "d" else "f"
        values = struct.unpack("<%d%s" % (length, fmt), zlib.decompress(raw) if encoding == 1 else raw)
    else:
        values = [v for v in props if isinstance(v, float)]
    if len(values) < 3:
        return 0.0
    xs, ys, zs = values[0::3], values[1::3], values[2::3]
    return max(max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))


def import_scale(path):
    """(the import's own scale, whether it converts the file's units) from a model's .meta."""
    try:
        meta = open(path + ".meta", "rb").read()
    except OSError:
        return 1.0, True
    scale = re.search(rb"globalScale: ([-\d.eE]+)", meta)
    convert = re.search(rb"useFileScale: (\d)", meta)
    return (float(scale.group(1)) if scale else 1.0), (convert is None or convert.group(1) == b"1")


LOD_SUFFIX = re.compile(r"_lod(\d+)$", re.I)
MODEL_CACHE = {}


def model_parts(path):
    """A model file's mesh nodes: [{name, tris, lod, materials:[names]}]."""
    if path in MODEL_CACHE:
        return MODEL_CACHE[path]
    parts = []
    try:
        data = open(path, "rb").read()
    except OSError:
        MODEL_CACHE[path] = parts
        return parts
    if not data.startswith(b"Kaydara FBX Binary"):
        MODEL_CACHE[path] = parts
        return parts
    try:
        version, nodes = fbx_records(data)
    except Exception:  # noqa: BLE001 - an unreadable model counts as unknown
        MODEL_CACHE[path] = parts
        return parts
    objects = next((n for n in nodes if n[0] == "Objects"), None)
    connections = next((n for n in nodes if n[0] == "Connections"), None)
    if objects is None:
        MODEL_CACHE[path] = parts
        return parts
    # Metres per file unit as Unity imports it (Convert Units: the file's unit scale, then the import's own scale).
    unit = 1.0
    settings = next((n for n in nodes if n[0] == "GlobalSettings"), None)
    for record in (settings[2] if settings else []):
        for p in record[2] if record[0] == "Properties70" else []:
            if p[0] == "P" and p[1] and text(p[1][0]) == "UnitScaleFactor" and isinstance(p[1][-1], float):
                unit = p[1][-1]
    scale, convert = import_scale(path)
    metres = (unit * .01 if convert else 1.0) * scale
    geometry, models, materials = {}, {}, {}
    for name, props, children in objects[2]:
        if name == "Geometry" and len(props) >= 2:
            index = next((c for c in children if c[0] == "PolygonVertexIndex"), None)
            vertices = next((c for c in children if c[0] == "Vertices"), None)
            geometry[props[0]] = (text(props[1]).replace("Geometry::", ""), tri_count(index[1]) if index else 0,
                                  extent(vertices[1]) * metres if vertices else 0.0)
        elif name == "Model" and len(props) >= 2:
            key = props[0]
            label = text(props[1]).replace("Model::", "")
            # FBX 6.1 keeps a mesh's polygons inside its Model.
            index = next((c for c in children if c[0] == "PolygonVertexIndex"), None)
            vertices = next((c for c in children if c[0] == "Vertices"), None)
            models[key] = {"name": label, "tris": tri_count(index[1]) if index else None, "materials": [],
                           "size": extent(vertices[1]) * metres if vertices else 0.0}
        elif name == "Material" and len(props) >= 2:
            materials[props[0]] = text(props[1]).replace("Material::", "")
    for name, props, _ in connections[2] if connections else []:
        if name not in ("C", "Connect") or len(props) < 3:
            continue
        kind, child, parent = text(props[0]), props[1], props[2]
        if kind != "OO":
            continue
        if child in geometry and parent in models:
            models[parent]["tris"] = geometry[child][1]
            models[parent]["size"] = geometry[child][2]
        elif child in materials and parent in models:
            models[parent]["materials"].append(materials[child])
    for model in models.values():
        if model["tris"]:
            lod = LOD_SUFFIX.search(model["name"])
            parts.append({"name": model["name"], "tris": model["tris"], "lod": int(lod.group(1)) if lod else -1,
                          "materials": model["materials"] or ["(default)"], "size": model["size"]})
    MODEL_CACHE[path] = parts
    return parts


META_NAMES = {}


def meta_names(path):
    """A model's .meta internal names by (class, fileID): {fileID: name}."""
    if path in META_NAMES:
        return META_NAMES[path]
    names = {}
    try:
        meta = open(path + ".meta", "rb").read()
    except OSError:
        META_NAMES[path] = names
        return names
    for cls, fid, name in re.findall(rb"- first:\s*\r?\n\s*(\d+): (-?\d+)\s*\r?\n\s*second: ([^\r\n]*)", meta):
        names[int(fid)] = name.decode("utf-8", "replace").strip()
    for fid, name in re.findall(rb"^\s*(-?\d+): ([^\r\n]+)$", meta.split(b"fileIDToRecycleName:")[1] if b"fileIDToRecycleName:" in meta else b"", re.M):
        names[int(fid)] = name.decode("utf-8", "replace").strip()
    META_NAMES[path] = names
    return names


MODEL_MATERIALS = {}


def model_materials(path):
    """What a model's import maps its materials to (its .meta's external objects): {material name: "guid:fileID"}."""
    if path in MODEL_MATERIALS:
        return MODEL_MATERIALS[path]
    found = {}
    try:
        meta = open(path + ".meta", "rb").read()
    except OSError:
        meta = b""
    for name, fid, guid in re.findall(rb"type: UnityEngine:Material\s*\r?\n\s*assembly: [^\r\n]*\r?\n\s*name: ([^\r\n]*)\r?\n\s*second: "
                                      rb"\{fileID: (-?\d+), guid: ([0-9a-f]{32})", meta):
        found[name.decode("utf-8", "replace").strip()] = "%s:%s" % (guid.decode(), int(fid))
    MODEL_MATERIALS[path] = found
    return found


# ---- Textures ----

TEXTURE_CACHE = {}


def image_size(path):
    """(width, height) from an image file's header, or None."""
    try:
        with open(path, "rb") as f:
            head = f.read(65536)
    except OSError:
        return None
    if head[:8] == b"\x89PNG\r\n\x1a\n":
        return struct.unpack(">II", head[16:24])
    if head[:2] == b"\xff\xd8":
        i = 2
        while i + 9 < len(head):
            if head[i] != 0xFF:
                i += 1
                continue
            marker = head[i + 1]
            if marker in (0xC0, 0xC1, 0xC2):
                h, w = struct.unpack(">HH", head[i + 5:i + 9])
                return w, h
            i += 2 + struct.unpack(">H", head[i + 2:i + 4])[0]
        return None
    if head[:4] == b"8BPS":
        h, w = struct.unpack(">II", head[14:22])
        return w, h
    if head[:2] == b"BM":
        w, h = struct.unpack("<ii", head[18:26])
        return w, abs(h)
    if head[:4] in (b"II*\x00", b"MM\x00*"):
        endian = "<" if head[:2] == b"II" else ">"
        offset = struct.unpack(endian + "I", head[4:8])[0]
        if offset + 2 > len(head):
            return None
        count = struct.unpack(endian + "H", head[offset:offset + 2])[0]
        w = h = None
        for k in range(count):
            e = offset + 2 + k * 12
            tag, kind = struct.unpack(endian + "HH", head[e:e + 4])
            val = struct.unpack(endian + ("H" if kind == 3 else "I"), head[e + 8:e + (10 if kind == 3 else 12)])[0]
            if tag == 256:
                w = val
            elif tag == 257:
                h = val
        return (w, h) if w and h else None
    if path.lower().endswith(".tga") and len(head) >= 18:
        return struct.unpack("<HH", head[12:16])
    return None


def texture_cost(guid):
    """(path, width, height after the import's cap, MB on the GPU) of a texture by GUID, or None."""
    if guid in TEXTURE_CACHE:
        return TEXTURE_CACHE[guid]
    path = PATHS.get(guid)
    cost = None
    if path and not path.lower().endswith((".mat", ".asset", ".rendertexture", ".shader")):
        size = image_size(path)
        if size:
            w, h = size
            cap = 2048
            try:
                meta = open(path + ".meta", "rb").read()
                default = re.search(rb"buildTarget: DefaultTexturePlatform\s*\r?\n\s*maxTextureSize: (\d+)", meta)
                standalone = re.search(rb"buildTarget: Standalone\s*\r?\n\s*maxTextureSize: (\d+)(?:.|\n)*?overridden: (\d)", meta)
                if default:
                    cap = int(default.group(1))
                if standalone and standalone.group(2) == b"1":
                    cap = int(standalone.group(1))
            except OSError:
                pass
            scale = max(1.0, max(w, h) / cap)
            w, h = int(w / scale), int(h / scale)
            # Block compressed about a byte a texel, its mip chain a third more.
            cost = (path, w, h, w * h * 4 / 3 / 1048576)
    TEXTURE_CACHE[guid] = cost
    return cost


def material_textures(key):
    """Texture GUIDs a material (\"guid:fileID\") uses."""
    guid = key.split(":")[0]
    path = PATHS.get(guid)
    if not path or not path.lower().endswith(".mat"):
        return []
    try:
        body = open(path, "rb").read()
    except OSError:
        return []
    return list({g.decode() for g in re.findall(rb"m_Texture: \{fileID: \d+, guid: ([0-9a-f]{32})", body)})


UNKNOWN = {}


def mesh_info(mesh, hint=""):
    """(triangles, longest side in metres; 0 when unknown) of a mesh reference (fileID, guid)."""
    if mesh is None or mesh[0] == 0:
        return 0, 0.0
    fid, guid = mesh
    if guid == BUILTIN:
        return BUILTIN_TRIS.get(fid, 0), 1.0
    path = PATHS.get(guid)
    if path is None:
        UNKNOWN[str(mesh)] = UNKNOWN.get(str(mesh), 0) + 1
        return 0, 0.0
    low = path.lower()
    if low.endswith(".asset"):
        try:
            body = open(path, "rb").read()
        except OSError:
            return 0, 0.0
        counts = re.findall(rb"indexCount: (\d+)\s*\r?\n\s*topology: (\d+)", body)
        box = re.search(rb"m_LocalAABB:\s*\r?\n\s*m_Center: \{[^}]*\}\s*\r?\n\s*m_Extent: \{x: ([-\d.eE]+), y: ([-\d.eE]+), z: ([-\d.eE]+)\}", body)
        size = 2 * max(float(v) for v in box.groups()) if box else 0.0
        return sum(int(c) // 3 for c, t in counts if t == b"0"), size
    if low.endswith((".fbx", ".obj")):
        parts = model_parts(path)
        if not parts:
            UNKNOWN[os.path.basename(path)] = UNKNOWN.get(os.path.basename(path), 0) + 1
            return 0, 0.0
        name = meta_names(path).get(fid)
        for wanted in (name, hint):
            if wanted:
                for part in parts:
                    if part["name"] == wanted:
                        return part["tris"], part["size"]
        if len(parts) == 1:
            return parts[0]["tris"], parts[0]["size"]
        lod0 = [p for p in parts if p["lod"] <= 0]
        return sum(p["tris"] for p in lod0) // max(1, len(lod0)), max((p["size"] for p in lod0), default=0.0)
    return 0, 0.0


# ---- Files (scenes and prefabs) ----

class Tally:
    def __init__(self):
        self.renderers = self.draws = self.tris = self.casters = self.small_casters = self.in_lod = 0
        self.materials = set()
        self.lights = {"realtime": 0, "mixed": 0, "baked": 0}
        self.live_shadows = self.decals = self.probes = self.lod_groups = 0
        self.colliders = {}
        self.copies = 0
        # The materials that resolve to a material file, for the textures they use.
        self.material_refs = set()

    def renderer(self, enabled, cast, materials, tris, lod, size):
        if not enabled:
            return
        self.renderers += 1
        self.draws += max(1, len(materials))
        for m in materials:
            self.materials.add(m)
        if lod >= 0:
            self.in_lod += 1
        if lod <= 0:
            self.tris += tris
        if cast and lod <= 0:
            self.casters += 1
            # Small: under SMALL_SIZE on its longest side (or, size unknown, few triangles).
            if (size > 0 and size < SMALL_SIZE) or (size <= 0 and tris < SMALL_CASTER):
                self.small_casters += 1

    def textures(self):
        """(MB on the GPU, count, biggest [(MB, path, w, h)]) of the textures the map's materials use."""
        seen = {}
        for key in self.material_refs:
            for guid in material_textures(key):
                cost = texture_cost(guid)
                if cost:
                    seen[guid] = cost
        biggest = sorted(((c[3], os.path.relpath(c[0], root).replace(os.sep, "/"), c[1], c[2]) for c in seen.values()), reverse=True)
        return sum(c[3] for c in seen.values()), len(seen), biggest[:12]

    def as_dict(self):
        mb, count, biggest = self.textures()
        return {"renderers": self.renderers, "draws": self.draws, "tris": self.tris, "materials": len(self.materials),
                "texture_mb": round(mb), "textures": count, "biggest_textures": [[round(b[0], 1), b[1], b[2], b[3]] for b in biggest],
                "casters": self.casters, "small_casters": self.small_casters, "lod_renderers": self.in_lod,
                "lod_groups": self.lod_groups, "lights": dict(self.lights), "live_shadows": self.live_shadows,
                "decals": self.decals, "probes": self.probes, "colliders": dict(self.colliders), "prefab_copies": self.copies}


FILE_CACHE = {}


def file_docs(path):
    if path not in FILE_CACHE:
        FILE_CACHE[path] = parse_docs(open(path, "rb").read())
    return FILE_CACHE[path]


def active_objects(docs):
    """GameObjects of a file that are active, their parents active too (fileIDs), and each object's components."""
    parent_of, go_of_transform, active, components = {}, {}, {}, {}
    for fid, (cls, stripped, body) in docs.items():
        if cls == 1 and not stripped:
            active[fid] = value(body, rb"m_IsActive", b"1") == b"1"
            components[fid] = [r[0] for r in refs_list(body, rb"m_Component")] or \
                [int(x) for x in re.findall(rb"component: \{fileID: (-?\d+)\}", body)]
        elif cls in (4, 224) and not stripped:
            go = ref(body, rb"m_GameObject")
            father = ref(body, rb"m_Father")
            if go:
                go_of_transform[fid] = go[0]
                parent_of[go[0]] = father[0] if father else 0
    transform_of = {go: t for t, go in go_of_transform.items()}

    def live(go, depth=0):
        if depth > 64 or go not in active:
            return go not in active
        if not active[go]:
            return False
        father = parent_of.get(go, 0)
        if father == 0 or father not in go_of_transform:
            return True
        return live(go_of_transform[father], depth + 1)

    return {go: live(go) for go in active}, transform_of


def tally_file(path, tally, removed=frozenset(), overrides=None, depth=0, in_group=False):
    """Adds a scene's or prefab's content to the tally (for a copy: minus its removed components, with its overrides;
    in_group: the copy's root holds a LOD group the scene added, so its parts without a level of their own are in it)."""
    docs = file_docs(path)
    live, transform_of = active_objects(docs)
    transforms = {go: docs[t][2] for go, t in transform_of.items() if t in docs}
    overrides = overrides or {}
    lod_of = {}
    # LOD groups this file adds to copies of other files (the builder's FitLods on a placed model): those copies'
    # renderers are in a group.
    grouped_copies = set()
    for fid, (cls, stripped, body) in docs.items():
        if cls == 205 and not stripped and fid not in removed:
            tally.lod_groups += 1
            for level, block in enumerate(re.split(rb"- screenRelativeHeight:", body)[1:]):
                for r in re.findall(rb"renderer: \{fileID: (-?\d+)\}", block):
                    lod_of[int(r)] = level
            go = ref(body, rb"m_GameObject")
            if go and go[0] in docs and docs[go[0]][1]:
                copy = ref(docs[go[0]][2], rb"m_PrefabInstance")
                if copy:
                    grouped_copies.add(copy[0])
    filters = {}
    for fid, (cls, stripped, body) in docs.items():
        if cls == 33 and not stripped:
            go = ref(body, rb"m_GameObject")
            if go:
                filters[go[0]] = ref(body, rb"m_Mesh")
    for fid, (cls, stripped, body) in docs.items():
        if stripped or fid in removed:
            continue
        go = ref(body, rb"m_GameObject")
        on = live.get(go[0], True) if go else True
        if cls in (23, 137):
            if not on:
                continue
            o = overrides.get(fid, {})
            enabled = o.get("m_Enabled", value(body, rb"m_Enabled", b"1")) in (b"1", "1")
            cast = o.get("m_CastShadows", value(body, rb"m_CastShadows", b"1")) not in (b"0", "0")
            mats = ["%s:%s" % (g, f) for f, g in refs_list(body, rb"m_Materials")]
            mesh = filters.get(go[0]) if cls == 23 else ref(body, rb"m_Mesh")
            name = ""
            if go and go[0] in docs:
                n = value(docs[go[0]][2], rb"m_Name", b"")
                name = n.decode("utf-8", "replace") if isinstance(n, bytes) else ""
            tris, size = mesh_info(mesh, name)
            # Its own scale (the layout's boxes take their size from it; parents are mostly unscaled roots).
            scale = num3(transforms.get(go[0]), rb"m_LocalScale") if go else None
            if scale:
                size *= max(abs(v) for v in scale)
            tally.renderer(enabled, cast, mats, tris, lod_of.get(fid, 0 if in_group else -1), size)
            for m in mats:
                tally.material_refs.add(m)
        elif cls == 108 and on:
            mode = value(body, rb"m_Lightmapping", b"4")
            key = {b"4": "realtime", b"1": "mixed", b"2": "baked"}.get(mode, "realtime")
            tally.lights[key] += 1
            shadows = re.search(rb"m_Shadows:\s*\r?\n\s*m_Type: (\d)", body)
            if key != "baked" and shadows and shadows.group(1) != b"0":
                tally.live_shadows += 1
        elif cls == 114 and on and DECAL_SCRIPT.encode() in body:
            tally.decals += 1
        elif cls == 215 and on:
            tally.probes += 1
        elif cls in COLLIDERS and on:
            enabled = value(body, rb"m_Enabled", b"1") == b"1"
            if enabled:
                kind = COLLIDERS[cls]
                tally.colliders[kind] = tally.colliders.get(kind, 0) + 1
    # Prefab copies in this file.
    for fid, (cls, stripped, body) in docs.items():
        if cls != 1001 or depth > 8:
            continue
        source = ref(body, rb"m_SourcePrefab")
        if not source or source[1] not in PATHS:
            continue
        src = PATHS[source[1]]
        tally.copies += 1
        gone = set(r[0] for r in refs_list(body, rb"m_RemovedComponents"))
        changes = {}
        for m in re.finditer(rb"- target: \{fileID: (-?\d+), guid: [0-9a-f]{32}, type: \d+\}\s*\r?\n\s*propertyPath: ([^\r\n]+)\s*\r?\n\s*value: ([^\r\n]*)", body):
            changes.setdefault(int(m.group(1)), {})[m.group(2).strip().decode()] = m.group(3).strip()
        low = src.lower()
        grouped = fid in grouped_copies or in_group
        if low.endswith(".prefab"):
            tally_file(src, tally, frozenset(gone), changes, depth + 1, grouped)
        elif low.endswith((".fbx", ".obj")):
            parts = model_parts(src)
            if not parts:
                UNKNOWN[os.path.basename(src)] = UNKNOWN.get(os.path.basename(src), 0) + 1
                continue
            has_lod = any(p["lod"] >= 0 for p in parts)
            # The copy's own scale (the builder scales a fitted model on its root).
            scales = [float(v) for t in changes.values() for k, v in t.items() if k.startswith("m_LocalScale.") and re.match(rb"^-?[\d.eE]+$", v)]
            copy_scale = max((abs(v) for v in scales), default=1.0)
            single = changes.get(next(iter(changes), None), {}) if len(parts) == 1 else {}
            enabled = single.get("m_Enabled", b"1") != b"0"
            cast = single.get("m_CastShadows", b"1") != b"0"
            # The model's own materials, or what its import maps them to (its .meta's external objects).
            remap = model_materials(src)
            for p in parts:
                mats = []
                for m in p["materials"]:
                    target = remap.get(m)
                    mats.append(target if target else "%s/%s" % (os.path.basename(src), m))
                    if target:
                        tally.material_refs.add(target)
                tally.renderer(enabled, cast, mats, p["tris"], p["lod"] if has_lod else (0 if grouped else -1), p["size"] * copy_scale)


def measure(scene):
    tally = Tally()
    tally_file(scene, tally)
    # A copy given a LOD group in the scene (the builder's FitLods) has its renderers in it.
    docs = file_docs(scene)
    added_groups = sum(1 for cls, stripped, body in docs.values() if cls == 205 and not stripped and b"m_PrefabInstance" in body)
    result = tally.as_dict()
    result["lod_groups_on_copies"] = added_groups
    return result


report = {}
for scene in scenes:
    stats = measure(scene)
    report[os.path.basename(scene)] = stats
    s = stats
    lod_share = s["lod_renderers"] / max(1, s["renderers"])
    print(f"{os.path.basename(scene)}: renderers {s['renderers']} (draws {s['draws']}), tris {s['tris']:,}, materials {s['materials']} "
          f"(textures {s['textures']}, ~{s['texture_mb']} MB), "
          f"casters {s['casters']} (small {s['small_casters']}), LOD {s['lod_renderers']}/{s['renderers']} ({lod_share:.0%}), "
          f"groups {s['lod_groups']}, lights {s['lights']} live shadows {s['live_shadows']}, decals {s['decals']}, probes {s['probes']}, "
          f"colliders {s['colliders']}, copies {s['prefab_copies']}")
if UNKNOWN:
    print("unknown meshes/models (counted 0):", sorted(UNKNOWN.items(), key=lambda kv: -kv[1])[:12])
if out_json:
    with open(out_json, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=1)
