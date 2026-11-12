"""The execution knife's stroke, drafted: writes Assets/_Project/Data/Data/Resources/Tables/Clips/KnifeStab.csv (KnifeStroke reads it,
ExecutionKnifeArm plays it on any character's arms, ExecutionKnife times it to the execution).

Plain Python (Blender's own python runs it too):
  F:\\SteamLibrary\\steamapps\\common\\Blender\\4.5\\python\\bin\\python.exe -I Tools/Blender/Knives/knife_stab.py [project_root]

The user's ask (2026-11-13): "일단 칼은 클래스별로 각자 전용 칼을 쓰게 하고 싶고" — every class stabs with its own knife in an
execution or a stealth kill. Every class's knife plays this one stroke (Tables/ClassKnives motion), held in a reverse grip:
the right hand draws it from the hip, raises it to eye height on the right of the view with the blade hanging below the
fist, drives it down into the victim's neck at the execution's impact, works it in, pulls it clear and puts it away; the
left hand takes the victim's head and lets go once it is done. In the game each wrist is also drawn toward the victim by
the key's aim (the right fist over the back of the neck, the left hand on top of the head), so the blade meets the neck
whatever the bodies' sizes.

Keys below: time (s) -> wrist and elbow pole from that arm's shoulder in that arm's lengths, the hand's run (wrist to
middle knuckle) and its palm's facing, all in the view's axes (x right, y up, z forward); finger and thumb closing
(degrees); aim (0-1, toward the victim); weight (0-1, how much of the arm the stroke has: it eases in from the body's own
pose and back out). The rows are sampled at 30 fps (plus the keys' own times) along a cardinal spline through the keys;
the marks (ready, strike, out, sheath) go on their keys' rows, and held says the knife is in the right fist. Change the
keys and run it again; the game reads the table again when it is saved.
"""
import math
import os
import sys

ROOT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
def table_file(root, name, new_in="Clips"):
    """The game's table of this name under the project root. Since 0.9.0 the tables are filed in sub-folders of
    Data/Resources/Tables by subject and found by file name alone, as the game finds them (GameTables); a table that isn't
    there yet is made in new_in."""
    base = os.path.join(root, "Assets", "_Project", "Data", "Resources", "Tables")
    for folder, _, files in sorted(os.walk(base)):
        if name + ".csv" in files:
            return os.path.join(folder, name + ".csv")
    return os.path.join(base, new_in, name + ".csv")


OUT = table_file(ROOT, "KnifeStab")
FPS = 30
TENSION = .3

# Right arm (the knife): (time, wrist, elbow, along, palm, curl, thumb, aim, weight). Kazusa's right shoulder is 11 cm
# right of, 17 below and 9 behind her eyes and her arm 49 cm long: the ready fist is then about 24 cm ahead of the eyes and
# 17 to the right, 5 below them, in the right of the view with the blade below it.
RIGHT = [
    (0.00, (0.06, -0.93, 0.12), (0.22, -0.47, -0.06), (0.05, -0.96, 0.26), (-1.0, 0.0, 0.0), 25, 25, 0.0, 0.0),
    (0.07, (0.07, -0.92, 0.10), (0.24, -0.48, -0.08), (0.05, -0.96, 0.26), (-1.0, 0.0, 0.0), 88, 70, 0.0, 1.0),
    (0.13, (0.20, -0.42, 0.20), (0.45, -0.42, -0.20), (0.10, 0.35, 0.93), (-0.98, 0.15, 0.0), 88, 70, 0.0, 1.0),
    (0.19, (0.12, 0.25, 0.68), (0.55, 0.05, 0.25), (0.08, 0.20, 0.98), (-0.98, 0.10, 0.10), 88, 70, 0.0, 1.0),
    (0.27, (0.05, 0.22, 0.80), (0.42, 0.02, 0.32), (0.00, -0.05, 1.00), (-0.97, -0.25, 0.0), 90, 72, 0.3, 1.0),
    (0.33, (0.00, 0.16, 0.86), (0.30, 0.00, 0.40), (-0.05, -0.20, 0.98), (-0.93, -0.35, 0.0), 92, 75, 0.8, 1.0),
    (0.35, (-0.01, 0.10, 0.88), (0.28, -0.02, 0.40), (-0.06, -0.25, 0.97), (-0.92, -0.38, 0.0), 92, 75, 1.0, 1.0),
    (0.46, (-0.03, 0.02, 0.88), (0.26, -0.08, 0.40), (-0.10, -0.35, 0.93), (-0.85, -0.50, 0.10), 92, 75, 1.0, 1.0),
    (0.55, (0.06, 0.22, 0.74), (0.40, 0.00, 0.28), (0.00, 0.05, 1.00), (-1.0, 0.0, 0.0), 90, 72, 0.4, 1.0),
    (0.62, (0.14, 0.05, 0.55), (0.45, -0.25, 0.05), (0.05, 0.25, 0.97), (-1.0, 0.0, 0.05), 88, 70, 0.0, 1.0),
    (0.72, (0.10, -0.55, 0.20), (0.32, -0.45, -0.12), (0.05, -0.55, 0.83), (-1.0, 0.0, 0.0), 88, 70, 0.0, 1.0),
    (0.80, (0.07, -0.92, 0.10), (0.24, -0.48, -0.08), (0.05, -0.96, 0.26), (-1.0, 0.0, 0.0), 60, 50, 0.0, 1.0),
    (0.92, (0.06, -0.93, 0.12), (0.22, -0.47, -0.06), (0.05, -0.96, 0.26), (-1.0, 0.0, 0.0), 25, 25, 0.0, 0.0),
]
# Left arm (the grab), from the left shoulder: reaches out as the knife comes up, the palm down on the head, squeezes
# through the strike, lets go as the blade comes out.
LEFT = [
    (0.00, (-0.08, -0.93, 0.10), (-0.25, -0.47, -0.05), (-0.05, -0.97, 0.20), (1.0, 0.0, 0.0), 25, 25, 0.0, 0.0),
    (0.09, (-0.02, 0.05, 0.70), (-0.42, -0.25, 0.25), (0.10, 0.15, 0.98), (0.25, -0.75, 0.60), 30, 30, 0.5, 1.0),
    (0.16, (0.02, 0.12, 0.86), (-0.36, -0.10, 0.40), (0.15, 0.05, 0.99), (0.15, -0.95, 0.25), 60, 45, 1.0, 1.0),
    (0.35, (0.02, 0.12, 0.86), (-0.36, -0.10, 0.40), (0.15, 0.05, 0.99), (0.15, -0.95, 0.25), 70, 50, 1.0, 1.0),
    (0.50, (0.02, 0.10, 0.86), (-0.36, -0.10, 0.40), (0.15, 0.00, 0.99), (0.15, -0.95, 0.25), 70, 50, 1.0, 1.0),
    (0.58, (-0.06, 0.08, 0.66), (-0.40, -0.25, 0.20), (0.05, 0.10, 0.99), (0.30, -0.80, 0.50), 20, 20, 0.3, 1.0),
    (0.72, (-0.08, -0.60, 0.20), (-0.28, -0.45, -0.10), (-0.05, -0.80, 0.60), (1.0, 0.0, 0.0), 25, 25, 0.0, 0.6),
    (0.84, (-0.08, -0.93, 0.10), (-0.25, -0.47, -0.05), (-0.05, -0.97, 0.20), (1.0, 0.0, 0.0), 25, 25, 0.0, 0.0),
]
# The knife is in the fist from the grip at the hip until it is back in its sheath.
HELD = (0.07, 0.80)
MARKS = {0.19: "ready", 0.35: "strike", 0.62: "out", 0.80: "sheath"}


def cardinal(keys, index, t):
    """A cardinal spline through one channel of the keys (a tuple of floats per key) at time t."""
    times = [k[0] for k in keys]
    if t <= times[0]:
        return keys[0][index]
    if t >= times[-1]:
        return keys[-1][index]
    i = max(j for j in range(len(times) - 1) if times[j] <= t)
    t0, t1 = times[i], times[i + 1]
    p0, p1 = keys[i][index], keys[i + 1][index]

    def tangent(j):
        if j == 0 or j == len(keys) - 1:
            return tuple(0.0 for _ in p0)
        a, b = keys[j - 1][index], keys[j + 1][index]
        span = times[j + 1] - times[j - 1]
        return tuple((1 - TENSION) * (b[c] - a[c]) / span for c in range(len(a)))

    m0, m1 = tangent(i), tangent(i + 1)
    h = t1 - t0
    s = (t - t0) / h
    h00, h10, h01, h11 = 2 * s ** 3 - 3 * s ** 2 + 1, s ** 3 - 2 * s ** 2 + s, -2 * s ** 3 + 3 * s ** 2, s ** 3 - s ** 2
    return tuple(h00 * p0[c] + h10 * h * m0[c] + h01 * p1[c] + h11 * h * m1[c] for c in range(len(p0)))


def eased(keys, index, t):
    """A scalar channel eased (smoothstep) from key to key: no overshoot in a weight or a closing."""
    times = [k[0] for k in keys]
    if t <= times[0]:
        return keys[0][index]
    if t >= times[-1]:
        return keys[-1][index]
    i = max(j for j in range(len(times) - 1) if times[j] <= t)
    s = (t - times[i]) / (times[i + 1] - times[i])
    s = s * s * (3 - 2 * s)
    return keys[i][index] + (keys[i + 1][index] - keys[i][index]) * s


def unit(v):
    n = math.sqrt(sum(c * c for c in v)) or 1.0
    return tuple(c / n for c in v)


def arm(keys, t):
    return (cardinal(keys, 1, t), cardinal(keys, 2, t), unit(cardinal(keys, 3, t)), unit(cardinal(keys, 4, t)),
            eased(keys, 5, t), eased(keys, 6, t), eased(keys, 7, t), eased(keys, 8, t))


def main():
    end = max(RIGHT[-1][0], LEFT[-1][0])
    times = sorted(set([round(f / FPS, 4) for f in range(int(math.floor(end * FPS)) + 1)] + [k[0] for k in RIGHT] + [end]))
    merged = []
    for t in times:
        if merged and abs(t - merged[-1]) < 1e-3:
            if t in MARKS:
                merged[-1] = t
            continue
        merged.append(t)
    header = ["id", "cycle"]
    for side in "rl":
        for name in ("wrist", "elbow", "along", "palm"):
            header += [side + name + axis for axis in "XYZ"]
        header += [side + "curl", side + "thumb", side + "aim", side + "weight"]
    header += ["held", "mark"]
    note = ("#설명 (처형·암살 칼 찌르기 초안: Tools/Blender/Knives/knife_stab.py가 그 안의 키에서 30fps로 쓴다. 손으로 고치지 말고 "
            "스크립트의 키를 고쳐 다시 돌린다. r…은 오른팔(칼) l…은 왼팔(머리 잡기): 그 팔 어깨에서 손목과 팔꿈치 방향까지 팔 길이의 배수 · "
            "손의 방향(손목→가운뎃손가락 마디)과 손바닥이 보는 쪽 · 모두 시선 축(x 오른쪽 y 위 z 앞). curl·thumb 손가락을 접는 각(도) · "
            "aim 손목을 처형 대상 쪽으로 당기는 몫(오른손은 목 위 · 왼손은 머리 위) · weight 이 팔을 이 동작이 갖는 몫. held 1인 동안 칼이 오른손에. "
            "mark: ready(달려드는 동안 뽑은 칼이 여기서 기다림) strike(칼이 목에 박힘 = 처형 타격) out(빠짐) sheath(칼집에 넣음))")
    lines = [",".join(header), note]

    def fmt(v):
        return "%.4f" % v

    for n, t in enumerate(merged):
        cells = ["k%03d" % n, fmt(t)]
        for keys in (RIGHT, LEFT):
            wrist, elbow, along, palm, curl, thumb, aim, weight = arm(keys, t)
            for vec in (wrist, elbow, along, palm):
                cells += [fmt(c) for c in vec]
            cells += ["%.1f" % curl, "%.1f" % thumb, "%.3f" % aim, "%.3f" % weight]
        cells.append("1" if HELD[0] - 1e-4 <= t < HELD[1] - 1e-4 else "0")
        cells.append(next((m for k, m in MARKS.items() if abs(k - t) < 1e-3), ""))
        lines.append(",".join(cells))
    text = "\ufeff" + "\n".join(lines) + "\n"
    # In place: the editor may hold the table mapped, and a mapped file can't be truncated by opening it for writing.
    data = text.encode("utf-8")
    if os.path.exists(OUT):
        with open(OUT, "r+b") as f:
            f.write(data)
            f.truncate()
    else:
        with open(OUT, "wb") as f:
            f.write(data)
    print("wrote", OUT, len(merged), "rows")


if __name__ == "__main__":
    main()
