"""Several same-size PNG renders on one sheet, in rows of <columns>, each scaled by <scale>.
usage: blender -b --factory-startup --disable-autoexec --python contact_sheet.py -- <out.png> <columns> <scale> <in.png> ..."""
import sys

import bpy
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
out, columns, scale, files = args[0], int(args[1]), float(args[2]), args[3:]
images = [bpy.data.images.load(f) for f in files]
w0, h0 = images[0].size
w, h = int(w0 * scale), int(h0 * scale)
rows = (len(images) + columns - 1) // columns
gap = 8
sheet = np.ones(((h + gap) * rows + gap, (w + gap) * columns + gap, 4), dtype=np.float32)
for i, img in enumerate(images):
    if img.size[0] != w0 or img.size[1] != h0:
        img.scale(w0, h0)
    img.scale(w, h)
    pixels = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    r, c = divmod(i, columns)
    # Blender images run bottom-up: the first row of the sheet is its top.
    y0 = (rows - 1 - r) * (h + gap) + gap
    x0 = c * (w + gap) + gap
    sheet[y0:y0 + h, x0:x0 + w] = pixels
result = bpy.data.images.new("sheet", sheet.shape[1], sheet.shape[0], alpha=True)
result.pixels = sheet.ravel()
result.filepath_raw = out
result.file_format = "PNG"
result.save()
print("[sheet] wrote", out, sheet.shape[1], "x", sheet.shape[0])
