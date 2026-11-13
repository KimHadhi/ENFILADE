"""Finds a song's highlights (the loud, full parts) by measuring how loud it is, for Music.csv's assault column.

    python Tools/Audio/song_sections.py Assets/_Project/Art/Resources/Audio/Music/OneBulletWellPlaced.mp3 [more files]

Prints, per file, its length, the assault cell to paste (start~end|start~end, seconds into the file) and a small
loudness graph to check it by eye. Nothing is written. It needs ffmpeg: on PATH, in FFMPEG, or the build unpacked in
Downloads (ffmpeg-*/bin/ffmpeg.exe).

How it reads a song: every second it measures the bass (kick and bass line, under 120 Hz) and the highs (hats and
cymbals, over 4 kHz), each as a share of the way from the song's own calm to its full. A highlight is where the two
stand halfway up or more on average for a while: the drop
of an electronic song, the full chorus of another. Short dips (a breath before a drop) are bridged, short bursts left
out. Ears decide in the end: the numbers it gives for the two songs of 2026-11-10 matched what was heard there (drops
at 16.1 s and 93.7 s of song 1, the assault at 99 s of song 2) to within a second or two.
"""
import array
import glob
import math
import os
import shutil
import subprocess
import sys

RATE = 11025
# A second of sound per measure; a highlight lasts this long at least; a dip this short inside one is bridged (s).
STEP, LEAST, BRIDGE = 1.0, 12, 6
# How far up from the song's calm toward its full the bass and the highs stand on average (share), smoothed over this
# many seconds.
RISE, SMOOTH = .5, 3


def ffmpeg():
    found = os.environ.get("FFMPEG") or shutil.which("ffmpeg")
    if found:
        return found
    downloads = os.path.join(os.path.expanduser("~"), "Downloads")
    builds = sorted(glob.glob(os.path.join(downloads, "ffmpeg-*", "bin", "ffmpeg*")))
    if builds:
        return builds[-1]
    sys.exit("ffmpeg not found: put it on PATH or set FFMPEG to ffmpeg.exe")


def band(tool, path, filters):
    command = [tool, "-v", "error", "-i", path, "-af", filters, "-ac", "1", "-ar", str(RATE), "-f", "s16le", "-"]
    samples = array.array("h")
    samples.frombytes(subprocess.run(command, capture_output=True, check=True).stdout)
    size = int(RATE * STEP)
    levels = []
    for start in range(0, len(samples) - size + 1, size):
        chunk = samples[start:start + size]
        power = sum(x * x for x in chunk) / size / (32768.0 * 32768.0)
        levels.append(10 * math.log10(power + 1e-12))
    return levels


def share(values):
    """Each level as a share of the way from the song's calm (its 20th percentile) to its full (its 90th)."""
    ordered = sorted(values)
    calm, full = ordered[len(ordered) // 5], ordered[len(ordered) * 9 // 10]
    room = max(.5, full - calm)
    return [(v - calm) / room for v in values]


def smooth(values, width):
    half = width // 2
    return [sum(values[max(0, i - half):i + half + 1]) / len(values[max(0, i - half):i + half + 1]) for i in range(len(values))]


def sections(low, high):
    score = smooth([(a + b) * .5 for a, b in zip(share(low), share(high))], SMOOTH)
    loud = [s >= RISE for s in score]
    # Bridge short dips, then keep the long stretches.
    for i in range(len(loud)):
        if loud[i]:
            continue
        j = i
        while j < len(loud) and not loud[j]:
            j += 1
        if 0 < i and j < len(loud) and j - i <= BRIDGE:
            for k in range(i, j):
                loud[k] = True
    spans, start = [], None
    for i, on in enumerate(loud + [False]):
        if on and start is None:
            start = i
        elif not on and start is not None:
            if i - start >= LEAST:
                spans.append((start * STEP, i * STEP))
            start = None
    return spans, score


def sharpen(low, spans):
    """Moves each start to the second the bass jumps most into its full near it: a drop lands on its first kick, and
    the smoothing lets the build before it in (an intro's lone hit jumps too, but not into the full)."""
    full = share(low)
    better = []
    for start, end in spans:
        at = int(start / STEP)
        window = [i for i in range(max(1, at - 3), min(len(low) - 1, at + 10) + 1) if full[i] >= .4]
        jump = max(window, key=lambda i: low[i] - low[i - 1]) if window else at
        better.append((jump * STEP, end))
    return better


def main(paths):
    tool = ffmpeg()
    for path in paths:
        low = band(tool, path, "lowpass=f=120")
        high = band(tool, path, "highpass=f=4000")
        spans, score = sections(low, high)
        spans = sharpen(low, spans)
        name = os.path.splitext(os.path.basename(path))[0]
        print(f"== {name}: {len(low) * STEP:.0f} s")
        print("assault: " + "|".join(f"{a:g}~{b:g}" for a, b in spans))
        for i in range(0, len(score), 2):
            mark = "#" if any(a <= i * STEP < b for a, b in spans) else "."
            print(f"  {i * STEP:5.0f} {mark} " + "=" * max(0, min(40, int((score[i] + .5) * 20))))


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    main(sys.argv[1:])
