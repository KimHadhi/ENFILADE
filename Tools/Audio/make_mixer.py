"""
Writes the game's AudioMixer, Assets/_Project/Data/Resources/Audio/GameMixer.mixer, and its .meta in one go.

Why a script and not the Unity editor: Unity has no public API to make a mixer, and a hand-made one can't be rebuilt
the same way twice. This one is the same file every run (every id is a hash of a name), so adding a channel is adding
a line to CHANNELS and running it again: python Tools/Audio/make_mixer.py

What is in it (Docs/Features/SoundRebuild.md 3.7 and 7, stage 1):
- Master, and under it one group per AudioChannel (GameAudio): Combat, Movement, Voice, UI, Music, Ambience.
- Under each channel a "<Channel> Loud" group: a sound whose Sounds.csv volume is over 1 plays there at volume / the
  boost, and the group adds the boost back (Sonity's voices are AudioSources and can't go over 1; its own volume
  increase multiplies AudioListener.volume by 1000, which every older AudioSource of this game would hear too).
- Exposed parameters GameAudio writes: <Channel>Volume (the settings' channel volume and ducking, in dB) for every
  channel, <Channel>Pitch (slow motion, as GameAudio.WorldPitch) for the world channels, and <Channel>LoudVolume (the
  boost, Tables/Audio/SoundEngine.csv Engine.loudDecibel) for every Loud group.

Never edit the .mixer by hand and never in Unity's mixer window: change this script and run it again. The .meta's guid
is kept from the file already there, so references to the mixer survive a rebuild.
"""
import hashlib
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ASSET = os.path.join(ROOT, 'Assets', '_Project', 'Data', 'Resources', 'Audio', 'GameMixer.mixer')
NAME = 'GameMixer'
# (AudioChannel name, does slow motion pitch it). UI and Music keep their pitch, as GameAudio.FollowWorldPitch skips them.
CHANNELS = [('Combat', True), ('Movement', True), ('Voice', True), ('UI', False), ('Music', False), ('Ambience', True)]
LOUD = ' Loud'


def guid(*parts):
    return hashlib.md5(('GameMixer/' + '/'.join(parts)).encode('utf-8')).hexdigest()


def file_id(*parts):
    # A signed 64-bit id, never one of Unity's own fixed ones below 100000000.
    value = int(hashlib.sha1(('GameMixer/' + '/'.join(parts)).encode('utf-8')).hexdigest()[:16], 16)
    value = value - (1 << 64) if value >= (1 << 63) else value
    return value if abs(value) > 100000000 else value + 1000000000


HEADER = '  m_ObjectHideFlags: {flags}\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n'


def effect(fid, name):
    return ('--- !u!244 &%d\nAudioMixerEffectController:\n' % fid + HEADER.format(flags=3) +
            '  m_Name: \n  m_EffectID: %s\n  m_EffectName: Attenuation\n  m_MixLevel: %s\n  m_Parameters: []\n'
            '  m_SendTarget: {fileID: 0}\n  m_EnableWetMix: 0\n  m_Bypass: 0\n' % (guid(name, 'effect'), guid(name, 'mix')))


def group(fid, name, children, effect_fid):
    kids = ''.join('  - {fileID: %d}\n' % c for c in children) if children else ''
    return ('--- !u!243 &%d\nAudioMixerGroupController:\n' % fid + HEADER.format(flags=0) +
            '  m_Name: %s\n  m_AudioMixer: {fileID: 24100000}\n  m_GroupID: %s\n' % (name, guid(name, 'group')) +
            ('  m_Children:\n' + kids if children else '  m_Children: []\n') +
            '  m_Volume: %s\n  m_Pitch: %s\n  m_Send: 00000000000000000000000000000000\n' % (guid(name, 'volume'), guid(name, 'pitch')) +
            '  m_Effects:\n  - {fileID: %d}\n  m_UserColorIndex: 0\n  m_Mute: 0\n  m_Solo: 0\n  m_BypassEffects: 0\n' % effect_fid)


def build():
    blocks = []
    exposed = []
    views = [guid('Master', 'group')]
    channel_fids = []
    loud_default = {}
    for channel, pitched in CHANNELS:
        loud = channel + LOUD
        loud_fid, loud_effect = file_id(loud, 'group'), file_id(loud, 'effect')
        fid, eff = file_id(channel, 'group'), file_id(channel, 'effect')
        blocks.append(group(loud_fid, loud, [], loud_effect))
        blocks.append(effect(loud_effect, loud))
        blocks.append(group(fid, channel, [loud_fid], eff))
        blocks.append(effect(eff, channel))
        channel_fids.append(fid)
        views += [guid(channel, 'group'), guid(loud, 'group')]
        exposed.append((guid(channel, 'volume'), channel + 'Volume'))
        if pitched:
            exposed.append((guid(channel, 'pitch'), channel + 'Pitch'))
        exposed.append((guid(loud, 'volume'), channel + 'LoudVolume'))
        # The boost is what the table says at run time; +12 dB until GameAudio has set it.
        loud_default[guid(loud, 'volume')] = 12
    blocks.append(group(24300002, 'Master', channel_fids, 24400004))
    blocks.append(effect(24400004, 'Master'))
    controller = ('--- !u!241 &24100000\nAudioMixerController:\n' + HEADER.format(flags=0) +
                  '  m_Name: %s\n  m_OutputGroup: {fileID: 0}\n  m_MasterGroup: {fileID: 24300002}\n' % NAME +
                  '  m_Snapshots:\n  - {fileID: 24500006}\n  m_StartSnapshot: {fileID: 24500006}\n'
                  '  m_SuspendThreshold: -80\n  m_EnableSuspend: 0\n  m_UpdateMode: 0\n  m_ExposedParameters:\n' +
                  ''.join('  - guid: %s\n    name: %s\n' % e for e in exposed) +
                  '  m_AudioMixerGroupViews:\n  - guids:\n' + ''.join('    - %s\n' % g for g in views) +
                  '    name: View\n  m_CurrentViewIndex: 0\n  m_TargetSnapshot: {fileID: 24500006}\n')
    snapshot = ('--- !u!245 &24500006\nAudioMixerSnapshotController:\n' + HEADER.format(flags=0) +
                '  m_Name: Snapshot\n  m_AudioMixer: {fileID: 24100000}\n  m_SnapshotID: %s\n' % guid('snapshot') +
                '  m_FloatValues:\n' + ''.join('    %s: %d\n' % pair for pair in loud_default.items()) +
                '  m_TransitionOverrides: {}\n')
    return '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n' + ''.join(blocks) + controller + snapshot


def meta_guid():
    meta = ASSET + '.meta'
    if os.path.exists(meta):
        found = re.search(r'guid: ([0-9a-f]{32})', open(meta, encoding='utf-8').read())
        if found:
            return found.group(1)
    return guid('asset')


def main():
    text = build()
    meta = ('fileFormatVersion: 2\nguid: %s\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 24100000\n'
            '  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % meta_guid())
    os.makedirs(os.path.dirname(ASSET), exist_ok=True)
    # The .meta first and the asset right after, in one run (an open editor must never see the asset without it).
    with open(ASSET + '.meta', 'w', encoding='utf-8', newline='\n') as f:
        f.write(meta)
    with open(ASSET, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    print('wrote', ASSET, len(text), 'bytes')


if __name__ == '__main__':
    sys.exit(main())
