# The chosen props for map dressing (picked from the contact sheets) and where each one's model and texture roles
# come from. Writes job.json for build_props.py (Blender) and the Dekogon select lists for import_unitypackage.py.
# Usage: python -I plan_props.py <scratch> <out_dir>
import json
import os
import re
import sys
import zipfile

S, OUT = sys.argv[1], sys.argv[2]
FAB = "F:/FabAssets"
os.makedirs(OUT, exist_ok=True)

QDIRS = {
    "Roadside": FAB + "/Roadside_Construction-6426cc8a/fbx/mid/roadside_construction_mi_extracted",
    "Warehouse": FAB + "/Warehouse-a3149fab/fbx/mid/warehouse_mid_extracted",
    "UnfinishedBuilding": FAB + "/Unfinished_Building-25f2e7e5/fbx/mid/unfinished_building_mid_extracted",
    "UrbanTrash": FAB + "/Urban_Trash-4eb4b87c/fbx/mid/urban_trash_mid_extracted",
    "Junkyard": S + "/extract/junkyard",
}
# kind, pack, Quixel id, readable name
QUIXEL = [
    ("cone", "Roadside", "vgkxcf3", "ConeOld"), ("cone", "Roadside", "vhvpabi", "ConeStriped"),
    ("sawhorse", "Roadside", "vgkxdhk", "Sawhorse"),
    ("warninglight", "Roadside", "vgledic", "WarningLight"),
    ("trafficdrum", "Roadside", "viysfij", "TrafficDrum"),
    ("delineator", "Roadside", "vjxscee", "Delineator"),
    ("roadplate", "Roadside", "vj1tfaj", "RoadPlate"), ("roadplate", "Roadside", "vj1vddw", "RoadPlate"),
    ("roadplate", "Roadside", "vj1wdcq", "RoadPlate"),
    ("jersey", "Roadside", "ubitfhtfa", "ConcreteBarrier"), ("jersey", "Roadside", "tlwtahufa", "PlasticBarrier"),
    ("rubblepile", "Roadside", "tfxteboda", "RubblePile"), ("rubblepile", "Roadside", "tgdtfg0da", "RubblePile"),
    ("sandpile", "Roadside", "vkpmcjk", "SandPile"),
    ("rubble", "UnfinishedBuilding", "tl0kcdmiw", "ConcreteChunk"), ("rubble", "UnfinishedBuilding", "tl0mbbihw", "ConcreteChunk"),
    ("rubble", "UnfinishedBuilding", "tl1gdgyhw", "ConcreteChunk"), ("rubble", "UnfinishedBuilding", "tmclbcpiw", "ConcreteChunk"),
    ("rubble", "UnfinishedBuilding", "tmclcaeiw", "ConcreteChunk"), ("rubble", "UnfinishedBuilding", "tmjgderjw", "ConcreteChunk"),
    ("cinderblock", "UnfinishedBuilding", "vk2sdis", "CinderBlock"), ("cinderblock", "UnfinishedBuilding", "vk2vcdl", "CinderBlock"),
    ("concretebeam", "UnfinishedBuilding", "ujlkbd1ga", "ConcreteBeam"), ("concretebeam", "UnfinishedBuilding", "ujonbdmfa", "ConcreteBeam"),
    ("concretebeam", "UnfinishedBuilding", "ujoqec3ga", "ConcreteBeam"),
    ("concretepillar", "UnfinishedBuilding", "ujikdbrva", "ConcretePillar"), ("concretepillar", "UnfinishedBuilding", "ujjleixga", "ConcretePillar"),
    ("rubblebag", "UnfinishedBuilding", "ukxkbesqa", "RubbleBag"),
    ("cementbags", "Roadside", "xgxhfg0", "CementBags"),
    ("wheelbarrow", "Roadside", "xf3qdjy", "Wheelbarrow"),
    ("spool", "Roadside", "uldhdhdva", "CableSpool"),
    ("electricbox", "Roadside", "ullibjd", "ElectricBox"), ("electricbox", "Warehouse", "ujyqaelga", "ElectricBox"),
    ("drum", "Junkyard", "teraccgda", "OilDrum"), ("drum", "Junkyard", "teufceuda", "OilDrum"),
    ("drum", "Junkyard", "tewscfuda", "OilDrum"), ("drum", "Warehouse", "vijnbjz", "OilDrum"),
    ("plasticdrum", "Warehouse", "vgyiciqaw", "PlasticDrum"), ("plasticdrum", "Warehouse", "virvfjk", "PlasticDrum"),
    ("gascylinder", "Junkyard", "tl1rbitfa", "GasCylinder"), ("gascylinder", "Junkyard", "vizqehw", "GasCylinder"),
    ("jerrycan", "Junkyard", "vh2mecws", "Jerrycan"), ("jerrycan", "Junkyard", "vktifj1ga", "Jerrycan"),
    ("jerrycan", "Junkyard", "xkzlae1", "Jerrycan"),
    ("corrugated", "Junkyard", "vb1kfhv", "CorrugatedSheet"), ("corrugated", "Junkyard", "vb1lafx", "CorrugatedSheet"),
    ("tire", "Junkyard", "vj2meecs", "Tire"),
    ("pallet", "Warehouse", "vh1icei", "Pallet"), ("pallet", "Warehouse", "vh1jeck", "Pallet"), ("pallet", "Warehouse", "vh1jegi", "Pallet"),
    ("palletload", "Junkyard", "xiwcfassc", "PalletTarp"), ("palletload", "Warehouse", "vhtibbe", "CoveredLoad"),
    ("cardboard", "Warehouse", "vgyidauaw", "CardboardBox"), ("cardboard", "Warehouse", "vh1hehj", "CardboardBox"),
    ("cardboard", "Warehouse", "vicifcy", "CardboardBox"),
    ("cardboardflat", "Warehouse", "vjvraja", "CardboardFlat"), ("cardboardflat", "Warehouse", "vjvrbbb", "CardboardFlat"),
    ("cardboardflat", "Warehouse", "vjwtbd1", "CardboardFlat"),
    ("plasticcrate", "Warehouse", "vh2jfg1s", "PlasticCrate"), ("plasticcrate", "Warehouse", "vifpcfpaw", "PlasticCrate"),
    ("plasticcrate", "Warehouse", "vifpceyaw", "PlasticCrate"),
    ("trashbag", "UrbanTrash", "vbelddldw", "TrashBag"), ("trashbag", "UrbanTrash", "vcblbchga", "TrashBag"),
    ("trashcan", "UrbanTrash", "ucmndaefa", "TrashCan"), ("trashcan", "UrbanTrash", "ucynedrfa", "TrashCan"),
    ("planks", "UrbanTrash", "tezvbcuda", "Plank"), ("planks", "UrbanTrash", "tjrmdagiw", "Plank"),
    ("planks", "UrbanTrash", "vbzlbcciw", "Plank"),
    ("wireshelf", "Warehouse", "vhtmaifaw", "WireShelf"),
    ("locker", "Warehouse", "vjhledeaw", "Locker"),
    ("stool", "Warehouse", "ukknbeyaw", "Stool"),
    ("worktable", "UrbanTrash", "veigfjmaw", "WorkTable"),
    ("handtruck", "Roadside", "virqcfw", "HandTruck"),
    ("toolbox", "Roadside", "vhvpado", "Toolbox"),
    ("ladder", "Warehouse", "vh2jfjes", "StepLadder"),
    ("lampindustrial", "Warehouse", "vgyide1aw", "IndustrialLamp"),
]
# kind, model zip, readable name, zip whose textures to use (None: its own)
SINGLES = [
    ("sandbags", "sandbag_barrier_udtvegsfa_low.zip", "SandbagWall_udtvegsfa", "sandbag_barrier_udtvegsfa_high.zip"),
    ("metalbarricade", "metal_barricade_vgledec_mid.zip", "MetalBarricade_vgledec", None),
    ("tirestack", "urban_race_track_tires_rubber_painted_xiwcfiesc_mid.zip", "TireStack_xiwcfiesc", None),
]
# kind, Unreal pack, mesh asset name
UNREAL = [
    ("checkpointgate", "MOUT", "SM_Checkpoint_Barrier_01"),
    ("guardtower", "MOUT", "SM_GuardTower_01"), ("guardtower", "MOUT", "SM_Guardtower_02"),
    ("hesco", "MOUT", "SM_Barrier_Walls"), ("hescosand", "MOUT", "SM_Barrier_Sand"),
    ("concreteblock", "MOUT", "SM_ConcreteBarrierBlock_01"),
    ("barbedwire", "MOUT", "SM_barbedwire"),
    ("fence", "MOUT", "SM_Fencing_01"),
    ("sandbag", "MOUT", "SM_Sandbags_01a"), ("sandbag", "MOUT", "SM_Sandbags_01b"), ("sandbag", "MOUT", "SM_Sandbags_01c"),
    ("streetlamp", "MOUT", "SM_DoubleLight_01"), ("streetlamp", "MOUT", "SM_SingleLight_01"),
    ("powerpole", "MOUT", "SM_ElectricityPole_01"),
    ("acunit", "MOUT", "SM_ACUnit_01"),
    ("watertank", "MOUT", "SM_WaterTank_01"),
    ("accessgate", "MOUT", "SM_Access_Gate_01"),
    ("wrecktruck", "Vigilante", "SM_West_Truck_M923_Damaged"),
    ("militarytruck", "Vigilante", "SK_West_Truck_M923"),
]
UE_DEST = {"MOUT": "Assets/ThirdParty/CGHERO/MOUT", "Vigilante": "Assets/ThirdParty/Vigilante/M923"}
# blend file (report name) -> destination, groups: kind, [objects] (the first names the model)
BLEND = {
    "abandoned_cars": ("F:/FabAssets/abandoned_cars.blend", "Assets/ThirdParty/Fab/AbandonedCars", [
        ("car", ["SM_CAR_CompactSedan"]), ("car", ["SM_CAR_CompactSedan_Rust"]), ("car", ["SM_CAR_Crossover"]),
        ("car", ["SM_CAR_Crossove_Rust"]), ("car", ["SM_Car_Hatchback"]), ("car", ["SM_Car_Hatchback_Rust"]),
        ("car", ["SM_Car_Sedan"]), ("car", ["SM_Car_Sedan_Rust"]), ("car", ["SM_Car_Minivan"]),
        ("car", ["SM_Car_Minivan_Rust"]), ("car", ["SM_Car_Pickup_Rust"]), ("car", ["SM_Car_Sedan_Taxi"]),
        ("van", ["SM_CAR_Van"]), ("van", ["SM_Car_Pickup"]),
        ("policecar", ["SM_Car_Sedan_Police"]), ("policecar", ["SM_Car_Hatchback_Police"]), ("policecar", ["SM_CAR_Crossover_Police"]),
    ]),
    "concrete_brick_pack": ("F:/FabAssets/concrete_brick_pack.blend", "Assets/ThirdParty/Fab/ConcreteBrickPack", [
        ("rubblepile", ["SM_brick_debris_large_01"]), ("rubblepile", ["SM_brick_debris_large_02"]), ("rubblepile", ["SM_brick_debris_large_03"]),
        ("brickdebris", ["SM_brick_debris_medium_01"]), ("brickdebris", ["SM_brick_debris_medium_02"]),
        ("brickdebris", ["SM_brick_debris_medium_03"]), ("brickdebris", ["SM_brick_debris_medium_04"]),
        ("brickdebris", ["SM_brick_debris_small_01"]),
        ("brickwall", ["SM_brick_wall_01"]), ("brickwall", ["SM_brick_wall_02"]), ("brickwall", ["SM_brick_wall_03"]),
        ("brickwall", ["SM_brick_wall_04"]),
        ("brokenblock", ["SM_block_broken_01"]), ("brokenblock", ["SM_block_broken_03"]),
    ]),
    "trashprops": ("F:/FabAssets/trashprops.blend", "Assets/ThirdParty/Fab/TrashProps", [
        ("dumpster", ["SM_Dumpster"]),
        ("skip", ["SM_Skip", "SM_SkipTrash"]),
        ("trashcan", ["SM_MetalTrashCan", "SM_MetalTrashCanLid"]), ("trashcan", ["SM_BoulevardBin"]),
        ("litter", ["SM_DrinkCan_1"]), ("litter", ["SM_DrinkCan_5"]), ("litter", ["SM_BeerBottle"]),
        ("litter", ["SM_Bottle_01"]), ("litter", ["SM_CoffeeCup01"]), ("litter", ["SM_SodaCup01"]),
        ("litter", ["SM_PizzaBox03"]), ("litter", ["SM_PizzaBox01"]), ("litter", ["SM_Cardboard_Open_2"]),
        ("litter", ["SM_Cardboard_Closed_2"]), ("litter", ["SM_EdgeTrashDebris_01"]),
        ("couch", ["SM_Couch"]), ("oldtv", ["SM_OldTV"]),
    ]),
    "switches_pack": ("F:/FabAssets/switches_pack.blend", "Assets/ThirdParty/Fab/SwitchesPack", [
        ("switchlever", ["SM_LeverSwitch_Body", "SM_LeverSwitch_Handle"]),
        ("switchlever", ["SM_KnifeSwitch_Body", "SM_KnifeSwitch_Handle"]),
        ("switchbutton", ["SM_CoveredButton_Body", "SM_CoveredButton_Button", "SM_CoveredButton_Cover"]),
        ("switchbutton", ["SM_GuardedButton_Body", "SM_GuardedButton_Button"]),
        ("switchbutton", ["SM_Hoist_Controller_Body", "SM_Hoist_Controller_Button_01", "SM_Hoist_Controller_Button_02",
                          "SM_Hoist_Controller_Button_03"]),
    ]),
    "Fence-Barricade_02": (S + "/extract/fence02/source/Fence-Barricade_02.blend", "Assets/ThirdParty/Fab/FenceBarricades/Barricade02", [
        ("plasticbarrier", ["Fence-Barricade_02"])]),
    "Fence-Barricade_03": (S + "/extract/fence03/source/Fence-Barricade_03.blend", "Assets/ThirdParty/Fab/FenceBarricades/Barricade03", [
        ("plasticbarrier", ["Fence-Barricade_03"])]),
}
# Dekogon prefabs by name (both volumes) and their kinds
DEKOGON = {
    "bricks": ["SM_BrickPaletteStack_01a", "SM_BrickPaletteStack_01c", "SM_BrickPaletteStack_01e", "SM_CinderStack_01a",
               "SM_CinderStack_Pallete_01a"],
    "cementbags": ["SM_ConcreteBag_01a", "SM_ConcreteBag_03a"],
    "concretepipe": ["SM_ConcretePipe_01a", "SM_ConcretePipe_02a", "SM_ConcretePipe_03a"],
    "constructionfence": ["SM_construction_fence_01a", "SM_construction_fence_01b"],
    "roadblock": ["SM_Roadblock_01a", "SM_Roadblock_02a", "SM_Roadblock_03a", "SM_Roadblock_04a", "SM_Trafficbarrier_01a"],
    "pylon": ["SM_ConstructionPylons_01a", "SM_ConstructionPylons_01c"],
    "portapotty": ["SM_Portapotty_01a"],
    "generator": ["SM_LightGenerator_01a"],
    "watertankportable": ["SM_PortableWaterTank_01a"],
    "concretemixer": ["SM_ConcreteMixer_01a"],
    "tarp": ["SM_Tarp_01a", "SM_Tarp_01c"],
    "container": ["SM_FreightContainer_01a"],
    "plywood": ["SM_Plywood_01a", "SM_Plywood_03a"],
    "woodpallet": ["SM_woodenpalette_01a"],
    "oilbarrel": ["SM_OilBarrel_01a", "SM_PlasticBarrel_01a"],
    "dumpster": ["SM_Dumpster_01a"],
    "crate": ["SM_Crate_01a", "SM_Crate_01b"],
    "wheelbarrow": ["SM_WheelBarrel_01a"],
    "ladder": ["SM_Ladder_01a"],
    "propane": ["SM_propanetank_01a"],
}

ROLE = [("color", r"(basecolor|albedo|diffuse)"), ("normal", r"normal"), ("rough", r"roughness"), ("gloss", r"gloss"),
        ("metal", r"metal(ness|lic)"), ("ao", r"(_ao|ambientocclusion)"), ("opacity", r"opacity")]


def roles(files):
    found = {}
    for f in sorted(files):
        low = os.path.basename(f).lower()
        if not low.endswith((".jpg", ".jpeg", ".png", ".tif", ".exr")):
            continue
        for role, pattern in ROLE:
            if role not in found and re.search(pattern, low):
                found[role] = f
                break
    return found


job = {"materials": {}, "props": [], "blend": [], "kinds": {}}
problems = []


def kind_add(kind, path):
    job["kinds"].setdefault(kind, []).append(path)


for kind, pack, ident, name in QUIXEL:
    folder = os.path.join(QDIRS[pack], ident)
    if not os.path.isdir(folder):
        problems.append("missing folder %s/%s" % (pack, ident))
        continue
    files = os.listdir(folder)
    fbxs = sorted(f for f in files if f.lower().endswith(".fbx"))
    fbx = next((f for f in fbxs if os.path.splitext(f)[0].lower() == ident.lower()), fbxs[0] if fbxs else None)
    if fbx is None:
        problems.append("no fbx %s/%s" % (pack, ident))
        continue
    stem = "%s_%s" % (name, ident)
    dest = "Assets/ThirdParty/Quixel/%s/%s" % (pack, stem)
    tex = roles([os.path.join(folder, f) for f in files])
    if "color" not in tex or "normal" not in tex:
        problems.append("textures %s/%s: %s" % (pack, ident, sorted(tex)))
    job["materials"][stem] = dict(dest=dest, name=stem, flip=False, **tex)
    job["props"].append({"src": os.path.join(folder, fbx), "dest": dest, "file": stem + ".fbx", "remap": {"*": stem}, "axis": 0})
    kind_add(kind, dest + "/" + stem + ".fbx")

singles_dir = os.path.join(S, "extract", "singles")
for kind, zname, name, texzip in SINGLES:
    for z in {zname, texzip or zname}:
        target = os.path.join(singles_dir, os.path.splitext(z)[0])
        if not os.path.isdir(target):
            with zipfile.ZipFile(os.path.join(FAB, z)) as archive:
                archive.extractall(target)
    model_dir = os.path.join(singles_dir, os.path.splitext(zname)[0])
    tex_dir = os.path.join(singles_dir, os.path.splitext(texzip or zname)[0])
    fbx = sorted(f for f in os.listdir(model_dir) if f.lower().endswith(".fbx"))[0]
    dest = "Assets/ThirdParty/Quixel/Singles/%s" % name
    tex = roles([os.path.join(tex_dir, f) for f in os.listdir(tex_dir)])
    job["materials"][name] = dict(dest=dest, name=name, flip=False, **tex)
    job["props"].append({"src": os.path.join(model_dir, fbx), "dest": dest, "file": name + ".fbx", "remap": {"*": name}, "axis": 0})
    kind_add(kind, dest + "/" + name + ".fbx")

# Unreal: mesh -> slots -> material instance -> texture parameters (parent defaults underneath) -> exported PNGs.
COLOR = ["Albedo", "Albedo ", "Base Colour", "baseColor", "BaseColor", "Base Color", "Diffuse", "Albedo Texture",
         "DiffuseSkin1"]
# Texture names written inside a material's T3D (materials that wire textures in instead of exposing parameters).
T3D_REF = re.compile(r"Texture2D'\"?(/Game/[^'\"]+)")
SUFFIX = [("color", r"_(bc|d|diff|albedo|basecolou?r)(_\d+a)?$"), ("normal", r"_(n|nrm|normal)(_\d+a)?$"),
          ("packed", r"_(rma|orm|arm|aomr|omr)(_\d+a)?$"), ("opacity", r"_(op|opc|opacity)(_\d+a)?$")]
NORMAL = ["Normal", "normal", "Normals", "Normal Map"]
manifests = {p: json.load(open("%s/FromUnreal/%s/manifest.json" % (FAB, p), encoding="utf-8")) for p in UE_DEST}


def ue_file(pack, object_path):
    if not object_path:
        return None
    package = object_path.split(".")[0]
    entry = manifests[pack]["assets"].get(package)
    if not entry or not entry.get("ok") or not entry.get("file"):
        return None
    return "%s/FromUnreal/%s/%s" % (FAB, pack, entry["file"])


def ue_material(pack, object_path):
    assets = manifests[pack]["assets"]
    package = object_path.split(".")[0]
    entry = assets.get(package, {})
    params = {}
    chain = [entry]
    parent = entry.get("parent")
    while parent:
        p = assets.get(parent.split(".")[0], {})
        chain.append(p)
        parent = p.get("parent")
    for e in reversed(chain):
        params.update({k: v for k, v in (e.get("textures") or {}).items() if v})
    used = [t for e in chain for t in (e.get("used_textures") or []) if t]

    def first(keys, hints):
        for k in keys:
            if params.get(k):
                return params[k]
        for t in used:
            name = t.split(".")[-1].lower()
            if any(re.search(h, name) for h in hints):
                return t
        return None
    mat = {"flip": True}
    mat["color"] = ue_file(pack, first(COLOR, [r"_(d|bc|albedo|basecolor|diffuse)$", r"albedo", r"diffuse", r"basecolo"]))
    mat["normal"] = ue_file(pack, first(NORMAL, [r"_n$", r"normal"]))
    for key, layout in (("ARM", "ARM"), ("RMA", "RMA"), ("ORM", "ORM"), ("ORM ", "ORM")):
        if params.get(key):
            mat["packed"] = {"path": ue_file(pack, params[key]), "layout": layout}
            break
    else:
        packed = next((t for t in used if re.search(r"_(arm|rma|orm|mra)$", t.split(".")[-1].lower())), None)
        if packed:
            mat["packed"] = {"path": ue_file(pack, packed), "layout": packed.split(".")[-1][-3:].upper()}
    rough = first(["Roughness", "roughness"], [r"_r$", r"rough"])
    if rough and "packed" not in mat:
        mat["rough"] = ue_file(pack, rough)
    metal = first(["metallic", "Metallic", "Metalness"], [r"_m$", r"metal"])
    if metal and "packed" not in mat:
        mat["metal"] = ue_file(pack, metal)
    ao = first(["ambientOcclusion", "AO"], [r"_ao$", r"occlusion"])
    if ao and "packed" not in mat:
        mat["ao"] = ue_file(pack, ao)
    # Whatever is still missing: the textures named in the T3D text of the material and its parents.
    refs = []
    for e in chain:
        t3d = "%s/FromUnreal/%s/%s" % (FAB, pack, e.get("file", "")) if e.get("file") else None
        if t3d and os.path.exists(t3d):
            refs += T3D_REF.findall(open(t3d, encoding="utf-8", errors="replace").read())
    for ref in refs:
        name = ref.split(".")[-1].lower()
        for role, pattern in SUFFIX:
            if re.search(pattern, name) and not mat.get(role):
                path = ue_file(pack, ref)
                if role == "packed":
                    tag = re.search(pattern, name).group(1).upper()
                    # AOMR and OMR are the same order: occlusion, metal, roughness.
                    mat[role] = {"path": path, "layout": "OMR" if tag in ("AOMR", "OMR") else tag}
                else:
                    mat[role] = path
                break
    if isinstance(mat.get("packed"), dict) and not mat["packed"].get("path"):
        del mat["packed"]
    return {k: v for k, v in mat.items() if v}


for kind, pack, asset in UNREAL:
    assets = manifests[pack]["assets"]
    package = next((p for p in assets if p.endswith("/" + asset) and assets[p]["class"] in ("StaticMesh", "SkeletalMesh")), None)
    if package is None:
        problems.append("unreal mesh not found: %s %s" % (pack, asset))
        continue
    entry = assets[package]
    dest = "%s/%s" % (UE_DEST[pack], asset)
    remap = {}
    for slot in entry.get("slots", []):
        mi = slot.get("material")
        if not mi:
            continue
        mi_name = mi.split(".")[-1]
        mat_id = "%s/%s" % (pack, mi_name)
        if mat_id not in job["materials"]:
            m = ue_material(pack, mi)
            if "color" not in m:
                problems.append("unreal material without colour: %s %s" % (pack, mi_name))
            m.update(dest=UE_DEST[pack] + "/Materials", name=mi_name)
            job["materials"][mat_id] = m
        remap[mi_name] = mat_id
    prop = {"src": "%s/FromUnreal/%s/%s" % (FAB, pack, entry["file"]), "dest": dest, "file": asset + ".fbx", "remap": remap,
            "axis": 1, "skeletal": entry["class"] == "SkeletalMesh"}
    job["props"].append(prop)
    kind_add(kind, dest + "/" + asset + ".fbx")

reports = {n: json.load(open(os.path.join(S, "blend_reports", n + ".json"), encoding="utf-8")) for n in BLEND}
for report, (path, dest, groups) in BLEND.items():
    names = {o["name"] for o in reports[report]["objects"]}
    exports = []
    for kind, objects in groups:
        missing = [o for o in objects if o not in names]
        if missing:
            problems.append("blend %s missing %s" % (report, missing))
            continue
        model = objects[0][3:] if objects[0].startswith("SM_") else objects[0]
        exports.append({"objects": objects, "dest": dest + "/" + model, "file": model + ".fbx"})
        kind_add(kind, dest + "/" + model + "/" + model + ".fbx")
    job["blend"].append({"file": path, "materials": dest + "/Materials", "exports": exports})

# Dekogon: select lists for the package importer and the prefab paths each kind gets.
dek_paths = {}
for e in json.load(open(os.path.join(S, "catalog.json"), encoding="utf-8")):
    if e["source"].startswith("Dekogon"):
        dek_paths[e["id"]] = (e["source"], e["path"])
selects = {}
for kind, prefabs in DEKOGON.items():
    for prefab in prefabs:
        if prefab not in dek_paths:
            problems.append("dekogon prefab missing: " + prefab)
            continue
        source, path = dek_paths[prefab]
        selects.setdefault(source, []).append("^" + re.escape(path) + "$")
        kind_add(kind, "ThirdParty/Dekogon/" + path[len("Assets/"):])
for source, patterns in selects.items():
    with open(os.path.join(OUT, "select_%s.json" % re.sub(r"\W+", "_", source)), "w", encoding="utf-8") as f:
        json.dump(patterns, f, indent=0)

# Kinds are written relative to Assets/ (MapArt's convention).
job["kinds"] = {k: [p[len("Assets/"):] if p.startswith("Assets/") else p for p in v] for k, v in job["kinds"].items()}
with open(os.path.join(OUT, "job.json"), "w", encoding="utf-8") as f:
    json.dump(job, f, ensure_ascii=False, indent=1)
print("materials %d, copied props %d, blend files %d (%d exports), kinds %d" % (
    len(job["materials"]), len(job["props"]), len(job["blend"]), sum(len(b["exports"]) for b in job["blend"]), len(job["kinds"])))
print("problems:", json.dumps(problems, ensure_ascii=False, indent=0) if problems else "none")
for mat_id, m in job["materials"].items():
    if "/" in mat_id:
        print("  UE", mat_id, {k: (os.path.basename(v) if isinstance(v, str) else (os.path.basename(v["path"]) + ":" + v["layout"])) for k, v in m.items() if k in ("color", "normal", "packed", "rough", "metal", "ao")})
