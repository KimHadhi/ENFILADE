"""Synthesizes the hub range's steel target sounds: Assets/_Project/Art/Audio/Bullets/Steel/Head, Body and Down.

Why: the user wanted the range targets to sound like real steel, not a body bursting ("타격음이 피 터지는 소리 말고
ㄹㅇ 쇠나 철로 된 타겟 때리는 사운드", 2026-11-12). A struck steel plate is a handful of inharmonic plate modes that ring
on after a hard, bright contact, so each clip is built from those parts:
  - contact: a 1-2 ms noise burst rung through a few high resonances, the crack of lead meeting hardened steel;
  - the plate: its flexural modes (a free plate's partials are not whole multiples, so it rings like metal, not a note),
    each one as a slightly detuned pair that beats, the high ones dying first, so the ring darkens as it fades;
  - the range: one slap-back echo off the berm and a short low-passed tail, quiet, as heard outdoors.
Head: a small round plate (23 cm), so a high ting (about 1.2 kHz, partials 1, 1.73, 2.32, 3.9, 4.1, 5.9), ringing long.
Body: the torso plate (46 x 62 cm), so a lower clang (about 330 Hz) with a dense cluster above it and a duller knock.
Down: the target landing on its back: a low thud, a short rattle of the stand and a dull ring of the torso plate.
Three variants of Head and Body about a semitone apart (SoundBank skips the last pick), two of Down.
Only the standard library is used. Run with:  python -I Tools/Audio/steel_target.py
The .meta files next to the clips keep their GUIDs, so re-running only replaces the sound.
"""
import math
import pathlib
import random
import struct
import wave

SR = 48000
ROOT = pathlib.Path(__file__).resolve().parents[2] / "Assets" / "_Project" / "Art" / "Audio" / "Bullets" / "Steel"

# (name, base frequency, seed)
HEAD = [(1180.0, 5), (1250.0, 17), (1320.0, 29)]
BODY = [(318.0, 41), (337.0, 53), (356.0, 67)]
DOWN = [(0.0, 71), (0.0, 83)]

# A small round plate hung by its edge: a free circular plate's modes over the first.
HEAD_MODES = ((1.0, 1.0, .55), (1.73, .62, .38), (2.32, .5, .3), (3.9, .26, .16), (4.1, .2, .14), (5.9, .1, .08))
# The torso plate: a free rectangular plate has its modes close together above the first, which is the clang.
BODY_MODES = ((1.0, .9, .62), (1.47, .7, .5), (1.94, .66, .42), (2.41, .52, .34), (2.87, .46, .28), (3.62, .34, .2),
              (4.38, .26, .15), (5.31, .18, .1), (6.7, .12, .07))


def biquad(x, b0, b1, b2, a0, a1, a2):
    b0, b1, b2, a1, a2 = b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0
    y = [0.0] * len(x)
    x1 = x2 = y1 = y2 = 0.0
    for i, v in enumerate(x):
        o = b0 * v + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        x2, x1, y2, y1 = x1, v, y1, o
        y[i] = o
    return y


def bandpass(x, f, q):
    w = 2 * math.pi * f / SR
    alpha, cw = math.sin(w) / (2 * q), math.cos(w)
    return biquad(x, alpha, 0.0, -alpha, 1 + alpha, -2 * cw, 1 - alpha)


def lowpass(x, f, q=.707):
    w = 2 * math.pi * f / SR
    alpha, cw = math.sin(w) / (2 * q), math.cos(w)
    return biquad(x, (1 - cw) / 2, 1 - cw, (1 - cw) / 2, 1 + alpha, -2 * cw, 1 - alpha)


def highpass(x, f, q=.707):
    w = 2 * math.pi * f / SR
    alpha, cw = math.sin(w) / (2 * q), math.cos(w)
    return biquad(x, (1 + cw) / 2, -(1 + cw), (1 + cw) / 2, 1 + alpha, -2 * cw, 1 - alpha)


def contact(n, rnd, decay, bands):
    """The strike: a short noise burst rung through a few resonances."""
    burst = [(rnd.random() * 2 - 1) * math.exp(-(i / SR) / decay) for i in range(n)]
    out = [0.0] * n
    for f, q, gain in bands:
        rung = bandpass(burst, f, q)
        for i in range(n):
            out[i] += rung[i] * gain
    return out


def plate(n, rnd, f0, modes, length_scale, strike):
    """The plate's modes as detuned pairs. `strike` shades the partials as where the bullet lands does."""
    attack = [1 - math.exp(-(i / SR) / .0004) for i in range(n)]
    out = [0.0] * n
    for ratio, amp, tau in modes:
        amp *= 1.0 + strike * (rnd.random() * 2 - 1)
        tau *= length_scale * (.9 + .2 * rnd.random())
        start = rnd.random() * 2 * math.pi
        spread = .0006 + .0012 * rnd.random()
        for detune in (-spread, spread):
            phase = start
            step = 2 * math.pi * f0 * ratio * (1 + detune) / SR
            for i in range(n):
                t = i / SR
                # Struck hard, a plate opens a touch sharp and settles within a few milliseconds.
                phase += step * (1 + .012 * math.exp(-t / .005))
                out[i] += amp * .5 * attack[i] * math.exp(-t / tau) * math.sin(phase)
    return out


def range_air(x, echo_s, echo_gain):
    """Outdoors: one slap-back off the berm, low-passed, and a short dark tail."""
    n = len(x)
    d = int(SR * echo_s)
    echo = [0.0] * n
    for i in range(d, n):
        echo[i] = x[i - d]
    echo = lowpass(echo, 2600)
    tail = [0.0] * n
    for delay_s, gain in ((.031, .3), (.047, .24), (.067, .18), (.089, .12)):
        k = int(SR * delay_s)
        for i in range(k, n):
            tail[i] += x[i - k] * gain
    tail = lowpass(highpass(tail, 220), 1800)
    return [v + echo_gain * e + .12 * t for v, e, t in zip(x, echo, tail)]


def finish(mix, n, fade_s, drive=1.3):
    peak = max(abs(v) for v in mix) or 1.0
    top = math.tanh(drive)
    mix = [math.tanh(drive * v * .92 / peak) / top for v in mix]
    fade = int(SR * fade_s)
    for i in range(fade):
        mix[n - fade + i] *= .5 + .5 * math.cos(math.pi * i / fade)
    peak = max(abs(v) for v in mix) or 1.0
    return [v * .89 / peak for v in mix]


def head(f0, seed):
    rnd = random.Random(seed)
    n = int(SR * 1.15)
    snap = contact(n, rnd, .0012, ((4300, 1.1, .9), (7600, 1.6, .55), (11000, 2.2, .3)))
    ring = plate(n, rnd, f0, HEAD_MODES, 1.0, .25)
    dry = [s * .7 + r for s, r in zip(snap, ring)]
    return finish(range_air(dry, .085 + .02 * rnd.random(), .16), n, .12)


def body(f0, seed):
    rnd = random.Random(seed)
    n = int(SR * 1.3)
    snap = contact(n, rnd, .0018, ((2600, 1.0, .9), (5200, 1.4, .6), (8800, 2.0, .3)))
    ring = plate(n, rnd, f0, BODY_MODES, 1.0, .35)
    # The plate's weight: a short knock that falls in pitch, felt more than heard.
    knock = [0.0] * n
    phase = 0.0
    for i in range(n):
        t = i / SR
        phase += 2 * math.pi * (150 + 260 * math.exp(-t / .006)) / SR
        knock[i] = .55 * math.exp(-t / .03) * math.sin(phase)
    dry = [s * .8 + r + k for s, r, k in zip(snap, ring, knock)]
    return finish(range_air(dry, .09 + .025 * rnd.random(), .18), n, .14)


def down(_, seed):
    rnd = random.Random(seed)
    n = int(SR * .95)
    # The thud of the frame on the ground.
    thud = [0.0] * n
    phase = 0.0
    for i in range(n):
        t = i / SR
        phase += 2 * math.pi * (58 + 70 * math.exp(-t / .02)) / SR
        thud[i] = math.exp(-t / .07) * math.sin(phase)
    noise = [(rnd.random() * 2 - 1) * math.exp(-(i / SR) / .03) for i in range(n)]
    thud = [a + .35 * b for a, b in zip(thud, lowpass(noise, 420))]
    # The stand rattling: a few small knocks scattered over the first 90 ms.
    rattle = [0.0] * n
    for k in range(6):
        at = int(SR * (.004 + .085 * rnd.random()))
        hit = contact(n - at, rnd, .0009, ((1900 + 2600 * rnd.random(), 3.0, .7), (5200 + 2000 * rnd.random(), 4.0, .4)))
        gain = .35 + .4 * rnd.random()
        for i in range(n - at):
            rattle[at + i] += hit[i] * gain
    # The torso plate rings dully as it lands, held against the ground.
    ring = plate(n, rnd, 300.0 + 30 * rnd.random(), BODY_MODES, .35, .3)
    dry = [h * 1.1 + r * .8 + g * .35 for h, r, g in zip(thud, rattle, ring)]
    return finish(range_air(dry, .1, .12), n, .1, drive=1.6)


def write(path, samples):
    with wave.open(str(path), "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(SR)
        f.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, v)) * 32767)) for v in samples))


if __name__ == "__main__":
    for folder, prefix, variants, make in (("Head", "steel_head", HEAD, head), ("Body", "steel_body", BODY, body),
                                           ("Down", "steel_down", DOWN, down)):
        out = ROOT / folder
        out.mkdir(parents=True, exist_ok=True)
        for index, (f0, seed) in enumerate(variants, 1):
            path = out / ("%s_%02d.wav" % (prefix, index))
            write(path, make(f0, seed))
            print(path, path.stat().st_size)
