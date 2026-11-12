"""Makes the callout voice lines (Tables/Callouts.csv) with Windows' own Korean voice.

A line's words are the Korean of the Texts.csv row its ko cell names (callout.line.<id>, since 2026-11-12: every line
the game shows is written in Texts.csv, Tools → 글자 → 글자 편집기); a cell holding plain words is spoken as written.
These are stand-ins until real recordings exist: each line's clip goes to
Assets/_Project/Art/Audio/Voice/Hiro/Callout/<id>/<id>_tts.wav, where the Voice sound bank picks it up as the key
"Callout/<id>" (SoundBankBuilder; this script also writes the bank's entries itself, so the project is complete
before Unity has seen the files). A folder that already holds a recording (any clip not ending in _tts.wav) is left
alone, so recording a line is: put the file in its folder and delete the _tts one.
What each clip says is kept in Tools/Audio/callout_voices_spoken.csv, so a line rewritten later is known to be stale:
--changed speaks only those and the lines with no clip yet (the 글자 편집기's '바뀐 콜아웃 목소리 다시 만들기' runs it).

Run from the project root:  python Tools/Audio/callout_voices.py            (every line)
                            python Tools/Audio/callout_voices.py --changed  (only rewritten or new lines)
                            python Tools/Audio/callout_voices.py move.go    (only these ids)
Needs Windows with the "Microsoft Heami Desktop" voice (ko-KR), which Windows 10/11 Korean installs have. No
packages beyond Python itself.
"""
import csv
import io
import json
import math
import os
import struct
import subprocess
import sys
import tempfile
import uuid
import wave

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TABLE = os.path.join(ROOT, 'Assets/_Project/Resources/Tables/Callouts.csv')
TEXTS = os.path.join(ROOT, 'Assets/_Project/Resources/Tables/Texts.csv')
SPOKEN = os.path.join(ROOT, 'Tools/Audio/callout_voices_spoken.csv')
VOICE_ROOT = 'Assets/_Project/Art/Audio/Voice/Hiro'
CALLOUT = VOICE_ROOT + '/Callout'
BANK = os.path.join(ROOT, 'Assets/_Project/Resources/Audio/Voice.asset')
RATE = 22050
VOICE = 'Microsoft Heami Desktop'

# The shout: a little faster and higher than the voice's reading tone.
SSML = ('<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="ko-KR">'
        '<prosody rate="+12%" pitch="+10%" volume="x-loud">{text}</prosody></speak>')

PS = r'''
param([string]$jobs)
Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$s.SelectVoice('VOICE')
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(RATE, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
foreach ($job in (Get-Content -Raw -Encoding UTF8 $jobs | ConvertFrom-Json)) {
    $s.SetOutputToWaveFile($job.path, $format)
    try { $s.SpeakSsml($job.ssml) } catch { $s.Rate = 2; $s.Speak($job.text); $s.Rate = 0 }
    $s.SetOutputToNull()
}
'''.replace('VOICE', VOICE).replace('RATE', str(RATE))

AUDIO_META = '''fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 1
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {{}}
  forceToMono: 0
  normalize: 1
  loadInBackground: 1
  ambisonic: 0
  3D: 1
  userData:
  assetBundleName:
  assetBundleVariant:
'''

FOLDER_META = '''fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
'''


def guid_of(path):
    """A fixed GUID per project path, so a rerun gives the same .meta and the bank keeps pointing at it."""
    return uuid.uuid5(uuid.NAMESPACE_URL, 'enfilade:' + path.replace('\\', '/')).hex


def write_text(path, text):
    """Writes over the file where it is, never cutting it to nothing first: while Unity has a file mapped (Voice.asset,
    a table), Windows refuses to shrink it, and open(path, 'w') shrinks first (Errno 22 = Win32 1224). A shorter text is
    padded with blank lines when the cut after it is refused too; every reader skips them."""
    data = text.encode('utf-8')
    if not os.path.exists(path):
        with open(path, 'wb') as f:
            f.write(data)
        return
    with open(path, 'r+b') as f:
        size = f.seek(0, 2)
        f.seek(0)
        f.write(data + b'\n' * max(0, size - len(data)))
        f.flush()
        try:
            f.truncate(len(data))
        except OSError:
            pass


def ensure_folder(project_path):
    full = os.path.join(ROOT, project_path)
    os.makedirs(full, exist_ok=True)
    meta = full + '.meta'
    if not os.path.exists(meta):
        write_text(meta, FOLDER_META.format(guid=guid_of(project_path)))


def texts_korean():
    """Texts.csv's Korean words by id (a later row with words wins, as the game reads it)."""
    words = {}
    if not os.path.exists(TEXTS):
        return words
    with open(TEXTS, encoding='utf-8-sig', newline='') as f:
        reader = csv.reader(f)
        header = [h.strip() for h in next(reader, [])]
        ko = header.index('ko') if 'ko' in header else 1
        for row in reader:
            if not row or not row[0].strip() or row[0].strip().startswith('#') or len(row) <= ko:
                continue
            if row[ko].strip():
                words[row[0].strip()] = row[ko].strip()
    return words


def looks_like_id(cell):
    return ' ' not in cell and '.' in cell and cell.isascii()


def read_lines(only):
    texts = texts_korean()
    lines = []
    with open(TABLE, encoding='utf-8-sig', newline='') as f:
        for row in csv.DictReader(f):
            line_id = (row.get('id') or '').strip()
            cell = (row.get('ko') or row.get('text') or '').strip()
            if not line_id or line_id.startswith('#') or not cell:
                continue
            if only and line_id not in only:
                continue
            if cell in texts:
                text = texts[cell]
            elif looks_like_id(cell):
                print(f'  {line_id}: Texts.csv has no row {cell}, skipped')
                continue
            else:
                text = cell
            # \n is a line break on screen; spoken, a pause is enough.
            lines.append((line_id, text.replace('\\n', ' ').strip()))
    return lines


def read_spoken():
    """What each clip says, by line id, as this script wrote it down."""
    spoken = {}
    if os.path.exists(SPOKEN):
        with open(SPOKEN, encoding='utf-8-sig', newline='') as f:
            for row in csv.reader(f):
                if len(row) >= 2 and row[0].strip() and row[0].strip() != 'id':
                    spoken[row[0].strip()] = row[1]
    return spoken


def write_spoken(spoken):
    out = io.StringIO()
    writer = csv.writer(out, lineterminator='\n')
    writer.writerow(['id', 'words'])
    for line_id in sorted(spoken):
        writer.writerow([line_id, spoken[line_id]])
    write_text(SPOKEN, '﻿' + out.getvalue())


def recorded(folder):
    full = os.path.join(ROOT, folder)
    if not os.path.isdir(full):
        return False
    return any(n.lower().endswith(('.wav', '.ogg', '.mp3')) and not n.lower().endswith('_tts.wav') for n in os.listdir(full))


def synthesize(jobs):
    with tempfile.TemporaryDirectory() as scratch:
        script = os.path.join(scratch, 'speak.ps1')
        listing = os.path.join(scratch, 'jobs.json')
        write_text(script, PS)
        with open(listing, 'w', encoding='utf-8') as f:
            json.dump([{'path': p, 'text': t, 'ssml': SSML.format(text=t)} for p, t in jobs], f, ensure_ascii=False)
        subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', script, listing], check=True)


def read_wav(path):
    with wave.open(path, 'rb') as w:
        n, width, rate, ch = w.getnframes(), w.getsampwidth(), w.getframerate(), w.getnchannels()
        data = w.readframes(n)
    if width != 2 or ch != 1:
        raise ValueError(f'{path}: expected 16-bit mono, got {width * 8}-bit x{ch}')
    return [s / 32768.0 for s in struct.unpack('<%dh' % (len(data) // 2), data)], rate


def write_wav(path, samples, rate):
    clipped = [max(-1.0, min(1.0, s)) for s in samples]
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(struct.pack('<%dh' % len(clipped), *[int(round(s * 32767)) for s in clipped]))


def shout(samples, rate):
    """Low rumble off, silence trimmed, pushed a little into saturation so it sits like a shout, peak at -1 dB."""
    # One-pole high-pass at 140 Hz.
    rc = 1.0 / (2 * math.pi * 140.0)
    a = rc / (rc + 1.0 / rate)
    out, prev_x, prev_y = [], 0.0, 0.0
    for x in samples:
        y = a * (prev_y + x - prev_x)
        out.append(y)
        prev_x, prev_y = x, y
    peak = max((abs(s) for s in out), default=0.0) or 1.0
    out = [s / peak for s in out]
    # Trim: first and last sample over -40 dB, 20 ms kept either side.
    gate = 10 ** (-40 / 20)
    loud = [i for i, s in enumerate(out) if abs(s) > gate]
    if loud:
        pad = int(.02 * rate)
        out = out[max(0, loud[0] - pad):min(len(out), loud[-1] + pad)]
    drive = 1.8
    norm = math.tanh(drive)
    out = [math.tanh(s * drive) / norm for s in out]
    peak = max((abs(s) for s in out), default=0.0) or 1.0
    gain = (10 ** (-1 / 20)) / peak
    out = [s * gain for s in out]
    fade_in, fade_out = int(.004 * rate), int(.03 * rate)
    for i in range(min(fade_in, len(out))):
        out[i] *= i / fade_in
    for i in range(min(fade_out, len(out))):
        out[-1 - i] *= i / fade_out
    return out


def update_bank(clips_by_key):
    """Puts every Callout/* key in Voice.asset, in SoundBankBuilder's order (keys and clip paths, case-insensitively)."""
    with open(BANK, encoding='utf-8', newline='') as f:
        text = f.read().replace('\r\n', '\n')
    head, sep, body = text.partition('  entries:\n')
    if not sep:
        raise ValueError('Voice.asset has no entries list')
    entries, current = {}, None
    for line in body.split('\n'):
        if line.startswith('  - key: '):
            current = line[len('  - key: '):].strip()
            entries[current] = []
        elif line.startswith('    - {fileID') and current is not None:
            entries[current].append(line)
    for key in [k for k in entries if k.startswith('Callout/')]:
        del entries[key]
    for key, guids in clips_by_key.items():
        entries[key] = ['    - {fileID: 8300000, guid: %s, type: 3}' % g for g in guids]
    lines = []
    for key in sorted(entries, key=lambda k: k.upper()):
        lines.append('  - key: ' + key)
        lines.append('    clips:')
        lines.extend(entries[key])
    write_text(BANK, head + sep + '\n'.join(lines) + '\n')


def bank_clips():
    """Every clip now in a Callout/<id> folder, by key, each folder's files in path order."""
    found = {}
    base = os.path.join(ROOT, CALLOUT)
    if not os.path.isdir(base):
        return found
    for name in sorted(os.listdir(base), key=str.upper):
        folder = os.path.join(base, name)
        if not os.path.isdir(folder):
            continue
        guids = []
        for clip in sorted(os.listdir(folder), key=str.upper):
            if clip.lower().endswith(('.wav', '.ogg', '.mp3')):
                meta = os.path.join(folder, clip + '.meta')
                if os.path.exists(meta):
                    with open(meta, encoding='utf-8') as f:
                        for line in f:
                            if line.startswith('guid:'):
                                guids.append(line.split(':', 1)[1].strip())
                                break
        if guids:
            found['Callout/' + name] = guids
    return found


def main():
    args = sys.argv[1:]
    changed_only = '--changed' in args
    only = {a for a in args if not a.startswith('--')}
    lines = read_lines(only)
    spoken = read_spoken()
    ensure_folder(CALLOUT)
    jobs = []
    for line_id, text in lines:
        folder = f'{CALLOUT}/{line_id}'
        if recorded(folder):
            print(f'  {line_id}: has a recording, left alone')
            continue
        path = os.path.join(ROOT, folder, f'{line_id}_tts.wav')
        # --changed: a clip that already says the line as it is now stays.
        if changed_only and os.path.exists(path) and spoken.get(line_id, '').strip() == text:
            continue
        ensure_folder(folder)
        jobs.append((path, text, line_id))
    if jobs:
        synthesize([(path, text) for path, text, _ in jobs])
    for path, text, line_id in jobs:
        samples, rate = read_wav(path)
        write_wav(path, shout(samples, rate), rate)
        project_path = os.path.relpath(path, ROOT).replace('\\', '/')
        meta = path + '.meta'
        if not os.path.exists(meta):
            write_text(meta, AUDIO_META.format(guid=guid_of(project_path)))
        spoken[line_id] = text
        print(f'  {os.path.basename(path)}: {text} ({len(samples) / rate:.2f}s)')
    if jobs:
        write_spoken(spoken)
    update_bank(bank_clips())
    print(f'{len(jobs)} lines spoken; Voice.asset updated.')


if __name__ == '__main__':
    main()
