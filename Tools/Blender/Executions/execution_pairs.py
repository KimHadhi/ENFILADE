"""The paired executions' choreography (first drafts), in the game's metres and 30 fps frames (frame 1 is 0 s).

Every pair: the zombie's start clip, where Kazusa's root stands from the zombie's root and which way she faces, the
frame of the kill (the blow that kills: the game hurts the zombie then, and its body goes limp into its ragdoll there),
the last frame, and two functions that key the zombie, then Kazusa from where the zombie's bones are (contact).
Frames, offsets and the kill frame the user changes in Blender are written to Tables/Executions.csv by the export; these
numbers are only where the drafts start.

Kinds (Executions.csv `kind`): takedown (a stealth kill: quick, quiet) or execution (a broken zombie: weightier).
"""
import math

from mathutils import Vector

from execution_drafts import PACK, Hold, Knife, Palm, bone_at, zv

# key: label, kind, the zombie's start clip and the second in it, Kazusa's root (right, forward of the zombie's root)
# and her turn (degrees, + to the left; 180 faces the zombie), the kill frame, the last frame, what the knife
# hits (for the game's blood: the zombie bone), and which way the dead body is sent (right, forward, up).
PAIRS = {
    'back': dict(label='서 있음 · 뒤에서', kind='takedown', clip=PACK + 'zombie_unalert_stand_idle_01.fbx', at=0.0,
                 root=(0.0, -.42), yaw=0.0, kill=16, frames=38, bone='Neck', fall=(-.3, .8, -.3)),
    'front': dict(label='서 있음 · 앞에서', kind='execution', clip=PACK + 'zombie_hit_react_F_01.fbx', at=0.0,
                  root=(0.0, .72), yaw=180.0, kill=21, frames=54, bone='Head', fall=(.2, -.9, -.2)),
    'sit': dict(label='앉아 졸기', kind='takedown', clip=PACK + 'zombie_sit_idle_01.fbx', at=0.0,
                root=(0.0, -.40), yaw=0.0, kill=16, frames=42, bone='Neck', fall=(.2, .7, -.5)),
    'feed': dict(label='먹기 · 무릎', kind='takedown', clip=PACK + 'zombie_feeding_01_Idle.fbx', at=0.0,
                 root=(0.0, -.38), yaw=0.0, kill=17, frames=44, bone='Neck', fall=(0.0, .8, -.6)),
    'crouch': dict(label='웅크리기', kind='takedown', clip=PACK + 'zombie_crouch_01_idle.fbx', at=0.0,
                   root=(0.0, -.36), yaw=0.0, kill=16, frames=42, bone='Neck', fall=(0.0, .8, -.5)),
    'prone': dict(label='엎드림 · 기기', kind='takedown', clip=PACK + 'crawl/zombie_FD_crawl_idle_01.fbx', at=0.0,
                  root=(-.38, .30), yaw=-90.0, kill=18, frames=46, bone='Neck', fall=(0.0, 0.0, -1.0)),
    'supine': dict(label='누움 · 등', kind='execution', clip=PACK + 'zombie_getup_UP_01.fbx', at=0.0,
                   root=(-.36, -.40), yaw=-90.0, kill=20, frames=48, bone='Head', fall=(0.0, 0.0, -1.0)),
}
ORDER = ('back', 'front', 'sit', 'feed', 'crouch', 'prone', 'supine')

ARMS_L, ARMS_R = ('Shoulder_L', 'Elbow_L'), ('Shoulder_R', 'Elbow_R')


class Her:
    """Points and ways in Kazusa's own frame (right, forward, up from her root, facing her way) in the zombie's frame."""

    def __init__(self, pair):
        self.root = pair['root']
        self.yaw = math.radians(pair['yaw'])

    def z(self, right, forward, up):
        c, s = math.cos(self.yaw), math.sin(self.yaw)
        return (self.root[0] + right * c - forward * s, self.root[1] + right * s + forward * c, up)

    def p(self, right, forward, up):
        return zv(*self.z(right, forward, up))

    def d(self, right, forward, up):
        c, s = math.cos(self.yaw), math.sin(self.yaw)
        return zv(right * c - forward * s, right * s + forward * c, up).normalized()

    def foot(self, right, forward, up=.083, yaw=0.0, toes=0.0):
        x, y, z = self.z(right, forward, up)
        return (x, y, z, yaw, toes)


def _ready(her, p, frame, hips=1.12, lean=8.0, kind='smooth'):
    """Standing ready, the knife low by the right hip, the left hand loose in front."""
    p.key(frame, kind, hips=her.z(0.0, 0.0, hips), hipsTurn=(lean * .4, 0.0, 0.0), spine=(lean, 0.0, 0.0), head=(4.0, 0.0, 0.0),
          lhand=Palm(her.p(-.17, .16, hips - .14), her.d(.3, 0.0, -1.0), her.d(.2, .8, -.3), .25),
          rhand=Knife(her.p(.2, .1, hips - .30), her.d(0.0, .15, -1.0), her.d(0.0, 1.0, 0.0)),
          lfoot=her.foot(-.095, .02), rfoot=her.foot(.095, -.06), lelbow=(.25, .3, .35), relbow=(.25, .35, .3),
          lcurl=.25, rcurl=.95)


# ---- Standing, from behind (a takedown) ----------------------------------------------------------------------------

def back_zombie(z, pair):
    z.key(1)
    z.key(8, {'Spine_02': (-6, 0, 0), 'Spine_03': (-10, 0, 0), 'Neck': (-18, 0, 0), 'Head': (-24, 0, 0),
              'Shoulder_L': (-45, 0, 0), 'Shoulder_R': (-45, 0, 0), 'Elbow_L': (-55, 0, 0), 'Elbow_R': (-55, 0, 0)},
          hips=(0, -.04, 0), kind='snap')
    z.key(13, {'Spine_02': (-7, 0, 0), 'Spine_03': (-12, 0, 0), 'Neck': (-20, 0, 0), 'Head': (-26, 4, 0),
               'Shoulder_L': (-70, 0, 0), 'Shoulder_R': (-62, 0, 0), 'Elbow_L': (-75, 0, 0), 'Elbow_R': (-70, 0, 0)},
          hips=(0, -.05, 0))
    z.key(16, {'Spine_02': (-9, 0, 0), 'Spine_03': (-16, 0, 0), 'Neck': (-22, 8, 0), 'Head': (-30, 16, -8),
               'Shoulder_L': (-55, 0, -20), 'Shoulder_R': (-55, 0, 20), 'Elbow_L': (-35, 0, 0), 'Elbow_R': (-35, 0, 0)},
          hips=(0, -.05, 0), kind='snap')
    z.shake(8, 16, ('Head', 'Shoulder_L', 'Shoulder_R'), 4.0, 8.0, 1)
    z.shake(16, 21, ('Spine_03', 'Head'), 6.0, 11.0, 2)
    # Past the kill: what the game shows only on one that lives through it (a brute takes a share): it folds, in place.
    z.key(21, {'Spine_02': (4, 0, 0), 'Spine_03': (6, 0, 0), 'Neck': (8, 0, 0), 'Head': (14, 0, 0),
               'Shoulder_L': (-20, 0, 0), 'Shoulder_R': (-20, 0, 0), 'Elbow_L': (-20, 0, 0), 'Elbow_R': (-20, 0, 0)},
          hips=(0, .02, -.06))
    z.key(29, {'Spine_02': (12, 0, 0), 'Spine_03': (12, 0, 0), 'Head': (10, 0, 0)}, hips=(0, .02, -.05))
    z.key(38)


def back_player(p, zf, pair):
    her = Her(pair)
    _ready(her, p, 1)
    p.key(4, lfoot=her.foot(-.1, .10, .15))
    p.key(6, hips=her.z(0.0, .12, 1.08), hipsTurn=(6, 0, 0), spine=(18, 0, 0), head=(8, 0, 0),
          lhand=Palm(her.p(-.10, .52, 1.36), her.d(.4, -.6, 0.0), her.d(1.0, .1, 0.0), .1),
          rhand=Knife(her.p(.22, .16, 1.10), her.d(0.0, .2, -1.0), her.d(0.0, 1.0, 0.0)),
          lfoot=her.foot(-.1, .18), lelbow=(.35, .15, .2))
    face = bone_at(zf, 8, 'Head', 0.0, .10, .02)
    p.key(8, 'snap', hips=her.z(0.0, .16, 1.08), spine=(14, 0, 0), head=(10, 0, 0),
          lhand=Hold('Head', Palm(face, zv(0, -1, 0), zv(1, 0, .1), .55)),
          rhand=Knife(her.p(.24, .22, 1.28), zv(-1, .25, 0), zv(0, 0, -1)), relbow=(.3, .25, .15))
    p.key(13, hips=her.z(0.0, .15, 1.08), spine=(10, 0, -4), head=(12, 0, 0),
          rhand=Knife(her.p(.32, .14, 1.40), zv(-1, .3, -.1), zv(0, 0, -1)), relbow=(.35, .1, .05))
    neck = bone_at(zf, 16, 'Neck', .02, .0, .04)
    p.key(16, 'snap', spine=(16, 6, 0), head=(14, 6, 0), lhand=Hold('Head'),
          rhand=Hold('Neck', Knife(neck, zv(-1, .15, -.05), zv(0, 0, -1))))
    p.key(18, rhand=Hold('Neck'))
    p.key(20, lhand=Palm(her.p(-.08, .42, 1.22), zv(0, 1, -.4), zv(1, 0, 0), .2), spine=(18, 0, 0))
    p.key(21, rhand=Knife(her.p(.34, .18, 1.30), zv(-1, .15, -.3), zv(0, 0, -1)))
    p.key(25, hips=her.z(0.0, .06, 1.10), spine=(10, 0, 0), head=(6, 0, 0),
          lhand=Palm(her.p(-.16, .2, 1.0), her.d(.3, 0, -1), her.d(.2, .8, -.3), .25),
          rhand=Knife(her.p(.21, .12, .84), her.d(0, .15, -1), her.d(0, 1, 0)))
    p.key(29, lfoot=her.foot(-.1, .18))
    p.key(32, lfoot=her.foot(-.1, .07, .13))
    _ready(her, p, 36, kind='smooth')
    p.key(38)


# ---- Standing, from the front (an execution) ------------------------------------------------------------------------

def front_zombie(z, pair):
    z.key(1)
    z.key(9, {'Spine_02': (8, 0, 0), 'Spine_03': (10, 0, 0), 'Neck': (-12, 0, 0), 'Head': (-16, 0, 0),
              'Shoulder_L': (-40, 0, 0), 'Shoulder_R': (-48, 0, 0), 'Elbow_L': (-50, 0, 0), 'Elbow_R': (-55, 0, 0)},
          hips=(0, .06, 0), kind='snap')
    z.key(16, {'Spine_02': (10, 0, 0), 'Spine_03': (12, 0, 0), 'Neck': (-18, -6, 0), 'Head': (-22, -8, 0),
               'Shoulder_L': (-55, 0, 0), 'Shoulder_R': (-62, 0, 0), 'Elbow_L': (-70, 0, 0), 'Elbow_R': (-72, 0, 0)},
          hips=(0, .10, .01))
    z.key(21, {'Spine_02': (2, 0, 0), 'Spine_03': (-8, 0, -6), 'Neck': (-22, 10, -10), 'Head': (-30, 18, -14),
               'Shoulder_L': (-28, 0, -35), 'Shoulder_R': (-28, 0, 35), 'Elbow_L': (-25, 0, 0), 'Elbow_R': (-25, 0, 0)},
          hips=(0, .09, 0), kind='snap')
    z.shake(9, 20, ('Shoulder_L', 'Shoulder_R', 'Head'), 3.0, 7.0, 3)
    z.shake(21, 28, ('Spine_03', 'Head', 'Shoulder_L', 'Shoulder_R'), 7.0, 10.0, 4)
    z.key(29, {'Spine_02': (-6, 0, 0), 'Spine_03': (-6, 0, 0), 'Head': (10, 0, 0),
               'Shoulder_L': (-30, 0, -10), 'Shoulder_R': (-30, 0, 10)}, hips=(0, -.10, -.04))
    z.key(41, {'Spine_02': (12, 0, 0), 'Spine_03': (12, 0, 0), 'Head': (15, 0, 0)}, hips=(0, -.08, -.06))
    z.key(54)


def front_player(p, zf, pair):
    her = Her(pair)
    _ready(her, p, 1, lean=6)
    p.key(5, lfoot=her.foot(-.1, .12, .15))
    collar = bone_at(zf, 9, 'Spine_03', 0.0, .14, .12)
    p.key(9, 'snap', hips=her.z(0.0, .10, 1.10), hipsTurn=(4, 0, 0), spine=(8, 0, 0), head=(6, 0, 0),
          lhand=Hold('Spine_03', Palm(collar, zv(0, -1, 0), zv(0, 0, 1), .7)),
          rhand=Knife(her.p(.30, .0, 1.52), her.d(-.2, .3, -1), her.d(0, 1, 0)), relbow=(.45, .1, -.05),
          lfoot=her.foot(-.1, .2))
    p.key(16, hips=her.z(0.0, .06, 1.08), hipsTurn=(-2, 0, 0), spine=(-4, -8, 0), head=(4, 0, 0),
          rhand=Knife(her.p(.36, -.05, 1.66), her.d(-.3, .4, -1), her.d(0, 1, 0)), relbow=(.5, .05, -.25))
    temple = bone_at(zf, 21, 'Head', -.09, .04, .07)
    p.key(21, 'snap', hips=her.z(0.0, .10, 1.06), hipsTurn=(6, 0, 0), spine=(14, -14, 0), head=(12, -6, 0),
          rhand=Hold('Head', Knife(temple, zv(1, -.15, -.35), zv(0, 1, 0))), relbow=(.3, .25, .15))
    p.key(22, lhand=Hold('Spine_03'))
    p.key(23, rhand=Hold('Head'))
    p.key(26, lhand=Palm(her.p(-.02, .40, 1.26), zv(0, -1, 0), zv(0, 0, 1), .1), spine=(10, -6, 0))
    p.key(27, rhand=Knife(her.p(.30, .25, 1.30), her.d(-.3, .3, -1), her.d(0, 1, 0)))
    p.key(30, hips=her.z(0.0, .06, 1.10), spine=(4, 0, 0),
          lhand=Palm(her.p(-.08, .46, 1.18), zv(0, -1, .2), zv(0, 0, 1), .1))
    p.key(40, rhand=Knife(her.p(.22, .10, .95), her.d(0, .15, -1), her.d(0, 1, 0)),
          lhand=Palm(her.p(-.17, .16, .98), her.d(.3, 0, -1), her.d(.2, .8, -.3), .25), spine=(6, 0, 0))
    p.key(44, lfoot=her.foot(-.1, .2))
    p.key(48, lfoot=her.foot(-.1, .08, .13))
    _ready(her, p, 52, lean=6)
    p.key(54)


# ---- Low, from behind: sitting, eating, crouched (takedowns) --------------------------------------------------------

def _kneel(her, p, frame, kind='smooth', hips=.62, back=-.16, **parts):
    """Down on the right knee: the right foot behind on its toes, the left foot ahead flat."""
    p.key(frame, kind, hips=her.z(0.0, back + .04, hips), lfoot=her.foot(-.13, back + .34),
          rfoot=her.foot(.12, back - .30, .12, 0.0, 55.0), rknee=(.45, 0.0, -.5), lknee=(.6, -.1, .2), **parts)


def sit_zombie(z, pair):
    z.key(1)
    z.key(10, {'Spine_03': (-5, 0, 0), 'Neck': (6, 0, 0), 'Head': (10, 0, 0), 'Shoulder_L': (-10, 0, 0),
               'Shoulder_R': (-10, 0, 0)}, kind='snap')
    z.key(16, {'Spine_02': (-6, 0, 0), 'Spine_03': (-12, 0, 0), 'Neck': (4, 0, 0), 'Head': (-4, 10, 0),
               'Shoulder_L': (-35, 0, 0), 'Shoulder_R': (-35, 0, 0), 'Elbow_L': (-30, 0, 0), 'Elbow_R': (-30, 0, 0)},
          kind='snap')
    z.shake(10, 16, ('Shoulder_L', 'Shoulder_R'), 3.0, 7.0, 5)
    z.shake(16, 22, ('Spine_03', 'Head'), 5.0, 11.0, 6)
    z.key(22, {'Spine_02': (10, 0, 0), 'Spine_03': (12, 0, 0), 'Head': (20, 0, 0)})
    z.key(30)
    z.key(42)


def sit_player(p, zf, pair):
    her = Her(pair)
    _ready(her, p, 1, hips=1.05, lean=12)
    p.key(4, lfoot=her.foot(-.12, .1, .14))
    _kneel(her, p, 7, hips=.82, back=.04, spine=(25, 0, 0), head=(10, 0, 0),
           lhand=Palm(her.p(-.08, .40, .86), zv(0, 0, -1), zv(0, 1, 0), .2),
           rhand=Knife(her.p(.18, .22, .86), her.d(0, .3, -1), her.d(0, 1, 0)))
    nape = bone_at(zf, 10, 'Head', 0.0, -.06, .12)
    _kneel(her, p, 10, 'snap', hips=.62, back=.06, spine=(34, 0, 0), head=(16, 0, 0), hipsTurn=(10, 0, 0),
           lhand=Hold('Head', Palm(nape, zv(0, .4, -1), zv(0, 1, 0), .5)),
           rhand=Knife(her.p(.16, .30, .98), zv(-.1, .55, -.8), zv(1, 0, 0)), relbow=(.35, .2, .1))
    neck = bone_at(zf, 16, 'Neck', 0.0, -.05, .05)
    p.key(16, 'snap', spine=(40, 0, 0), head=(18, 0, 0), lhand=Hold('Head'),
          rhand=Hold('Neck', Knife(neck, zv(0, .45, -.9), zv(1, 0, 0))))
    p.key(18, rhand=Hold('Neck'))
    p.key(20, lhand=Palm(her.p(-.1, .34, .74), zv(0, 0, -1), zv(0, 1, 0), .2), spine=(30, 0, 0))
    p.key(22, rhand=Knife(her.p(.16, .20, .90), her.d(0, .3, -1), her.d(0, 1, 0)))
    _kneel(her, p, 26, hips=.70, back=.04, spine=(26, 0, 0), head=(10, 0, 0))
    p.key(33, hips=her.z(0, .04, .95), spine=(16, 0, 0), lfoot=her.foot(-.12, .1), rfoot=her.foot(.1, -.08, .13, 0, 20))
    _ready(her, p, 40, hips=1.08, lean=10)
    p.key(42)


def feed_zombie(z, pair):
    z.key(1)
    # Pulled up by the hair from bent over its meal: up onto its knees, the head back.
    z.key(10, {'Spine_01': (-22, 0, 0), 'Spine_02': (-24, 0, 0), 'Spine_03': (-20, 0, 0), 'Neck': (-12, 0, 0),
               'Head': (-22, 0, 0), 'Shoulder_L': (-10, 0, 0), 'Shoulder_R': (-10, 0, 0)},
          hips=(0, -.06, .12), kind='snap')
    z.key(17, {'Spine_01': (-24, 0, 0), 'Spine_02': (-26, 0, 0), 'Spine_03': (-26, 0, -4), 'Neck': (-16, 6, 0),
               'Head': (-28, 14, -8), 'Shoulder_L': (-35, 0, -15), 'Shoulder_R': (-35, 0, 15)},
          hips=(0, -.07, .13), kind='snap')
    z.shake(10, 17, ('Shoulder_L', 'Shoulder_R', 'Spine_03'), 4.0, 8.0, 7)
    z.shake(17, 23, ('Spine_03', 'Head'), 6.0, 11.0, 8)
    z.key(24, {'Spine_01': (-6, 0, 0), 'Spine_02': (-6, 0, 0), 'Spine_03': (-2, 0, 0), 'Head': (10, 0, 0)},
          hips=(0, -.02, .04))
    z.key(32)
    z.key(44)


def feed_player(p, zf, pair):
    her = Her(pair)
    _ready(her, p, 1, hips=1.08, lean=12)
    p.key(4, lfoot=her.foot(-.13, .12, .14))
    hair = bone_at(zf, 10, 'Head', 0.0, -.05, .12)
    p.key(7, hips=her.z(0, .12, .98), spine=(30, 0, 0), head=(14, 0, 0), lfoot=her.foot(-.14, .24),
          lhand=Palm(her.p(-.05, .62, .66), zv(0, .3, -1), zv(0, 1, 0), .2),
          rhand=Knife(her.p(.2, .26, .92), her.d(0, .3, -1), her.d(0, 1, 0)))
    p.key(10, 'snap', hips=her.z(0, .10, .90), hipsTurn=(6, 0, 0), spine=(30, 0, 0), head=(16, 0, 0),
          lhand=Hold('Head', Palm(hair, zv(0, .3, -1), zv(0, 1, 0), .8)),
          rhand=Knife(her.p(.24, .30, 1.10), zv(-.6, .4, -.6), zv(0, 0, -1)), relbow=(.35, .2, .15))
    neck = bone_at(zf, 17, 'Neck', .05, .02, .02)
    p.key(17, 'snap', hips=her.z(0, .12, .88), spine=(34, 6, 0), head=(18, 0, 0), lhand=Hold('Head'),
          rhand=Hold('Neck', Knife(neck, zv(-.85, .35, -.3), zv(0, 0, -1))))
    p.key(19, rhand=Hold('Neck'))
    p.key(21, lhand=Palm(her.p(-.05, .52, .74), zv(0, .5, -1), zv(0, 1, 0), .2), spine=(28, 0, 0))
    p.key(23, rhand=Knife(her.p(.28, .30, .98), zv(-.6, .4, -.6), zv(0, 0, -1)))
    p.key(28, hips=her.z(0, .08, 1.02), spine=(18, 0, 0),
          lhand=Palm(her.p(-.16, .2, .9), her.d(.3, 0, -1), her.d(.2, .8, -.3), .25),
          rhand=Knife(her.p(.2, .14, .84), her.d(0, .15, -1), her.d(0, 1, 0)))
    p.key(33, lfoot=her.foot(-.13, .24))
    p.key(37, lfoot=her.foot(-.1, .08, .13))
    _ready(her, p, 42, hips=1.1, lean=10)
    p.key(44)


def crouch_zombie(z, pair):
    z.key(1)
    z.key(10, {'Spine_01': (-12, 0, 0), 'Spine_02': (-16, 0, 0), 'Spine_03': (-16, 0, 0), 'Neck': (-12, 0, 0),
               'Head': (-22, 0, 0), 'Shoulder_L': (-20, 0, 0), 'Shoulder_R': (-20, 0, 0)},
          hips=(0, -.05, .06), kind='snap')
    z.key(16, {'Spine_01': (-14, 0, 0), 'Spine_02': (-18, 0, 0), 'Spine_03': (-22, 0, -4), 'Neck': (-16, 6, 0),
               'Head': (-28, 14, -8), 'Shoulder_L': (-40, 0, -15), 'Shoulder_R': (-40, 0, 15), 'Elbow_L': (-30, 0, 0),
               'Elbow_R': (-30, 0, 0)}, hips=(0, -.06, .07), kind='snap')
    z.shake(10, 16, ('Shoulder_L', 'Shoulder_R', 'Spine_03'), 4.0, 8.0, 9)
    z.shake(16, 22, ('Spine_03', 'Head'), 6.0, 11.0, 10)
    z.key(23, {'Spine_02': (6, 0, 0), 'Spine_03': (8, 0, 0), 'Head': (14, 0, 0)}, hips=(0, .0, -.04))
    z.key(31)
    z.key(42)


def crouch_player(p, zf, pair):
    her = Her(pair)
    _ready(her, p, 1, hips=1.08, lean=12)
    p.key(4, lfoot=her.foot(-.13, .12, .14))
    hair = bone_at(zf, 10, 'Head', 0.0, -.05, .12)
    p.key(7, hips=her.z(0, .14, .96), spine=(26, 0, 0), head=(14, 0, 0), lfoot=her.foot(-.14, .24),
          lhand=Palm(her.p(-.05, .62, .82), zv(0, .3, -1), zv(0, 1, 0), .2),
          rhand=Knife(her.p(.2, .26, .95), her.d(0, .3, -1), her.d(0, 1, 0)))
    p.key(10, 'snap', hips=her.z(0, .12, .90), hipsTurn=(6, 0, 0), spine=(28, 0, 0), head=(16, 0, 0),
          lhand=Hold('Head', Palm(hair, zv(0, .3, -1), zv(0, 1, 0), .8)),
          rhand=Knife(her.p(.24, .30, 1.12), zv(-.6, .4, -.6), zv(0, 0, -1)), relbow=(.35, .2, .15))
    neck = bone_at(zf, 16, 'Neck', .05, .02, .02)
    p.key(16, 'snap', hips=her.z(0, .14, .88), spine=(32, 6, 0), head=(18, 0, 0), lhand=Hold('Head'),
          rhand=Hold('Neck', Knife(neck, zv(-.85, .35, -.3), zv(0, 0, -1))))
    p.key(18, rhand=Hold('Neck'))
    p.key(20, lhand=Palm(her.p(-.05, .52, .82), zv(0, .5, -1), zv(0, 1, 0), .2), spine=(26, 0, 0))
    p.key(22, rhand=Knife(her.p(.28, .30, 1.0), zv(-.6, .4, -.6), zv(0, 0, -1)))
    p.key(27, hips=her.z(0, .08, 1.02), spine=(16, 0, 0),
          lhand=Palm(her.p(-.16, .2, .9), her.d(.3, 0, -1), her.d(.2, .8, -.3), .25),
          rhand=Knife(her.p(.2, .14, .84), her.d(0, .15, -1), her.d(0, 1, 0)))
    p.key(32, lfoot=her.foot(-.13, .24))
    p.key(36, lfoot=her.foot(-.1, .08, .13))
    _ready(her, p, 40, hips=1.1, lean=10)
    p.key(42)


# ---- Lying: face down (calm on the floor, a crawler, knocked down) and on its back (knocked down) --------------------

def prone_zombie(z, pair):
    z.key(1)
    z.key(11, {'Neck': (14, 0, 0), 'Head': (22, 0, 0), 'Shoulder_L': (6, 0, 0), 'Shoulder_R': (6, 0, 0)}, kind='snap')
    z.key(18, {'Spine_03': (-6, 0, 0), 'Neck': (8, 0, 0), 'Head': (12, 10, 0), 'Shoulder_L': (-15, 0, -10),
               'Shoulder_R': (-15, 0, 10), 'Elbow_L': (-20, 0, 0), 'Elbow_R': (-20, 0, 0)}, kind='snap')
    z.shake(11, 18, ('Shoulder_L', 'Shoulder_R', 'Elbow_L', 'Elbow_R'), 5.0, 9.0, 11)
    z.shake(18, 24, ('Spine_03', 'Head'), 6.0, 12.0, 12)
    z.key(25, {'Neck': (16, 0, 0), 'Head': (26, 0, 0), 'Shoulder_L': (10, 0, 0), 'Shoulder_R': (10, 0, 0)},
          hips=(0, 0, -.02))
    z.key(34)
    z.key(46)


def prone_player(p, zf, pair):
    her = Her(pair)
    _ready(her, p, 1, hips=1.08, lean=12)
    p.key(4, lfoot=her.foot(-.12, .1, .14))
    head = bone_at(zf, 11, 'Head', 0.0, -.04, .12)
    _kneel(her, p, 7, hips=.78, back=.02, spine=(26, 0, 0), head=(12, 0, 0),
           lhand=Palm(her.p(-.06, .42, .70), zv(0, 0, -1), zv(0, 1, 0), .2),
           rhand=Knife(her.p(.16, .26, .86), her.d(0, .3, -1), her.d(0, 1, 0)))
    _kneel(her, p, 11, 'snap', hips=.62, back=.04, spine=(38, 0, 0), head=(18, 0, 0), hipsTurn=(10, 0, 0),
           lhand=Hold('Head', Palm(head, zv(0, 0, -1), zv(0, 1, 0), .5)),
           rhand=Knife(her.p(.14, .34, .92), zv(0, .25, -1), zv(1, 0, 0)), relbow=(.35, .2, .1))
    nape = bone_at(zf, 18, 'Neck', 0.0, -.02, .07)
    p.key(18, 'snap', spine=(44, 0, 0), head=(20, 0, 0), lhand=Hold('Head'),
          rhand=Hold('Neck', Knife(nape, zv(0, .2, -1), zv(1, 0, 0))))
    p.key(20, rhand=Hold('Neck'))
    p.key(22, lhand=Palm(her.p(-.08, .36, .62), zv(0, 0, -1), zv(0, 1, 0), .2), spine=(32, 0, 0))
    p.key(24, rhand=Knife(her.p(.14, .24, .86), her.d(0, .3, -1), her.d(0, 1, 0)))
    _kneel(her, p, 30, hips=.70, back=.02, spine=(24, 0, 0), head=(10, 0, 0))
    p.key(37, hips=her.z(0, .04, .95), spine=(14, 0, 0), lfoot=her.foot(-.12, .1), rfoot=her.foot(.1, -.08, .13, 0, 20))
    _ready(her, p, 44, hips=1.08, lean=10)
    p.key(46)


def supine_zombie(z, pair):
    z.key(1)
    z.key(9, {'Neck': (10, 0, 0), 'Head': (12, 0, 0), 'Shoulder_L': (-30, 0, 0), 'Shoulder_R': (-30, 0, 0),
              'Elbow_L': (-40, 0, 0), 'Elbow_R': (-40, 0, 0)}, kind='snap')
    z.key(15, {'Neck': (14, 0, 0), 'Head': (16, -6, 0), 'Shoulder_L': (-55, 0, 0), 'Shoulder_R': (-50, 0, 0),
               'Elbow_L': (-55, 0, 0), 'Elbow_R': (-60, 0, 0)})
    z.key(20, {'Neck': (-4, 0, 0), 'Head': (-10, 8, 0), 'Shoulder_L': (-30, 0, -20), 'Shoulder_R': (-30, 0, 20),
               'Elbow_L': (-20, 0, 0), 'Elbow_R': (-20, 0, 0)}, kind='snap')
    z.shake(9, 20, ('Shoulder_L', 'Shoulder_R', 'Elbow_L', 'Elbow_R'), 5.0, 8.0, 13)
    z.shake(20, 26, ('Spine_03', 'Head', 'Shoulder_L', 'Shoulder_R'), 6.0, 12.0, 14)
    z.key(27, {'Shoulder_L': (6, 0, 0), 'Shoulder_R': (6, 0, 0)})
    z.key(36)
    z.key(48)


def supine_player(p, zf, pair):
    her = Her(pair)
    _ready(her, p, 1, hips=1.08, lean=12)
    p.key(4, lfoot=her.foot(-.12, .1, .14))
    chest = bone_at(zf, 9, 'Spine_03', 0.0, 0.0, .14)
    _kneel(her, p, 6, hips=.80, back=.0, spine=(24, 0, 0), head=(12, 0, 0),
           lhand=Palm(her.p(-.04, .40, .66), zv(0, 0, -1), her.d(0, 1, 0), .2),
           rhand=Knife(her.p(.16, .2, 1.0), her.d(0, .3, -1), her.d(0, 1, 0)))
    _kneel(her, p, 9, 'snap', hips=.62, back=.02, spine=(34, 0, 0), head=(16, 0, 0), hipsTurn=(8, 0, 0),
           lhand=Hold('Spine_03', Palm(chest, zv(0, 0, -1), her.d(0, 1, 0), .6)),
           rhand=Knife(her.p(.10, .30, 1.10), her.d(0, .2, -1), her.d(0, 1, 0)), relbow=(.3, .2, .15))
    p.key(15, rhand=Knife(her.p(.12, .32, 1.24), her.d(0, .1, -1), her.d(0, 1, 0)), spine=(26, 0, 0),
          relbow=(.35, .05, -.1))
    face = bone_at(zf, 20, 'Head', 0.0, 0.0, .13)
    p.key(20, 'snap', spine=(42, 0, 0), head=(22, 0, 0), lhand=Hold('Spine_03'),
          rhand=Hold('Head', Knife(face, zv(0, 0, -1), her.d(0, 1, 0))), relbow=(.3, .25, .15))
    p.key(22, rhand=Hold('Head'))
    p.key(24, lhand=Palm(her.p(-.06, .36, .62), zv(0, 0, -1), her.d(0, 1, 0), .2), spine=(30, 0, 0))
    p.key(26, rhand=Knife(her.p(.14, .26, .96), her.d(0, .3, -1), her.d(0, 1, 0)))
    _kneel(her, p, 32, hips=.70, back=.02, spine=(22, 0, 0), head=(10, 0, 0))
    p.key(39, hips=her.z(0, .04, .95), spine=(14, 0, 0), lfoot=her.foot(-.12, .1), rfoot=her.foot(.1, -.08, .13, 0, 20))
    _ready(her, p, 46, hips=1.08, lean=10)
    p.key(48)


CHOREOGRAPHY = {
    'back': (back_zombie, back_player), 'front': (front_zombie, front_player), 'sit': (sit_zombie, sit_player),
    'feed': (feed_zombie, feed_player), 'crouch': (crouch_zombie, crouch_player), 'prone': (prone_zombie, prone_player),
    'supine': (supine_zombie, supine_player),
}
