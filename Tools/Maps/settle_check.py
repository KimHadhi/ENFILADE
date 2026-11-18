"""What the builder's new rule (PropSolid settles; GreyboxBuilder.Standing) will do to the solid props a built scene
already has: each prop of a Tables/MapArt kind is asked to stand where the scene has it, against the layout's boxes and
the props before it. Prints the ones it will move or refuse.
usage: python sim_settle.py <MapArt.csv> <scene.unity> [skip x0 x1 z0 z1]"""
import csv
import math
import os
import sys
sys.path.insert(0, os.path.dirname(__file__))
import standing as st

art, scene = sys.argv[1], sys.argv[2]
skip = [float(v) for v in sys.argv[4:8]] if len(sys.argv) > 7 and sys.argv[3] == "skip" else None
kinds = set()
for row in csv.reader(open(art, encoding="utf-8-sig")):
    if row and not row[0].startswith("#"):
        kinds.add(row[0])
w = st.World()
props = []
for o, block in st.scene_blocks(scene):
    name = block.name[:-len(" (충돌)")] if block.prop else None
    if block.prop and name in kinds:
        if skip and skip[0] <= block.c[0] <= skip[1] and skip[2] <= block.c[2] <= skip[3]:
            continue
        props.append((name, block))
    else:
        w.blocks.append(block)
print(os.path.basename(scene), "layout boxes", len(w.blocks), "props of MapArt kinds", len(props))
# Lower ones first, so a prop stacked on another finds it there.
props.sort(key=lambda p: (p[1].min_y, p[1].c[0], p[1].c[2]))
for name, block in props:
    floor = (block.c[0], block.min_y, block.c[2])
    w.prop_solid(name, floor, block.size, block.yaw, cap=True)
moved = refused = 0
for kind_, name, asked, floor, why, block, half, yaw in w.log:
    if kind_ == "refused":
        refused += 1
        print("  REFUSED %-18s at (%.2f, %.2f, %.2f) size %.2fx%.2fx%.2f yaw %.0f: in %s" % (name, asked[0], asked[1], asked[2], half[0] * 2, half[1] * 2, half[2] * 2, yaw, why))
    elif kind_ == "moved":
        moved += 1
        print("  moved   %-18s (%.2f, %.2f, %.2f) by %.2f m out of %s" % (name, asked[0], asked[1], asked[2], math.hypot(floor[0] - asked[0], floor[2] - asked[2]), why))
print("  as they stand", len(props) - moved - refused, "moved", moved, "refused", refused, "| crossing after all", len(w.audit()))
