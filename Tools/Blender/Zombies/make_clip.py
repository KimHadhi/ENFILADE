"""Build zombie clips from recipe files and send them into the game, without opening the Blender window.

    blender --background ArtSource/Zombies/ENFILADE_Zombie_Animation.blend --python Tools/Blender/Zombies/make_clip.py -- ArtSource/Zombies/Recipes/<name>.json [...] [--out <folder>]

Each recipe (ArtSource/Zombies/Recipes, format in Tools/Blender/Zombies/README.md) is built on the workspace's rig
and exported to Assets/_Project/Art/Animations/Zombies/<slot>/enf_<name>.fbx, replacing its own earlier export.
The .blend is never saved. This is how Claude turns "lower the head around frame 20" into a clip in the game.
"""
import json
import runpy
import sys
from pathlib import Path

import bpy

HERE = Path(__file__).resolve().parent
sys.dont_write_bytecode = True
zombie = runpy.run_path(str(HERE / 'zombie_tools.py'), run_name='zombie_tools_headless')
zombie['register']()


def main():
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    # --out DIR: a stand-in project folder to export into instead of the game (a dry run).
    out = None
    if '--out' in args:
        at = args.index('--out')
        out = Path(args[at + 1]).resolve()
        del args[at:at + 2]
    recipes = [Path(p).resolve() for p in args]
    if not recipes:
        raise SystemExit('Pass one or more recipe .json files after --')
    context = bpy.context
    rig = zombie['zombie_rig'](context)
    if rig is None:
        raise SystemExit('Open ArtSource/Zombies/ENFILADE_Zombie_Animation.blend first.')
    context.view_layer.objects.active = rig
    failures = 0
    for path in recipes:
        try:
            recipe = json.loads(path.read_text(encoding='utf-8'))
            zombie['apply_recipe'](context, rig, recipe)
            settings = context.scene.enf_zombie
            settings.overwrite = True
            exported = zombie['export_to_game'](context, rig, settings, out)
            print('ZOMBIE_CLIP_READY', path.name, '->', exported)
        except Exception as error:  # One bad recipe must not stop the rest; the exit code says something failed.
            failures += 1
            print('ZOMBIE_CLIP_FAILED', path.name, type(error).__name__, error)
    if failures:
        raise SystemExit(1)


main()
