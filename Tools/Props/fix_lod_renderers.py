"""Take out of every prefab's LODGroup the renderers that live in another asset.

usage: python -I fix_lod_renderers.py FOLDER [FOLDER ...]

The Dekogon construction prefabs (Assets/ThirdParty/Dekogon) list in their LODGroup, beside their own LOD meshes, the
UCX_ collision mesh's renderer inside the source FBX (its importer made a renderer of it, and the FBX's own LODGroup holds
it as LOD0). A prefab instance never draws a renderer that sits in another asset, so the entry does nothing, but every
time the prefab is placed Unity warns "Renderer 'UCX_..._LOD0' is registered with more than one LODGroup" (the map
builders placed hundreds: 11-12, the ENFILADE tower's crates). The prefab's own renderers stay; a LOD left with none
gets an empty list. import_unitypackage.py does the same on import, so a fresh import is already clean.
"""
import os
import re
import sys

FOREIGN = re.compile(rb"^    - renderer: \{fileID: -?\d+, guid: [0-9a-f]{32},(?:\r?\n[ \t]+| )type: \d+\}\r?\n", re.M)
EMPTY = re.compile(rb"^(    renderers:)(\r?\n)(?!    - renderer:)", re.M)


def clean(data):
    """The prefab text without foreign LOD renderers, and how many it dropped."""
    data, dropped = FOREIGN.subn(b"", data)
    if dropped:
        data = EMPTY.sub(rb"\1 []\2", data)
    return data, dropped


if __name__ == "__main__":
    files = changed = dropped = 0
    for root in sys.argv[1:]:
        for folder, _, names in os.walk(root):
            for name in names:
                if not name.endswith(".prefab"):
                    continue
                path = os.path.join(folder, name)
                with open(path, "rb") as f:
                    data = f.read()
                files += 1
                new, n = clean(data)
                if n:
                    with open(path, "wb") as f:
                        f.write(new)
                    changed += 1
                    dropped += n
    print(f"{files} prefabs, {changed} changed, {dropped} foreign LOD renderers taken out")
