# Blender (headless): a viewmodel hand animation in a Source MDL -> one arm's path as table rows, from a recipe.
# usage: blender -b --factory-startup --python hand_path.py -- <recipe.json> <addon_folder> <out.csv>
#   recipe.json: Tools/Blender/HandPaths/<Name>.json (which model, which view bone, which arm's bones, which clips in
#   order, whether to mirror a right hand onto the left, which tag is the thing in the hand)
#   addon_folder: the addon unpacked with Tools/Extract-Gma.ps1 (paths inside it as in the addon), outside the repo.
#
# What comes out is what the arm does, not where a rig's bones are (the same as vmanip_throw_path.py, which this
# generalises): per frame, the wrist and the elbow from the shoulder in arm lengths, which way the hand runs (wrist to
# middle knuckle) and faces (out of the palm), how far the fingers and the thumb close, and whether the thing is in the
# hand; in the view bone's axes turned to Unity's camera axes (x right, y up, z forward). GrenadeArm fits it to any
# humanoid's left arm from its own shoulder. A right-handed clip (a knife thrown with the right hand while the gun is
# away) is mirrored left-right, so our left hand throws it while the gun stays in the right.
# The clips are laid end to end; a row's cycle is its second at the clip's own speed (frames / fps), and the
# printout names the moments to put in Tables/ThrowArms (where each clip ends, where the wrist is furthest forward).
import csv
import json
import math
import os
import sys

import addon_utils
import bpy  # noqa: F401  (Blender must be up for SourceIO)
from mathutils import Matrix, Quaternion, Vector

args = sys.argv[sys.argv.index("--") + 1:]
recipe_path, addon_root, out_csv = (os.path.abspath(a) for a in args[:3])
with open(recipe_path, encoding="utf-8") as f:
    recipe = json.load(f)

addon_utils.enable("SourceIO-master", default_set=False, persistent=False)
from SourceIO.library.models.mdl.v49.mdl_file import MdlV49  # noqa: E402
from SourceIO.library.utils import FileBuffer  # noqa: E402

with FileBuffer(os.path.join(addon_root, recipe["mdl"].replace("/", os.sep))) as f:
    mdl = MdlV49.from_buffer(f)
idx = {b.name: i for i, b in enumerate(mdl.bones)}
descs = {d.name.lstrip("@"): i for i, d in enumerate(mdl.anim_descs)}
INCH = 0.0254
mirror = bool(recipe.get("mirror", False))
arm = recipe["arm"]
fingers = recipe["fingers"]


def quat(r):
    return Quaternion((r[3], r[0], r[1], r[2]))


def world_at(data, fr):
    pos_all, rot_all = data["pos"], data["rot"]
    out = [None] * len(mdl.bones)
    for i, b in enumerate(mdl.bones):
        local = Matrix.Translation(Vector(pos_all[fr, i])) @ quat(rot_all[fr, i]).to_matrix().to_4x4()
        out[i] = local if b.parent_id == -1 else out[b.parent_id] @ local
    return out


def to_view(v):
    """Source axes (x forward, y left, z up) -> Unity camera axes (x right, y up, z forward), mirrored if asked."""
    u = Vector((-v.y, v.z, v.x))
    if mirror:
        u.x = -u.x
    return u


def cross(a, b):
    """Unity's Vector3.Cross on vectors already in Unity's axes."""
    return Vector((a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x))


def bend(a, b):
    return math.degrees(a.angle(b)) if a.length > 1e-6 and b.length > 1e-6 else 0.0


rows, moments = [], []
offset = 0.0
for part in recipe["parts"]:
    n = descs.get(part["anim"])
    if n is None:
        sys.exit("no clip %s" % part["anim"])
    data = mdl.animations[n]
    frames = int(data.shape[0])
    fps = float(part.get("fps") or mdl.anim_descs[n].fps or 30.0)
    # A clip can start a little after the last one ends, as the engine fades one sequence into the next.
    offset += float(part.get("lead", 0.0))
    released_from = part.get("released_from")
    best, best_frame = -1e9, 0
    for fr in range(frames):
        w = world_at(data, fr)
        view = w[idx[recipe["view"]]].inverted()

        def at(name):
            return to_view((view @ w[idx[name]]).to_translation()) * INCH

        shoulder, elbow, wrist = at(arm["shoulder"]), at(arm["elbow"]), at(arm["wrist"])
        length = (elbow - shoulder).length + (wrist - elbow).length
        middle, index, little = at(fingers["middle"][0]), at(fingers["index"][0]), at(fingers["little"][0])
        along = (middle - wrist).normalized()
        palm = -cross(along, index - little)
        palm = (palm - along * palm.dot(along)).normalized()

        def curl(names):
            j = [at(name) for name in names]
            first, second = j[1] - j[0], j[2] - j[1]
            return bend(middle - wrist, first) + bend(first, second) * 1.7

        fingers_curl = sum(curl(fingers[k]) for k in ("index", "middle", "ring", "little")) / 4.0
        thumb = curl(fingers["thumb"])
        held = 1 if released_from is None or fr < released_from else 0
        cycle = offset + fr / fps
        rows.append([cycle, *((wrist - shoulder) / length), *((elbow - shoulder) / length), *along, *palm, fingers_curl, thumb, held])
        if wrist.z > best:
            best, best_frame = wrist.z, fr
        if "held" in recipe and fr == 0 and part is recipe["parts"][0]:
            # The thing in the hand, in the hand's own axes (x across to the little finger, y out of the palm, z along),
            # in hand lengths, and its tag's axes there, for Tables/Grenades held*.
            frame_x = cross(palm, along)
            tag = view @ w[idx[recipe["held"]]]
            off = at(recipe["held"]) - wrist
            hand = (middle - wrist).length
            axes = [to_view(tag.to_3x3() @ Vector(axis)) for axis in ((1, 0, 0), (0, 1, 0), (0, 0, 1))]
            print("HELD across %.2f palm %.2f along %.2f (hand lengths, hand %.3f m)" % (
                off.dot(frame_x) / hand, off.dot(palm) / hand, off.dot(along) / hand, hand))
            for name, axis in zip("XYZ", axes):
                print("  tag %s in hand axes: across %+.2f palm %+.2f along %+.2f" % (name, axis.dot(frame_x), axis.dot(palm), axis.dot(along)))
    moments.append("%s: %.3f..%.3f s (%d frames at %g fps), wrist furthest forward at %.3f (frame %d)" % (
        part["anim"], offset, offset + (frames - 1) / fps, frames, fps, offset + best_frame / fps, best_frame))
    offset += frames / fps

with open(out_csv, "w", newline="", encoding="utf-8-sig") as f:
    out = csv.writer(f)
    out.writerow(["id", "cycle", "wristX", "wristY", "wristZ", "elbowX", "elbowY", "elbowZ",
                  "alongX", "alongY", "alongZ", "palmX", "palmY", "palmZ", "curl", "thumb", "held"])
    for n, r in enumerate(rows):
        out.writerow(["k%02d" % n] + ["%.3f" % v for v in r[:-1]] + [str(r[-1])])
for line in moments:
    print("MOMENT", line)
print("WROTE", len(rows), "rows to", out_csv)
