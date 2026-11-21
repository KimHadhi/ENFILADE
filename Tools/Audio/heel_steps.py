"""Lays the high-heel footsteps out as the Movement bank's keys: Assets/_Project/Art/Audio/Movement/Heels/<Kind>/<Surface>.

Why: the user gave the footsteps of the Garry's Mod addon "DSteps Dynamic High Heels Footsteps" (Workshop 2789283802),
one folder per surface the addon knows (concrete, marble, metalbar, ...), each with walk, run and "wander" clips
(2026-11-21, after asking for feet in heels to sound like heels). The game picks a heel cue's sound by its own surfaces
(Tables/Maps/Surfaces.csv) and a kind of step, so the addon's folders are laid out under those names:
  Walk    a walking step (StepWalk@Heels; crouched steps and soft landings play it quieter, as the addon does)
  Run     a running step (StepRun@Heels; a landing is two of them, as the addon does)
  Settle  the addon's "wander": the step it plays when the feet stop moving or take off for a jump (Jump@Heels)
  Land    the addon's landing clips (dirt, gravel, metal; it never used them itself)
Which addon clips go where follows the addon's own Lua (dsteps_autorun.lua): metal is "metalbar" and grates, vents and
boxes are "metalbox"; tile is "marble"; wood panels are "squeakywood"; where a surface has no run or wander of its own
the addon reuses its walk (wood, squeaky wood, mud), the dirt wander (sand) or the squeaky wood wander (wood), and water
is "through" for walking and running. The clips it shipped but never used are given the surfaces their names say:
stone, rug (carpet and tarp), bluntwood and deckwood (floor planks and decks), and the land clips. Where a kind is the
same clips as another, they are copied: a surface's sound is the folder under its name (SoundBank), and a folder that
is missing would make the surface fall back to its parent (wood to concrete), which is the wrong floor.

Run with:  python -I Tools/Audio/heel_steps.py <folder holding the addon's surface folders>
A clip already in place with the same bytes is left alone; a .meta is written only for a clip or folder that has none
(Preload Audio Data and Load In Background on, as every movement sound), so GUIDs stay. Nothing is deleted.
"""
import pathlib
import sys
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]
TARGET = ROOT / 'Assets/_Project/Art/Audio/Movement/Heels'

# kind -> game surface -> the addon's clip names (folder_kind), in the order they are taken.
LAYOUT = {
    'Walk': {
        'Concrete': ['concrete_walk'], 'Stone': ['stone_walk'], 'Marble': ['marble_walk'],
        'Glass': ['glass_hard', 'glass_hit'],
        'Metal': ['metalbar_walk'], 'MetalBox': ['metalbox_walk'], 'Gantry': ['metalbox_walk'], 'Duct': ['metalbox_walk'],
        'Wood': ['wood_walk'], 'WoodPanel': ['squeakywood_walk'], 'WoodPlank': ['bluntwood_walk'],
        'Gravel': ['gravel_walk'], 'Dirt': ['dirt_walk'], 'Grass': ['grass_walk'], 'Sand': ['sand_walk'],
        'Snow': ['snow_walk'], 'Mud': ['mud_walk'], 'Water': ['water_through'], 'Tarp': ['rug_walk'],
    },
    'Run': {
        'Concrete': ['concrete_run'], 'Stone': ['stone_run'], 'Marble': ['marble_run'],
        'Glass': ['glass_hard', 'glass_hit'],
        'Metal': ['metalbar_run'], 'MetalBox': ['metalbox_run'], 'Gantry': ['metalbox_run'], 'Duct': ['metalbox_run'],
        'Wood': ['wood_walk'], 'WoodPanel': ['squeakywood_walk'], 'WoodPlank': ['deckwood_run'],
        'Gravel': ['gravel_run'], 'Dirt': ['dirt_run'], 'Grass': ['grass_run'], 'Sand': ['sand_run'],
        'Snow': ['snow_run'], 'Mud': ['mud_walk'], 'Water': ['water_through'], 'Tarp': ['rug_walk'],
    },
    'Settle': {
        'Concrete': ['concrete_wander'], 'Stone': ['stone_wander'], 'Marble': ['marble_wander'],
        'Glass': ['glass_hard', 'glass_hit'],
        'Metal': ['metalbar_wander'], 'MetalBox': ['metalbox_wander'], 'Gantry': ['metalbox_wander'], 'Duct': ['metalbox_wander'],
        'Wood': ['squeakywood_wander'], 'WoodPanel': ['squeakywood_wander'], 'WoodPlank': ['bluntwood_wander'],
        'Gravel': ['gravel_wander'], 'Dirt': ['dirt_wander'], 'Grass': ['grass_wander'], 'Sand': ['dirt_wander'],
        'Snow': ['snow_wander'], 'Mud': ['mud_wander'], 'Water': ['water_wander'], 'Tarp': ['rug_walk'],
    },
    'Land': {
        'Dirt': ['dirt_land'], 'Gravel': ['gravel_land'], 'Metal': ['metalbar_land'],
    },
}

CLIP_META = """fileFormatVersion: 2
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
"""

FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def clips(source, name):
    """The addon's clips of one kind (concrete_walk1.wav ... concrete_walk11.wav), in number order."""
    folder = source / name.split('_')[0]
    found = [p for p in folder.iterdir() if p.is_file() and p.suffix.lower() in ('.wav', '.ogg')
             and p.stem.rstrip('0123456789') == name]
    if not found:
        sys.exit(f'no clips named {name}* in {folder}')
    return sorted(found, key=lambda p: int(p.stem[len(name):] or 0))


def folder(path, made):
    """Makes the folder and its parents under TARGET's parent, each with a .meta of its own if it has none."""
    for part in reversed([path, *path.parents]):
        if not str(part).startswith(str(TARGET.parent)) or part == TARGET.parent:
            continue
        if not part.exists():
            meta = part.with_name(part.name + '.meta')
            # The .meta first, so the open editor never gives the folder a GUID of its own.
            if not meta.exists():
                meta.write_text(FOLDER_META.format(guid=uuid.uuid4().hex), encoding='utf-8', newline='\n')
            part.mkdir()
            made.append(part)


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    source = pathlib.Path(sys.argv[1])
    made, copied, same = [], 0, 0
    for kind, surfaces in LAYOUT.items():
        for surface, names in surfaces.items():
            to = TARGET / kind / surface
            folder(to, made)
            for name in names:
                for clip in clips(source, name):
                    target = to / clip.name
                    data = clip.read_bytes()
                    if target.exists() and target.read_bytes() == data:
                        same += 1
                        continue
                    meta = target.with_name(target.name + '.meta')
                    if not meta.exists():
                        meta.write_text(CLIP_META.format(guid=uuid.uuid4().hex), encoding='utf-8', newline='\n')
                    target.write_bytes(data)
                    copied += 1
    print(f'{copied} clips copied, {same} already in place, {len(made)} folders made under {TARGET.relative_to(ROOT)}')


if __name__ == '__main__':
    main()
