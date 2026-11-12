"""Plan one pack exported from Unreal (export_for_unity.py) on its own: every static mesh of it, or those named, with its
materials, as a job for build_props.py (copy mode), and the map dressing kinds (Tables/MapArt) the kinds file names.
plan_props.py plans the whole 11-12 batch at once; this one brings in a later pack without touching what is in already.

usage: python -I plan_unreal.py <pack> <dest under Assets> <out folder> [kinds.json]
  <pack>     a folder under F:/FabAssets/FromUnreal (its manifest.json)
  <dest>     e.g. Assets/ThirdParty/Dekogon/MilitaryTrainingFacility: each mesh goes to <dest>/<group>/<mesh>/<mesh>.fbx
             (<group> the folder it sat in under the pack: FiringRange, PipesVents), its materials to <dest>/Materials
  kinds.json {"kind": ["SM_Name", ...]}: the meshes each MapArt kind picks among (written to <out>/kinds.json as paths)

Material roles follow plan_props.py: texture parameters (the parent's underneath), then the textures named in the
T3D text, the packed map's channel layout taken from its parameter or suffix (RMA, ORM, ARM, AOMR).
"""
import json
import os
import re
import sys

FAB = "F:/FabAssets"
pack, dest_root, out = sys.argv[1], sys.argv[2].rstrip("/"), sys.argv[3]
kinds_in = json.load(open(sys.argv[4], encoding="utf-8")) if len(sys.argv) > 4 else {}
os.makedirs(out, exist_ok=True)
manifest = json.load(open("%s/FromUnreal/%s/manifest.json" % (FAB, pack), encoding="utf-8"))
assets = manifest["assets"]

COLOR = ["Albedo", "Albedo ", "Base Colour", "baseColor", "BaseColor", "Base Color", "Diffuse", "Albedo Texture", "DiffuseSkin1",
         "L1 Albedo ", "Albedo Tiling"]
# The Dekogon masters' defaults (MM_MasterMaterial_01a, MM_Master_PV_01a): what a vector is when no instance sets it.
VECTOR_DEFAULTS = {"Albedo Color Tint (Base)": (1, 1, 1), "Base Color Tint (Red)": (1, 0, 0),
                   "Base Color Tint (Green)": (0, 1, 0.087874), "Base Color Tint (Blue)": (0, 0.295874, 1),
                   "Base Color Tint (Mask)": (1, 0, 0), "Emissive Color Multi": (0, 0, 0)}
# Masters drawn see-through (the kit's glass): their colour comes from the instance's Glass Color, kept under 1.
GLASS_MASTERS = {"MM_Glass_01a": 0.28, "MM_Reinforced_Glass_01a": 0.4}
SWITCH = re.compile(r'\((?:Value=(True|False),)?ParameterInfo=\(Name="([^"]+)"\),bOverride=True')
NORMAL = ["Normal", "normal", "Normals", "Normal Map", "L1 Normal", "Normal Baked"]
T3D_REF = re.compile(r"Texture2D'\"?(/Game/[^'\"]+)")
SUFFIX = [("color", r"_(bc|d|diff|albedo|alb|basecolou?r)(_\d+a)?$"), ("normal", r"_(n|nrm|normal)(_\d+a)?$"),
          ("packed", r"_(rma|orm|arm|aomr|omr)(_\d+a)?$"), ("opacity", r"_(op|opc|opacity)(_\d+a)?$")]
problems = []


def ue_file(object_path):
    if not object_path:
        return None
    entry = assets.get(object_path.split(".")[0])
    if not entry or not entry.get("ok") or not entry.get("file"):
        return None
    return "%s/FromUnreal/%s/%s" % (FAB, pack, entry["file"])


def ue_material(object_path):
    entry = assets.get(object_path.split(".")[0], {})
    chain, names = [entry], [object_path.split(".")[-1]]
    parent = entry.get("parent")
    while parent:
        p = assets.get(parent.split(".")[0], {})
        chain.append(p)
        names.append(parent.split(".")[-1])
        parent = p.get("parent")
    params = {}
    for e in reversed(chain):
        params.update({k: v for k, v in (e.get("textures") or {}).items() if v})
    used = [t for e in chain for t in (e.get("used_textures") or []) if t]

    def first(keys, hints):
        for k in keys:
            if params.get(k):
                return params[k]
        for t in used:
            if any(re.search(h, t.split(".")[-1].lower()) for h in hints):
                return t
        return None

    mat = {"flip": True}
    mat["color"] = ue_file(first(COLOR, [r"_(d|bc|alb|albedo|basecolor|diffuse)$", r"albedo", r"diffuse", r"basecolo"]))
    mat["normal"] = ue_file(first(NORMAL, [r"_(n|nrm)$", r"normal"]))
    if params.get("AO Baked"):
        mat["ao"] = ue_file(params["AO Baked"])
    for key, layout in (("ARM", "ARM"), ("RMA", "RMA"), ("L1 RMA", "RMA"), ("ORM", "ORM"), ("ORM ", "ORM")):
        if params.get(key):
            mat["packed"] = {"path": ue_file(params[key]), "layout": layout}
            break
    else:
        packed = next((t for t in used if re.search(r"_(arm|rma|orm|mra)$", t.split(".")[-1].lower())), None)
        if packed:
            mat["packed"] = {"path": ue_file(packed), "layout": packed.split(".")[-1][-3:].upper()}
    refs = []
    for e in chain:
        t3d = "%s/FromUnreal/%s/%s" % (FAB, pack, e["file"]) if e.get("file") else None
        if t3d and os.path.exists(t3d):
            refs += T3D_REF.findall(open(t3d, encoding="utf-8", errors="replace").read())
    for ref in refs:
        name = ref.split(".")[-1].lower()
        for role, pattern in SUFFIX:
            if re.search(pattern, name) and not mat.get(role):
                path = ue_file(ref)
                if role == "packed":
                    tag = re.search(pattern, name).group(1).upper()
                    mat[role] = {"path": path, "layout": "OMR" if tag in ("AOMR", "OMR") else tag}
                else:
                    mat[role] = path
                break
    if isinstance(mat.get("packed"), dict) and not mat["packed"].get("path"):
        del mat["packed"]
    looks(mat, chain, names, params)
    return {k: v for k, v in mat.items() if v}


def looks(mat, chain, names, params):
    """What the Dekogon masters do beyond the maps, for build_props.py to bake: the albedo tints (Tint By Mask, one
    masked colour or three), the emissive panel, the cut-out from the albedo's alpha, and glass. Static switches come
    from each instance's T3D text, vectors and scalars from the manifest, the instance's own over its parents'."""
    switches, vectors, scalars = {}, {}, {}
    for e in reversed(chain):
        t3d = "%s/FromUnreal/%s/%s" % (FAB, pack, e["file"]) if e.get("file") else None
        if t3d and os.path.exists(t3d) and str(e.get("class", "")).startswith("MaterialInstance"):
            for m in SWITCH.finditer(open(t3d, encoding="utf-8", errors="replace").read()):
                switches[m.group(2)] = m.group(1) == "True"
        vectors.update({k: tuple(v[:3]) for k, v in (e.get("vectors") or {}).items() if v})
        scalars.update(e.get("scalars") or {})

    def vec(name):
        return tuple(vectors.get(name, VECTOR_DEFAULTS.get(name, (1, 1, 1))))

    master = names[-1]
    if master in GLASS_MASTERS:
        c = vectors.get("Glass Color", (0.6, 0.65, 0.7))
        top = max(c[:3])
        c = [x / top * 0.85 for x in c[:3]] if top > 1 else list(c[:3])
        mat["glass"] = [round(x, 3) for x in c] + [GLASS_MASTERS[master]]
        # The master's fill maps (flat normal, mid roughness) would only dull it.
        for key in ("color", "normal", "packed", "ao"):
            mat.pop(key, None)
        return
    layered = master == "MM_Surface_VertBlend_01a"
    tint = {}
    base = vec(("L1 " if layered else "") + "Albedo Color Tint (Base)")
    if any(abs(x - 1) > 1e-3 for x in base):
        tint["base"] = base
    mask = ue_file(params.get("Albedo Multi Tint Mask ")) if not layered and switches.get("Tint By Mask?") else None
    if mask:
        if switches.get("Use Base Color Tint (Multiple)?"):
            tint.update(mode="multi", mask=mask, red=vec("Base Color Tint (Red)"), green=vec("Base Color Tint (Green)"),
                        blue=vec("Base Color Tint (Blue)"))
        else:
            tint.update(mode="mask", mask=mask, maskTint=vec("Base Color Tint (Mask)"))
    if tint:
        tint.setdefault("base", (1, 1, 1))
        mat["tint"] = tint
    if switches.get("Emissive?") and params.get("Emissive") and max(vec("Emissive Color Multi")) > 0:
        mat["emission"] = {"path": ue_file(params["Emissive"]), "color": vec("Emissive Color Multi"),
                           "power": scalars.get("Emissive Power", 1), "desat": scalars.get("Emissive Desat", 0),
                           "intensity": scalars.get("Emissive Intensity", 1)}
    if switches.get("Opacity?") and switches.get("Opacity in Albedo Alpha?", True):
        mat["opacityAlpha"] = True


job = {"materials": {}, "props": [], "blend": [], "kinds": {}}
wanted = {name for names in kinds_in.values() for name in names}
where = {}
for package, entry in sorted(assets.items()):
    if entry.get("class") != "StaticMesh" or not entry.get("ok") or not entry.get("file"):
        continue
    asset = package.split("/")[-1]
    # With a kinds file, only the meshes it names come in (a pack's other 300 stay in the export until wanted).
    if wanted and asset not in wanted:
        continue
    group = entry["file"].split("/")[0]
    dest = "%s/%s/%s" % (dest_root, group, asset)
    remap = {}
    for slot in entry.get("slots", []):
        mi = slot.get("material")
        if not mi:
            continue
        mi_name = mi.split(".")[-1]
        mat_id = "%s/%s" % (pack, mi_name)
        if mat_id not in job["materials"]:
            m = ue_material(mi)
            if "color" not in m and "glass" not in m:
                problems.append("material without colour: " + mi_name)
            m.update(dest=dest_root + "/Materials", name=mi_name)
            job["materials"][mat_id] = m
        remap[mi_name] = mat_id
    job["props"].append({"src": "%s/FromUnreal/%s/%s" % (FAB, pack, entry["file"]), "dest": dest, "file": asset + ".fbx",
                         "remap": remap, "axis": 1, "skeletal": False})
    where[asset] = dest + "/" + asset + ".fbx"
for kind, names in kinds_in.items():
    for name in names:
        if name not in where:
            problems.append("kind %s: no mesh %s" % (kind, name))
            continue
        path = where[name]
        job["kinds"].setdefault(kind, []).append(path[len("Assets/"):] if path.startswith("Assets/") else path)
with open(os.path.join(out, "job.json"), "w", encoding="utf-8") as f:
    json.dump(job, f, ensure_ascii=False, indent=1)
with open(os.path.join(out, "kinds.json"), "w", encoding="utf-8") as f:
    json.dump(job["kinds"], f, ensure_ascii=False, indent=1)
print("%s: materials %d, meshes %d, kinds %d" % (pack, len(job["materials"]), len(job["props"]), len(job["kinds"])))
print("problems:", json.dumps(problems, ensure_ascii=False, indent=0) if problems else "none")
