r"""port_assets.py 자체 시험. 진짜 애드온 없이 합성 VPK(1판, 2판, zip)와 GMA, 가짜 Steam 폴더, 가짜 프로젝트,
가짜 Blender를 임시 폴더에 만들고 진짜 port_manifest.csv·sound_rules.csv로 port_assets.py를 돌려 본다.
끝나면 임시 폴더를 지운다(실패하면 남겨 두고 경로를 알려 준다).

  & 'F:\SteamLibrary\steamapps\common\Blender\4.5\python\bin\python.exe' -I .\Tools\Workshop\test_port_assets.py
"""
import io
import math
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import wave
import zipfile
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
PORT = os.path.join(HERE, 'port_assets.py')
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True
import port_assets  # noqa: E402

FAILS = []


def check(ok, what):
    print(('  ok   ' if ok else '  FAIL ') + what)
    if not ok:
        FAILS.append(what)


def tone(seconds, rate=44100, amp=0.5, late=None, channels=1, freq=220.0, lead=0.2, tail=0.3):
    """PCM16 WAV: silence, a decaying tone (or a quiet one that swells at `late` s), silence."""
    samples = [0] * int(lead * rate) * channels
    for i in range(int(seconds * rate)):
        t = i / rate
        env = math.exp(-3 * t / seconds) if late is None else (0.05 if t < late else 1.0)
        samples += [int(amp * 32767 * env * math.sin(2 * math.pi * freq * t))] * channels
    samples += [0] * int(tail * rate) * channels
    buf = io.BytesIO()
    with wave.open(buf, 'wb') as w:
        w.setnchannels(channels)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(struct.pack('<%dh' % len(samples), *samples))
    return buf.getvalue()


def adpcm():
    fmt = struct.pack('<HHIIHH', 2, 1, 22050, 11100, 512, 4)
    data = bytes(1000)
    return (b'RIFF' + struct.pack('<I', 4 + 8 + len(fmt) + 8 + len(data)) + b'WAVE' + b'fmt '
            + struct.pack('<I', len(fmt)) + fmt + b'data' + struct.pack('<I', len(data)) + data)


def vpk(files, version=2, preload=()):
    """A single-file VPK like L4D2 workshop addons; paths in `preload` keep their first 16 bytes in the tree."""
    groups = {}
    for path, body in files.items():
        folder, _, name = path.rpartition('/')
        stem, _, ext = name.rpartition('.')
        groups.setdefault(ext, {}).setdefault(folder or ' ', []).append((path, stem, body))
    tree, data = bytearray(), bytearray()
    for ext, folders in groups.items():
        tree += ext.encode() + b'\0'
        for folder, items in folders.items():
            tree += folder.encode() + b'\0'
            for path, stem, body in items:
                head = body[:16] if path in preload else b''
                rest = body[len(head):]
                tree += stem.encode() + b'\0'
                tree += struct.pack('<IHHIIH', zlib.crc32(body), len(head), 0x7FFF, len(data), len(rest), 0xFFFF) + head
                data += rest
            tree += b'\0'
        tree += b'\0'
    tree += b'\0'
    head = (struct.pack('<III', 0x55AA1234, 1, len(tree)) if version == 1
            else struct.pack('<7I', 0x55AA1234, 2, len(tree), len(data), 0, 0, 0))
    return bytes(head + tree + data)


def gma(files):
    out = bytearray(b'GMAD' + bytes([3]) + struct.pack('<QQ', 0, 0) + b'\0' + b'test\0desc\0author\0'
                    + struct.pack('<i', 1))
    for i, (path, body) in enumerate(files.items(), 1):
        out += struct.pack('<I', i) + path.encode() + b'\0' + struct.pack('<qI', len(body), zlib.crc32(body))
    out += struct.pack('<I', 0)
    for body in files.values():
        out += body
    return bytes(out + struct.pack('<I', 0))


def zipped(files):
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, 'w') as z:
        for path, body in files.items():
            z.writestr(path, body)
    return buf.getvalue()


def model(stem, folder='models/weapons/melee', material='models/weapons/melee/katana'):
    return {'%s/%s.mdl' % (folder, stem): b'IDST' + bytes(60), '%s/%s.vvd' % (folder, stem): b'IDSV' + bytes(30),
            '%s/%s.dx90.vtx' % (folder, stem): bytes(40), 'materials/%s.vmt' % material: b'"VertexLitGeneric" {}',
            'materials/%s.vtf' % material: b'VTF\0' + bytes(100)}


def build(root):
    """Fake Steam library, project and Blender. Returns the paths."""
    steam = os.path.join(root, 'Steam')
    l4d2 = os.path.join(steam, 'steamapps', 'common', 'Left 4 Dead 2', 'left4dead2', 'addons', 'workshop')
    gmod = os.path.join(steam, 'steamapps', 'workshop', 'content', '4000')
    os.makedirs(l4d2)
    ci = 'sound/npc/infected/'
    bark = tone(0.5, amp=0.6, freq=300)
    re6 = {
        ci + 'idle/moaning/moan01.wav': tone(1.5), ci + 'idle/mumbling/moan01.wav': tone(1.2, freq=180),
        ci + 'idle/moaning/male/moan02.wav': tone(2.0, amp=0.2, channels=2),
        ci + 'idle/mumbling/female/mumble03.wav': tone(1.0, rate=22050),
        ci + 'idle/breathing/breathe01.wav': tone(1.0, amp=0.02),
        ci + 'idle/breathing/male/breathe02.wav': tone(1.1, amp=0.03),
        ci + 'idle/moaning/moan05.wav': tone(6.0), ci + 'idle/moaning/moan_dup.wav': bark,
        ci + 'idle/moaning/moan_adpcm.wav': adpcm(),
        ci + 'alert/alert/male/alert11.wav': tone(1.2, amp=0.8),
        ci + 'alert/becomeenraged/female/become_enraged05.wav': tone(1.8),
        ci + 'action/been_shot/been_shot_01.wav': tone(0.4, amp=0.9),
        ci + 'action/been_shot/male/been_shot_12.wav': tone(1.6),
        ci + 'action/been_shot/female/been_shot_03.wav': tone(0.8, late=0.6),
        ci + 'action/die/female/death_31.wav': tone(2.0), ci + 'action/die/death_14.wav': tone(1.0),
        ci + 'action/rageat/male/rage_at_victim21.wav': bark, ci + 'action/rage/female/rage_57.wav': tone(0.7),
        ci + 'hit/hit_punch_99.wav': tone(0.2), ci + 'action/unknownthing/x.wav': tone(0.5),
        'sound/npc/mega_mob/mega_mob_incoming.wav': tone(3.0),
    }
    fleshy = {ci + 'hit/hit_punch_01.wav': tone(0.2, amp=0.9), ci + 'hit/punch_boxing_facehit4.wav': tone(0.3),
              'sound/player/survivor/hit/int/punch_boxing_bodyhit01.wav': tone(0.3)}
    katana_sfx = ['katana_swing_miss1', 'katana_impact_world1', 'melee_katana_01', 'katana_deploy_1']
    meaty = {'sound/weapons/katana/%s.wav' % n: tone(0.3) for n in katana_sfx}
    meaty.update({ci + 'gore/headless/headless_1.wav': tone(0.5), 'sound/player/neck_snap_01.wav': tone(0.4),
                  'sound/weapons/machete/machete_swing_miss1.wav': tone(0.3)})
    cavalry = {'sound/weapons/tonfa/%s.wav' % n: tone(0.3) for n in
               ('tonfa_swing_miss1', 'tonfa_impact_world1', 'melee_tonfa_01', 'tonfa_deploy_1', 'tonfa_weird')}
    cavalry.update(model('w_tonfa', material='models/weapons/melee/tonfa'))
    krait = {'sound/weapons/pistol/gunfire/%s.wav' % n: tone(0.3) for n in ('pistol_fire', 'pistol_dual_fire')}
    krait.update({'sound/weapons/pistol/gunother/%s.wav' % n: tone(0.3) for n in (
        'pistol_clip_out_1', 'pistol_clip_in_1', 'pistol_clip_locked_1', 'pistol_slideback_1', 'pistol_slideforward_1',
        'pistol_deploy_1', 'clipempty_pistol')})
    krait.update({'sound/kraitp68/inspect_empty1.wav': tone(0.3), 'sound/kraitp68/weird.wav': tone(0.3)})
    krait.update(model('w_pistol_a', 'models/w_models/weapons', 'nmw4/krait'))
    krait.update(model('v_pistola', 'models/v_models', 'nmw4/krait_v'))
    archives = {'3675704366': vpk(re6), '3725589072': vpk(fleshy, 1, preload={ci + 'hit/hit_punch_01.wav'}),
                '3802606110': zipped(model('w_katana')), '3532432838': vpk(model('w_katana')),
                '3661236204': vpk(cavalry, 1), '3660295000': vpk(meaty), '3803570564': vpk(krait)}
    for addon, body in archives.items():
        with open(os.path.join(l4d2, addon + '.vpk'), 'wb') as f:
            f.write(body)
    os.makedirs(os.path.join(gmod, '999000111'))
    with open(os.path.join(gmod, '999000111', 'test.gma'), 'wb') as f:
        f.write(gma({'sound/test/one.wav': tone(0.3), 'lua/autorun/x.lua': b'print(1)'}))

    repo = os.path.join(root, 'Game', 'ENFILADE')
    project = os.path.join(repo, 'Assets', '_Project')
    zombie = os.path.join(project, 'Art', 'Audio', 'Enemies', 'Zombie')
    os.makedirs(os.path.join(project, 'Docs'))
    shutil.copy(os.path.join(REPO, 'Assets', '_Project', 'Docs', 'AssetSources.md'), os.path.join(project, 'Docs'))
    for rel, body in (('Attack/rage_at_victim01.wav', tone(1.0, amp=0.7)), ('Leap/Male/rage_52.wav', tone(0.6, amp=0.4))):
        path = os.path.join(zombie, *rel.split('/'))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, 'wb') as f:
            f.write(body)
    meta = os.path.join(REPO, 'Assets', '_Project', *port_assets.META_TEMPLATE_FOLDER, 'rage_at_victim01.wav.meta')
    shutil.copy(meta, os.path.join(zombie, 'Attack'))

    fake = os.path.join(root, 'fake_blender.py')
    with open(fake, 'w', encoding='utf-8') as f:
        f.write('import os, sys\n'
                'args = sys.argv[sys.argv.index("--") + 1:]\n'
                'mdl, out, name = args[0], args[1], args[2]\n'
                'root = mdl.replace("\\\\", "/").rsplit("/models/", 1)[0]\n'
                'assert os.path.isdir(os.path.join(root, "materials")), "materials missing"\n'
                'os.makedirs(os.path.join(out, "Textures"), exist_ok=True)\n'
                'open(os.path.join(out, name + ".fbx"), "wb").write(("FBX " + mdl).encode())\n'
                'open(os.path.join(out, "Textures", "krait_d.png"), "wb").write(("PNG " + str(args[3:])).encode())\n'
                'print("MESH", name, 12, ["mat"])\nprint("EXPORTED", out)\n')
    if os.name == 'nt':
        blender = os.path.join(root, 'fake_blender.cmd')
        with open(blender, 'w') as f:
            f.write('@"%s" -I "%s" %%*\n' % (sys.executable, fake))
    else:
        blender = os.path.join(root, 'fake_blender.sh')
        with open(blender, 'w') as f:
            f.write('#!/bin/sh\nexec "%s" -I "%s" "$@"\n' % (sys.executable, fake))
        os.chmod(blender, 0o755)
    return {'steam': steam, 'l4d2': l4d2, 'repo': repo, 'project': project, 'zombie': zombie, 'blender': blender,
            'staging': os.path.join(root, 'Game', '_WorkshopImport')}


def run(env, *args):
    command = [sys.executable, '-I', PORT, '--repo', env['repo'], '--steam', env['steam'], '--blender', env['blender']]
    out = subprocess.run(command + list(args), stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    text = out.stdout.decode('utf-8', 'replace')
    return out.returncode, text


def files(root):
    found = set()
    for folder, _, names in os.walk(root):
        found.update(os.path.relpath(os.path.join(folder, n), root).replace('\\', '/') for n in names)
    return found


def at(env, rel):
    return os.path.join(env['project'], *rel.split('/'))


def loudness(path):
    wav = port_assets.parse_wav(open(path, 'rb').read())
    shape = port_assets.analyze(port_assets.samples16(wav.pcm), wav.channels, wav.rate)
    return wav, shape, 20 * math.log10(shape.loudness / 32768), 20 * math.log10(max(shape.peak, 1) / 32768)


def main():
    root = tempfile.mkdtemp(prefix='port_assets_test_')
    env = build(root)
    before = files(env['repo'])
    doc = at(env, 'Docs/AssetSources.md')
    original_doc = open(doc, encoding='utf-8').read()

    print('1. 시험 실행(--dry-run)')
    code, text = run(env, '--dry-run')
    check(code == 0, 'dry run exit 0 (got %s)' % code)
    check(files(env['repo']) == before and not os.path.exists(env['staging']), 'dry run writes nothing')
    check('Idle/Male 1' in text and 'Breathe/Male 1' in text and 'Hurt 1' in text, 'dry run shows the keys')
    check('변환할 명령' in text and 'AssetSources.md에 더할 줄' in text, 'dry run shows Blender commands and doc rows')

    print('2. 실제 실행')
    code, text = run(env)
    check(code == 0, 'run exit 0 (got %s)' % code)
    z = 'Art/Audio/Enemies/Zombie/'
    wanted = ['Idle/moan01.wav', 'Idle/mumbling_moan01.wav', 'Idle/Male/moan02.wav', 'Idle/Female/mumble03.wav',
              'Breathe/breathe01.wav', 'Breathe/Male/breathe02.wav', 'Alert/Male/alert11.wav',
              'Alert/Female/become_enraged05.wav', 'Hurt/been_shot_01.wav', 'Death/Female/death_31.wav',
              'Death/death_14.wav']
    for rel in wanted:
        check(os.path.isfile(at(env, z + rel)) and os.path.isfile(at(env, z + rel + '.meta')), 'voice ' + rel)
    for rel in ('Idle/moan05.wav', 'Hurt/Male/been_shot_12.wav', 'Hurt/Female/been_shot_03.wav', 'Idle/moan_dup.wav',
                'Idle/moan_adpcm.wav', 'Attack/Male/rage_at_victim21.wav', 'Leap/Female/rage_57.wav',
                'Hurt/hit_punch_99.wav'):
        check(not os.path.exists(at(env, z + rel)), 'not taken: ' + rel)
    check('규칙 없음 1' in text and 'action/unknownthing' in text, 'unmatched folder reported')
    for reason in ('긺', '가장 큰 곳이 늦음', '같은 녹음', '형식'):
        check(reason in text, 'drop reason shown: ' + reason)
    meta = open(at(env, z + 'Idle/moan01.wav.meta')).read()
    template = open(os.path.join(env['zombie'], 'Attack', 'rage_at_victim01.wav.meta')).read()
    guid = re.search(r'(?m)^guid: ([0-9a-f]{32})$', meta)
    check(guid is not None and guid.group(1) not in template, 'wav meta has a fresh guid')
    check(re.sub(r'guid: \w+', '', meta) == re.sub(r'guid: \w+', '', template), 'wav meta copies the Attack clip meta')
    check('preloadAudioData: 1' in meta and 'loadInBackground: 1' in meta, 'wav meta loads in background')
    check('folderAsset: yes' in open(at(env, z + 'Idle/Male.meta')).read(), 'new folders get folder metas')
    _, _, target, _ = loudness(os.path.join(env['zombie'], 'Leap', 'Male', 'rage_52.wav'))
    _, _, other, _ = loudness(os.path.join(env['zombie'], 'Attack', 'rage_at_victim01.wav'))
    goal = (target + other) / 2
    for rel in ('Idle/moan01.wav', 'Death/death_14.wav', 'Breathe/breathe01.wav'):
        wav, shape, level, peak = loudness(at(env, z + rel))
        check(abs(level - goal) < 0.6 or peak > -1.2, '%s level %.1f dBFS (goal %.1f, peak %.1f)' % (rel, level, goal, peak))
        check(peak <= -0.9, '%s peak under the ceiling' % rel)
    src = port_assets.parse_wav(tone(1.5))
    wav, shape, _, _ = loudness(at(env, z + 'Idle/moan01.wav'))
    check(len(wav.pcm) < len(src.pcm) and shape.start < 0.02 * 44100, 'silence trimmed')
    check(loudness(at(env, z + 'Idle/Female/mumble03.wav'))[0].rate == 22050, '22 kHz kept')
    check(loudness(at(env, z + 'Idle/Male/moan02.wav'))[0].channels == 2, 'stereo kept')

    fleshy = at(env, 'Art/Audio/Combat/FleshyHit/hit_punch_01.wav')
    check(open(fleshy, 'rb').read() == tone(0.2, amp=0.9), 'copy is byte-exact (VPK v1 with preload)')
    check(not os.path.exists(at(env, 'Art/Audio/Combat/FleshyHit/punch_boxing_bodyhit01.wav')), 'survivor hit left out')
    for rel in ('Swing/katana_swing_miss1.wav', 'HitWorld/katana_impact_world1.wav', 'HitFlesh/melee_katana_01.wav',
                'Draw/katana_deploy_1.wav', 'Swing/tonfa_swing_miss1.wav', 'HitFlesh/melee_tonfa_01.wav'):
        check(os.path.isfile(at(env, 'Art/Audio/Melee/Katana/' + rel)), 'katana ' + rel)
    for rel in ('Shot/pistol_fire.wav', 'DualShot/pistol_dual_fire.wav', 'MagOut/pistol_clip_out_1.wav',
                'MagIn/pistol_clip_in_1.wav', 'MagLock/pistol_clip_locked_1.wav', 'SlideBack/pistol_slideback_1.wav',
                'SlideForward/pistol_slideforward_1.wav', 'Draw/pistol_deploy_1.wav', 'DryFire/clipempty_pistol.wav',
                'Inspect/inspect_empty1.wav', 'Other/weird.wav'):
        check(os.path.isfile(at(env, 'Art/Audio/Guns/KraitP68/' + rel)), 'krait ' + rel)
    check(not os.path.exists(at(env, 'Art/Models/Weapons/Katana')), 'katana candidates stay out of Assets')
    for row in ('katana-model-zzz', 'katana-model-yamato', 'katana-model-cavalry'):
        check(os.path.isfile(os.path.join(env['staging'], '_converted', row, 'Katana.fbx')), 'candidate converted: ' + row)
    fbx = at(env, 'Art/Models/Weapons/KraitP68/KraitP68.fbx')
    check(os.path.isfile(fbx) and 'w_pistol_a.mdl' in open(fbx).read(), 'krait model placed')
    check("['2048']" in open(at(env, 'Art/Models/Weapons/KraitP68/Textures/krait_d.png')).read(), 'texture size passed')
    check(not os.path.exists(fbx + '.meta') and os.path.isfile(at(env, 'Art/Models/Weapons/KraitP68.meta')),
          'no fbx meta, folder metas made')
    check(os.path.isfile(os.path.join(env['staging'], '3803570564', 'materials', 'nmw4', 'krait.vtf')), 'materials staged')
    check(os.path.isfile(os.path.join(env['staging'], '3675704366', 'sound', 'npc', 'infected', 'idle', 'moaning',
                                      'moan01.wav')), 'raw sound staged')
    text_doc = open(doc, encoding='utf-8').read()
    for row in ('zombie-voice', 'fleshy-hit', 'katana-sfx-meaty', 'katana-sfx-cavalry', 'krait-model', 'krait-sfx'):
        check(text_doc.count('port_assets.py) `%s`' % row) == 1, 'AssetSources row ' + row)
    check('`katana-model`' not in text_doc, 'no AssetSources row for unpicked candidates')
    lines = text_doc.split('\n')
    added = [i for i, l in enumerate(lines) if 'port_assets.py) `' in l]
    section = lines.index(port_assets.SOURCES_SECTION)
    first_table_end = next(i for i in range(section + 1, len(lines))
                           if lines[i - 1].startswith('|') and not lines[i].startswith('|'))
    check(added == list(range(added[0], added[-1] + 1)) and lines[added[0] - 1].startswith('|')
          and added[-1] + 1 == first_table_end and lines[first_table_end] == '', 'rows go at the end of the first table')
    check([l for l in lines if 'port_assets.py) `' not in l] == original_doc.split('\n'), 'other lines untouched')

    print('3. 다시 실행(같은 결과)')
    os.remove(at(env, z + 'Death/death_14.wav.meta'))
    code, text = run(env)
    check(code == 0 and '새 파일' not in text and '다른 파일이 있어 건너뜀' not in text, 'second run changes nothing')
    check(open(doc, encoding='utf-8').read() == text_doc, 'second run leaves AssetSources as it was')
    check('loadInBackground: 1' in open(at(env, z + 'Death/death_14.wav.meta')).read(), 'a lost wav meta is made again')

    print('4. 덮어쓰기 막기와 --force')
    with open(fleshy, 'ab') as f:
        f.write(b'edited')
    code, text = run(env, '--only', 'fleshy-hit')
    check(open(fleshy, 'rb').read().endswith(b'edited') and '다른 파일이 있어 건너뜀' in text, 'edited file kept')
    raw = open(doc, 'rb').read().replace(b'\r\n', b'\n').replace(b'\n', b'\r\n')  # a Windows checkout (autocrlf)
    open(doc, 'wb').write(raw)
    code, text = run(env, '--only', 'fleshy-hit', '--force')
    check(open(fleshy, 'rb').read() == tone(0.2, amp=0.9), '--force restores it')
    check(open(doc, encoding='utf-8').read().count('`fleshy-hit`') == 1, '--force rewrites the row, no duplicate')
    raw = open(doc, 'rb').read()
    check(raw.count(b'\n') == raw.count(b'\r\n'), 'CRLF kept in AssetSources.md')
    os.remove(os.path.join(env['l4d2'], '3725589072.vpk'))
    code, text = run(env, '--only', 'fleshy-hit')
    check(code == 0 and '전에 푼 것' in text, 'falls back to the staging folder')

    print('5. 카타나 후보 고르기')
    code, text = run(env, '--pick', '3532432838,3802606110')
    check(code == 2 and '하나만' in text, 'two picks in one group refused')
    code, text = run(env, '--only', '카타나', '--pick', '3532432838')
    katana = at(env, 'Art/Models/Weapons/Katana/Katana.fbx')
    check(code == 0 and os.path.isfile(katana) and '3532432838' in open(katana).read(), 'picked candidate placed')
    check('전에 변환한 것을 쓴다' in text, 'conversion reused')
    check(open(doc, encoding='utf-8').read().count('port_assets.py) `katana-model`') == 1, 'one row for the pick')

    print('6. 금지 애드온, GMA, 목록, 오류')
    code, text = run(env, '--only', '3111178360')
    check(code == 1 and '거부' in text, 'no-reuse addon refused')
    manifest = os.path.join(root, 'test_manifest.csv')
    head = ','.join(port_assets.MANIFEST_COLUMNS)
    with open(manifest, 'w', encoding='utf-8-sig') as f:
        f.write(head + '\n#설명\n'
                'gma-copy,9 시험,999000111,GMod,test,시험,^sound/.+\\.wav$,Art/Audio/Library/Test,copy,,1,,unknown — 시험,\n'
                'vj,9 시험,131759821,GMod,VJ Base,시험,.,Art/Audio/Library/VJ,copy,,1,,original,\n')
    code, text = run(env, '--manifest', manifest)
    check(code == 1 and '거부' in text, 'no-reuse row refused')
    check(os.path.isfile(at(env, 'Art/Audio/Library/Test/one.wav')), 'GMA read and copied')
    check(not os.path.exists(at(env, 'Art/Audio/Library/VJ')), 'nothing taken from the refused addon')
    code, text = run(env, '--list', '3803570564')
    check(code == 0 and 'models/w_models/weapons/w_pistol_a.mdl' in text, '--list shows the files')
    code, text = run(env, '--only', 'glock21-sfx')
    check(code == 1 and '찾지 못했다' in text, 'missing addon reported')
    code, text = run(env, '--staging', os.path.join(env['repo'], 'Temp'))
    check(code == 2, 'staging inside the project refused')
    with open(manifest, 'w', encoding='utf-8-sig') as f:
        f.write(head + '\nbad,9,1,L4D2,t,w,([,Art/X,copy,,1,,s,\n../up,9,1,L4D2,t,w,x,Art/Y,copy,,1,,s,\n')
    code, text = run(env, '--manifest', manifest)
    check(code == 2 and '정규식' in text and '행 이름은' in text, 'bad manifest refused')

    if FAILS:
        print('\n실패 %d개. 임시 폴더를 남겨 두었다: %s' % (len(FAILS), root))
        return 1
    shutil.rmtree(root, ignore_errors=True)
    print('\n모두 통과')
    return 0


if __name__ == '__main__':
    sys.exit(main())
