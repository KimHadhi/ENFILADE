"""Synthesizes the headshot confirm clips: Assets/_Project/Art/Audio/HitConfirm/Head/headshot_ping_01..03.wav.

Why a synthesized clip: the user found the old headshot sound mechanical and asked for one in the spirit of Overwatch's
headshot ("찰지고 시원하고 경쾌한": snappy, airy, light, 2026-11-12). Pure sine partials read as a beep, so this is a
struck piece of metal instead:
  - a snap: a 1 ms noise burst rung through two resonances (3.4 and 7.2 kHz), the crack you feel before the tone;
  - the ting: a free bar's inharmonic partials (1, 2, 2.76, 4.1, 5.4) as slightly detuned pairs that shimmer, opening
    4.5% sharp and dropping to pitch within a few milliseconds, which is what makes it sound quick and bright;
  - weight: a short knock whose pitch falls (1150 to 540 Hz) and a soft low thump, so it lands instead of only ringing;
  - air: a small bright room (four damped combs, two all-passes, high-passed) mixed in lightly, gone within .35 s.
Three variants about a semitone apart, so a run of headshots doesn't repeat one sample (SoundBank skips the last pick).
Only the standard library is used. Run with:  python -I Tools/Audio/headshot_ping.py
The .meta files next to the clips keep their GUIDs, so re-running only replaces the sound.
"""
import math
import pathlib
import random
import struct
import wave

SR = 48000
LENGTH = .36
OUT = pathlib.Path(__file__).resolve().parents[2] / "Assets" / "_Project" / "Art" / "Audio" / "HitConfirm" / "Head"
VARIANTS = [(1880.0, 11), (1990.0, 23), (2110.0, 37)]


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


def highpass(x, f, q=.707):
    w = 2 * math.pi * f / SR
    alpha, cw = math.sin(w) / (2 * q), math.cos(w)
    return biquad(x, (1 + cw) / 2, -(1 + cw), (1 + cw) / 2, 1 + alpha, -2 * cw, 1 - alpha)


def room(x):
    """A small bright room: four damped comb filters in parallel, then two all-passes."""
    out = [0.0] * len(x)
    for delay_s, feedback in ((.0297, .6), (.0371, .58), (.0411, .56), (.0437, .54)):
        d = int(SR * delay_s)
        line, at, low = [0.0] * d, 0, 0.0
        for i, v in enumerate(x):
            y = line[at]
            low = y * .55 + low * .45
            line[at] = v + low * feedback
            at = (at + 1) % d
            out[i] += y * .25
    for delay_s in (.005, .0017):
        d = int(SR * delay_s)
        line, at, g = [0.0] * d, 0, .7
        passed = [0.0] * len(out)
        for i, v in enumerate(out):
            b = line[at]
            y = -g * v + b
            line[at] = v + g * y
            at = (at + 1) % d
            passed[i] = y
        out = passed
    return highpass(out, 1200)


def render(f0, seed):
    rnd = random.Random(seed)
    n = int(SR * LENGTH)
    attack = [1 - math.exp(-(i / SR) / .0005) for i in range(n)]

    burst = [(rnd.random() * 2 - 1) * math.exp(-(i / SR) / .0011) for i in range(n)]
    low_ring, high_ring = bandpass(burst, 3400, .9), bandpass(burst, 7200, 1.6)
    snap = [a * .8 + b * .5 for a, b in zip(low_ring, high_ring)]

    ting = [0.0] * n
    for ratio, amp, tau in ((1.0, 1.0, .10), (2.0, .2, .055), (2.756, .45, .05), (4.1, .12, .03), (5.404, .14, .022)):
        # Both halves of a pair start in phase, so every variant opens at full strength and the shimmer only grows as
        # they drift apart; random phases let some pairs start cancelled, and the hit went soft for a beat.
        start = rnd.random() * 2 * math.pi
        for detune in (-.0008, .0008):
            phase = start
            for i in range(n):
                t = i / SR
                f = f0 * ratio * (1 + detune) * (1 + .045 * math.exp(-t / .007))
                phase += 2 * math.pi * f / SR
                ting[i] += amp * .5 * attack[i] * math.exp(-t / tau) * math.sin(phase)

    knock, thump = [0.0] * n, [0.0] * n
    pk = pt = 0.0
    for i in range(n):
        t = i / SR
        pk += 2 * math.pi * (540 + 610 * math.exp(-t / .008)) / SR
        pt += 2 * math.pi * (95 + 95 * math.exp(-t / .01)) / SR
        knock[i] = .42 * attack[i] * math.exp(-t / .016) * math.sin(pk)
        thump[i] = .3 * attack[i] * math.exp(-t / .03) * math.sin(pt)

    dry = [s * .85 + g * .75 + k + h for s, g, k, h in zip(snap, ting, knock, thump)]
    wet = room(dry)
    mix = [d + .16 * w for d, w in zip(dry, wet)]

    peak = max(abs(v) for v in mix) or 1.0
    drive = math.tanh(1.5)
    mix = [math.tanh(1.5 * v * .9 / peak) / drive for v in mix]
    fade = int(SR * .04)
    for i in range(fade):
        mix[n - fade + i] *= .5 + .5 * math.cos(math.pi * i / fade)
    peak = max(abs(v) for v in mix) or 1.0
    return [v * .89 / peak for v in mix]


def write(path, samples):
    with wave.open(str(path), "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(SR)
        f.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, v)) * 32767)) for v in samples))


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for index, (f0, seed) in enumerate(VARIANTS, 1):
        path = OUT / ("headshot_ping_%02d.wav" % index)
        write(path, render(f0, seed))
        print(path, path.stat().st_size)
