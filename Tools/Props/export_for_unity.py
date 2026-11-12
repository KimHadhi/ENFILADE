# Run inside Unreal (UnrealEditor-Cmd -run=pythonscript). Exports the user's three Unreal-only packs for Unity:
# meshes as FBX (LODs and vertex colours kept, no collision hulls), textures as PNG (EXR/TGA when PNG can't hold them),
# materials, material functions and blueprints as T3D text, and one manifest.json per pack listing every mesh's
# material slots, LODs, triangle counts, Nanite flag and bounds, and every material's parent, texture/scalar/vector
# parameters and texture settings, so the Unity side rebuilds URP materials without guessing.
# Progress goes to <OUT>/export_log.txt line by line (the commandlet's own log is too noisy to follow).
import json
import os
import time
import traceback

import unreal

OUT = "F:/FabAssets/FromUnreal"
PACKS = [
    ("/Game/VigilanteContent", "Vigilante"),
    ("/Game/MOUT_Fabrication", "MOUT"),
    ("/Game/MilitaryFiringRange", "MilitaryTrainingFacility"),
]
TEXT_CLASSES = {
    "Material", "MaterialInstanceConstant", "MaterialFunction", "MaterialFunctionMaterialLayer",
    "MaterialFunctionMaterialLayerBlend", "Blueprint",
}
MESH_CLASSES = {"StaticMesh", "SkeletalMesh"}
TEXTURE_CLASSES = {"Texture2D"}
# Particle effects don't survive the trip to Unity (Niagara/Cascade), so their meshes and textures stay behind.
SKIP_FOLDERS = ("/particles/", "/fx/", "/vfx/", "/effects/")

os.makedirs(OUT, exist_ok=True)
LOG = open(os.path.join(OUT, "export_log.txt"), "a", encoding="utf-8")
# Crash recovery: the asset being exported is written here first; if Unreal dies on it, the next run finds it here,
# lists it in crashed.txt and skips it, so one bad asset can't stop the rest. Finished files are not exported again.
CURRENT = os.path.join(OUT, "current.txt")
CRASHED = os.path.join(OUT, "crashed.txt")


def log(message):
    line = time.strftime("[%H:%M:%S] ") + message
    LOG.write(line + "\n")
    LOG.flush()
    unreal.log("[ExportForUnity] " + message)


def safe(read, fallback=None):
    try:
        return read()
    except Exception:
        return fallback


def class_of(data):
    path = safe(lambda: data.asset_class_path)
    if path is not None:
        return str(path.asset_name)
    return str(safe(lambda: data.asset_class, ""))


def path_of(obj):
    return obj.get_path_name() if obj is not None else None


def fbx_options():
    options = unreal.FbxExportOption()
    for name, value in (("ascii", False), ("force_front_x_axis", False), ("vertex_color", True),
                        ("level_of_detail", True), ("collision", False), ("export_source_mesh", False),
                        ("export_morph_targets", False), ("export_preview_mesh", False)):
        try:
            options.set_editor_property(name, value)
        except Exception as error:
            log("fbx option %s not set: %s" % (name, error))
    return options


FBX = fbx_options()


def export(obj, filename, options=None):
    if os.path.exists(filename) and os.path.getsize(filename) > 0:
        return True, []
    os.makedirs(os.path.dirname(filename), exist_ok=True)
    task = unreal.AssetExportTask()
    task.set_editor_property("object", obj)
    task.set_editor_property("filename", filename)
    task.set_editor_property("automated", True)
    task.set_editor_property("prompt", False)
    task.set_editor_property("replace_identical", True)
    if options is not None:
        task.set_editor_property("options", options)
    ok = unreal.Exporter.run_asset_export_task(task)
    errors = [str(e) for e in safe(lambda: task.get_editor_property("errors"), [])]
    return bool(ok) and os.path.exists(filename) and os.path.getsize(filename) > 0, errors


def mesh_entry(obj, kind):
    entry = {"class": kind}
    if kind == "StaticMesh":
        slots = safe(lambda: obj.get_editor_property("static_materials"), [])
        entry["slots"] = [{"slot": str(s.get_editor_property("material_slot_name")),
                           "material": path_of(s.get_editor_property("material_interface"))} for s in slots]
        lods = safe(lambda: obj.get_num_lods(), 0)
        entry["lods"] = lods
        entry["tris"] = [safe(lambda i=i: obj.get_num_triangles(i)) for i in range(lods or 0)]
        entry["nanite"] = safe(lambda: obj.get_editor_property("nanite_settings").get_editor_property("enabled"))
        box = safe(lambda: obj.get_bounding_box())
        if box is not None:
            entry["min"] = [box.min.x, box.min.y, box.min.z]
            entry["max"] = [box.max.x, box.max.y, box.max.z]
    else:
        slots = safe(lambda: obj.get_editor_property("materials"), [])
        entry["slots"] = [{"slot": str(s.get_editor_property("material_slot_name")),
                           "material": path_of(s.get_editor_property("material_interface"))} for s in slots]
        bounds = safe(lambda: obj.get_bounds())
        if bounds is not None:
            entry["extent"] = [bounds.box_extent.x, bounds.box_extent.y, bounds.box_extent.z]
    return entry


def material_entry(obj, kind):
    entry = {"class": kind}
    if kind == "MaterialInstanceConstant":
        entry["parent"] = path_of(safe(lambda: obj.get_editor_property("parent")))
        entry["textures"] = {str(p.parameter_info.name): path_of(p.parameter_value)
                             for p in safe(lambda: obj.get_editor_property("texture_parameter_values"), [])}
        entry["scalars"] = {str(p.parameter_info.name): p.parameter_value
                            for p in safe(lambda: obj.get_editor_property("scalar_parameter_values"), [])}
        entry["vectors"] = {str(p.parameter_info.name): [p.parameter_value.r, p.parameter_value.g,
                                                         p.parameter_value.b, p.parameter_value.a]
                            for p in safe(lambda: obj.get_editor_property("vector_parameter_values"), [])}
    elif kind == "Material":
        library = safe(lambda: unreal.MaterialEditingLibrary)
        if library is not None:
            names = safe(lambda: library.get_texture_parameter_names(obj), [])
            entry["textures"] = {str(n): path_of(safe(lambda n=n: library.get_material_default_texture_parameter_value(obj, n)))
                                 for n in names}
            names = safe(lambda: library.get_scalar_parameter_names(obj), [])
            entry["scalars"] = {str(n): safe(lambda n=n: library.get_material_default_scalar_parameter_value(obj, n))
                                for n in names}
            entry["used_textures"] = [path_of(t) for t in safe(lambda: library.get_used_textures(obj), [])]
        entry["blend_mode"] = str(safe(lambda: obj.get_editor_property("blend_mode")))
        entry["two_sided"] = safe(lambda: obj.get_editor_property("two_sided"))
    return entry


def texture_entry(obj):
    return {
        "class": "Texture2D",
        "compression": str(safe(lambda: obj.get_editor_property("compression_settings"))),
        "srgb": safe(lambda: obj.get_editor_property("srgb")),
        "flip_green": safe(lambda: obj.get_editor_property("flip_green_channel")),
        "lod_group": str(safe(lambda: obj.get_editor_property("lod_group"))),
    }


def crashed_list():
    if os.path.exists(CURRENT):
        with open(CURRENT, encoding="utf-8") as f:
            culprit = f.read().strip()
        if culprit:
            with open(CRASHED, "a", encoding="utf-8") as f:
                f.write(culprit + "\n")
            log("previous run died on %s; skipping it from now on" % culprit)
        os.remove(CURRENT)
    if not os.path.exists(CRASHED):
        return set()
    with open(CRASHED, encoding="utf-8") as f:
        return {line.strip() for line in f if line.strip()}


def export_pack(root, name, crashed):
    target = os.path.join(OUT, name)
    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    assets = list(registry.get_assets_by_path(root, recursive=True))
    log("%s: %d assets under %s" % (name, len(assets), root))
    manifest = {"pack": name, "root": root, "assets": {}, "skipped": {}, "crashed": sorted(crashed)}
    done = failed = 0
    for index, data in enumerate(assets):
        kind = class_of(data)
        package = str(data.package_name)
        relative = package[len(root):].lstrip("/")
        base = os.path.join(target, relative.replace("/", os.sep))
        if kind not in MESH_CLASSES and kind not in TEXTURE_CLASSES and kind not in TEXT_CLASSES:
            manifest["skipped"][kind] = manifest["skipped"].get(kind, 0) + 1
            continue
        if any(folder in package.lower() + "/" for folder in SKIP_FOLDERS):
            manifest["skipped"]["effects"] = manifest["skipped"].get("effects", 0) + 1
            continue
        if package in crashed:
            manifest["assets"][package] = {"class": kind, "ok": False, "error": "Unreal crashed exporting this"}
            failed += 1
            continue
        with open(CURRENT, "w", encoding="utf-8") as f:
            f.write(package)
        try:
            obj = unreal.load_asset(package + "." + str(data.asset_name))
            if obj is None:
                raise RuntimeError("did not load")
            if kind in MESH_CLASSES:
                entry = mesh_entry(obj, kind)
                ok, errors = export(obj, base + ".fbx", FBX)
                entry["file"] = relative + ".fbx"
            elif kind in TEXTURE_CLASSES:
                entry = texture_entry(obj)
                ok, errors = False, []
                for extension in (".png", ".exr", ".tga"):
                    ok, errors = export(obj, base + extension)
                    if ok:
                        entry["file"] = relative + extension
                        break
            else:
                entry = material_entry(obj, kind)
                ok, errors = export(obj, base + ".t3d")
                entry["file"] = relative + ".t3d"
            entry["ok"] = ok
            if errors:
                entry["errors"] = errors
            manifest["assets"][package] = entry
            if ok:
                done += 1
            else:
                failed += 1
                log("FAILED %s %s %s" % (kind, package, "; ".join(errors)))
        except Exception:
            failed += 1
            manifest["assets"][package] = {"class": kind, "ok": False, "error": traceback.format_exc(limit=2)}
            log("ERROR %s %s\n%s" % (kind, package, traceback.format_exc(limit=2)))
        if os.path.exists(CURRENT):
            os.remove(CURRENT)
        if (index + 1) % 25 == 0:
            log("%s %d/%d (ok %d, failed %d)" % (name, index + 1, len(assets), done, failed))
            unreal.SystemLibrary.collect_garbage()
    with open(os.path.join(target, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=1, default=str)
    log("%s finished: ok %d, failed %d, skipped %s" % (name, done, failed, json.dumps(manifest["skipped"])))


def main():
    log("start")
    crashed = crashed_list()
    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    registry.scan_paths_synchronous([root for root, _ in PACKS], True)
    for root, name in PACKS:
        try:
            export_pack(root, name, crashed)
        except Exception:
            log("PACK ERROR %s\n%s" % (name, traceback.format_exc()))
        unreal.SystemLibrary.collect_garbage()
    log("DONE")


main()
LOG.close()
