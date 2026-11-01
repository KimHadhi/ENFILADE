"""Reads L4D2 VPK and GMod GMA addons for porting.

Windows' python is only a store stub here, so use the one inside Blender:
  F:\\SteamLibrary\\steamapps\\common\\Blender\\4.5\\python\\bin\\python.exe -I workshop.py ...

  workshop.py inventory <l4d2_workshop_dir> <gmod_workshop_dir> <out_dir>
      Lists every addon with its Steam title and extracts its text files.
  workshop.py extract <archive_or_folder> <out_dir>
      Unpacks one addon completely, models and sounds included.

Archive contents are treated as data: nothing extracted here is executed.
"""
import collections
import json
import os
import re
import struct
import sys
import urllib.parse
import urllib.request
import zipfile

TEXT_EXT = {'lua', 'nut', 'txt', 'cfg', 'json', 'vdf', 'res', 'kv', 'properties',
            'sp', 'inc', 'md', 'qc', 'vmf_notes', 'nuc', 'ini', 'xml', 'yml', 'yaml'}
MAX_TEXT_FILE = 2 * 1024 * 1024
DESC_CAP = 1500


def cstr(buf, pos):
    end = buf.index(b'\0', pos)
    return buf[pos:end].decode('utf-8', 'replace'), end + 1


def read_vpk(path):
    """Returns (entries, reader) where entries is [(name, size)]."""
    with open(path, 'rb') as f:
        data = f.read()
    sig, ver, tree = struct.unpack_from('<III', data, 0)
    if sig != 0x55AA1234:
        raise ValueError('not a VPK')
    head = 12 if ver == 1 else 28
    pos, end = head, head + tree
    entries, where = [], {}
    while pos < end:
        ext, pos = cstr(data, pos)
        if not ext:
            break
        while True:
            folder, pos = cstr(data, pos)
            if not folder:
                break
            while True:
                name, pos = cstr(data, pos)
                if not name:
                    break
                crc, preload, arch, off, length, term = struct.unpack_from('<IHHIIH', data, pos)
                pos += 18
                pre = pos
                pos += preload
                full = (name + '.' + ext) if folder.strip() == '' else (folder + '/' + name + '.' + ext)
                entries.append((full, preload + length))
                where[full] = (pre, preload, arch, off, length)

    def reader(full):
        pre, preload, arch, off, length = where[full]
        body = data[pre:pre + preload]
        if length and arch == 0x7FFF:
            body += data[head + tree + off:head + tree + off + length]
        return body
    return entries, reader


def read_gma(path):
    """Returns (meta, entries, reader)."""
    with open(path, 'rb') as f:
        data = f.read()
    if data[:4] != b'GMAD':
        raise ValueError('not a GMA')
    ver = data[4]
    pos = 5 + 8 + 8
    if ver > 1:
        while True:
            s, pos = cstr(data, pos)
            if not s:
                break
    name, pos = cstr(data, pos)
    desc, pos = cstr(data, pos)
    author, pos = cstr(data, pos)
    pos += 4
    files = []
    while True:
        num = struct.unpack_from('<I', data, pos)[0]
        pos += 4
        if num == 0:
            break
        fname, pos = cstr(data, pos)
        size = struct.unpack_from('<q', data, pos)[0]
        pos += 8 + 4
        files.append((fname, size))
    where, cursor = {}, pos
    for fname, size in files:
        where[fname] = (cursor, size)
        cursor += size

    def reader(full):
        start, size = where[full]
        return data[start:start + size]
    return {'name': name, 'description': desc, 'author': author}, files, reader


def read_zip(path):
    archive = zipfile.ZipFile(path)
    return [(i.filename, i.file_size) for i in archive.infolist() if not i.is_dir()], archive.read


def read_folder(path):
    files = []
    for root, _, names in os.walk(path):
        for n in names:
            p = os.path.join(root, n)
            files.append((os.path.relpath(p, path).replace('\\', '/'), os.path.getsize(p)))

    def reader(full):
        with open(os.path.join(path, full), 'rb') as f:
            return f.read()
    return files, reader


def safe_target(root, rel):
    parts = [p for p in rel.replace('\\', '/').split('/') if p not in ('', '.')]
    if not parts or any(p == '..' or ':' in p for p in parts):
        return None
    parts = [re.sub(r'[<>"|?*]', '_', p) for p in parts]
    return os.path.join(root, *parts)


def summarise(addon, entries, reader, extract_root):
    ext_counts = collections.Counter()
    ext_bytes = collections.Counter()
    tops = collections.Counter()
    for name, size in entries:
        low = name.lower()
        ext = low.rsplit('.', 1)[-1] if '.' in low.rsplit('/', 1)[-1] else '(none)'
        ext_counts[ext] += 1
        ext_bytes[ext] += size
        tops['/'.join(low.split('/')[:2])] += 1
    addon['files'] = len(entries)
    addon['ext'] = {e: [ext_counts[e], round(ext_bytes[e] / 1048576, 2)]
                    for e, _ in ext_bytes.most_common(12)}
    addon['top_dirs'] = tops.most_common(15)
    low_names = sorted(n for n, _ in entries)
    addon['models'] = [n for n in low_names if n.lower().endswith('.mdl')][:40]
    addon['sounds_sample'] = [n for n in low_names if n.lower().endswith(('.wav', '.mp3', '.ogg'))][:15]

    os.makedirs(extract_root, exist_ok=True)
    scripts, text_bytes = [], 0
    for name, size in entries:
        ext = name.lower().rsplit('.', 1)[-1]
        if ext not in TEXT_EXT or size > MAX_TEXT_FILE:
            continue
        target = safe_target(extract_root, name)
        if target is None:
            continue
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with open(target, 'wb') as f:
            f.write(reader(name))
        text_bytes += size
        if ext in ('lua', 'nut', 'sp', 'nuc'):
            scripts.append([name, size])
    addon['text_bytes'] = text_bytes
    addon['script_bytes'] = sum(s for _, s in scripts)
    addon['scripts'] = sorted(scripts, key=lambda s: -s[1])[:40]
    addon['script_count'] = len(scripts)


def steam_details(ids):
    result = {}
    for i in range(0, len(ids), 100):
        batch = ids[i:i + 100]
        form = {'itemcount': len(batch)}
        for n, pid in enumerate(batch):
            form['publishedfileids[%d]' % n] = pid
        req = urllib.request.Request(
            'https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/',
            urllib.parse.urlencode(form).encode())
        with urllib.request.urlopen(req, timeout=40) as r:
            body = json.load(r)
        for d in body['response'].get('publishedfiledetails', []):
            result[d.get('publishedfileid')] = d
    return result


def extract(path, out):
    if os.path.isdir(path):
        entries, reader = read_folder(path)
    elif path.lower().endswith('.gma'):
        _, entries, reader = read_gma(path)
    else:
        with open(path, 'rb') as f:
            is_zip = f.read(2) == b'PK'
        entries, reader = read_zip(path) if is_zip else read_vpk(path)
    written = 0
    for name, _ in entries:
        target = safe_target(out, name)
        if target is None:
            continue
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with open(target, 'wb') as f:
            f.write(reader(name))
        written += 1
    print('extracted', written, 'files to', out)


def inventory(l4d2, gmod, out):
    extract = os.path.join(out, 'extract')
    addons, errors = [], []

    for name in sorted(os.listdir(l4d2)):
        path = os.path.join(l4d2, name)
        addon = {'game': 'L4D2', 'path': path}
        try:
            if name.endswith('.vpk'):
                addon['id'] = name[:-4]
                with open(path, 'rb') as f:
                    is_zip = f.read(2) == b'PK'
                entries, reader = read_zip(path) if is_zip else read_vpk(path)
                addon['container'] = 'zip' if is_zip else 'vpk'
                addon['size_mb'] = round(os.path.getsize(path) / 1048576, 1)
            elif os.path.isdir(path):
                addon['id'] = name
                entries, reader = read_folder(path)
                addon['size_mb'] = round(sum(s for _, s in entries) / 1048576, 1)
            else:
                continue
            info = next((n for n, _ in entries if n.lower() == 'addoninfo.txt'), None)
            if info:
                addon['addoninfo'] = reader(info).decode('utf-8', 'replace')[:1500]
            summarise(addon, entries, reader, os.path.join(extract, 'L4D2_' + addon['id']))
            addons.append(addon)
        except Exception as e:
            errors.append([name, repr(e)])

    for pid in sorted(os.listdir(gmod)):
        folder = os.path.join(gmod, pid)
        if not os.path.isdir(folder):
            continue
        for fn in os.listdir(folder):
            if not fn.lower().endswith('.gma'):
                continue
            path = os.path.join(folder, fn)
            addon = {'game': 'GMod', 'id': pid, 'path': path,
                     'size_mb': round(os.path.getsize(path) / 1048576, 1)}
            try:
                meta, entries, reader = read_gma(path)
                addon['local_title'] = meta['name']
                addon['local_author'] = meta['author']
                addon['local_desc'] = meta['description'][:DESC_CAP]
                summarise(addon, entries, reader, os.path.join(extract, 'GMod_' + pid))
                addons.append(addon)
            except Exception as e:
                errors.append([pid + '/' + fn, repr(e)])

    steam_error = None
    try:
        details = steam_details(sorted({a['id'] for a in addons}))
    except Exception as e:
        details, steam_error = {}, repr(e)
    for a in addons:
        d = details.get(a['id'])
        if d and d.get('result') == 1:
            a['title'] = d.get('title')
            a['tags'] = [t.get('tag') for t in d.get('tags', [])]
            a['steam_desc'] = re.sub(r'\[/?img\].*?(\[/img\])?', '', d.get('description', ''))[:DESC_CAP]
            a['updated'] = d.get('time_updated')
        elif d:
            a['steam_result'] = d.get('result')

    summary = os.path.join(out, 'summary')
    os.makedirs(summary, exist_ok=True)
    for a in addons:
        with open(os.path.join(summary, '%s_%s.json' % (a['game'], a['id'])), 'w', encoding='utf-8') as f:
            json.dump(a, f, ensure_ascii=False, indent=1)
    with open(os.path.join(out, 'index.tsv'), 'w', encoding='utf-8') as f:
        f.write('game\tid\tsize_mb\tfiles\tscripts\tscript_kb\ttitle\ttags\n')
        for a in addons:
            title = ' '.join((a.get('title') or a.get('local_title') or '').split())
            f.write('%s\t%s\t%s\t%s\t%s\t%d\t%s\t%s\n' % (
                a['game'], a['id'], a['size_mb'], a['files'], a['script_count'],
                a['script_bytes'] // 1024, title, ','.join(a.get('tags', []))))

    print('addons', len(addons), 'errors', len(errors), 'steam', steam_error or 'ok %d' % len(details))
    for e in errors:
        print('ERR', e)
    print('text_mb', round(sum(a['text_bytes'] for a in addons) / 1048576, 1),
          'script_mb', round(sum(a['script_bytes'] for a in addons) / 1048576, 1))


if __name__ == '__main__':
    command = sys.argv[1] if len(sys.argv) > 1 else ''
    if command == 'inventory' and len(sys.argv) == 5:
        inventory(*sys.argv[2:5])
    elif command == 'extract' and len(sys.argv) == 4:
        extract(*sys.argv[2:4])
    else:
        print(__doc__)
