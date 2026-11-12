# Blender (headless): a VManip hand animation (GMod "[VManip] Quick Grenades") -> the left arm's path as table rows.
# usage: blender -b --factory-startup --python vmanip_throw_path.py -- <c_vmanipgrenade.mdl> <out.csv> [step]
#   The MDL comes out of the VManip base addon (workshop 2155366756, models/c_vmanipgrenade.mdl) unpacked with
#   Tools/Extract-Gma.ps1 into a folder outside the repo; its one sequence "thrownade" is the off hand reaching to the
#   belt, bringing a frag up, cocking it and throwing it.
#
# Why a path and not a clip: the motion is a viewmodel's (c_arms), and our hand is a whole body's arm, on any humanoid
# a modder brings. So nothing of the c_arms rig is kept but what an arm does: where the wrist is from the shoulder (in
# arm lengths, so a long or short arm reaches the same way), which way the elbow points, which way the hand faces
# (from its knuckles: along = wrist to the middle knuckle, palm = out of the palm) and how far the fingers close.
# Everything is in the view's axes (Unity camera space: x right, y up, z forward), because VManip plays the model at
# the view. GrenadeArm puts our wrist there from our own shoulder with TwoBoneIK and turns our hand to match.
# VManip plays its cycle at one per second (cl_vmanip.lua: Cycle += FrameTime() * speed), so a row's cycle is also
# its second at VManip's own speed.
import csv
import math
import sys

import addon_utils
import bpy  # noqa: F401  (Blender must be up for SourceIO)
from mathutils import Matrix, Quaternion, Vector

args = sys.argv[sys.argv.index("--") + 1:]
mdl_path, out_csv = args[0], args[1]
step = int(args[2]) if len(args) > 2 else 3

addon_utils.enable("SourceIO-master", default_set=False, persistent=False)
from SourceIO.library.models.mdl.v49.mdl_file import MdlV49  # noqa: E402
from SourceIO.library.utils import FileBuffer  # noqa: E402

with FileBuffer(mdl_path) as f:
    mdl = MdlV49.from_buffer(f)
idx = {b.name: i for i, b in enumerate(mdl.bones)}
data = mdl.animations[0]
pos_all, rot_all = data["pos"], data["rot"]
frames = int(data.shape[0])
last = frames - 1
P = "ValveBiped.Bip01_L_"
ARM = (mdl.bones[idx[P + "Forearm"]].position[0] + mdl.bones[idx[P + "Hand"]].position[0])  # upper arm + forearm, units
INCH = 0.0254


def quat(r):
    return Quaternion((r[3], r[0], r[1], r[2]))


def world_at(fr):
    out = [None] * len(mdl.bones)
    for i, b in enumerate(mdl.bones):
        local = Matrix.Translation(Vector(pos_all[fr, i])) @ quat(rot_all[fr, i]).to_matrix().to_4x4()
        out[i] = local if b.parent_id == -1 else out[b.parent_id] @ local
    return out


def view(v):
    """Source view space (x forward, y left, z up) -> Unity camera space (x right, y up, z forward)."""
    return Vector((-v.y, v.z, v.x))


def cross(a, b):
    """Unity's Vector3.Cross, on vectors already in Unity's axes (so it agrees with what the game computes)."""
    return Vector((a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x))


def bend(a, b):
    return math.degrees(a.angle(b)) if a.length > 1e-6 and b.length > 1e-6 else 0.0


def finger_curl(w, base):
    """
    How far one finger closes from the hand's along (degrees): the knuckle's turn plus the middle joint's. The rig has
    no fingertip bone, so the last joint is taken to follow the middle one by 0.7, as a real finger's does.
    """
    hand = view(w[idx[P + "Hand"]].to_translation())
    along = view(w[idx[P + "Finger2"]].to_translation()) - hand
    joints = [view(w[idx[P + base + s]].to_translation()) for s in ("", "1", "2")]
    first, second = joints[1] - joints[0], joints[2] - joints[1]
    return bend(along, first) + bend(first, second) * 1.7


rows = []
for fr in list(range(0, frames, step)) + ([last] if last % step else []):
    w = world_at(fr)
    at = lambda n: view(w[idx[n]].to_translation()) * INCH  # noqa: E731
    shoulder, elbow, wrist = at(P + "UpperArm"), at(P + "Forearm"), at(P + "Hand")
    middle, index, little = at(P + "Finger2"), at(P + "Finger1"), at(P + "Finger4")
    along = (middle - wrist).normalized()
    palm = -cross(along, index - little)
    palm = (palm - along * palm.dot(along)).normalized()
    grenade = at("ValveBiped.Grenade_body")
    arm = ARM * INCH
    rel_wrist, rel_elbow = (wrist - shoulder) / arm, (elbow - shoulder) / arm
    curl = sum(finger_curl(w, f) for f in ("Finger1", "Finger2", "Finger3", "Finger4")) / 4.0
    thumb = finger_curl(w, "Finger0")
    held = 1 if (grenade - wrist).length < 0.2 else 0
    rows.append([fr / last, *rel_wrist, *rel_elbow, *along, *palm, curl, thumb, held])
    if held and abs(fr / last - 0.4) < 0.02:
        # Where VManip's grenade sits in the hand at the hold, in the hand's own axes: along, palm, and across
        # (index side), for GrenadeRules heldAlong/heldPalm/heldAcross.
        across = cross(palm, along)
        off = grenade - wrist
        print("HELD at cycle %.3f: along %.3f palm %.3f across %.3f (m), hand %.3f m" % (
            fr / last, off.dot(along), off.dot(palm), off.dot(across), (middle - wrist).length))

with open(out_csv, "w", newline="", encoding="utf-8-sig") as f:
    out = csv.writer(f)
    out.writerow(["id", "cycle", "wristX", "wristY", "wristZ", "elbowX", "elbowY", "elbowZ",
                  "alongX", "alongY", "alongZ", "palmX", "palmY", "palmZ", "curl", "thumb", "held"])
    for n, r in enumerate(rows):
        out.writerow(["k%02d" % n] + ["%.3f" % v if isinstance(v, float) else str(v) for v in r[:-1]] + [str(r[-1])])
print("WROTE", len(rows), "rows to", out_csv, "frames", frames, "fps", mdl.anim_descs[0].fps, "arm m", round(ARM * INCH, 4))
