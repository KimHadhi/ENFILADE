# Run inside Blender: blender -b --factory-startup --disable-autoexec <file.blend> --python inspect_blend.py -- <out.json>
# Lists every mesh object (triangles after modifiers, materials, size), every image (packed or external, found or
# missing, pixel size) and, per material, which image feeds which shader input, so the Unity side knows whether the
# file is complete and how to rebuild its materials.
import json
import os
import sys

import bpy

out = sys.argv[sys.argv.index("--") + 1]
depsgraph = bpy.context.evaluated_depsgraph_get()
report = {"file": bpy.data.filepath, "objects": [], "images": [], "materials": {}}

for obj in bpy.data.objects:
    if obj.type != "MESH":
        continue
    entry = {"name": obj.name, "materials": [m.name if m else None for m in obj.data.materials],
             "collections": [c.name for c in obj.users_collection], "size": [round(v, 3) for v in obj.dimensions],
             "modifiers": [m.type for m in obj.modifiers], "hidden": obj.hide_get() or obj.hide_render,
             "parent": obj.parent.name if obj.parent else None}
    try:
        mesh = obj.evaluated_get(depsgraph).to_mesh()
        entry["tris"] = sum(len(p.vertices) - 2 for p in mesh.polygons)
        obj.evaluated_get(depsgraph).to_mesh_clear()
    except Exception as error:
        entry["tris"] = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        entry["eval_error"] = repr(error)
    report["objects"].append(entry)

for image in bpy.data.images:
    path = bpy.path.abspath(image.filepath) if image.filepath else ""
    entry = {"name": image.name, "packed": image.packed_file is not None, "path": image.filepath,
             "exists": bool(path) and os.path.exists(path), "source": image.source}
    try:
        entry["size"] = list(image.size)
    except Exception:
        entry["size"] = None
    report["images"].append(entry)


def feeds(node, seen=None):
    """Names of the shader inputs an image node ends up driving (through normal maps, mixes, separates)."""
    seen = seen or set()
    found = []
    for output in node.outputs:
        for link in output.links:
            target = link.to_node
            if target.name in seen:
                continue
            seen.add(target.name)
            if target.type in ("BSDF_PRINCIPLED", "OUTPUT_MATERIAL", "EMISSION", "BSDF_DIFFUSE"):
                found.append(link.to_socket.name)
            else:
                found.extend(target.type + ">" + name for name in feeds(target, seen))
    return found


for material in bpy.data.materials:
    if not material.use_nodes or material.node_tree is None:
        continue
    images = []
    for node in material.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image is not None:
            images.append({"image": node.image.name, "feeds": feeds(node),
                           "colorspace": node.image.colorspace_settings.name})
    report["materials"][material.name] = images

with open(out, "w", encoding="utf-8") as f:
    json.dump(report, f, ensure_ascii=False, indent=1)
print("INSPECTED", len(report["objects"]), "objects", len(report["images"]), "images")
