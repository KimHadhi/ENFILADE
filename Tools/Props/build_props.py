# Run inside Blender: turns the chosen props (props/out/job.json) into Unity assets in the project, ready to import:
# the model (copied FBX, or exported from a .blend / an Unreal skeletal mesh baked to a static one), its textures
# (Unity's layout: base colour, OpenGL normal, metallic + smoothness in one map, occlusion), a URP Lit material and
# every .meta (fixed GUIDs from the asset path, the model's materials remapped by name to ours).
# Usage: blender -b [file.blend] --factory-startup --disable-autoexec --python build_props.py -- <mode> <job.json> <project> [blend index]
#   mode copy:     textures + materials of job["materials"], copied FBX props (not skeletal)
#   mode skeletal: Unreal skeletal props -> static FBX
#   mode blend:    job["blend"][index] (the .blend is the open file): exports + its own materials
import hashlib
import json
import os
import re
import shutil
import struct
import sys

import bpy
import numpy as np
from mathutils import Vector

ARGS = sys.argv[sys.argv.index("--") + 1:]
MODE, JOB, PROJECT = ARGS[0], json.load(open(ARGS[1], encoding="utf-8")), ARGS[2]
LARGEST, MASK_SIZE = 2048, 1024
LOG = []


def note(message):
    LOG.append(message)
    print("[props]", message)


def guid(asset_path):
    return hashlib.md5(("enfilade-props:" + asset_path).encode("utf-8")).hexdigest()


def full(asset_path):
    path = os.path.join(PROJECT, asset_path.replace("/", os.sep))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    return path


# ---------------------------------------------------------------- images

def load(path):
    image = bpy.data.images.load(path, check_existing=False)
    image.colorspace_settings.name = "Non-Color"
    return image


def pixels(image, largest=LARGEST):
    w, h = image.size
    if max(w, h) > largest:
        k = largest / max(w, h)
        image.scale(max(1, int(w * k)), max(1, int(h * k)))
        w, h = image.size
    buffer = np.empty(w * h * 4, dtype=np.float32)
    image.pixels.foreach_get(buffer)
    return buffer.reshape(h, w, 4)


def read(source, largest=LARGEST):
    """Pixels of a file path or a Blender image, at most `largest` on the long side."""
    # A Blender image is read through a copy so shrinking it here doesn't shrink it for the next material.
    image = load(source) if isinstance(source, str) else source.copy()
    try:
        return pixels(image, largest)
    finally:
        bpy.data.images.remove(image)


def save(array, asset_path, kind):
    h, w = array.shape[:2]
    image = bpy.data.images.new("out", w, h, alpha=True, float_buffer=False)
    image.colorspace_settings.name = "Non-Color"
    image.pixels.foreach_set(np.clip(array, 0, 1).astype(np.float32).ravel())
    image.filepath_raw = full(asset_path)
    image.file_format = "JPEG" if asset_path.lower().endswith(".jpg") else "PNG"
    try:
        image.save(quality=92)
    except TypeError:
        image.save()
    bpy.data.images.remove(image)
    write_texture_meta(asset_path, kind)


def resize_to(array, h, w):
    if array.shape[0] == h and array.shape[1] == w:
        return array
    ys = (np.arange(h) * array.shape[0] / h).astype(int)
    xs = (np.arange(w) * array.shape[1] / w).astype(int)
    return array[ys][:, xs]


def size_of(path):
    with open(path, "rb") as f:
        head = f.read(262144)
    if head[:8] == b"\x89PNG\r\n\x1a\n":
        return struct.unpack(">II", head[16:24])
    if head[:2] == b"\xff\xd8":
        i = 2
        while i + 9 < len(head):
            if head[i] != 0xFF:
                i += 1
                continue
            if head[i + 1] in (0xC0, 0xC1, 0xC2):
                h, w = struct.unpack(">HH", head[i + 5:i + 9])
                return w, h
            i += 2 + struct.unpack(">H", head[i + 2:i + 4])[0]
    return None


TEXTURE_META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 11
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: {srgb}
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: {coverage}
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 0
    wrapV: 0
    wrapW: 0
  nPOTScale: 1
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: {transparency}
  spriteTessellationDetail: -1
  textureType: {type}
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
  spritePackingTag:
  pSDRemoveMatte: 0
  pSDShowRemoveMatteOption: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def write_texture_meta(asset_path, kind):
    """kind: color, cutout (colour with alpha), normal, data (linear masks)."""
    text = TEXTURE_META.format(guid=guid(asset_path), srgb=1 if kind in ("color", "cutout") else 0,
                               coverage=1 if kind == "cutout" else 0, transparency=1 if kind == "cutout" else 0,
                               type=1 if kind == "normal" else 0)
    with open(full(asset_path) + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def copy_texture(source, asset_path, kind):
    shutil.copyfile(source, full(asset_path))
    write_texture_meta(asset_path, kind)


# ---------------------------------------------------------------- materials

MATERIAL = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &-159612351583568813
MonoBehaviour:
  m_ObjectHideFlags: 11
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: d0353a89b1f911e48b9e16bdc9f2e058, type: 3}}
  m_Name:
  m_EditorClassIdentifier:
  version: 10
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {name}
  m_Shader: {{fileID: 4800000, guid: 933532a4fcc9baf4fa0491de14d08ed7, type: 3}}
  m_Parent: {{fileID: 0}}
  m_ModifiedSerializedProperties: 0
  m_ValidKeywords:{keywords}
  m_InvalidKeywords: []
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 1
  m_DoubleSidedGI: {double}
  m_CustomRenderQueue: {queue}
  stringTagMap:
    RenderType: {render_type}
  disabledShaderPasses:
  - MOTIONVECTORS
  m_LockedProperties:
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
{textures}
    m_Ints: []
    m_Floats:
    - _AddPrecomputedVelocity: 0
    - _AlphaClip: {clip}
    - _AlphaToMask: 0
    - _Blend: 0
    - _BlendModePreserveSpecular: 1
    - _BumpScale: 1
    - _ClearCoatMask: 0
    - _ClearCoatSmoothness: 0
    - _Cull: {cull}
    - _Cutoff: 0.5
    - _DetailAlbedoMapScale: 1
    - _DetailNormalMapScale: 1
    - _DstBlend: 0
    - _DstBlendAlpha: 0
    - _EnvironmentReflections: 1
    - _GlossMapScale: 1
    - _Glossiness: 0.5
    - _GlossyReflections: 1
    - _Metallic: 0
    - _OcclusionStrength: 1
    - _Parallax: 0.02
    - _QueueOffset: 0
    - _ReceiveShadows: 1
    - _Smoothness: {smoothness}
    - _SmoothnessTextureChannel: 0
    - _SpecularHighlights: 1
    - _SrcBlend: 1
    - _SrcBlendAlpha: 1
    - _Surface: 0
    - _WorkflowMode: 1
    - _ZWrite: 1
    m_Colors:
    - _BaseColor: {{r: {grey}, g: {grey}, b: {grey}, a: 1}}
    - _Color: {{r: {grey}, g: {grey}, b: {grey}, a: 1}}
    - _EmissionColor: {{r: 0, g: 0, b: 0, a: 1}}
    - _SpecColor: {{r: 0.19999996, g: 0.19999996, b: 0.19999996, a: 1}}
  m_BuildTextureStacks: []
  m_AllowLocking: 1
"""
SLOTS = ["_BaseMap", "_BumpMap", "_DetailAlbedoMap", "_DetailMask", "_DetailNormalMap", "_EmissionMap", "_MainTex",
         "_MetallicGlossMap", "_OcclusionMap", "_ParallaxMap", "_SpecGlossMap", "unity_Lightmaps", "unity_LightmapsInd",
         "unity_ShadowMasks"]


def write_material(asset_path, name, maps, clip, two_sided):
    """maps: slot -> texture asset path."""
    lines = []
    for slot in SLOTS:
        source = maps.get("_BaseMap") if slot == "_MainTex" else maps.get(slot)
        ref = "{fileID: 2800000, guid: %s, type: 3}" % guid(source) if source else "{fileID: 0}"
        lines += ["    - %s:" % slot, "        m_Texture: %s" % ref, "        m_Scale: {x: 1, y: 1}",
                  "        m_Offset: {x: 0, y: 0}"]
    keywords = []
    if maps.get("_BumpMap"):
        keywords.append("_NORMALMAP")
    if maps.get("_MetallicGlossMap"):
        keywords.append("_METALLICSPECGLOSSMAP")
    if maps.get("_OcclusionMap"):
        keywords.append("_OCCLUSIONMAP")
    if clip:
        keywords.append("_ALPHATEST_ON")
    text = MATERIAL.format(
        name=name, keywords="".join("\n  - " + k for k in sorted(keywords)) if keywords else " []",
        double=1 if two_sided else 0, queue=2450 if clip else -1, render_type="TransparentCutout" if clip else "Opaque",
        textures="\n".join(lines), clip=1 if clip else 0, cull=0 if two_sided else 2,
        smoothness=1 if maps.get("_MetallicGlossMap") else 0.35, grey=1 if maps.get("_BaseMap") else 0.5)
    with open(full(asset_path), "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    with open(full(asset_path) + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write("fileFormatVersion: 2\nguid: %s\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n"
                "  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % guid(asset_path))


CHANNELS = {"ORM": (0, 1, 2), "ARM": (0, 1, 2), "RMA": (2, 0, 1), "OMR": (0, 2, 1)}  # (ao, rough, metal)


def build_material(mat_id, m):
    """m: dest, name, color, normal, rough, gloss, metal, ao, packed{path,layout}, opacity, flip -> material asset path.
    Sources are file paths, or Blender images (blend mode)."""
    base = "%s/%s" % (m["dest"], safe(m["name"]))
    maps = {}
    color, opacity = m.get("color"), m.get("opacity")
    if color is not None:
        if opacity is not None:
            rgb = read(color)
            alpha = resize_to(read(opacity), rgb.shape[0], rgb.shape[1])
            rgb[..., 3] = alpha[..., 0]
            path = base + "_BaseColor.png"
            save(rgb, path, "cutout")
        elif isinstance(color, str) and (size_of(color) or (99999, 0))[0] <= LARGEST and color.lower().endswith((".jpg", ".png")):
            path = base + "_BaseColor" + os.path.splitext(color)[1].lower()
            copy_texture(color, path, "color")
        else:
            path = base + "_BaseColor.jpg"
            save(read(color), path, "color")
        maps["_BaseMap"] = path
    normal = m.get("normal")
    if normal is not None:
        if m.get("flip") or not isinstance(normal, str) or (size_of(normal) or (99999, 0))[0] > LARGEST:
            n = read(normal)
            if m.get("flip"):
                n[..., 1] = 1 - n[..., 1]
            n[..., 3] = 1
            path = base + "_Normal.png"
            save(n, path, "normal")
        else:
            path = base + "_Normal" + os.path.splitext(normal)[1].lower()
            copy_texture(normal, path, "normal")
        maps["_BumpMap"] = path
    packed = m.get("packed")
    packed_px = read(packed["path"], MASK_SIZE) if packed else None
    ao_index = rough_index = metal_index = None
    if packed is not None:
        ao_index, rough_index, metal_index = CHANNELS.get(packed["layout"], CHANNELS["ORM"])
    smooth = None
    if m.get("gloss") is not None:
        smooth = read(m["gloss"], MASK_SIZE)[..., 0]
    elif m.get("rough") is not None:
        smooth = 1 - read(m["rough"], MASK_SIZE)[..., 0]
    elif packed_px is not None:
        smooth = 1 - packed_px[..., rough_index]
    metal = None
    if m.get("metal") is not None:
        metal = read(m["metal"], MASK_SIZE)[..., 0]
    elif packed_px is not None:
        metal = packed_px[..., metal_index]
    if smooth is not None:
        h, w = smooth.shape
        ms = np.zeros((h, w, 4), dtype=np.float32)
        if metal is not None:
            ms[..., 0] = resize_to(metal[..., None], h, w)[..., 0]
        ms[..., 3] = smooth
        path = base + "_MetallicSmoothness.png"
        save(ms, path, "data")
        maps["_MetallicGlossMap"] = path
    ao = m.get("ao")
    if ao is not None:
        if isinstance(ao, str) and (size_of(ao) or (99999, 0))[0] <= LARGEST and ao.lower().endswith((".jpg", ".png")):
            path = base + "_AO" + os.path.splitext(ao)[1].lower()
            copy_texture(ao, path, "data")
        else:
            a = read(ao, MASK_SIZE)
            a[..., 1] = a[..., 2] = a[..., 0]
            a[..., 3] = 1
            path = base + "_AO.png"
            save(a, path, "data")
        maps["_OcclusionMap"] = path
    elif packed_px is not None:
        a = np.repeat(packed_px[..., ao_index:ao_index + 1], 4, axis=2)
        a[..., 3] = 1
        path = base + "_AO.png"
        save(a, path, "data")
        maps["_OcclusionMap"] = path
    clip = opacity is not None
    material = base + ".mat"
    write_material(material, safe(m["name"]), maps, clip, clip or m.get("twoSided", False))
    note("material %s: %s" % (material, ", ".join(sorted(k.strip("_") for k in maps))))
    return material


def safe(name):
    return re.sub(r"[^A-Za-z0-9_\-]+", "_", name).strip("_")


# ---------------------------------------------------------------- models

MODEL_META = """fileFormatVersion: 2
guid: {guid}
ModelImporter:
  serializedVersion: 21100
  internalIDToNameTable: []
  externalObjects:{remaps}
  materials:
    materialImportMode: 2
    materialName: 0
    materialSearch: 1
    materialLocation: 1
  animations:
    legacyGenerateAnimations: 4
    bakeSimulation: 0
    resampleCurves: 1
    optimizeGameObjects: 0
    motionNodeName:
    rigImportErrors:
    rigImportWarnings:
    animationImportErrors:
    animationImportWarnings:
    animationRetargetingWarnings:
    animationDoRetargetingWarnings: 0
    importAnimatedCustomProperties: 0
    importConstraints: 0
    animationCompression: 1
    animationRotationError: 0.5
    animationPositionError: 0.5
    animationScaleError: 0.5
    animationWrapMode: 0
    extraExposedTransformPaths: []
    extraUserProperties: []
    clipAnimations: []
    isReadable: 0
  meshes:
    lODScreenPercentages: []
    globalScale: 1
    meshCompression: 0
    addColliders: 0
    useSRGBMaterialColor: 1
    sortHierarchyByName: 1
    importVisibility: 0
    importBlendShapes: 0
    importCameras: 0
    importLights: 0
    fileIdsGeneration: 2
    swapUVChannels: 0
    generateSecondaryUV: 0
    useFileUnits: 1
    keepQuads: 0
    weldVertices: 1
    bakeAxisConversion: {axis}
    preserveHierarchy: 0
    skinWeightsMode: 0
    maxBonesPerVertex: 4
    minBoneWeight: 0.001
    meshOptimizationFlags: -1
    indexFormat: 0
    secondaryUVAngleDistortion: 8
    secondaryUVAreaDistortion: 15.000001
    secondaryUVHardAngle: 88
    secondaryUVMarginMethod: 1
    secondaryUVMinLightmapResolution: 40
    secondaryUVMinObjectScale: 1
    secondaryUVPackMargin: 4
    useFileScale: 1
  tangentSpace:
    normalSmoothAngle: 60
    normalImportMode: 0
    tangentImportMode: 3
    normalCalculationMode: 4
    legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes: 0
    blendShapeNormalImportMode: 1
    normalSmoothingSource: 0
  referencedClips: []
  importAnimation: 0
  humanDescription:
    serializedVersion: 3
    human: []
    skeleton: []
    armTwist: 0.5
    foreArmTwist: 0.5
    upperLegTwist: 0.5
    legTwist: 0.5
    armStretch: 0.05
    legStretch: 0.05
    feetSpacing: 0
    globalScale: 1
    rootMotionBoneName:
    hasTranslationDoF: 0
    hasExtraRoot: 0
    skeletonHasParents: 1
  lastHumanDescriptionAvatarSource: {{instanceID: 0}}
  autoGenerateAvatarMappingIfUnspecified: 1
  animationType: 0
  humanoidOversampling: 1
  avatarSetup: 0
  addHumanoidExtraRootOnlyWhenUsingAvatar: 1
  additionalBone: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def fbx_materials(path):
    """Material names in a binary FBX (Objects > Material)."""
    with open(path, "rb") as f:
        data = f.read()
    if not data.startswith(b"Kaydara FBX Binary"):
        return []
    wide = struct.unpack_from("<I", data, 23)[0] >= 7500
    head, size = ("<QQQ", 24) if wide else ("<III", 12)
    names = []

    def walk(pos, end):
        while pos + size < end:
            end_offset, count, plen = struct.unpack_from(head, data, pos)
            if end_offset == 0:
                return
            name_len = data[pos + size]
            name = data[pos + size + 1:pos + size + 1 + name_len]
            start = pos + size + 1 + name_len
            if name == b"Material" and count >= 2 and data[start] == ord("L") and data[start + 9] == ord("S"):
                n = struct.unpack_from("<I", data, start + 10)[0]
                names.append(data[start + 14:start + 14 + n].split(b"\x00")[0].decode("utf-8", "replace"))
            elif name == b"Objects":
                walk(start + plen, end_offset)
            pos = end_offset

    walk(27, len(data))
    return names


def write_model_meta(asset_path, remaps, axis):
    lines = ""
    for name, material in sorted(remaps.items()):
        lines += ("\n  - first:\n      type: UnityEngine:Material\n      assembly: UnityEngine.CoreModule\n"
                  "      name: %s\n    second: {fileID: 2100000, guid: %s, type: 2}" % (name, guid(material)))
    text = MODEL_META.format(guid=guid(asset_path), remaps=lines if lines else " {}", axis=axis)
    with open(full(asset_path) + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def remap_for(names, wanted, materials):
    """FBX material name -> our material asset path, through the plan's remap (name or '*')."""
    out = {}
    for name in names:
        # Exporters add counters to repeated names (Blender ".001", Unreal "_4").
        key = name if name in wanted else re.sub(r"\.\d{3}$", "", name)
        key = key if key in wanted else re.sub(r"_\d+$", "", key)
        mat_id = wanted.get(key) or wanted.get("*")
        if mat_id is None and len(set(wanted.values())) == 1:
            mat_id = next(iter(wanted.values()))
        if mat_id is None or mat_id not in materials:
            note("  no material for '%s'" % name)
            continue
        out[name] = materials[mat_id]
    return out


def export_selected(asset_path):
    bpy.ops.export_scene.fbx(filepath=full(asset_path), use_selection=True, object_types={"MESH"},
                             use_mesh_modifiers=True, mesh_smooth_type="FACE", apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, add_leaf_bones=False, path_mode="STRIP",
                             embed_textures=False, use_custom_props=False, bake_anim=False)


def ensure_in_view(obj, layer=None):
    """Un-excludes and unhides every view-layer collection holding the object, so it can be selected."""
    layer = layer or bpy.context.view_layer.layer_collection
    found = obj.name in layer.collection.objects
    for child in layer.children:
        found = ensure_in_view(obj, child) or found
    if found:
        layer.exclude = False
        layer.hide_viewport = False
        layer.collection.hide_viewport = False
        layer.collection.hide_select = False
    return found


def select_only(objects):
    for obj in objects:
        ensure_in_view(obj)
    bpy.context.view_layer.update()
    for obj in bpy.context.view_layer.objects:
        obj.select_set(False)
    for obj in objects:
        obj.hide_set(False)
        obj.hide_viewport = False
        obj.hide_select = False
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def centre_on_origin(objects):
    """Moves the group so its footprint centre sits on the origin and its lowest point on the floor; returns the undo."""
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in objects:
        for corner in obj.bound_box:
            p = obj.matrix_world @ Vector(corner)
            lo = Vector(map(min, lo, p))
            hi = Vector(map(max, hi, p))
    offset = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    tops = [o for o in objects if o.parent not in objects]
    saved = [(o, o.matrix_world.copy()) for o in tops]
    for obj in tops:
        obj.matrix_world.translation -= offset
    bpy.context.view_layer.update()
    return saved, hi - lo


# ---------------------------------------------------------------- modes

if MODE == "copy":
    materials_file = os.path.join(os.path.dirname(ARGS[1]), "materials.json")
    if len(ARGS) > 3 and ARGS[3] == "models-only":
        materials = json.load(open(materials_file, encoding="utf-8"))
    else:
        materials = {}
        for mat_id, m in JOB["materials"].items():
            try:
                materials[mat_id] = build_material(mat_id, m)
            except Exception as error:
                note("MATERIAL FAIL %s: %r" % (mat_id, error))
        with open(materials_file, "w", encoding="utf-8") as f:
            json.dump(materials, f, indent=0)
    for prop in JOB["props"]:
        if prop.get("skeletal"):
            continue
        asset = prop["dest"] + "/" + prop["file"]
        shutil.copyfile(prop["src"], full(asset))
        remaps = remap_for(fbx_materials(prop["src"]), prop["remap"], materials)
        write_model_meta(asset, remaps, prop.get("axis", 0))
        note("model %s (%d materials)" % (asset, len(remaps)))

elif MODE == "skeletal":
    materials = json.load(open(os.path.join(os.path.dirname(ARGS[1]), "materials.json"), encoding="utf-8"))
    for prop in JOB["props"]:
        if not prop.get("skeletal"):
            continue
        for obj in list(bpy.data.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.ops.import_scene.fbx(filepath=prop["src"], use_anim=False, ignore_leaf_bones=True)
        meshes = [o for o in bpy.data.objects if o.type == "MESH"]
        for obj in meshes:
            for modifier in list(obj.modifiers):
                if modifier.type == "ARMATURE":
                    with bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj]):
                        bpy.ops.object.modifier_apply(modifier=modifier.name)
            if obj.parent and obj.parent.type == "ARMATURE":
                world = obj.matrix_world.copy()
                obj.parent = None
                obj.matrix_world = world
        for obj in [o for o in bpy.data.objects if o.type == "ARMATURE"]:
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.context.view_layer.update()
        select_only(meshes)
        saved, size = centre_on_origin(meshes)
        asset = prop["dest"] + "/" + prop["file"]
        export_selected(asset)
        names = sorted({s.material.name for o in meshes for s in o.material_slots if s.material})
        write_model_meta(asset, remap_for(names, prop["remap"], materials), 0)
        note("skeletal %s baked static, size %.2f x %.2f x %.2f m, materials %s" % (asset, size.x, size.y, size.z, names))

elif MODE == "blend":
    entry = JOB["blend"][int(ARGS[3])]
    materials = {}

    def trace(socket, seen=0):
        """(image, channel or None, inverted) feeding a shader input, through normal maps, separates and mixes."""
        if seen > 8 or not socket.is_linked:
            return None
        link = socket.links[0]
        node, out = link.from_node, link.from_socket.name
        if node.type == "TEX_IMAGE":
            return (node.image, None, False) if node.image else None
        if node.type in ("SEPARATE_COLOR", "SEPRGB", "SEPARATE_RGB"):
            r = trace(node.inputs[0], seen + 1)
            channel = {"Red": 0, "R": 0, "Green": 1, "G": 1, "Blue": 2, "B": 2}.get(out)
            return (r[0], channel, r[2]) if r else None
        if node.type == "INVERT":
            r = trace(node.inputs["Color"], seen + 1)
            return (r[0], r[1], not r[2]) if r else None
        if node.type == "NORMAL_MAP":
            return trace(node.inputs["Color"], seen + 1)
        for inp in node.inputs:
            if inp.is_linked and inp.type in ("RGBA", "VALUE", "VECTOR"):
                r = trace(inp, seen + 1)
                if r:
                    return r
        return None

    def channel_of(found):
        image, channel, inverted = found
        px = read(image, MASK_SIZE)[..., channel if channel is not None else 0]
        return 1 - px if inverted else px

    def blend_material(material):
        if material.name in materials:
            return materials[material.name]
        name = safe(material.name)
        base = "%s/%s" % (entry["materials"], name)
        bsdf = next((n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None) if material.use_nodes else None
        maps = {}
        if bsdf is not None:
            color = trace(bsdf.inputs["Base Color"])
            if color:
                rgb = read(color[0])
                rgb[..., 3] = 1
                save(rgb, base + "_BaseColor.jpg", "color")
                maps["_BaseMap"] = base + "_BaseColor.jpg"
            normal = trace(bsdf.inputs["Normal"])
            if normal:
                n = read(normal[0])
                n[..., 3] = 1
                save(n, base + "_Normal.png", "normal")
                maps["_BumpMap"] = base + "_Normal.png"
            rough = trace(bsdf.inputs["Roughness"])
            metal = trace(bsdf.inputs["Metallic"])
            if rough:
                smooth = 1 - channel_of(rough)
                ms = np.zeros(smooth.shape + (4,), dtype=np.float32)
                ms[..., 3] = smooth
                if metal:
                    mp = channel_of(metal)
                    ms[..., 0] = resize_to(mp[..., None], smooth.shape[0], smooth.shape[1])[..., 0]
                save(ms, base + "_MetallicSmoothness.png", "data")
                maps["_MetallicGlossMap"] = base + "_MetallicSmoothness.png"
                # A packed occlusion-roughness-metal map (roughness from its green) carries occlusion in red.
                if rough[1] == 1 and re.search(r"(occlusion|orm)", rough[0].name, re.I):
                    ao = read(rough[0], MASK_SIZE)
                    ao[..., 1] = ao[..., 2] = ao[..., 0]
                    ao[..., 3] = 1
                    save(ao, base + "_AO.png", "data")
                    maps["_OcclusionMap"] = base + "_AO.png"
        path = base + ".mat"
        write_material(path, name, maps, False, False)
        note("material %s: %s" % (path, ", ".join(sorted(k.strip("_") for k in maps))))
        materials[material.name] = path
        return path

    for export in entry["exports"]:
        objects = [bpy.data.objects[n] for n in export["objects"]]
        select_only(objects)
        saved, size = centre_on_origin(objects)
        asset = export["dest"] + "/" + export["file"]
        export_selected(asset)
        for obj, matrix in saved:
            obj.matrix_world = matrix
        remaps = {}
        for obj in objects:
            for slot in obj.material_slots:
                if slot.material is not None:
                    remaps[slot.material.name] = blend_material(slot.material)
        write_model_meta(asset, remaps, 0)
        tris = sum(len(p.vertices) - 2 for o in objects for p in o.data.polygons)
        note("model %s %.2f x %.2f x %.2f m, %d tris, materials %s" % (asset, size.x, size.y, size.z, tris, sorted(remaps)))

with open(os.path.join(os.path.dirname(ARGS[1]), "log_%s%s.txt" % (MODE, ("_" + ARGS[3]) if len(ARGS) > 3 else "")),
          "w", encoding="utf-8") as f:
    f.write("\n".join(LOG))
print("[props] done", MODE, len(LOG))
