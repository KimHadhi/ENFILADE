"""Lists the assets under Assets/_Project that nothing reaches. Read-only: it deletes nothing.

    python Tools/Find-UnusedAssets.py

Reached means: a scene, ProjectSettings, anything in a Resources drawer, code, or a file whose own path or name is
written in code, tools or tables, and from those everything named by GUID (a shader's #include by file name too).
The report (Logs/unused-assets.txt) has two lists:
  1. not reached at all;
  2. reached only because the name appears somewhere (a name like "Stagger" matches by chance, so read this one).
What cannot be seen from the files and is left out of both: textures beside a model under Art/Models (the import
rules bind them by name when the model is imported), the map surface sets (a palette the map builder picks from by
name), a model's clip list (X.clips.csv) and git's own files.
Before deleting something from the lists, think whether it is material worth keeping: that goes to ArtSource/Library."""
import collections, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "Assets")
PROJECT = "Assets/_Project/"
GUID = re.compile(rb'guid:\s*([0-9a-f]{32})')
INCLUDE = re.compile(r'#include\s+"([^"]+)"')
YAML = ('.unity', '.prefab', '.asset', '.controller', '.playable', '.overridecontroller', '.mat', '.anim', '.shadergraph',
        '.shadersubgraph', '.vfx', '.lighting', '.spriteatlas', '.mixer', '.physicmaterial', '.physicsmaterial',
        '.rendertexture', '.terrainlayer', '.preset', '.mask', '.flare', '.fontsettings', '.guiskin', '.cubemap', '.signal',
        '.brush', '.scenetemplate', '.inputactions', '.vfxoperator', '.vfxblock')
# Bound by name at import or picked by name by a builder: the files do not show it.
UNSEEN = (PROJECT + "Art/Models/", PROJECT + "Art/Textures/Surfaces/")

guid_of, path_of, refs, files = {}, {}, collections.defaultdict(set), []
for folder, _, names in os.walk(ASSETS):
    for name in names:
        full = os.path.join(folder, name)
        rel = os.path.relpath(full, ROOT).replace("\\", "/")
        if name.endswith(".meta"):
            try: found = GUID.findall(open(full, "rb").read())
            except OSError: continue
            if found:
                guid_of[rel[:-5]] = found[0].decode(); path_of[found[0].decode()] = rel[:-5]
                refs[rel[:-5]].update(g.decode() for g in found[1:])
        else:
            files.append(rel)
            if name.lower().endswith(YAML):
                try: refs[rel].update(g.decode() for g in set(GUID.findall(open(full, "rb").read())))
                except OSError: pass

project = [f for f in files if f.startswith(PROJECT)]
known = set(project)
by_name = collections.defaultdict(list)
for a in project: by_name[os.path.basename(a)].append(a)
for a in project:
    if a.endswith((".shader", ".hlsl", ".cginc", ".compute")):
        try: text = open(os.path.join(ROOT, a), encoding="utf-8", errors="replace").read()
        except OSError: continue
        for inc in INCLUDE.findall(text):
            for target in by_name.get(os.path.basename(inc), ()): refs[a].add(guid_of.get(target, ""))

roots, by_chance = set(), set()
settings = os.path.join(ROOT, "ProjectSettings")
for name in os.listdir(settings):
    try: data = open(os.path.join(settings, name), "rb").read()
    except OSError: continue
    for g in set(GUID.findall(data)):
        if g.decode() in path_of: roots.add(path_of[g.decode()])
for a in project:
    if a.endswith(".unity") or "/Resources/" in a or a.endswith((".cs", ".md", ".asmdef", ".inputactions")): roots.add(a)

# What code, tools and tables write out: whole paths, and names.
written = []
for base, kinds in ((PROJECT + "Editor", (".cs", ".csv")), (PROJECT + "Scripts", (".cs",)), (PROJECT + "Data/Resources/Tables", (".csv",)),
                    ("Tools", (".py", ".json", ".ps1")), ("ArtSource", (".json", ".txt", ".cmd"))):
    for folder, _, names in os.walk(os.path.join(ROOT, base)):
        for name in names:
            if name.endswith(kinds):
                try: written.append(open(os.path.join(folder, name), "rb").read().decode("utf-8", "replace"))
                except OSError: pass
written = "\n".join(written)
paths = set(re.findall(r'Assets/_Project/[A-Za-z0-9_ .\-/()가-힣+&]+', written))
for match in re.finditer(r'"Assets",\s*"_Project"((?:,\s*"[^"]+")+)', written):
    paths.add(PROJECT + "/".join(re.findall(r'"([^"]+)"', match.group(1))))
for p in paths:
    if p.rstrip("/. ") in known: roots.add(p.rstrip("/. "))
tokens = set(re.findall(r'[A-Za-z0-9_][A-Za-z0-9_ .\-]{2,}[A-Za-z0-9_)]', written)) | set(re.findall(r'[A-Za-z0-9_\-]{4,}', written))
for a in project:
    stem = os.path.splitext(os.path.basename(a))[0]
    if a not in roots and len(stem) >= 4 and stem in tokens: by_chance.add(a)

def closure(start):
    seen, stack = set(start), list(start)
    while stack:
        for g in refs.get(stack.pop(), ()):
            target = path_of.get(g)
            if target and target not in seen:
                seen.add(target); stack.append(target)
    return seen

sure = closure(roots)
loose = closure(roots | by_chance)

def skipped(a):
    base = os.path.basename(a)
    if base in (".gitkeep", ".gitattributes") or a.endswith(".clips.csv") or a.startswith(UNSEEN): return True
    scene = re.match(r'(Assets/_Project/Scenes/.+?)/[^/]+$', a)   # a scene's own baked data lies in the folder named like it
    return bool(scene and (scene.group(1) + ".unity") in known)

def size(a):
    try: return os.path.getsize(os.path.join(ROOT, a))
    except OSError: return 0

unused = sorted(a for a in project if a not in loose and not skipped(a))
by_name_only = sorted(a for a in project if a in loose and a not in sure and not skipped(a))
lines = [f"assets under {PROJECT}: {len(project)}",
         f"1. not reached at all: {len(unused)} ({sum(map(size, unused)) / 1e6:.1f} MB)"]
lines += ["   " + a[len(PROJECT):] for a in unused]
lines += ["", f"2. reached only by a name that appears in code, tools or tables: {len(by_name_only)} ({sum(map(size, by_name_only)) / 1e6:.1f} MB)"]
lines += ["   " + a[len(PROJECT):] for a in by_name_only]
os.makedirs(os.path.join(ROOT, "Logs"), exist_ok=True)
out = os.path.join(ROOT, "Logs", "unused-assets.txt")
open(out, "w", encoding="utf-8").write("\n".join(lines) + "\n")
print(lines[1]); print(lines[len(unused) + 3]); print("->", os.path.relpath(out, ROOT))
