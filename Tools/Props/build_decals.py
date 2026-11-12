# Run inside Blender: the map decals of plan_decals.py's job.json as Unity assets, for URP's decal projector.
# Per set: <name>_Decal.png (colour with the opacity map in its alpha: Shader Graphs/Decal reads both from Base Map),
# <name>_Normal.png when the set has one (Megascans normals are OpenGL, as Unity's), both clamped at the edge and no
# bigger than the job says, and <name>.mat on URP's own Shader Graphs/Decal (the one the bullet holes use, so no new
# shader to compile), with every .meta (GUIDs from the asset path, as build_props.py's).
# usage: blender -b --factory-startup --disable-autoexec --python build_decals.py -- <job.json> <project or staging folder>
import hashlib
import json
import os
import sys

import bpy
import numpy as np

ARGS = sys.argv[sys.argv.index("--") + 1:]
JOB, PROJECT = json.load(open(ARGS[0], encoding="utf-8")), ARGS[1]
# URP's decal shader graph (Packages/com.unity.render-pipelines.universal/Shaders/Decal.shadergraph).
DECAL_SHADER = "{fileID: -6465566751694194690, guid: 9b4e681081e2b4c469111bb649e2f7ee, type: 3}"


def guid(asset_path):
    return hashlib.md5(("enfilade-props:" + asset_path).encode("utf-8")).hexdigest()


def full(asset_path):
    path = os.path.join(PROJECT, asset_path.replace("/", os.sep))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    return path


def read(path, largest):
    image = bpy.data.images.load(path, check_existing=False)
    image.colorspace_settings.name = "Non-Color"
    try:
        w, h = image.size
        if max(w, h) > largest:
            k = largest / max(w, h)
            image.scale(max(1, int(w * k)), max(1, int(h * k)))
            w, h = image.size
        buffer = np.empty(w * h * 4, dtype=np.float32)
        image.pixels.foreach_get(buffer)
        return buffer.reshape(h, w, 4)
    finally:
        bpy.data.images.remove(image)


def resize_to(array, h, w):
    if array.shape[0] == h and array.shape[1] == w:
        return array
    ys = (np.arange(h) * array.shape[0] / h).astype(int)
    xs = (np.arange(w) * array.shape[1] / w).astype(int)
    return array[ys][:, xs]


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
    mipMapsPreserveCoverage: 0
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
    filterMode: 2
    aniso: 4
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
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

MATERIAL = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &-5268460438910183700
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
  m_Shader: {shader}
  m_Parent: {{fileID: 0}}
  m_ModifiedSerializedProperties: 0
  m_ValidKeywords: []
  m_InvalidKeywords: []
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 1
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  stringTagMap: {{}}
  disabledShaderPasses: []
  m_LockedProperties:
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
    - Base_Map:
        m_Texture: {base}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - Normal_Map:
        m_Texture: {normal}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    m_Ints: []
    m_Floats:
    - Normal_Blend: {blend}
    - _DecalMeshBiasType: 0
    - _DecalMeshDepthBias: 0
    - _DecalMeshViewBias: 0
    - _DrawOrder: 0
    m_Colors: []
  m_BuildTextureStacks: []
  m_AllowLocking: 1
"""


def save(array, asset_path, kind):
    h, w = array.shape[:2]
    image = bpy.data.images.new("out", w, h, alpha=True, float_buffer=False)
    image.colorspace_settings.name = "Non-Color"
    image.pixels.foreach_set(np.clip(array, 0, 1).astype(np.float32).ravel())
    image.filepath_raw = full(asset_path)
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)
    meta = TEXTURE_META.format(guid=guid(asset_path), srgb=1 if kind == "decal" else 0,
                               transparency=1 if kind == "decal" else 0, type=1 if kind == "normal" else 0)
    with open(full(asset_path) + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write(meta)


def faded(alpha, mode):
    """A leak's left, right and bottom edges softened ("sides": it hangs from its top, the wall's edge, and a tileable
    one would show its cut sides), or a square patch cut into an uneven round pile ("round"). Rows start at the bottom."""
    h, w = alpha.shape
    if mode == "sides":
        x, y = np.linspace(0, 1, w)[None, :], np.linspace(0, 1, h)[:, None]
        side = np.clip(np.minimum(x, 1 - x) / .15, 0, 1)
        bottom = np.clip(y / .2, 0, 1)
        return alpha * side * side * (3 - 2 * side) * bottom * bottom * (3 - 2 * bottom)
    if mode == "round":
        x, y = np.linspace(-1, 1, w)[None, :], np.linspace(-1, 1, h)[:, None]
        angle = np.arctan2(y, x)
        r = np.sqrt(x * x + y * y) * (1 + .12 * np.sin(5 * angle) + .08 * np.sin(9 * angle + 1))
        f = np.clip((1 - r) / .45, 0, 1)
        return alpha * f * f * (3 - 2 * f)
    return alpha


def ref(asset_path):
    return "{fileID: 2800000, guid: %s, type: 3}" % guid(asset_path) if asset_path else "{fileID: 0}"


for d in JOB:
    base = d["dest"] + "/" + d["name"]
    rgba = read(d["color"], d["largest"])
    alpha = resize_to(read(d["opacity"], d["largest"]), rgba.shape[0], rgba.shape[1])
    rgba[..., 3] = faded(alpha[..., 0], d.get("fade"))
    decal = base + "_Decal.png"
    save(rgba, decal, "decal")
    normal = None
    if d.get("normal"):
        n = read(d["normal"], d["largest"])
        n[..., 3] = 1
        normal = base + "_Normal.png"
        save(n, normal, "normal")
    material = base + ".mat"
    with open(full(material), "w", encoding="utf-8", newline="\n") as f:
        f.write(MATERIAL.format(name=d["name"], shader=DECAL_SHADER, base=ref(decal), normal=ref(normal),
                                blend=d["normalBlend"] if normal else 0))
    with open(full(material) + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write("fileFormatVersion: 2\nguid: %s\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n"
                "  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % guid(material))
    print("[decals] %s %dx%d%s" % (material, rgba.shape[1], rgba.shape[0], " + normal" if normal else ""))
print("[decals] done", len(JOB))
