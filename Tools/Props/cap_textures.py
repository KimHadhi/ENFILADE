"""Shrinks how big textures are imported, game-wide, by editing their .meta only (the images stay as they are, so
git checkout of the .meta files brings everything back). For the user's 2026-11-14 ask after the night's lag:
"내가 보기엔 텍스쳐 문제 같은데 최적화. 최대한 줄일 수 있는 대로 줄여 봐."

usage: python -I cap_textures.py <project root> [--cap N] [--compress] [--write]
  --cap N      lower maxTextureSize to N for every mipmapped texture above it (default 2048: only the 4K ones)
  --compress   mipmapped textures imported uncompressed (Compression None) and 1 MB or more get High Quality
               compression (BC7 on PC; BC5 for normal maps), about a quarter of the memory
  --write      write the .meta files; without it nothing is written and the list, the memory and the reimport time
               it would take are printed
Left alone: textures without mipmaps (UI sprites, icons, font atlases: the runtime texture quality setting can't
reach them either, and they are small), HDR images (skies), anything under an Editor folder or Scenes (lightmaps,
probes, baked data), and every .meta someone has changed but not committed (git status).
Each changed texture is imported again by the open editor at its next refresh: that is the cost to plan for.
"""
import os
import re
import struct
import subprocess
import sys

root = sys.argv[1]
cap = int(sys.argv[sys.argv.index("--cap") + 1]) if "--cap" in sys.argv else 2048
compress = "--compress" in sys.argv
write = "--write" in sys.argv
IMAGES = (".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".bmp")
# Seconds the user's editor took to import textures again on 2026-11-14 (Editor.log, 821 TextureImporter lines):
# 2048 px about 0.45 s, 1024 px and less under 0.1 s. A 4096 source read and scaled down costs about twice a 2048.
SECONDS = {4096: 1.0, 2048: 0.45, 1024: 0.15}


def image_size(path):
    with open(path, "rb") as f:
        head = f.read(262144)
    if head[:8] == b"\x89PNG\r\n\x1a\n":
        return struct.unpack(">II", head[16:24])
    if head[:2] == b"\xff\xd8":
        i = 2
        while i + 9 < len(head):
            if head[i] != 0xFF:
                i += 1
                continue
            if head[i + 1] in (0xC0, 0xC1, 0xC2):
                h, w = struct.unpack(">HH", head[i + 5:i + 9])
                return w, h
            i += 2 + struct.unpack(">H", head[i + 2:i + 4])[0]
        return None
    if head[:4] == b"8BPS":
        h, w = struct.unpack(">II", head[14:22])
        return w, h
    if head[:2] == b"BM":
        w, h = struct.unpack("<ii", head[18:26])
        return w, abs(h)
    if path.lower().endswith(".tga") and len(head) >= 18:
        return struct.unpack("<HH", head[12:16])
    if head[:4] in (b"II*\x00", b"MM\x00*"):
        endian = "<" if head[:2] == b"II" else ">"
        offset = struct.unpack(endian + "I", head[4:8])[0]
        count = struct.unpack(endian + "H", head[offset:offset + 2])[0]
        w = h = None
        for k in range(count):
            e = offset + 2 + k * 12
            tag, kind = struct.unpack(endian + "HH", head[e:e + 4])
            val = struct.unpack(endian + ("H" if kind == 3 else "I"), head[e + 8:e + (10 if kind == 3 else 12)])[0]
            if tag == 256:
                w = val
            elif tag == 257:
                h = val
        return (w, h) if w and h else None
    return None


def uncommitted():
    out = subprocess.run(["git", "-C", root, "status", "--porcelain", "-uall", "--", "Assets"], capture_output=True, text=True,
                         encoding="utf-8", errors="replace").stdout
    return {line[3:].strip().strip('"').replace("\\", "/") for line in out.splitlines() if line.endswith(".meta") or line.endswith('.meta"')}


busy = uncommitted()
changes, skipped_busy = [], 0
before_mb = after_mb = seconds = 0.0
for dirpath, dirs, files in os.walk(os.path.join(root, "Assets")):
    rel_dir = os.path.relpath(dirpath, root).replace("\\", "/")
    if "/Editor" in rel_dir + "/" and ("/Editor/" in rel_dir + "/") or rel_dir.startswith("Assets/_Project/Scenes"):
        continue
    for name in files:
        if not name.lower().endswith(IMAGES):
            continue
        path = os.path.join(dirpath, name)
        rel = os.path.relpath(path, root).replace("\\", "/")
        meta_path = path + ".meta"
        if not os.path.exists(meta_path):
            continue
        meta = open(meta_path, "rb").read().decode("utf-8")
        if "TextureImporter:" not in meta or re.search(r"\n\s*enableMipMap: 0", meta):
            continue
        size = image_size(path)
        if not size:
            continue
        w, h = size
        new = meta
        notes = []
        # Every platform block's size and the importer's own field above them.
        sizes = [int(v) for v in re.findall(r"maxTextureSize: (\d+)", meta)]
        largest_import = min(max(w, h), max(sizes) if sizes else 2048)
        if largest_import > cap:
            new = re.sub(r"maxTextureSize: (\d+)", lambda m: f"maxTextureSize: {min(int(m.group(1)), cap)}", new)
            notes.append(f"{largest_import}->{cap}")
        normal = re.search(r"\n\s*textureType: 1\b", meta) is not None
        uncompressed = re.search(r"buildTarget: DefaultTexturePlatform\s*\n(?:\s{4,}[^\n]*\n)*?\s{4,}textureCompression: 0", meta) is not None
        side = min(max(w, h), cap if notes else largest_import)
        texels = side * side * (w * h / max(w, h) ** 2)
        if compress and uncompressed and texels * 4 * 4 / 3 >= 1048576:
            new = re.sub(r"(\n\s{4,}textureCompression: )0\b", r"\g<1>2", new)
            notes.append("compressed")
        if new == meta:
            continue
        if rel + ".meta" in busy:
            skipped_busy += 1
            continue
        bpt_before = 4 if uncompressed else (1 if normal else 1)
        bpt_after = 1 if (uncompressed and "compressed" in notes) else bpt_before
        old_side = largest_import
        before_mb += old_side * old_side * (w * h / max(w, h) ** 2) * bpt_before * 4 / 3 / 1048576
        after_mb += texels * bpt_after * 4 / 3 / 1048576
        seconds += SECONDS.get(min((k for k in SECONDS if k >= old_side), default=4096), 1.0) if old_side > 1024 else 0.15
        changes.append((rel, meta_path, new, " ".join(notes)))

folders = {}
for rel, _, _, notes in changes:
    key = "/".join(rel.split("/")[1:3])
    folders[key] = folders.get(key, 0) + 1
print(f"{len(changes)} textures to change (cap {cap}{', compress' if compress else ''}); {skipped_busy} left alone (their .meta has uncommitted changes)")
print(f"memory on the card for them if all loaded: {before_mb:.0f} MB -> {after_mb:.0f} MB")
print(f"import again: about {seconds / 60:.1f} min of work; the editor spreads it over its import workers, so the wait is"
      f" roughly {seconds / 60 / 3:.0f}-{seconds / 60 / 1.5:.0f} min")
for key, n in sorted(folders.items(), key=lambda kv: -kv[1])[:20]:
    print(f"  {n:5d}  {key}")
if write:
    for rel, meta_path, new, _ in changes:
        data = new.encode("utf-8")
        with open(meta_path, "r+b") as f:
            old = f.read()
            f.seek(0)
            f.write(data)
            if len(data) < len(old):
                f.truncate(len(data))
    print(f"written: {len(changes)} .meta files")
