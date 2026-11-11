# Blender (headless): Source "mod2x" blood decals -> textures the game's two blood systems can use.
# usage: blender -b --factory-startup --python addon_blood_decals.py -- <splat_out_dir> <decal_out_dir> <image> [more ...]
#   splat_out_dir: white detail + alpha, for ImpactVfx's per-hit splats (FeelParticle multiplies by the blood colour)
#   decal_out_dir: blood colour + alpha and a normal map from its thickness, for URP decal projectors (GoreLibrary)
#
# Why the conversion: the Cry of Fear decals (L4D2 workshop 3612824343) are mod2x textures, drawn as dst * 2 * src, so
# mid grey (0.5) leaves the wall alone and only darker, redder texels show. Unity's decals blend by alpha instead, so the
# coverage is read back from how far a texel's green/blue drop below the grey (blood is red, the grey is grey), and the
# colour is what the decal would have made of a mid-grey wall. The edge is faded over a few texels so nothing square shows.
import os
import sys

import bpy
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
splat_dir, decal_dir, sources = os.path.abspath(args[0]), os.path.abspath(args[1]), [os.path.abspath(a) for a in args[2:]]
os.makedirs(splat_dir, exist_ok=True)
os.makedirs(decal_dir, exist_ok=True)
GREY, SPAN, EDGE = .47, .42, 6


def blur(a, passes=2):
    for _ in range(passes):
        p = np.pad(a, 1, mode="edge")
        a = (p[:-2, :-2] + p[:-2, 1:-1] + p[:-2, 2:] + p[1:-1, :-2] + p[1:-1, 1:-1] + p[1:-1, 2:] + p[2:, :-2] + p[2:, 1:-1] + p[2:, 2:]) / 9.0
    return a


def save(name, folder, rgba, w, h):
    image = bpy.data.images.new(name, w, h, alpha=True)
    image.pixels.foreach_set(rgba.astype(np.float32).ravel())
    image.filepath_raw = os.path.join(folder, name + ".png")
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)


for path in sources:
    src = bpy.data.images.load(path)
    w, h = src.size
    px = np.empty(w * h * 4, np.float32)
    src.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)
    r, g, b = px[..., 0], px[..., 1], px[..., 2]
    coverage = np.clip((GREY - np.maximum(g, b)) / SPAN, 0, 1) ** .8
    yy, xx = np.mgrid[0:h, 0:w]
    border = np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy))
    coverage *= np.clip(border / EDGE, 0, 1)
    name = os.path.splitext(os.path.basename(path))[0]

    detail = np.clip(.55 + .9 * r, .55, 1.0)
    splat = np.dstack([detail, detail, detail, coverage])
    save("cof_" + name, splat_dir, splat, w, h)

    colour = np.clip(px[..., :3] * 2 * .85, 0, 1)
    decal = np.dstack([colour, coverage])
    save("cof_" + name, decal_dir, decal, w, h)

    height = blur(coverage, 3)
    dx = np.zeros_like(height); dy = np.zeros_like(height)
    dx[:, 1:-1] = height[:, 2:] - height[:, :-2]
    dy[1:-1, :] = height[2:, :] - height[:-2, :]
    n = np.dstack([-dx * 3.0, -dy * 3.0, np.ones_like(height)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    normal = np.dstack([n * .5 + .5, np.ones_like(height)])
    save("cof_" + name + "_Normal", decal_dir, normal, w, h)
    print(f"OK {name} {w}x{h} coverage={coverage.mean():.3f}")
    bpy.data.images.remove(src)
