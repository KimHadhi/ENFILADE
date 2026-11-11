"""Makes the callout voice lines (Tables/Callouts.csv, the ko column) with Windows' own Korean voice.

These are stand-ins until real recordings exist: each line's clip goes to
Assets/_Project/Art/Audio/Voice/Hiro/Callout/<id>/<id>_tts.wav, where the Voice sound bank picks it up as the key
"Callout/<id>" (SoundBankBuilder; this script also writes the bank's entries itself, so the project is complete
before Unity has seen the files). A folder that already holds a recording (any clip not ending in _tts.wav) is left
alone, so recording a line is: put the file in its folder and delete the _tts one.

Run from the project root:  python Tools/Audio/callout_voices.py            (every line)
                            python Tools/Audio/callout_voices.py move.go    (only these ids)
Needs Windows with the "Microsoft Heami Desktop" voice (ko-KR), which Windows 10/11 Korean installs have. No
packages beyond Python itself.
"""
import csv
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
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)


def ensure_folder(project_path):
    full = os.path.join(ROOT, project_path)
    os.makedirs(full, exist_ok=True)
    meta = full + '.meta'
    if not os.path.exists(meta):
        write_text(meta, FOLDER_META.format(guid=guid_of(project_path)))


def read_lines(only):
    lines = []
    with open(TABLE, encoding='utf-8-sig', newline='') as f:
        for row in csv.DictReader(f):
            line_id = (row.get('id') or '').strip()
            text = (row.get('ko') or '').strip()
            if not line_id or line_id.startswith('#') or not text:
                continue
            if only and line_id not in only:
                continue
            lines.append((line_id, text))
    return lines


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
    only = set(sys.argv[1:])
    lines = read_lines(only)
    ensure_folder(CALLOUT)
    jobs = []
    for line_id, text in lines:
        folder = f'{CALLOUT}/{line_id}'
        if recorded(folder):
            print(f'  {line_id}: has a recording, left alone')
            continue
        ensure_folder(folder)
        jobs.append((os.path.join(ROOT, folder, f'{line_id}_tts.wav'), text))
    if jobs:
        synthesize(jobs)
    for path, text in jobs:
        samples, rate = read_wav(path)
        write_wav(path, shout(samples, rate), rate)
        project_path = os.path.relpath(path, ROOT).replace('\\', '/')
        meta = path + '.meta'
        if not os.path.exists(meta):
            write_text(meta, AUDIO_META.format(guid=guid_of(project_path)))
        print(f'  {os.path.basename(path)}: {text} ({len(samples) / rate:.2f}s)')
    update_bank(bank_clips())
    print(f'{len(jobs)} lines spoken; Voice.asset updated.')


if __name__ == '__main__':
    main()
