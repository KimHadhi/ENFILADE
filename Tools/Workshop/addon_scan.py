"""Read-only scan of workshop addons for the addon table (Docs/AddonPortNotes.md).

Run with -I so nothing next to the archives can be imported:
  python -I addon_scan.py list <file.vpk|file.gma|legacy.bin|folder>
      Prints every file in one addon with its size.
  python -I addon_scan.py scan <l4d2_workshop_dir> <gmod_workshop_dir> <out.json>
      Summarises every addon in both folders: title, author, description, what kinds of files it holds and how
      many, texture sizes (from each .vtf header), model vertex counts (from each .vvd header), bone and sequence
      counts (from each .mdl header), maps, scripts and particles.

Nothing is extracted, written next to the addons or executed: only an archive's directory is read, and of a file
only its first bytes (a texture's or model's header). Workshop folders: L4D2 left4dead2/addons/workshop/<id>.vpk
(single-file VPK v1/v2, or an unpacked <id>/ folder), GMod workshop/content/4000/<id>/*.gma (GMAD) or the
LZMA-packed *_legacy.bin of old items.
"""
import datetime
import json
import lzma
import os
import re
import struct
import sys

HEAD_BYTES = 200  # enough for a .mdl header up to its sequence count


class Archive:
    """One addon's files: names, sizes, and the first bytes of any file."""

    def __init__(self, path):
        self.path = path
        self.files = {}  # name -> size
        self.meta = {}

    def head(self, name, count=HEAD_BYTES):
        raise NotImplementedError

    def text(self, name, cap=65536):
        return self.head(name, min(cap, self.files.get(name, 0))).decode('utf-8', 'replace')


def _cstr(read):
    out = bytearray()
    while True:
        c = read(1)
        if not c or c == b'\0':
            return out.decode('utf-8', 'replace')
        out += c


class Vpk(Archive):
    """A single-file VPK (version 1 or 2): the directory tree is read, data only on request."""

    def __init__(self, path):
        super().__init__(path)
        self.where = {}
        with open(path, 'rb') as f:
            sig, version, tree = struct.unpack('<III', f.read(12))
            if sig != 0x55AA1234:
                raise ValueError('not a VPK')
            self.head_size = 12 if version == 1 else 28
            f.seek(self.head_size)
            data = f.read(tree)
        pos = 0

        def cstr():
            nonlocal pos
            end = data.index(b'\0', pos)
            text = data[pos:end].decode('utf-8', 'replace')
            pos = end + 1
            return text

        while pos < len(data):
            ext = cstr()
            if not ext:
                break
            while True:
                folder = cstr()
                if not folder:
                    break
                while True:
                    name = cstr()
                    if not name:
                        break
                    _crc, preload, archive, offset, length, _term = struct.unpack_from('<IHHIIH', data, pos)
                    pos += 18
                    pre = data[pos:pos + preload]
                    pos += preload
                    full = (name + '.' + ext) if folder.strip() == '' else (folder + '/' + name + '.' + ext)
                    full = full.lower()
                    self.files[full] = preload + length
                    self.where[full] = (pre, archive, offset, length)
        self.tree = tree

    def head(self, name, count=HEAD_BYTES):
        pre, archive, offset, length = self.where[name]
        body = pre[:count]
        if len(body) < count and length and archive == 0x7FFF:
            with open(self.path, 'rb') as f:
                f.seek(self.head_size + self.tree + offset)
                body += f.read(min(length, count - len(body)))
        return body


class Gma(Archive):
    """A GMod GMAD archive, from a file or from bytes (an unpacked legacy .bin)."""

    def __init__(self, path, blob=None):
        super().__init__(path)
        self.blob = blob
        f = open(path, 'rb') if blob is None else None
        try:
            src = f if f is not None else _Bytes(blob)
            if src.read(4) != b'GMAD':
                raise ValueError('not a GMA')
            version = src.read(1)[0]
            src.read(16)  # steam id, timestamp
            if version > 1:
                while _cstr(src.read):
                    pass
            self.meta['title'] = _cstr(src.read)
            self.meta['description'] = _cstr(src.read)
            self.meta['author'] = _cstr(src.read)
            src.read(4)
            table = []
            while True:
                number = struct.unpack('<I', src.read(4))[0]
                if number == 0:
                    break
                name = _cstr(src.read).lower()
                size = struct.unpack('<q', src.read(8))[0]
                src.read(4)
                table.append((name, size))
            offset = src.tell()
            self.offsets = {}
            for name, size in table:
                self.files[name] = size
                self.offsets[name] = offset
                offset += size
        finally:
            if f is not None:
                f.close()

    def head(self, name, count=HEAD_BYTES):
        start, size = self.offsets[name], self.files[name]
        if self.blob is not None:
            return self.blob[start:start + min(size, count)]
        with open(self.path, 'rb') as f:
            f.seek(start)
            return f.read(min(size, count))


class _Bytes:
    def __init__(self, blob):
        self.blob, self.pos = blob, 0

    def read(self, n):
        out = self.blob[self.pos:self.pos + n]
        self.pos += n
        return out

    def tell(self):
        return self.pos


class Zip(Archive):
    """A workshop item packed as a zip under a .vpk name (some carry programs: listed, never run)."""

    def __init__(self, path):
        super().__init__(path)
        import zipfile
        self.zip = zipfile.ZipFile(path)
        self.names = {}
        for info in self.zip.infolist():
            if info.is_dir():
                continue
            low = info.filename.lower()
            self.files[low] = info.file_size
            self.names[low] = info.filename

    def head(self, name, count=HEAD_BYTES):
        with self.zip.open(self.names[name]) as f:
            return f.read(count)


class Folder(Archive):
    """An addon left unpacked in a folder."""

    def __init__(self, path):
        super().__init__(path)
        self.where = {}
        for root, _dirs, names in os.walk(path):
            for name in names:
                full = os.path.join(root, name)
                rel = os.path.relpath(full, path).replace(os.sep, '/').lower()
                self.files[rel] = os.path.getsize(full)
                self.where[rel] = full

    def head(self, name, count=HEAD_BYTES):
        with open(self.where[name], 'rb') as f:
            return f.read(count)


def open_addon(path):
    if os.path.isdir(path):
        return Folder(path)
    low = path.lower()
    with open(path, 'rb') as f:
        magic = f.read(4)
    if magic[:2] == b'PK':
        return Zip(path)
    if low.endswith('.vpk'):
        return Vpk(path)
    if low.endswith('.bin'):
        with open(path, 'rb') as f:
            blob = lzma.decompress(f.read(), format=lzma.FORMAT_ALONE)
        return Gma(path, blob)
    return Gma(path)


# ---- what the files say ----

KINDS = {
    'mdl': 'models', 'vvd': 'models', 'vtx': 'models', 'phy': 'models', 'ani': 'models',
    'vtf': 'textures', 'vmt': 'materials', 'png': 'images', 'jpg': 'images', 'jpeg': 'images', 'tga': 'images',
    'wav': 'sounds', 'mp3': 'sounds', 'ogg': 'sounds',
    'bsp': 'maps', 'nav': 'maps', 'ain': 'maps', 'lmp': 'maps',
    'lua': 'lua', 'nut': 'vscripts', 'nuc': 'vscripts', 'txt': 'texts', 'res': 'texts', 'cfg': 'texts',
    'pcf': 'particles', 'ttf': 'fonts', 'otf': 'fonts',
    'exe': 'programs', 'dll': 'programs', 'bat': 'programs', 'cmd': 'programs', 'ps1': 'programs', 'so': 'programs',
}


def kv_pairs(text):
    """Key/value pairs of a KeyValues text (addoninfo.txt), keys lower-cased; nesting ignored."""
    pairs = {}
    for m in re.finditer(r'"?([A-Za-z0-9_]+)"?\s+"([^"]*)"', text):
        pairs.setdefault(m.group(1).lower(), m.group(2))
    return pairs


def vtf_size(raw):
    if len(raw) >= 20 and raw[:4] == b'VTF\0':
        return struct.unpack_from('<HH', raw, 16)
    return None


def vvd_verts(raw):
    if len(raw) >= 20 and raw[:4] == b'IDSV':
        return struct.unpack_from('<i', raw, 16)[0]
    return None


def mdl_info(raw):
    if len(raw) >= 192 and raw[:4] == b'IDST':
        bones = struct.unpack_from('<i', raw, 156)[0]
        seqs = struct.unpack_from('<i', raw, 188)[0]
        return bones, seqs
    return None


def summarise(addon):
    out = {'files': len(addon.files), 'bytes': sum(addon.files.values()), 'counts': {}, 'bytes_by': {}, 'folders': {}}
    for name, size in addon.files.items():
        ext = name.rsplit('.', 1)[-1] if '.' in name.rsplit('/', 1)[-1] else ''
        kind = KINDS.get(ext, 'other')
        out['counts'][kind] = out['counts'].get(kind, 0) + 1
        out['bytes_by'][kind] = out['bytes_by'].get(kind, 0) + size
        parts = name.split('/')
        top = '/'.join(parts[:2]) if len(parts) > 2 else parts[0]
        out['folders'][top] = out['folders'].get(top, 0) + 1
    # Textures: sizes from the headers.
    sizes = []
    for name in addon.files:
        if name.endswith('.vtf'):
            try:
                wh = vtf_size(addon.head(name, 20))
            except Exception:
                wh = None
            if wh:
                sizes.append(max(wh))
    if sizes:
        sizes.sort()
        out['textures'] = {'n': len(sizes), 'max': sizes[-1], 'median': sizes[len(sizes) // 2],
                           'le256': sum(1 for s in sizes if s <= 256), 'ge2048': sum(1 for s in sizes if s >= 2048)}
    # Models: vertex count (LOD 0), bones and sequences per .mdl.
    models = []
    for name in addon.files:
        if not name.endswith('.mdl'):
            continue
        entry = {'path': name}
        try:
            info = mdl_info(addon.head(name, 192))
            if info:
                entry['bones'], entry['seqs'] = info
        except Exception:
            pass
        vvd = name[:-4] + '.vvd'
        if vvd in addon.files:
            try:
                v = vvd_verts(addon.head(vvd, 20))
                if v is not None:
                    entry['verts'] = v
            except Exception:
                pass
        models.append(entry)
    if models:
        verts = sorted(m['verts'] for m in models if 'verts' in m)
        out['models'] = {'n': len(models), 'with_mesh': len(verts),
                         'verts_min': verts[0] if verts else 0, 'verts_median': verts[len(verts) // 2] if verts else 0,
                         'verts_max': verts[-1] if verts else 0,
                         'seqs_total': sum(m.get('seqs', 0) for m in models),
                         'anim_only': sum(1 for m in models if 'verts' not in m and m.get('seqs', 0) > 0),
                         'list': sorted(models, key=lambda m: -m.get('verts', 0))[:12]}
    maps = sorted(n.rsplit('/', 1)[-1] for n in addon.files if n.endswith('.bsp'))
    if maps:
        out['maps'] = maps[:20]
    return out


def describe(addon, source):
    info = {}
    if source == 'L4D2':
        name = next((n for n in addon.files if n == 'addoninfo.txt'), None)
        if name:
            kv = kv_pairs(addon.text(name))
            info['title'] = kv.get('addontitle', '')
            info['author'] = kv.get('addonauthor', '')
            info['description'] = kv.get('addondescription', '')
            info['flags'] = sorted(k[len('addoncontent_'):] for k, v in kv.items()
                                   if k.startswith('addoncontent_') and v.strip() not in ('', '0'))
    else:
        info['title'] = addon.meta.get('title', '')
        info['author'] = addon.meta.get('author', '')
        raw = addon.meta.get('description', '')
        try:
            parsed = json.loads(raw)
            info['description'] = parsed.get('description', '')
            info['flags'] = [parsed.get('type', '')] + list(parsed.get('tags', []))
        except Exception:
            info['description'] = raw
            info['flags'] = []
    info['description'] = (info.get('description') or '')[:700]
    return info


def scan(l4d2_dir, gmod_dir):
    found = []
    for name in sorted(os.listdir(l4d2_dir)):
        path = os.path.join(l4d2_dir, name)
        if name.lower().endswith('.vpk'):
            found.append(('L4D2', name[:-4], path))
        elif os.path.isdir(path):
            found.append(('L4D2', name, path))
    for folder in sorted(os.listdir(gmod_dir)):
        base = os.path.join(gmod_dir, folder)
        if not os.path.isdir(base):
            continue
        names = sorted(os.listdir(base))
        # gmpublisher.gma is what Steam puts there; any other copy beside it is someone's spare.
        pick = next((n for n in names if n.lower() == 'gmpublisher.gma'), None) or \
            next((n for n in names if n.lower().endswith(('.gma', '.bin'))), None)
        if pick:
            found.append(('GMod', folder, os.path.join(base, pick)))
    rows = []
    for source, addon_id, path in found:
        row = {'source': source, 'id': addon_id, 'file': os.path.basename(path.rstrip('/\\'))}
        stamp = os.path.getmtime(path)
        row['downloaded'] = datetime.datetime.fromtimestamp(stamp).strftime('%Y-%m-%d %H:%M')
        try:
            addon = open_addon(path)
            row.update(describe(addon, source))
            row.update(summarise(addon))
        except Exception as error:  # a broken archive is a finding, not a stop
            row['error'] = '%s: %s' % (type(error).__name__, error)
        rows.append(row)
        print('%s %s %s' % (source, addon_id, row.get('title', row.get('error', ''))[:60]), file=sys.stderr)
    return rows


def main(argv):
    if len(argv) >= 2 and argv[0] == 'list':
        addon = open_addon(argv[1])
        for name, size in sorted(addon.files.items()):
            print('%10d %s' % (size, name))
        return 0
    if len(argv) >= 4 and argv[0] == 'scan':
        rows = scan(argv[1], argv[2])
        with open(argv[3], 'w', encoding='utf-8') as f:
            json.dump(rows, f, ensure_ascii=False, indent=1)
        print('%d addons -> %s' % (len(rows), argv[3]))
        return 0
    print(__doc__)
    return 2


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
