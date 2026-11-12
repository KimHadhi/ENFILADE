"""Unpacks the parts of BOOTH .unitypackage files the game uses into Assets/ThirdParty/BOOTH, without Unity.

A .unitypackage is a gzip tar of folders named by GUID, each holding `pathname`, `asset` and `asset.meta`. This keeps
every GUID, so materials, textures and prefabs still find each other, and a newer package imported later through
Unity lands on the same files. Only what the chosen prefabs need comes in: their references are followed through
prefabs, materials and model metas, across packages (an outfit's prefabs in one package, its materials in another).
VRChat-only files (animator controllers, expression menus, AFK effects) and paint sources (PSD, CLIP, BLEND) stay out
unless a kept material uses them. Model metas are switched to Read/Write, which the body needs to measure skin and
skirts (BodySurface, SkinSample) and to find which meshes sit on the head.

Usage (each package a chain of zips ending in the .unitypackage, then the prefabs to keep, by part of their path;
none keeps the whole package):
    python -I booth_extract.py <out folder> "<zip>::<inner zip>::<pkg>.unitypackage=0.MAYO/MAYO.prefab" ...
Run it with -I and pass the paths as arguments: the packages are downloaded data, never imported as code.
"""
import gzip
import io
import os
import re
import sys
import tarfile
import uuid
import zipfile

SKIP_EXTENSIONS = {".controller", ".anim", ".overridecontroller", ".mask", ".asset", ".playable", ".cs", ".asmdef",
                   ".dll", ".unitypackage", ".url", ".pdf", ".clip", ".blend", ".lilcontainer", ".lilblock", ".hlsl",
                   ".shader", ".txt", ".md", ".json"}
SKIP_PARTS = ("/AFK/", "/Animation/", "/icons/", "/STAMP/")
TEXT_EXTENSIONS = {".prefab", ".mat"}
GUID = re.compile(r"guid: ([0-9a-f]{32})")


def zip_name(info):
    if info.flag_bits & 0x800:
        return info.filename
    try:
        raw = info.filename.encode("cp437")
    except UnicodeEncodeError:
        # Newer Pythons already read an unflagged UTF-8 name as UTF-8 (エネ_Earring_ver1.2.zip, 2026-11-12), and such a
        # name has no cp437 bytes to go back to.
        return info.filename
    for encoding in ("utf-8", "cp932", "cp949"):
        try:
            return raw.decode(encoding)
        except UnicodeDecodeError:
            pass
    return info.filename


def open_chain(spec):
    parts = spec.split("::")
    stream = open(parts[0], "rb")
    for part in parts[1:]:
        archive = zipfile.ZipFile(stream)
        info = next(i for i in archive.infolist() if zip_name(i) == part or zip_name(i).endswith("/" + part))
        stream = archive.open(info)
    return stream


def read_package(spec, keep_assets=None):
    """GUID -> {path, meta, text (prefab/mat), asset (only GUIDs in keep_assets), folder}."""
    entries = {}
    with tarfile.open(fileobj=open_chain(spec), mode="r|gz") as tar:
        for member in tar:
            name = member.name.lstrip("./")
            if "/" not in name or not member.isfile():
                continue
            guid, leaf = name.split("/", 1)
            entry = entries.setdefault(guid, {"folder": True})
            data = tar.extractfile(member).read()
            if leaf == "pathname":
                entry["path"] = data.decode("utf-8").splitlines()[0]
            elif leaf == "asset.meta":
                entry["meta"] = data
            elif leaf == "asset":
                entry["folder"] = False
                entry["size"] = len(data)
                if keep_assets is not None and guid in keep_assets:
                    entry["asset"] = data
                elif keep_assets is None:
                    entry["head"] = data if len(data) < 2_000_000 else None
    for entry in entries.values():
        path = entry.get("path", "")
        if keep_assets is None and os.path.splitext(path)[1].lower() in TEXT_EXTENSIONS and entry.get("head") is not None:
            entry["text"] = entry["head"].decode("utf-8", "replace")
        entry.pop("head", None)
    return entries


def skipped(path):
    extension = os.path.splitext(path)[1].lower()
    return extension in SKIP_EXTENSIONS or any(part in path for part in SKIP_PARTS)


def main(out_root, specs):
    packages = []
    index = {}
    for spec in specs:
        chain, _, roots = spec.partition("=")
        entries = read_package(chain)
        packages.append((chain, [r for r in roots.split(",") if r], entries))
        for guid, entry in entries.items():
            index.setdefault(guid, entry)
        print("read", chain.split("::")[-1], len(entries), "entries")

    wanted = set()
    for _, roots, entries in packages:
        for guid, entry in entries.items():
            path = entry.get("path", "")
            if entry["folder"] or skipped(path):
                continue
            if not roots or any(root in path for root in roots):
                wanted.add(guid)
    queue = list(wanted)
    missing = set()
    while queue:
        entry = index[queue.pop()]
        for text in (entry.get("text") or "", (entry.get("meta") or b"").decode("utf-8", "replace")):
            for guid in GUID.findall(text):
                if guid in wanted:
                    continue
                found = index.get(guid)
                if found is None:
                    missing.add(guid)
                elif not found["folder"] and not skipped(found.get("path", "")):
                    wanted.add(guid)
                    queue.append(guid)

    written = 0
    total = 0
    folders = {}
    for guid, entry in index.items():
        if entry["folder"] and "path" in entry:
            folders[entry["path"]] = entry.get("meta")
    for chain, _, entries in packages:
        mine = {g for g in wanted if g in entries}
        if not mine:
            continue
        for guid, entry in read_package(chain, mine).items():
            if guid not in mine or "asset" not in entry:
                continue
            relative = entry["path"][len("Assets/"):] if entry["path"].startswith("Assets/") else entry["path"]
            target = os.path.join(out_root, relative)
            os.makedirs(os.path.dirname(target), exist_ok=True)
            if not (os.path.exists(target) and os.path.getsize(target) == entry["size"]):
                with open(target, "wb") as handle:
                    handle.write(entry["asset"])
            meta = entry.get("meta") or b""
            if target.lower().endswith(".fbx"):
                meta = re.sub(rb"(\n\s+isReadable:) 0", rb"\1 1", meta)
            with open(target + ".meta", "wb") as handle:
                handle.write(meta)
            written += 1
            total += entry["size"]

    # Folders keep the package's GUIDs; the BOOTH folder and any folder a package lacks get new ones.
    for folder, _, _ in os.walk(out_root):
        relative = os.path.relpath(folder, out_root).replace("\\", "/")
        meta_path = folder.rstrip("/\\") + ".meta"
        if os.path.exists(meta_path):
            continue
        meta = folders.get("Assets/" + relative) if relative != "." else None
        if meta is None:
            meta = ("fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
                    "  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % uuid.uuid4().hex).encode()
        with open(meta_path, "wb") as handle:
            handle.write(meta)
    print("wrote %d files, %.1f MB" % (written, total / 1e6))
    if missing:
        print("references outside the packages (shaders, built-ins):", len(missing))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2:])
