r"""워크숍 애드온 에셋을 ENFILADE로 들여온다. 무엇을 어디에 넣을지는 port_manifest.csv, 소리를 나누는 규칙은
sound_rules.csv에 있다. 안내는 같은 폴더의 README.md.

쓰는 법 (PowerShell, 프로젝트 폴더 F:\GameProject\ENFILADE에서. workshop.py처럼 Blender의 파이썬으로 돌린다):

  $py = 'F:\SteamLibrary\steamapps\common\Blender\4.5\python\bin\python.exe'
  & $py -I .\Tools\Workshop\port_assets.py --dry-run   무엇을 어디에 넣을지 보여 주기만 한다(아무것도 쓰지 않는다)
  & $py -I .\Tools\Workshop\port_assets.py             기본 행(default 1)을 모두 들여온다
  & $py -I .\Tools\Workshop\port_assets.py --pick 3532432838
                                                       후보 묶음(choice)에서 고른 것을 Assets에 넣는다(카타나 모델)
  --only 5, --only 카타나, --only 3803570564, --only krait-sfx
                                                       묶음 번호·묶음 이름·워크숍 번호·행 이름으로 고른다
  --list 3660295000                                    애드온 안의 파일 목록(pattern을 고칠 때)
  --force                                              이미 있는 다른 파일도 덮어쓰고 AssetSources 줄도 새로 쓴다

하는 일:
 1. L4D2·GMod 워크숍 폴더에서 애드온을 찾는다(F:\SteamLibrary와 기본 Steam 폴더. --steam, --l4d2, --gmod로 바꾼다).
 2. 필요한 파일만 프로젝트 밖 F:\GameProject\_WorkshopImport\<워크숍 번호>에 원래 경로대로 푼다(git에 안 들어간다).
 3. 소리는 sound_rules.csv대로 폴더를 나눠 Assets/_Project 아래에 넣는다. 좀비 목소리는 0.8.15의 공격 음성처럼
    길이와 첫머리로 고르고, 앞뒤 무음을 자르고, 이미 있는 Attack·Leap와 크기를 맞춘다. .wav.meta는 Attack 클립의
    .meta를 GUID만 바꿔 쓴다(Preload Audio Data, Load In Background 켬).
 4. 모델은 Blender를 백그라운드로 돌려(Tools/Blender/source_prop_to_fbx.py) FBX와 PNG로 바꾸고 큰 텍스처를 줄인다.
    FBX·PNG의 .meta는 Unity가 처음 가져올 때 만든다(그래야 ModelImportRules가 적용된다).
 5. Docs/AssetSources.md의 '나중에 교체할 것' 표에 행마다 한 줄을 더한다(임시 자료, 출처 표시 포함).
 6. 요약을 보여 준다. 이미 있는 다른 파일은 --force 없이 덮어쓰지 않는다.

작성자가 이식·재사용을 금지한 애드온(아래 NO_REUSE)은 수치만 참고하고 에셋은 꺼내지 않는다.
"""
import argparse
import array
import collections
import csv
import datetime
import hashlib
import io
import math
import os
import re
import shutil
import statistics
import struct
import subprocess
import sys
import uuid
import wave

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)  # python -I leaves the script's own folder off sys.path
sys.dont_write_bytecode = True  # no untracked __pycache__ beside the tools
import workshop  # noqa: E402  (VPK, GMA, zip and folder readers, safe_target)

REPO = os.path.dirname(os.path.dirname(HERE))
BLENDER_TOOLS = os.path.join(REPO, 'Tools', 'Blender')

# Addons whose authors forbid porting or reuse (the porting table): their numbers may be read, their files never leave
# the archive.
NO_REUSE = {
    '3798417245': '《三角洲行动》-93R-VHK改装套件(MAC-10)',
    '3690367452': 'RE9 Requiem - 990-TAC (Autoshotgun)',
    '3613078303': '[MWII / Hellsing] .50 GS - Casull & Jackal',
    '131759821': 'VJ Base',
    '3111178360': 'Modern Warfare 2019 SWEPS - Melee (UNOFFICIAL)',
    '3037375111': 'Quick Slide With Legs [VManip]',
    '3771427587': '魔女审判-二阶堂希罗 (for Nick)',
    '3416396538': '[VRCHAT] yugi&miyo',
}

STEAM_ROOTS = [r'F:\SteamLibrary', r'C:\Program Files (x86)\Steam']
L4D2_DIRS = [('steamapps', 'common', 'Left 4 Dead 2', 'left4dead2', 'addons', 'workshop'),
             ('steamapps', 'common', 'Left 4 Dead 2', 'left4dead2', 'addons'),
             ('steamapps', 'workshop', 'content', '550')]
GMOD_DIRS = [('steamapps', 'workshop', 'content', '4000')]
CONVERTERS = {'fbx': 'source_prop_to_fbx.py', 'fbx-character': 'source_model_to_fbx.py'}
BLENDER_TIMEOUT = 1800  # s for one model

MANIFEST_COLUMNS = ('id', 'bundle', 'addon', 'game', 'title', 'what', 'pattern', 'destination', 'conversion',
                    'rules', 'default', 'choice', 'source', 'notes')
RULE_COLUMNS = ('rules', 'pattern', 'key', 'minSeconds', 'maxSeconds', 'peakWithin', 'note')
CONVERSION = re.compile(r'(copy|sort|voice|fbx-character|fbx)(?::(\d+))?$')

# Voices are picked and cleaned the way the 37 attack clips of 0.8.15 were (Docs/AssetSources.md): length and an early
# loudest moment decide (thresholds per rule in sound_rules.csv), silence is trimmed at both ends, and every clip is
# brought to the loudness of the clips already there. A recording already in (or skipped as Attack/Leap) is dropped,
# as 0.8.17 dropped Leap/Female/rage_69.
WINDOW = 0.01                     # s, one step of the loudness envelope
TRIM_DB = -40.0                   # silence: this far below the loudest step
HEAD_PAD, TAIL_PAD = 0.01, 0.03   # s kept before the first and after the last step above the silence line
FADE_IN, FADE_OUT = 0.002, 0.01   # s
LOUD_SPAN = 0.3                   # s, a clip's loudness is the RMS of its loudest stretch this long
CEILING_DB = -1.0                 # no peak above this after the gain
FALLBACK_LOUDNESS_DB = -14.0      # when no existing clip can be measured (LFS files not pulled)
MAX_GAIN_DB = 30.0                # a clip that would need more is too quiet to use (breathing is quiet, so not less)

# A new clip's .meta is a copy of an existing 0.8.15 zombie clip's .meta with a fresh GUID: Preload Audio Data and Load
# In Background on, as ZombieVoice needs and MaintenanceGuide asks of every new clip, and nothing in the editor sets them
# on import. AUDIO_META is that file as committed, for when it can't be read. Folders get Unity's folder metadata. FBX
# and PNG get none: ModelImportRules applies its rules only when Unity imports a model or texture without metadata.
META_TEMPLATE_FOLDER = ('Art', 'Audio', 'Enemies', 'Zombie', 'Attack')
AUDIO_META = '\n'.join([
    'fileFormatVersion: 2', 'guid: @GUID@', 'AudioImporter:', '  externalObjects: {}', '  serializedVersion: 8',
    '  defaultSettings:', '    serializedVersion: 2', '    loadType: 0', '    sampleRateSetting: 0',
    '    sampleRateOverride: 44100', '    compressionFormat: 1', '    quality: 1', '    conversionMode: 0',
    '    preloadAudioData: 1', '  platformSettingOverrides: {}', '  forceToMono: 0', '  normalize: 1',
    '  loadInBackground: 1', '  ambisonic: 0', '  3D: 1', '  userData: ', '  assetBundleName: ',
    '  assetBundleVariant: ', ''])
FOLDER_META = '\n'.join([
    'fileFormatVersion: 2', 'guid: @GUID@', 'folderAsset: yes', 'DefaultImporter:', '  externalObjects: {}',
    '  userData: ', '  assetBundleName: ', '  assetBundleVariant: ', ''])

SOURCES_DOC = ('Docs', 'AssetSources.md')
SOURCES_SECTION = '## 나중에 교체할 것'
STATUS = {'new': '새 파일', 'same': '같은 파일이 이미 있음', 'replaced': '덮어씀', 'exists': '다른 파일이 있어 건너뜀'}

Job = collections.namedtuple('Job', 'id bundle addon game title what pattern regex destination kind max_px rules '
                                    'default choice source notes')
Rule = collections.namedtuple('Rule', 'regex key min_s max_s peak_s')
Wav = collections.namedtuple('Wav', 'tag channels rate bits pcm')
Shape = collections.namedtuple('Shape', 'start end seconds peak_at loudness peak')


# ---------------------------------------------------------------------------------------------------------- tables

def read_table(path, columns):
    """Rows of a CSV with a header row; rows whose first cell is empty or starts with '#' (the #설명 row) are skipped."""
    with open(path, encoding='utf-8-sig', newline='') as f:
        records = list(csv.reader(f))
    head = [h.strip() for h in records[0]] if records else []
    missing = [c for c in columns if c not in head]
    if missing:
        raise SystemExit('%s: 칸이 없다: %s' % (path, ', '.join(missing)))
    rows = []
    for line, cells in enumerate(records[1:], start=2):
        if not cells or not cells[0].strip() or cells[0].lstrip().startswith('#'):
            continue
        row = {h: (cells[i].strip() if i < len(cells) else '') for i, h in enumerate(head)}
        row['_line'] = line
        rows.append(row)
    return rows


def load_rules(path):
    sets, problems = collections.OrderedDict(), []
    for row in read_table(path, RULE_COLUMNS):
        where = '%s:%d' % (os.path.basename(path), row['_line'])
        try:
            regex = re.compile(row['pattern'], re.IGNORECASE)
            numbers = [float(row[c]) if row[c] else None for c in ('minSeconds', 'maxSeconds', 'peakWithin')]
        except (re.error, ValueError) as e:
            problems.append('%s: %s' % (where, e))
            continue
        key = row['key'].replace('\\', '/').strip('/')
        if not key or '..' in key.split('/'):
            problems.append('%s: key가 비었거나 잘못됐다' % where)
            continue
        sets.setdefault(row['rules'], []).append(Rule(regex, key, *numbers))
    return sets, problems


def load_manifest(path, rule_sets):
    jobs, problems, ids = [], [], set()
    for row in read_table(path, MANIFEST_COLUMNS):
        where = '%s:%d (%s)' % (os.path.basename(path), row['_line'], row['id'])
        conversion = CONVERSION.match(row['conversion'].lower())
        destination = row['destination'].replace('\\', '/').strip('/')
        try:
            regex = re.compile(row['pattern'], re.IGNORECASE)
        except re.error as e:
            problems.append('%s: 정규식 오류: %s' % (where, e))
            continue
        if not conversion:
            problems.append('%s: conversion을 모른다: %s' % (where, row['conversion']))
            continue
        kind, max_px = conversion.group(1), int(conversion.group(2) or 0)
        if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_-]*', row['id']):  # it names a folder under _converted
            problems.append('%s: 행 이름은 영문·숫자·-·_만 쓴다' % where)
        if row['id'] in ids:
            problems.append('%s: 행 이름이 겹친다' % where)
        ids.add(row['id'])
        if not row['addon'].isdigit():
            problems.append('%s: 워크숍 번호가 숫자가 아니다' % where)
        if row['game'] not in ('L4D2', 'GMod'):
            problems.append('%s: game은 L4D2 또는 GMod다' % where)
        if not destination or destination.lower().startswith('assets/') or '..' in destination.split('/'):
            problems.append('%s: destination은 Assets/_Project 기준 경로다(Art/...)' % where)
        if kind.startswith('fbx') != destination.lower().endswith('.fbx'):
            problems.append('%s: 모델 행만 destination이 .fbx로 끝난다' % where)
        if kind in ('sort', 'voice') and row['rules'] not in rule_sets:
            problems.append('%s: sound_rules.csv에 규칙 묶음 %r이 없다' % (where, row['rules']))
        if row['default'] not in ('0', '1'):
            problems.append('%s: default는 0 또는 1이다' % where)
        jobs.append(Job(row['id'], row['bundle'], row['addon'], row['game'], row['title'], row['what'],
                        row['pattern'], regex, destination, kind, max_px, row['rules'], row['default'] == '1',
                        row['choice'], row['source'], row['notes']))
    return jobs, problems


# ---------------------------------------------------------------------------------------------------------- addons

class Context:
    def __init__(self, args):
        self.dry_run, self.force = args.dry_run, args.force
        self.repo = os.path.abspath(args.repo or REPO)
        self.project = os.path.join(self.repo, 'Assets', '_Project')
        self.staging = os.path.abspath(args.staging or os.path.join(os.path.dirname(self.repo), '_WorkshopImport'))
        self.only = tokens(args.only)
        self.picks = set(tokens(args.pick))
        roots = args.steam or STEAM_ROOTS
        self.l4d2_dirs = args.l4d2 or [os.path.join(r, *d) for r in roots for d in L4D2_DIRS]
        self.gmod_dirs = args.gmod or [os.path.join(r, *d) for r in roots for d in GMOD_DIRS]
        self.blender_arg = args.blender
        self.blender_path = None
        self.claims = {}      # destination (lower case) -> (addon, archive path) that took it in this run
        self.loudness = {}    # row id -> (dBFS, clips measured)
        self.audio_meta = self.audio_meta_source = None
        self.choice_titles, self.rule_sets = {}, {}
        self.searched = set()  # folders searched for addons that weren't found
        self.today = datetime.date.today().isoformat()

    def blender(self):
        if self.blender_path is None:
            self.blender_path = find_blender(self.blender_arg) or ''
        return self.blender_path or None


def tokens(values):
    return [t.strip() for v in (values or []) for t in v.split(',') if t.strip()]


def inside(path, root):
    path, root = os.path.normcase(os.path.abspath(path)), os.path.normcase(os.path.abspath(root))
    try:
        return os.path.commonpath([path, root]) == root
    except ValueError:  # another drive
        return False


def find_blender(explicit):
    if explicit:
        return explicit if os.path.isfile(explicit) else None
    candidates = [os.environ.get('ENFILADE_BLENDER'), os.environ.get('BLENDER_EXE')]
    home = os.path.abspath(sys.executable)
    for _ in range(4):  # <Blender>\4.5\python\bin\python.exe -> <Blender>
        home = os.path.dirname(home)
    candidates += [os.path.join(home, 'blender.exe'), os.path.join(home, 'blender')]
    candidates += [os.path.join(r, 'steamapps', 'common', 'Blender', 'blender.exe') for r in STEAM_ROOTS]
    return next((c for c in candidates if c and os.path.isfile(c)), None)


def locate(ctx, addon_id, game):
    """('vpk' | 'gma' | 'folder' | 'staged' | 'bin', path), or (None, the folders searched)."""
    dirs = ctx.l4d2_dirs if game == 'L4D2' else ctx.gmod_dirs if game == 'GMod' else ctx.l4d2_dirs + ctx.gmod_dirs
    for d in dirs:
        for ext in ('.vpk', '.gma'):
            if os.path.isfile(os.path.join(d, addon_id + ext)):
                return ext[1:], os.path.join(d, addon_id + ext)
        folder = os.path.join(d, addon_id)
        if os.path.isdir(folder):
            names = sorted(os.listdir(folder))
            for ext in ('.gma', '.vpk'):
                hit = next((n for n in names if n.lower().endswith(ext)), None)
                if hit:
                    return ext[1:], os.path.join(folder, hit)
            if names and all(n.lower().endswith('.bin') for n in names):
                return 'bin', folder
            if names:
                return 'folder', folder
    # Last resort: what an earlier run (or workshop.py extract) left in the staging folder.
    staged = os.path.join(ctx.staging, addon_id)
    if os.path.isdir(staged) and os.listdir(staged):
        return 'staged', staged
    return None, dirs


class Addon:
    def __init__(self, addon_id, kind, path):
        self.id, self.kind, self.path = addon_id, kind, path
        if kind in ('folder', 'staged'):
            entries, self.read = workshop.read_folder(path)
        elif kind == 'gma':
            _, entries, self.read = workshop.read_gma(path)
        else:
            with open(path, 'rb') as f:
                zipped = f.read(2) == b'PK'
            entries, self.read = workshop.read_zip(path) if zipped else workshop.read_vpk(path)
        self.files = collections.OrderedDict()  # lower-case path with '/' -> (name in the archive, size)
        for name, size in sorted(entries):
            self.files.setdefault(name.replace('\\', '/').lower(), (name, size))

    def match(self, regex):
        return [n for n in self.files if regex.search(n)]

    def data(self, norm):
        return self.read(self.files[norm][0])


class Result:
    def __init__(self, job):
        self.job = job
        self.matched = self.staged = self.ruled_out = 0
        self.placed = []      # (path under Assets/_Project, bytes, status, key)
        self.dropped = collections.Counter()
        self.examples = collections.defaultdict(list)
        self.unmatched, self.notes, self.warnings, self.errors = [], [], [], []
        self.review = None    # a converted choice candidate left in the staging folder
        self.target = None    # (dBFS, clips measured) for voices
        self.model = None     # the .mdl converted

    def drop(self, reason, name):
        self.dropped[reason] += 1
        if len(self.examples[reason]) < 3:
            self.examples[reason].append(name)


def stage(ctx, addon, names, res):
    """Copies archive files to <staging>/<id>/ under their own paths, as workshop.py extract does. Returns local paths."""
    root = os.path.join(ctx.staging, addon.id)
    local = {}
    for norm in names:
        name, size = addon.files[norm]
        target = workshop.safe_target(root, name)
        if target is None:
            res.warnings.append('위험한 경로라 건너뜀: ' + name)
            continue
        local[norm] = target
        if ctx.dry_run or addon.kind == 'staged':
            continue
        if os.path.isfile(target) and not ctx.force:
            if os.path.getsize(target) != size:
                res.warnings.append('_WorkshopImport의 파일이 애드온과 크기가 다르다(--force면 다시 푼다): ' + name)
            continue
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with open(target, 'wb') as f:
            f.write(addon.read(name))
        res.staged += 1
    return local


# ---------------------------------------------------------------------------------------------------------- placing

def write_meta(path, template):
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(template.replace('@GUID@', uuid.uuid4().hex))


def audio_meta(ctx):
    """The .meta template for a new clip: the first 0.8.15 Attack clip's .meta with its GUID taken out and both loading
    flags forced on, or AUDIO_META when there is none to read."""
    if ctx.audio_meta is None:
        ctx.audio_meta, ctx.audio_meta_source = AUDIO_META, '들어 있는 기본 사본(Attack 클립의 .meta를 읽지 못함)'
        folder = os.path.join(ctx.project, *META_TEMPLATE_FOLDER)
        names = sorted(n for n in os.listdir(folder) if n.lower().endswith('.wav.meta')) if os.path.isdir(folder) else []
        for name in names:
            with open(os.path.join(folder, name), encoding='utf-8', errors='replace') as f:
                text = f.read().replace('\r\n', '\n')
            template, found = re.subn(r'(?m)^guid: [0-9a-f]{32}[ \t]*$', 'guid: @GUID@', text, count=1)
            if found and 'AudioImporter:' in text:
                template = re.sub(r'(?m)^(\s*preloadAudioData:) *\d+', r'\1 1', template)
                template = re.sub(r'(?m)^(\s*loadInBackground:) *\d+', r'\1 1', template)
                if all(re.search(r'(?m)^\s*%s: 1' % flag, template) for flag in ('preloadAudioData', 'loadInBackground')):
                    ctx.audio_meta = template
                    ctx.audio_meta_source = '/'.join(META_TEMPLATE_FOLDER + (name,)) + '의 사본(GUID만 새로)'
                    break
    return ctx.audio_meta


def make_folders(ctx, folder):
    missing = []
    while not os.path.isdir(folder):
        missing.append(folder)
        parent = os.path.dirname(folder)
        if parent == folder:
            raise SystemExit('폴더를 만들 수 없다: ' + folder)
        folder = parent
    for f in reversed(missing):
        os.mkdir(f)
        if inside(f, os.path.join(ctx.repo, 'Assets')) and not os.path.exists(f + '.meta'):
            write_meta(f + '.meta', FOLDER_META)


def place(ctx, res, rel, data, key=None):
    """Writes data to Assets/_Project/<rel>. A different file already there stays unless --force."""
    path = os.path.join(ctx.project, *rel.split('/'))
    status = 'new'
    if os.path.isfile(path):
        with open(path, 'rb') as f:
            status = 'same' if f.read() == data else 'replaced' if ctx.force else 'exists'
    res.placed.append((rel, len(data), status, key))
    if ctx.dry_run or status == 'exists':
        return
    if status != 'same':
        make_folders(ctx, os.path.dirname(path))
        with open(path, 'wb') as f:
            f.write(data)
    # Without it Unity would make one with its own defaults (no Preload Audio Data, no Load In Background).
    if rel.lower().endswith('.wav') and not os.path.exists(path + '.meta'):
        write_meta(path + '.meta', audio_meta(ctx))


def destination(ctx, folder, addon_id, norm, name):
    """A file name in folder for this archive file. Another file that already took the name in this run (same name in
    another source folder) makes this one take its source folder's name in front."""
    parts = re.sub(r'[<>:"|?*]', '_', name.replace('\\', '/')).split('/')
    stem, ext = os.path.splitext(parts[-1])
    options = [parts[-1]] + (['%s_%s' % (parts[-2], parts[-1])] if len(parts) > 1 else [])
    options += ['%s_%d%s' % (stem, n, ext) for n in range(2, 1000)]
    for option in options:
        rel = folder + '/' + option
        owner = ctx.claims.setdefault(rel.lower(), (addon_id, norm))
        if owner == (addon_id, norm):
            return rel
    raise SystemExit('이름을 정할 수 없다: ' + name)


def read_file(path):
    with open(path, 'rb') as f:
        return f.read()


# ---------------------------------------------------------------------------------------------------------- sound

def parse_wav(data):
    """Format tag, channels, rate, bits and the data chunk of a RIFF WAVE file; None if it isn't one."""
    if len(data) < 12 or data[:4] != b'RIFF' or data[8:12] != b'WAVE':
        return None
    pos, fmt, pcm = 12, None, None
    while pos + 8 <= len(data):
        chunk, size = data[pos:pos + 4], struct.unpack_from('<I', data, pos + 4)[0]
        body = data[pos + 8:pos + 8 + size]
        if chunk == b'fmt ' and len(body) >= 16:
            tag, channels, rate, _, _, bits = struct.unpack_from('<HHIIHH', body)
            if tag == 0xFFFE and len(body) >= 26:  # WAVE_FORMAT_EXTENSIBLE: the sub-format GUID starts with the tag
                tag = struct.unpack_from('<H', body, 24)[0]
            fmt = (tag, channels, rate, bits)
        elif chunk == b'data' and pcm is None:
            pcm = body
        pos += 8 + size + (size & 1)
    if fmt is None or pcm is None or not fmt[1]:
        return None
    return Wav(*fmt, pcm)


def format_name(wav):
    names = {1: 'PCM', 2: 'MS ADPCM', 3: 'float', 0x11: 'IMA ADPCM', 0x55: 'MP3'}
    return '%s %d비트' % (names.get(wav.tag, 'format %d' % wav.tag), wav.bits)


def samples16(pcm):
    samples = array.array('h')
    samples.frombytes(pcm[:len(pcm) // 2 * 2])
    if sys.byteorder == 'big':
        samples.byteswap()
    return samples


def wav_bytes(samples, channels, rate):
    if sys.byteorder == 'big':
        samples = array.array('h', samples)
        samples.byteswap()
    buf = io.BytesIO()
    with wave.open(buf, 'wb') as w:
        w.setnchannels(channels)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(samples.tobytes())
    return buf.getvalue()


def analyze(samples, channels, rate):
    """Where the sound is (silence trimmed, in frames), how long, when its loudest 10 ms come and how loud it is."""
    frames = len(samples) // channels
    if not frames or rate <= 0:
        return None
    step = max(1, int(round(rate * WINDOW)))
    energy = []
    for f in range(0, frames, step):
        chunk = samples[f * channels:(f + step) * channels]
        energy.append(sum(x * x for x in chunk) / len(chunk))
    loudest = max(energy)
    if loudest <= 0:
        return None
    floor = loudest * 10 ** (TRIM_DB / 10)
    first = next(i for i, e in enumerate(energy) if e >= floor)
    last = len(energy) - 1 - next(i for i, e in enumerate(reversed(energy)) if e >= floor)
    start = max(0, first * step - int(rate * HEAD_PAD))
    end = min(frames, (last + 1) * step + int(rate * TAIL_PAD))
    top = max(range(first, last + 1), key=energy.__getitem__)
    kept = energy[first:last + 1]
    span = min(len(kept), max(1, int(round(LOUD_SPAN / WINDOW))))
    total = best = sum(kept[:span])
    for i in range(span, len(kept)):
        total += kept[i] - kept[i - span]
        best = max(best, total)
    part = samples[start * channels:end * channels]
    peak = max(max(part), -min(part))
    return Shape(start, end, (end - start) / rate, max(0.0, (top * step - start) / rate),
                 math.sqrt(max(best, 0.0) / span), peak)


def render(samples, channels, rate, shape, gain):
    part = samples[shape.start * channels:shape.end * channels]
    out = array.array('h', [max(-32768, min(32767, int(round(x * gain)))) for x in part])
    frames = len(out) // channels
    for length, at_end in ((min(int(rate * FADE_IN), frames // 2), False), (min(int(rate * FADE_OUT), frames // 2), True)):
        for f in range(length):
            frame = frames - 1 - f if at_end else f
            for c in range(channels):
                out[frame * channels + c] = int(out[frame * channels + c] * f / length)
    return out


def target_loudness(ctx, job, rules):
    """The median loudness of the clips already in the row's folder that this row doesn't make (for the zombie voices:
    the Attack and Leap clips of 0.8.15), so a second run gives the same files."""
    if job.id in ctx.loudness:
        return ctx.loudness[job.id]
    made = {(job.destination + '/' + r.key.split('/')[0]).lower() + '/' for r in rules if r.key != '-'}
    folder = os.path.join(ctx.project, *job.destination.split('/'))
    values = []
    for root, _, names in os.walk(folder):
        for name in sorted(names):
            path = os.path.join(root, name)
            rel = os.path.relpath(path, ctx.project).replace('\\', '/').lower()
            if not name.lower().endswith('.wav') or any(rel.startswith(m) for m in made):
                continue
            wav = parse_wav(read_file(path))
            if wav and wav.tag == 1 and wav.bits == 16:
                shape = analyze(samples16(wav.pcm), wav.channels, wav.rate)
                if shape:
                    values.append(20 * math.log10(max(shape.loudness, 1e-9) / 32768))
    ctx.loudness[job.id] = (statistics.median(values), len(values)) if values else (FALLBACK_LOUDNESS_DB, 0)
    return ctx.loudness[job.id]


def clean_voice(data, rule, res, norm, seen, target_db):
    """The clip trimmed and brought to target_db, or None (with the reason counted) when it isn't taken."""
    wav = parse_wav(data)
    if wav is None:
        res.drop('WAV가 아님', norm)
        return None
    if wav.tag != 1 or wav.bits != 16:
        res.drop('형식(%s)' % format_name(wav), norm)
        return None
    digest = hashlib.sha1(wav.pcm).hexdigest()
    if digest in seen:
        res.drop('같은 녹음', '%s = %s' % (norm, seen[digest]))
        return None
    samples = samples16(wav.pcm)
    shape = analyze(samples, wav.channels, wav.rate)
    if shape is None:
        res.drop('소리 없음', norm)
        return None
    if rule.min_s is not None and shape.seconds < rule.min_s:
        res.drop('짧음(%g초 미만)' % rule.min_s, norm)
        return None
    if rule.max_s is not None and shape.seconds > rule.max_s:
        res.drop('긺(%g초 초과)' % rule.max_s, norm)
        return None
    if rule.peak_s is not None and shape.peak_at > rule.peak_s:
        res.drop('가장 큰 곳이 늦음(%g초 뒤)' % rule.peak_s, norm)
        return None
    gain = 32768 * 10 ** (target_db / 20) / max(shape.loudness, 1e-9)
    gain = min(gain, 32767 * 10 ** (CEILING_DB / 20) / max(shape.peak, 1))
    if 20 * math.log10(gain) > MAX_GAIN_DB:
        res.drop('너무 작음', norm)
        return None
    seen[digest] = norm
    return wav_bytes(render(samples, wav.channels, wav.rate, shape, gain), wav.channels, wav.rate)


def run_sounds(ctx, job, addon, names, rules, res):
    picked, seen = [], {}
    for norm in names:
        if job.kind == 'copy':
            picked.append((norm, None))
            continue
        rule = next((r for r in rules if r.regex.search(norm)), None)
        if rule is None:
            res.unmatched.append(norm)
        elif rule.key == '-':
            res.ruled_out += 1
            if job.kind == 'voice':  # remember it, so the same recording isn't brought in under another key
                wav = parse_wav(addon.data(norm))
                if wav:
                    seen.setdefault(hashlib.sha1(wav.pcm).hexdigest(), norm)
        else:
            picked.append((norm, rule))
    stage(ctx, addon, [n for n, _ in picked], res)
    if job.kind == 'voice':
        res.target = target_loudness(ctx, job, rules)
    for norm, rule in picked:
        data = addon.data(norm)
        if job.kind == 'voice':
            data = clean_voice(data, rule, res, norm, seen, res.target[0])
            if data is None:
                continue
        else:
            wav = parse_wav(data)
            if wav is None:
                res.warnings.append('WAV가 아니다(그대로 넣음): ' + norm)
            elif wav.tag not in (1, 3):
                res.warnings.append('%s라 Unity가 못 읽을 수 있다: %s' % (format_name(wav), norm))
        folder = job.destination + ('/' + rule.key if rule else '')
        rel = destination(ctx, folder, addon.id, norm, addon.files[norm][0])
        place(ctx, res, rel, data, rule.key if rule else None)


def run_model(ctx, job, addon, names, res):
    models = [n for n in names if n.endswith('.mdl')]
    if not models:
        res.errors.append('pattern에 맞는 .mdl이 없다')
        return
    if job.choice and job.addon not in ctx.picks:
        res.notes.append('후보 묶음 %s: 변환만 해 두고, --pick %s로 고르면 Assets에 넣는다' % (job.choice, job.addon))
    if len(models) > 1:
        res.warnings.append('mdl이 여러 개라 첫 번째만 바꾼다: ' + ', '.join(models))
    mdl = res.model = models[0]
    stem = mdl[:-len('.mdl')]
    # The model's own files (.vvd, .dx90.vtx, .phy...) and every material of the addon: SourceIO finds them by path.
    needed = [n for n in addon.files if n.startswith(stem + '.') or n.startswith('materials/')]
    local = stage(ctx, addon, needed, res)
    if mdl not in local:
        res.errors.append('mdl 경로를 풀 수 없다: ' + mdl)
        return
    name = os.path.splitext(job.destination.rsplit('/', 1)[-1])[0]
    out_dir = os.path.join(ctx.staging, '_converted', job.id)
    fbx = os.path.join(out_dir, name + '.fbx')
    if os.path.isfile(fbx) and not ctx.force:
        res.notes.append('전에 변환한 것을 쓴다(--force면 다시 변환): ' + fbx)
    else:
        script = os.path.join(BLENDER_TOOLS, CONVERTERS[job.kind])
        args = [local[mdl], out_dir, name] + ([str(job.max_px)] if job.kind == 'fbx' else [])
        blender = ctx.blender()
        command = [blender or 'blender.exe', '-b', '--factory-startup', '--python-exit-code', '1', '--python',
                   script, '--'] + args
        if ctx.dry_run:
            res.notes.append('변환할 명령: ' + subprocess.list2cmdline(command))
            if blender is None:
                res.warnings.append('Blender를 찾지 못했다(--blender로 blender.exe 경로를 준다)')
            return
        if blender is None:
            res.errors.append('Blender를 찾지 못했다(--blender로 blender.exe 경로를 준다)')
            return
        shutil.rmtree(out_dir, ignore_errors=True)  # this tool's own cache: no textures left from an earlier conversion
        os.makedirs(out_dir)
        log = os.path.join(out_dir, 'blender.log')
        print('  [%s] Blender로 %s 변환 중...' % (job.id, mdl), flush=True)
        try:
            run = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=BLENDER_TIMEOUT)
            output, code = run.stdout.decode('utf-8', 'replace'), run.returncode
        except (OSError, subprocess.SubprocessError) as e:
            output, code = repr(e), None
        with open(log, 'w', encoding='utf-8') as f:
            f.write(subprocess.list2cmdline(command) + '\n\n' + output)
        res.notes += [line.strip() for line in output.splitlines() if line.startswith(('MESH ', 'TEX ', 'BONES '))]
        if code != 0 or not os.path.isfile(fbx):
            res.errors.append('변환 실패(종료 코드 %s). 기록: %s' % (code, log))
            return
    if job.choice and job.addon not in ctx.picks:
        res.review = out_dir
        return
    folder = job.destination.rsplit('/', 1)[0]
    place(ctx, res, folder + '/' + name + '.fbx', read_file(fbx))
    tex_dir = os.path.join(out_dir, 'Textures')
    made = sorted(os.listdir(tex_dir)) if os.path.isdir(tex_dir) else []
    for png in made:
        place(ctx, res, folder + '/Textures/' + png, read_file(os.path.join(tex_dir, png)))
    old = os.path.join(ctx.project, *folder.split('/'), 'Textures')
    stale = sorted(n for n in os.listdir(old) if not n.endswith('.meta') and n not in made) if os.path.isdir(old) else []
    if stale:
        res.warnings.append('Textures에 이번 변환에 없는 파일 %d개가 있다(전에 고른 후보 것이면 지운다): %s'
                            % (len(stale), ', '.join(stale[:5])))


def run_job(ctx, job, addon, rule_sets):
    res = Result(job)
    names = addon.match(job.regex)
    res.matched = len(names)
    if not names:
        res.errors.append('pattern %s에 맞는 파일이 없다. --list %s로 애드온 안을 보고 port_manifest.csv를 고친다'
                          % (job.pattern, job.addon))
    elif job.kind.startswith('fbx'):
        run_model(ctx, job, addon, names, res)
    else:
        run_sounds(ctx, job, addon, names, rule_sets.get(job.rules, []), res)
    return res


# ---------------------------------------------------------------------------------------------------------- record

def cell(text):
    return ' '.join(text.replace('|', '/').split())


def megabytes(size):
    mb = size / 1048576.0
    return '%.2f' % max(mb, 0.01) if mb < 0.1 else ('%.1f' % mb).rstrip('0').rstrip('.') if mb < 10 else '%d' % round(mb)


def source_line(ctx, res):
    """The row for Docs/AssetSources.md, in the table's own form: | where | how many | source |."""
    job = res.job
    placed = [p for p in res.placed if p[2] != 'exists']
    if not placed:
        return None
    tool = '[port_assets.py](../../../Tools/Workshop/port_assets.py) `%s`' % (job.choice or job.id)
    size = megabytes(sum(p[1] for p in placed))
    keys = collections.Counter(p[3] for p in placed if p[3])
    if job.kind.startswith('fbx'):
        where = '`%s`' % job.destination.rsplit('/', 1)[0]
        count = 'FBX 1개, 텍스처 %d개, %sMB' % (sum(1 for p in placed if p[0].lower().endswith('.png')), size)
        script = CONVERTERS[job.kind]
        how = 'Blender([%s](../../../Tools/Blender/%s))로 `%s`에서 FBX와 PNG를 만들었다' % (script, script, res.model)
        if job.max_px:
            how += '. 텍스처는 %dpx 이하로 줄였다' % job.max_px
        how += '. 애니메이션은 넣지 않았다'
        if job.choice:
            how += '. 후보(%s) 가운데 사용자가 고른 것이다' % ', '.join(sorted(ctx.choice_titles.get(job.choice, [])))
    else:
        order = [r.key.split('/')[0] for r in ctx.rule_sets.get(job.rules, [])]  # the order the rules list them in
        tops = sorted({k.split('/')[0] for k in keys}, key=lambda t: (order.index(t) if t in order else len(order), t))
        where = '`%s`' % job.destination + ('의 ' + '·'.join('`%s`' % t for t in tops) if tops else '')
        count = '%d개, %sMB' % (len(placed), size)
        rules = '[sound_rules.csv](../../../Tools/Workshop/sound_rules.csv) `%s`' % job.rules
        if job.kind == 'voice':
            db, measured = res.target
            how = ('0.8.15의 공격 음성처럼 %s의 길이·첫머리 기준으로 고르고 앞뒤 무음을 자르고 크기를 맞췄다(%.1fdBFS, %s). '
                   '남녀는 L4D2 폴더 이름(`male`, `female`)으로 나눴고 성별 폴더 밖의 것은 키 폴더 바로 아래에 두었다. '
                   '`.wav.meta`는 Attack 클립의 것을 본떠 Preload Audio Data와 Load In Background를 켰다. '
                   '들어 보지 않고 길이와 파형 수치로 고른 것이라 어색한 파일은 폴더에서 지우면 된다'
                   % (rules, db, '이미 있던 %d개의 가운데값' % measured if measured else '기본값'))
        elif job.kind == 'sort':
            how = '%s 규칙으로 파일 이름에 따라 폴더를 나눴고 소리는 원본 그대로다' % rules
        else:
            how = '원본 그대로 넣었다'
        if keys:
            how += '. 폴더별 ' + ', '.join('`%s` %d' % kv for kv in sorted(keys.items()))
    text = '**임시 자료**(나중에 교체). %s. %s 워크숍 %s "%s"에서 꺼냈다(%s, %s). 출처 표시: %s. %s.' % (
        cell(job.what).rstrip('.'), job.game, job.addon, cell(job.title), ctx.today, tool, cell(job.source).rstrip('.'),
        cell(how))
    return 'port_assets.py) `%s`' % (job.choice or job.id), '| %s | %s | %s |' % (where, count, text)


def record(ctx, results):
    """Adds a row per placed result to the first table of Docs/AssetSources.md (the temporary assets)."""
    rows = [(res, line) for res in results for line in [source_line(ctx, res)] if line]
    if not rows:
        return
    doc = os.path.join(ctx.project, *SOURCES_DOC)
    with open(doc, encoding='utf-8', newline='') as f:
        text = f.read()
    newline = '\r\n' if '\r\n' in text else '\n'
    lines = text.split(newline)
    if SOURCES_SECTION not in lines:
        print('AssetSources.md에서 "%s" 표를 찾지 못했다. 아래 줄을 손으로 넣는다:' % SOURCES_SECTION)
        for _, (_, line) in rows:
            print(line)
        return
    at = lines.index(SOURCES_SECTION) + 1
    while at < len(lines) and not lines[at].startswith('|'):
        at += 1
    while at < len(lines) and lines[at].startswith('|'):
        at += 1
    changed = []
    for res, (marker, line) in rows:
        old = next((i for i, l in enumerate(lines) if marker in l), None)
        if old is not None and not ctx.force:
            res.notes.append('AssetSources.md에 이미 줄이 있다(--force면 새로 쓴다)')
            continue
        if old is not None:
            lines[old] = line
        else:
            lines.insert(at, line)
            at += 1
        changed.append(line)
        res.notes.append('AssetSources.md에 %s' % ('줄을 더할 것이다' if ctx.dry_run else '줄을 더했다'))
    if changed and ctx.dry_run:
        print('\nAssetSources.md에 더할 줄:')
        for line in changed:
            print(line)
    elif changed:
        with open(doc, 'w', encoding='utf-8', newline='') as f:
            f.write(newline.join(lines))


# ---------------------------------------------------------------------------------------------------------- main

def token_hits(token, job):
    if token in (job.id, job.addon):
        return True
    if not job.default:
        return False
    number = job.bundle.split(' ', 1)[0]
    if token.isdigit():
        return len(token) <= 2 and token == number
    return token.lower() in job.bundle.lower()


def select(ctx, jobs):
    chosen, problems = [], []
    for job in jobs:
        wanted = any(token_hits(t, job) for t in ctx.only) if ctx.only else job.default
        if wanted or (job.choice and job.addon in ctx.picks):
            chosen.append(job)
    for token in ctx.only:
        if not any(token_hits(token, j) for j in jobs):
            problems.append('--only %s: 맞는 행이 없다' % token)
    groups = collections.defaultdict(set)
    for job in jobs:
        if job.choice:
            groups[job.choice].add(job.addon)
    for pick in sorted(ctx.picks):
        if not any(pick in addons for addons in groups.values()):
            problems.append('--pick %s: 후보 묶음(choice)에 없는 워크숍 번호다' % pick)
    for group, addons in groups.items():
        if len(addons & ctx.picks) > 1:
            problems.append('--pick: 후보 묶음 %s에서는 하나만 고른다' % group)
    ctx.choice_titles = {g: {'%s %s' % (j.addon, j.title) for j in jobs if j.choice == g} for g in groups}
    return chosen, problems


def list_addon(ctx, addon_id):
    kind, where = locate(ctx, addon_id, None)
    if kind in (None, 'bin'):
        print('애드온 %s을 읽을 수 없다. 찾아본 곳:' % addon_id)
        for d in ([where] if kind else where):
            print('  ', d)
        return 1
    addon = Addon(addon_id, kind, where)
    for name, size in addon.files.values():
        print('%11d  %s' % (size, name))
    print('%d개 파일: %s' % (len(addon.files), where))
    return 0


def summary(ctx, results, problems):
    print('\n== 요약%s ==' % (' (시험 실행: 아무것도 쓰지 않았다)' if ctx.dry_run else ''))
    for res in results:
        job = res.job
        print('[%s] %s %s → %s' % (job.id, job.addon, job.title, job.destination))
        parts = ['맞은 파일 %d' % res.matched]
        if res.staged:
            parts.append('_WorkshopImport에 풂 %d' % res.staged)
        statuses = collections.Counter(p[2] for p in res.placed)
        if res.placed:
            parts.append('넣음 %d (%s)' % (sum(n for s, n in statuses.items() if s != 'exists'),
                                          ', '.join('%s %d' % (STATUS[s], n) for s, n in sorted(statuses.items()))))
        if res.ruled_out:
            parts.append('규칙이 건너뜀 %d' % res.ruled_out)
        if res.dropped:
            parts.append('뺌 %d' % sum(res.dropped.values()))
        if res.unmatched:
            parts.append('규칙 없음 %d' % len(res.unmatched))
        print('   ' + ' · '.join(parts))
        keys = collections.Counter(p[3] for p in res.placed if p[3] and p[2] != 'exists')
        if keys:
            print('   폴더별: ' + ', '.join('%s %d' % kv for kv in sorted(keys.items())))
        if res.target:
            print('   크기 기준 %.1fdBFS (%s)' % (res.target[0], '이미 있던 클립 %d개의 가운데값' % res.target[1]
                                              if res.target[1] else '기본값: 이미 있던 클립을 읽지 못함'))
        for reason, count in sorted(res.dropped.items()):
            print('   뺌: %s %d (예: %s)' % (reason, count, ', '.join(res.examples[reason])))
        if res.unmatched:
            folders = collections.Counter(n.rsplit('/', 1)[0] for n in res.unmatched)
            print('   규칙 없음(sound_rules.csv에 줄을 더하면 들어간다): '
                  + ', '.join('%s/ %d' % kv for kv in folders.most_common(6)))
        for status in ('exists', 'replaced'):
            hits = [p[0] for p in res.placed if p[2] == status]
            if hits:
                print('   %s %d: %s' % (STATUS[status], len(hits), ', '.join(hits[:4]) + (' ...' if len(hits) > 4 else '')))
        for line in res.notes:
            print('   ' + line)
        for line in res.warnings:
            print('   주의: ' + line)
        for line in res.errors:
            print('   오류: ' + line)
        if res.review:
            print('   후보로 변환만 해 두었다: ' + res.review)
    for line in problems:
        print('주의: ' + line)
    if ctx.searched:
        print('애드온을 찾아본 곳: ' + '; '.join(sorted(ctx.searched)))
    if any(p[0].lower().endswith('.wav') and p[2] in ('new', 'replaced') for r in results for p in r.placed):
        audio_meta(ctx)
        print('새 .wav.meta의 본: ' + ctx.audio_meta_source)

    print('\n== 다음 할 일 ==')
    reviews = [r for r in results if r.review]
    if reviews:
        print('- 모델 후보를 눈으로 고른다(Blender에서 File > Import > FBX로 연다):')
        for res in reviews:
            print('    --pick %s   %s  (%s)' % (res.job.addon, res.job.title, res.review))
        print("  고른 뒤: & '%s' -I '%s' --only %s --pick <워크숍 번호>" % (
            sys.executable, os.path.abspath(__file__), ','.join(sorted({r.job.bundle.split(' ', 1)[0] for r in reviews}))))
    if ctx.searched:
        print('- 찾지 못한 애드온은 구독하고 게임을 한 번 켜서 받거나, 있는 폴더를 --l4d2·--gmod·--steam으로 알려 준다.')
    if any(p[2] == 'exists' for r in results for p in r.placed):
        print('- 다른 파일이 있어 건너뛴 것은 확인한 뒤 --force로 덮어쓴다.')
    if not ctx.dry_run and any(p[2] in ('new', 'replaced') for r in results for p in r.placed):
        print('- Unity를 연다. 새 소리는 소리 묶음(Resources/Audio)에 저절로 들어가고, FBX·PNG는 가져오기 규칙을 받으며 '
              '.meta가 생긴다. 그다음 .\\Tools\\Test-ProjectStructure.ps1로 확인하고 커밋한다.')
    if ctx.dry_run:
        print('- 시험 실행이었다. --dry-run을 빼고 다시 실행하면 위대로 넣는다.')


def parse_args(argv):
    p = argparse.ArgumentParser(description='워크숍 애드온 에셋을 port_manifest.csv대로 들여온다(안내: README.md).')
    p.add_argument('--dry-run', action='store_true', help='무엇을 할지 보여 주기만 한다. 아무것도 쓰지 않는다')
    p.add_argument('--only', action='append', metavar='X', help='묶음 번호(5), 묶음 이름(카타나), 워크숍 번호, 행 이름. '
                   '쉼표로 잇거나 여러 번 쓴다. 워크숍 번호와 행 이름은 default 0인 행도 부른다')
    p.add_argument('--pick', action='append', metavar='ID', help='후보 묶음(choice)에서 Assets에 넣을 워크숍 번호')
    p.add_argument('--force', action='store_true', help='이미 있는 다른 파일, 변환 결과, AssetSources 줄을 새로 쓴다')
    p.add_argument('--list', metavar='ID', help='애드온 안의 파일 목록만 보여 준다')
    p.add_argument('--repo', help='프로젝트 폴더(기본: 이 파일의 두 단계 위)')
    p.add_argument('--staging', help='애드온을 풀 곳(기본: 프로젝트 옆의 _WorkshopImport). 프로젝트 안은 안 된다')
    p.add_argument('--steam', action='append', help=r'Steam 라이브러리(기본: F:\SteamLibrary, C:\Program Files (x86)\Steam)')
    p.add_argument('--l4d2', action='append', help='<번호>.vpk가 든 L4D2 애드온 폴더(기본: Steam 라이브러리에서 찾는다)')
    p.add_argument('--gmod', action='append', help=r'<번호>\*.gma가 든 GMod 워크숍 폴더(기본: steamapps\workshop\content\4000)')
    p.add_argument('--blender', help='blender.exe 경로(기본: 이 파이썬이 든 Blender, ENFILADE_BLENDER, BLENDER_EXE)')
    p.add_argument('--manifest', default=os.path.join(HERE, 'port_manifest.csv'), help=argparse.SUPPRESS)
    p.add_argument('--rules', default=os.path.join(HERE, 'sound_rules.csv'), help=argparse.SUPPRESS)
    return p.parse_args(argv)


def main(argv=None):
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(errors='replace')  # a Korean console can't print every title
        except (AttributeError, ValueError):
            pass
    args = parse_args(argv)
    ctx = Context(args)
    if not os.path.isdir(ctx.project):
        print('프로젝트 폴더가 아니다(Assets/_Project 없음): ' + ctx.repo)
        return 2
    if inside(ctx.staging, ctx.repo):
        print('풀 곳이 프로젝트 안이다. 프로젝트 밖을 --staging으로 준다: ' + ctx.staging)
        return 2
    rule_sets, problems = load_rules(args.rules)
    ctx.rule_sets = rule_sets
    jobs, more = load_manifest(args.manifest, rule_sets)
    if problems + more:
        print('표에 문제가 있다:')
        for line in problems + more:
            print('  ' + line)
        return 2
    if args.list:
        return list_addon(ctx, args.list)
    for token in ctx.only + sorted(ctx.picks):
        if token in NO_REUSE:
            print('거부: %s %s. 작성자가 이식·재사용을 금지한 애드온이라 수치만 참고하고 에셋은 꺼내지 않는다.'
                  % (token, NO_REUSE[token]))
            return 1
    chosen, problems = select(ctx, jobs)
    if any(p.startswith('--pick') for p in problems):  # a wrong pick would put the wrong model in place
        for line in problems:
            print(line)
        return 2
    print('port_assets: 프로젝트 %s, 풀 곳 %s%s' % (ctx.repo, ctx.staging, ', 시험 실행' if ctx.dry_run else ''))
    print('행 %d개: %s' % (len(chosen), ', '.join(j.id for j in chosen) or '없음'), flush=True)

    results, groups = [], collections.OrderedDict()
    for job in chosen:
        if job.addon in NO_REUSE:
            res = Result(job)
            res.errors.append('거부: 작성자가 이식·재사용을 금지한 애드온이다(%s). 수치만 참고한다' % NO_REUSE[job.addon])
            results.append(res)
            continue
        groups.setdefault((job.addon, job.game), []).append(job)
    for (addon_id, game), group in groups.items():
        kind, where = locate(ctx, addon_id, game)
        addon, error = None, None
        if kind is None:
            error = '애드온을 찾지 못했다(구독했는지 확인)'
            ctx.searched.update(where)
        elif kind == 'bin':
            error = '압축된 GMod 애드온(.bin)은 못 읽는다. gmad로 푼 폴더나 .gma를 --gmod로 준다: ' + where
        else:
            try:
                addon = Addon(addon_id, kind, where)
                print('%s %s: %s (파일 %d개%s)' % (game, addon_id, where, len(addon.files),
                                                 ', 전에 푼 것' if kind == 'staged' else ''), flush=True)
            except Exception as e:  # a damaged or unknown archive stops only its own rows
                error = '애드온을 읽지 못했다(%r): %s' % (e, where)
        for job in group:
            if addon is None:
                res = Result(job)
                res.errors.append(error)
            else:
                res = run_job(ctx, job, addon, rule_sets)
            results.append(res)
    record(ctx, results)
    summary(ctx, results, problems)
    return 1 if any(r.errors for r in results) else 0


if __name__ == '__main__':
    sys.exit(main())
