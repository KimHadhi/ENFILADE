# Run inside Blender: every .tga under a folder becomes a .png with the same pixels (alpha kept); its .meta moves to
# the new name so the GUID (and every material pointing at it) stays. Usage: blender -b --python tga_to_png.py -- <folder>
import os
import struct
import sys

import bpy

root = sys.argv[sys.argv.index("--") + 1]
done = 0
for base, _, files in os.walk(root):
    for name in files:
        if not name.lower().endswith(".tga"):
            continue
        tga = os.path.join(base, name)
        png = tga[:-4] + ".png"
        image = bpy.data.images.load(tga, check_existing=False)
        image.colorspace_settings.name = "Non-Color"
        # Blender reads the pixels lazily; asking for the size loads them before the save.
        if image.size[0] == 0 or not image.has_data:
            print("[tga] FAILED to read", tga)
            continue
        image.filepath_raw = png
        image.file_format = "PNG"
        image.save()
        bpy.data.images.remove(image)
        with open(png, "rb") as f:
            head = f.read(26)
        ok = head[:8] == b"\x89PNG\r\n\x1a\n" and struct.unpack(">II", head[16:24])[0] > 0
        if not ok:
            print("[tga] FAILED", tga)
            continue
        if os.path.exists(tga + ".meta"):
            os.replace(tga + ".meta", png + ".meta")
        os.remove(tga)
        done += 1
print("[tga] converted", done)
