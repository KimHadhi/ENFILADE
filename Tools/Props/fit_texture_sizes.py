"""Map textures sized to what wears them (the user, 2026-11-14: "맵 최적화"): a bought prop's textures came in at up to
2048 whatever the prop's size, so a can or a cone took as much video memory as a wall. Each texture a map's props
use gets the import cap its largest wearer needs: TEXEL pixels per metre of the prop's longest side, as a power of
two from LEAST to MOST (a 0.2 m can 512, a 0.4 m crate or bottle 1024, 0.5 m and more 2048: a prop's UVs wrap its whole surface, so a texture covers more than its longest side). Only caps that go down are written;
the images stay as they are, so raising a cap again brings the detail back.
Left alone: the maps' own surfaces (Art/Textures/Surfaces, tiled in metres over whole walls), the Heist pack (tools held
in the hand), and every material something besides a map uses (the project's prefabs and assets: pickups, the hub's
items, held things). Props' sizes and their materials are read from the scenes as scene_stats.py reads them.

usage: python -I fit_texture_sizes.py <project root> [--write] <scene.unity> [<scene.unity> ...]
"""
import collections
import math
import os
import re
import sys

TEXEL, LEAST, MOST = 2048, 512, 2048

args = sys.argv[1:]
write = "--write" in args
args = [a for a in args if a != "--write"]
root, scenes = args[0], args[1:]
sys.argv = [sys.argv[0], root] + scenes
here = os.path.dirname(os.path.abspath(__file__))
source = open(os.path.join(here, "scene_stats.py"), encoding="utf-8").read()
definitions, _ = source.split("\nreport = {}\n", 1)
stats = {"__name__": "fit_texture_sizes"}
exec(compile(definitions, "scene_stats.py", "exec"), stats)

# Materials used outside the maps keep their textures as they are.
elsewhere = set()
for base, _, files in os.walk(os.path.join(root, "Assets", "_Project")):
    for name in files:
        if name.endswith((".prefab", ".asset", ".controller")):
            try:
                body = open(os.path.join(base, name), "rb").read()
            except OSError:
                continue
            elsewhere.update(g.decode() for g in re.findall(rb"guid: ([0-9a-f]{32})", body))

Tally = stats["Tally"]
largest = collections.defaultdict(float)
counted = Tally.renderer


def renderer(self, enabled, cast, materials, tris, lod, size):
    counted(self, enabled, cast, materials, tris, lod, size)
    for material in materials:
        # A size not known counts as big: its textures keep their cap.
        largest[material] = max(largest[material], size if size > 0 else 99.0)


Tally.renderer = renderer
for scene in scenes:
    stats["tally_file"](scene, Tally())

need = {}
for key, size in largest.items():
    if key.split(":")[0] in elsewhere:
        continue
    for guid in stats["material_textures"](key):
        px = min(MOST, max(LEAST, 2 ** math.ceil(math.log2(max(1.0, size * TEXEL)))))
        need[guid] = max(need.get(guid, 0), px)

saved = collections.Counter()
changes = []
for guid, px in need.items():
    cost = stats["texture_cost"](guid)
    if not cost:
        continue
    path, w, h, mb = cost
    rel = os.path.relpath(path, root).replace(os.sep, "/")
    if rel.startswith("Assets/_Project/Art/Textures/Surfaces") or "/SierraDivision/" in rel or max(w, h) <= px:
        continue
    scale = max(w, h) / px
    saved[rel.split("/")[2] if rel.startswith("Assets/ThirdParty/") else "Assets"] += mb - mb / (scale * scale)
    changes.append((rel, max(w, h), px, mb - mb / (scale * scale)))
    if write:
        meta = path + ".meta"
        with open(meta, "rb") as f:
            data = f.read()
        lowered = re.sub(rb"maxTextureSize: (\d+)", lambda m: b"maxTextureSize: " + str(min(int(m.group(1)), px)).encode(), data)
        if lowered != data:
            with open(meta, "wb") as f:
                f.write(lowered)

print("%s %d textures, %.0f MB less on the GPU" % ("lowered" if write else "would lower", len(changes), sum(saved.values())))
for pack, mb in saved.most_common():
    print("  %6.0f MB  %s" % (mb, pack))
for rel, was, now, mb in sorted(changes, key=lambda c: -c[3])[:15]:
    print("  %4d -> %4d  %s" % (was, now, rel))
