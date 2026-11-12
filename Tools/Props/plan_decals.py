"""The map decals (map dressing stage 6): which Megascans decal sets come in, as what, and the rows of Tables/MapDecals.
Unpacks only the maps a decal needs (colour, opacity, normal) from each zip into <out>/src/<name> (a new folder), and
writes <out>/job.json for build_decals.py and <out>/MapDecals.rows.csv (the table's rows, to paste or merge).

usage: python -I plan_decals.py <folder with the zips> <out folder>

Each set: the zip, the name it gets under Assets/ThirdParty/Quixel/Decals, and its uses. A use is a row of the table: the
group a MapSurfaces look asks for (leak, splash, wallcrack on walls; oil, concretecrack, asphaltcrack, grime, mud, leaves
on tops), its size as laid in metres (Megascans' own size, mostly raised: a leak runs 2 m down a wall), where on a wall
it sits (top = hangs from the top edge, foot = stands on the floor before the wall, any = anywhere; floor = lies on a
top), how much each copy's size varies, how far it is drawn (m), whether a floor copy is turned at random, and how
strongly it shows (the projector's fade: a Megascans oil stain is solid black).
"""
import json
import os
import re
import sys
import zipfile

SETS = [
    # (zip, name, fade, uses): fade "sides" softens a leak's left, right and bottom edges (it starts at its top, the
    # wall's edge), "round" cuts a square patch into an uneven round pile. Left out on purpose: dry_leaves_oktna2 and
    # _qfkjg2 (sheets of single leaves in rows), mud_stain_wbhtagkv and _wbhtbcev and paper_grime_sdfrmgi (a square
    # that shows its edges), subtle_leakage_tdiledyc (all but transparent), the tire tracks (no opacity map).
    ("leakage_skphbg3c_2k", "Leakage_skphbg3c", "sides", [("leak_skphbg3c", "leak", 1.0, 2.0, "top", .3, 70, 0, .85)]),
    ("leakage_tdfgahvc_2k", "Leakage_tdfgahvc", "sides", [("leak_tdfgahvc", "leak", 1.0, 2.0, "top", .3, 70, 0, .9)]),
    ("industrial_decal_leak_02_tdlodebc_2k", "Industrial_Leak_tdlodebc", "sides", [("leak_tdlodebc", "leak", 1.0, 2.0, "top", .3, 70, 0, .85)]),
    ("leakage_tcumebvc_2k", "Leakage_tcumebvc", "sides", [("leak_tcumebvc", "leak", .35, 2.4, "top", .3, 70, 0, .9)]),
    ("leakage_tdcafitc_2k", "Leakage_tdcafitc", "sides", [("leak_tdcafitc", "leak", 1.8, 1.8, "top", .25, 70, 0, .85)]),
    ("tileable_leakage_tc1fdi2c_2k", "Tileable_Leakage_tc1fdi2c", "sides", [("leak_tc1fdi2c", "leak", 3.0, 1.5, "top", .25, 70, 0, .8)]),
    ("mud_splash_vlpgeddv_2k", "Mud_Splash_vlpgeddv", None, [("splash_vlpgeddv", "splash", 1.4, 1.0, "foot", .3, 50, 0, .9)]),
    ("mud_splash_vlpgeeev_2k", "Mud_Splash_vlpgeeev", None, [("splash_vlpgeeev", "splash", 1.4, 1.0, "foot", .3, 50, 0, .9),
                                                             ("mud_vlpgeeev", "mud", 1.2, 1.2, "floor", .3, 40, 1, .8)]),
    ("mud_splash_vlpgefev_2k", "Mud_Splash_vlpgefev", None, [("splash_vlpgefev", "splash", 1.4, 1.0, "foot", .3, 50, 0, .9)]),
    ("mud_splash_vlpgefpv_2k", "Mud_Splash_vlpgefpv", None, [("splash_vlpgefpv", "splash", 1.4, 1.0, "foot", .3, 50, 0, .9)]),
    ("mud_splash_vlpgegov_2k", "Mud_Splash_vlpgegov", None, [("splash_vlpgegov", "splash", 1.4, 1.0, "foot", .3, 50, 0, .9),
                                                             ("mud_vlpgegov", "mud", 1.2, 1.2, "floor", .3, 40, 1, .8)]),
    ("wall_crack_selgtvn_2k", "Wall_Crack_selgtvn", None, [("wallcrack_selgtvn", "wallcrack", .6, .6, "any", .3, 40, 0, 1)]),
    ("concrete_crack_sdokhyi_2k", "Concrete_Crack_sdokhyi", None, [("wallcrack_sdokhyi", "wallcrack", 1.0, .5, "any", .3, 40, 0, .9)]),
    ("concrete_crack_sfcsmqg_2k", "Concrete_Crack_sfcsmqg", None, [("wallcrack_sfcsmqg", "wallcrack", 1.6, .8, "any", .3, 40, 0, 1),
                                                                   ("crack_sfcsmqg", "concretecrack", 1.6, .8, "floor", .3, 40, 1, 1)]),
    ("concrete_crack_sfelnxg_2k", "Concrete_Crack_sfelnxg", None, [("wallcrack_sfelnxg", "wallcrack", 2.4, .6, "any", .25, 45, 0, 1),
                                                                   ("crack_sfelnxg", "concretecrack", 2.4, .6, "floor", .25, 45, 1, 1)]),
    ("concrete_crack_sdfm3fd_2k", "Concrete_Crack_sdfm3fd", None, [("crack_sdfm3fd", "concretecrack", 1.0, 1.0, "floor", .3, 40, 1, .75)]),
    ("concrete_crack_seznlkg_2k", "Concrete_Crack_seznlkg", None, [("crack_seznlkg", "concretecrack", 1.0, 1.0, "floor", .3, 40, 1, 1)]),
    ("asphalt_crack_rmopqjp2_2k", "Asphalt_Crack_rmopqjp2", None, [("asphaltcrack_rmopqjp2", "asphaltcrack", 1.2, 1.2, "floor", .3, 45, 1, 1)]),
    ("road_crack_sfisjpg_2k", "Road_Crack_sfisjpg", None, [("asphaltcrack_sfisjpg", "asphaltcrack", 3.0, .75, "floor", .25, 50, 1, .9)]),
    ("oil_stain_sdjliyi_2k", "Oil_Stain_sdjliyi", None, [("oil_sdjliyi", "oil", 2.0, 1.0, "floor", .3, 45, 1, .55)]),
    ("oil_stain_sdjmigi_2k", "Oil_Stain_sdjmigi", None, [("oil_sdjmigi", "oil", 1.2, .6, "floor", .3, 40, 1, .5)]),
    ("oil_stain_semlsbi_2k", "Oil_Stain_semlsbi", None, [("oil_semlsbi", "oil", 1.1, 1.1, "floor", .3, 40, 1, .7)]),
    ("oil_stain_semqccd_2k", "Oil_Stain_semqccd", None, [("oil_semqccd", "oil", 1.3, 1.3, "floor", .3, 40, 1, .55)]),
    ("paper_grime_sdeovii_2k", "Paper_Grime_sdeovii", None, [("grime_sdeovii", "grime", 1.0, 1.0, "floor", .35, 40, 1, .8)]),
    ("dry_leaves_pjak02_2k", "Dry_Leaves_pjak02", "round", [("leaves_pjak02", "leaves", 1.4, 1.4, "floor", .35, 35, 1, 1)]),
]
DEST = "Assets/ThirdParty/Quixel/Decals"
# How much a set's own normal map tilts the surface's (0 = the decal only colours: a leak or a splash has no relief).
NORMAL_BLEND = {"leak": .3, "splash": 0., "wallcrack": .7, "concretecrack": .7, "asphaltcrack": .7, "oil": .4,
                "grime": .5, "mud": .4, "leaves": .8}
ROLE = {"color": re.compile(r"_2K_Base[Cc]olor\.jpg$"), "opacity": re.compile(r"_2K_Opacity\.jpg$"),
        "normal": re.compile(r"_2K_Normal\.jpg$")}

zips, out = sys.argv[1], sys.argv[2]
src_root = os.path.join(out, "src")
os.makedirs(src_root, exist_ok=True)
job, rows, problems = [], [], []
for stem, name, fade, uses in SETS:
    z = zipfile.ZipFile(os.path.join(zips, stem + ".zip"))
    maps = {}
    for member in z.namelist():
        base = member.replace("\\", "/").split("/")[-1]
        if "/" in member.replace("\\", "/") or base.startswith("."):
            continue
        for role, pattern in ROLE.items():
            if pattern.search(base):
                maps[role] = base
    if "color" not in maps or "opacity" not in maps:
        problems.append("%s: no colour or opacity map" % stem)
        continue
    folder = os.path.join(src_root, name)
    os.makedirs(folder, exist_ok=True)
    for role, member in maps.items():
        target = os.path.join(folder, role + ".jpg")
        with z.open(member) as f, open(target, "wb") as g:
            g.write(f.read())
        maps[role] = target.replace("\\", "/")
    longest = max(max(u[2], u[3]) for u in uses)
    blend = max(NORMAL_BLEND.get(u[1], .5) for u in uses) if "normal" in maps else 0.
    job.append({"name": name, "dest": "%s/%s" % (DEST, name), "color": maps["color"], "opacity": maps["opacity"],
                "normal": maps.get("normal"), "largest": 2048 if longest >= 2.4 else 1024, "normalBlend": blend,
                "fade": fade})
    for row_id, group, width, height, align, vary, far, spin, opacity in uses:
        rows.append("%s,%s,ThirdParty/Quixel/Decals/%s/%s.mat,%g,%g,%s,%g,%g,%d,%g" % (
            row_id, group, name, name, width, height, align, vary, far, spin, opacity))
with open(os.path.join(out, "job.json"), "w", encoding="utf-8") as f:
    json.dump(job, f, indent=1)
with open(os.path.join(out, "MapDecals.rows.csv"), "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(rows) + "\n")
print("decal sets %d, rows %d" % (len(job), len(rows)))
print("problems:", problems if problems else "none")
