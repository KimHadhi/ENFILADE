"""Brings a Garry's Mod addon's picture into the game for the HUD addon layer (HudAddons.Pictures): a .vtf decoded to
.png at FULL size (mip 0), or a .png copied as it is, each with a matching Unity .meta written in the same breath.

Where pictures go: Assets/_Project/Art/Resources/UI/Addons/<Module>/... (PlayerHud, HitFeedback, EnemyBars, Grenades,
DeathScreen), read at run time as HudAddons.Pictures.Get("<Module>/<name>") (a Resources path, no extension).

The .meta is a UI picture's: sRGB colour, alpha is transparency, mipmaps on with the Kaiser filter (addon art is often
1024 px drawn at 40 px, and Kaiser keeps thin lines from going soft as fast as a box filter), trilinear filtering, clamped
edges, no power-of-two rescale, the global mipmap limit ignored (the texture detail setting must not blur HUD art), high
quality compression, and a max size no smaller than the picture so nothing is cut below its source size. The .meta is
written right before its picture in the same run (an open editor that sees a picture without its .meta gives it a GUID
of its own); an existing .meta is never rewritten, so a picture already imported keeps its GUID and its references.

Formats decoded: DXT1 (with and without one-bit alpha), DXT3, DXT5, RGBA8888, ABGR8888, ARGB8888, BGRA8888, BGRX8888,
RGB888, BGR888, RGB565, BGR565, BGRA4444, BGRA5551, BGRX5551, I8, IA88, A8, UV88 (VTF 7.0-7.5; 7.3+ finds the image
through its resource list). An animated VTF gives its first frame unless --frame says another (or --frames writes all,
name_0.png, name_1.png, ...). Cube maps and volume textures are refused: no HUD draws them.

Usage:
  python -I addon_picture.py <in.vtf|in.png> <out.png> [--frame N | --frames] [--replace]
  python -I addon_picture.py <addon folder> <out folder> [--replace]
      every .vtf and .png under the folder (materials/ trees included), mirrored under the out folder, names lowercased
  python -I addon_picture.py --info <in.vtf|folder>   print size, format, frames and mips without writing anything
--replace writes the picture again over an existing one and keeps its .meta (and GUID) as it is.
Pure Python (no Pillow); a 1024 x 1024 DXT picture takes a few seconds.
"""
import os
import struct
import sys
import uuid
import zlib

# ---- VTF -----------------------------------------------------------------------------------------------------------

FORMATS = {
    0: 'RGBA8888', 1: 'ABGR8888', 2: 'RGB888', 3: 'BGR888', 4: 'RGB565', 5: 'I8', 6: 'IA88', 7: 'P8', 8: 'A8',
    9: 'RGB888_BLUESCREEN', 10: 'BGR888_BLUESCREEN', 11: 'ARGB8888', 12: 'BGRA8888', 13: 'DXT1', 14: 'DXT3', 15: 'DXT5',
    16: 'BGRX8888', 17: 'BGR565', 18: 'BGRX5551', 19: 'BGRA4444', 20: 'DXT1_ONEBITALPHA', 21: 'BGRA5551', 22: 'UV88',
}
BYTES_PER_PIXEL = {0: 4, 1: 4, 2: 3, 3: 3, 4: 2, 5: 1, 6: 2, 8: 1, 9: 3, 10: 3, 11: 4, 12: 4, 16: 4, 17: 2, 18: 2, 19: 2, 21: 2, 22: 2}
ENVMAP = 0x4000
HIGH_RES_TAG = b'\x30\x00\x00'


def image_size(fmt, w, h):
    if fmt in (13, 20):
        return max(1, (w + 3) // 4) * max(1, (h + 3) // 4) * 8
    if fmt in (14, 15):
        return max(1, (w + 3) // 4) * max(1, (h + 3) // 4) * 16
    if fmt in BYTES_PER_PIXEL:
        return w * h * BYTES_PER_PIXEL[fmt]
    raise SystemExit('VTF format %d (%s) is not decoded' % (fmt, FORMATS.get(fmt, '?')))


class Vtf:
    def __init__(self, path):
        self.path = path
        data = open(path, 'rb').read()
        if data[:4] != b'VTF\0':
            raise SystemExit('%s: not a VTF file' % path)
        self.data = data
        self.major, self.minor, self.header = struct.unpack_from('<III', data, 4)
        self.width, self.height, self.flags, self.frames, self.first_frame = struct.unpack_from('<HHIHH', data, 16)
        self.format = struct.unpack_from('<i', data, 52)[0]
        self.mips = data[56]
        self.low_format = struct.unpack_from('<i', data, 57)[0]
        self.low_w, self.low_h = data[61], data[62]
        self.depth = struct.unpack_from('<H', data, 63)[0] if (self.major, self.minor) >= (7, 2) else 1
        self.depth = max(1, self.depth)
        self.faces = 1
        if self.flags & ENVMAP:
            self.faces = 6
        self.start = self.high_res_start()

    def high_res_start(self):
        if (self.major, self.minor) >= (7, 3):
            count = struct.unpack_from('<I', self.data, 68)[0]
            for i in range(count):
                tag, _, offset = struct.unpack_from('<3sBI', self.data, 80 + 8 * i)
                if tag == HIGH_RES_TAG:
                    return offset
            raise SystemExit('%s: no high-resolution image resource' % self.path)
        low = image_size(self.low_format, self.low_w, self.low_h) if self.low_format >= 0 and self.low_w and self.low_h else 0
        return self.header + low

    def describe(self):
        return '%dx%d %s, VTF %d.%d, %d mip(s), %d frame(s)%s%s' % (
            self.width, self.height, FORMATS.get(self.format, str(self.format)), self.major, self.minor, self.mips,
            self.frames, ', cube map' if self.faces > 1 else '', ', depth %d' % self.depth if self.depth > 1 else '')

    def pixels(self, frame=0):
        """RGBA bytes of mip 0 of one frame. Mips are stored smallest first, each holding every frame, face and slice."""
        if self.faces > 1 or self.depth > 1:
            raise SystemExit('%s: cube maps and volume textures are not HUD pictures' % self.path)
        if not 0 <= frame < self.frames:
            raise SystemExit('%s: frame %d of %d' % (self.path, frame, self.frames))
        offset = self.start
        for mip in range(self.mips - 1, 0, -1):
            offset += image_size(self.format, max(1, self.width >> mip), max(1, self.height >> mip)) * self.frames
        one = image_size(self.format, self.width, self.height)
        offset += one * frame
        body = self.data[offset:offset + one]
        if len(body) != one:
            raise SystemExit('%s: file ends inside the image (%d of %d bytes)' % (self.path, len(body), one))
        return decode(self.format, body, self.width, self.height)


def c565(c):
    r, g, b = (c >> 11) & 31, (c >> 5) & 63, c & 31
    return ((r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2))


def colour_block(block, at, four_always):
    c0, c1, bits = struct.unpack_from('<HHI', block, at)
    p0, p1 = c565(c0), c565(c1)
    if four_always or c0 > c1:
        table = [p0 + (255,), p1 + (255,),
                 tuple((2 * a + b + 1) // 3 for a, b in zip(p0, p1)) + (255,),
                 tuple((a + 2 * b + 1) // 3 for a, b in zip(p0, p1)) + (255,)]
    else:
        table = [p0 + (255,), p1 + (255,), tuple((a + b) // 2 for a, b in zip(p0, p1)) + (255,), (0, 0, 0, 0)]
    return [table[(bits >> (2 * k)) & 3] for k in range(16)]


def decode_dxt(fmt, data, w, h):
    out = bytearray(w * h * 4)
    step = 8 if fmt in (13, 20) else 16
    i = 0
    for by in range(0, h, 4):
        for bx in range(0, w, 4):
            if fmt in (13, 20):
                texels = colour_block(data, i, False)
                alphas = None
            else:
                texels = colour_block(data, i + 8, True)
                if fmt == 14:
                    bits = int.from_bytes(data[i:i + 8], 'little')
                    alphas = [((bits >> (4 * k)) & 15) * 17 for k in range(16)]
                else:
                    a0, a1 = data[i], data[i + 1]
                    bits = int.from_bytes(data[i + 2:i + 8], 'little')
                    if a0 > a1:
                        table = [a0, a1] + [((7 - k) * a0 + k * a1 + 3) // 7 for k in range(1, 7)]
                    else:
                        table = [a0, a1] + [((5 - k) * a0 + k * a1 + 2) // 5 for k in range(1, 5)] + [0, 255]
                    alphas = [table[(bits >> (3 * k)) & 7] for k in range(16)]
            for k in range(16):
                x, y = bx + (k & 3), by + (k >> 2)
                if x < w and y < h:
                    r, g, b, a = texels[k]
                    if alphas is not None:
                        a = alphas[k]
                    o = (y * w + x) * 4
                    out[o] = r; out[o + 1] = g; out[o + 2] = b; out[o + 3] = a
            i += step
    return out


def decode(fmt, data, w, h):
    if fmt in (13, 14, 15, 20):
        return decode_dxt(fmt, data, w, h)
    n = w * h
    out = bytearray(n * 4)
    if fmt == 0:
        out[:] = data
    elif fmt in (12, 16):
        out[0::4], out[1::4], out[2::4] = data[2::4], data[1::4], data[0::4]
        out[3::4] = data[3::4] if fmt == 12 else bytes([255]) * n
    elif fmt == 1:
        out[0::4], out[1::4], out[2::4], out[3::4] = data[3::4], data[2::4], data[1::4], data[0::4]
    elif fmt == 11:
        out[0::4], out[1::4], out[2::4], out[3::4] = data[1::4], data[2::4], data[3::4], data[0::4]
    elif fmt in (2, 9):
        out[0::4], out[1::4], out[2::4], out[3::4] = data[0::3], data[1::3], data[2::3], bytes([255]) * n
    elif fmt in (3, 10):
        out[0::4], out[1::4], out[2::4], out[3::4] = data[2::3], data[1::3], data[0::3], bytes([255]) * n
    elif fmt == 5:
        out[0::4] = out[1::4] = out[2::4] = data; out[3::4] = bytes([255]) * n
    elif fmt == 6:
        out[0::4] = out[1::4] = out[2::4] = data[0::2]; out[3::4] = data[1::2]
    elif fmt == 8:
        out[0::4] = out[1::4] = out[2::4] = bytes([255]) * n; out[3::4] = data
    elif fmt == 22:
        out[0::4], out[1::4], out[2::4], out[3::4] = data[0::2], data[1::2], bytes(n), bytes([255]) * n
    else:
        for p in range(n):
            v = data[2 * p] | (data[2 * p + 1] << 8)
            if fmt in (4, 17):
                hi, mid, lo = (v >> 11) & 31, (v >> 5) & 63, v & 31
                first, second, third = (hi << 3) | (hi >> 2), (mid << 2) | (mid >> 4), (lo << 3) | (lo >> 2)
                r, g, b = (first, second, third) if fmt == 4 else (third, second, first)
                a = 255
            elif fmt == 19:
                b, g, r, a = (v & 15) * 17, ((v >> 4) & 15) * 17, ((v >> 8) & 15) * 17, ((v >> 12) & 15) * 17
            else:  # 18 BGRX5551, 21 BGRA5551
                b, g, r = (v & 31) * 255 // 31, ((v >> 5) & 31) * 255 // 31, ((v >> 10) & 31) * 255 // 31
                a = 255 if fmt == 18 or v >> 15 else 0
            out[4 * p:4 * p + 4] = bytes((r, g, b, a))
    return out


# ---- PNG -----------------------------------------------------------------------------------------------------------

def png_bytes(w, h, rgba):
    raw = b''.join(b'\x00' + bytes(rgba[y * w * 4:(y + 1) * w * 4]) for y in range(h))

    def chunk(tag, body):
        return struct.pack('>I', len(body)) + tag + body + struct.pack('>I', zlib.crc32(tag + body) & 0xffffffff)
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))


def png_size(data):
    if data[:8] != b'\x89PNG\r\n\x1a\n' or data[12:16] != b'IHDR':
        raise SystemExit('not a PNG file')
    return struct.unpack_from('>II', data, 16)


# ---- Unity .meta ---------------------------------------------------------------------------------------------------

FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

TEXTURE_META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 1
    enableMipMap: 1
    sRGBTexture: 1
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
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 1
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: {size}
  textureSettings:
    serializedVersion: 2
    filterMode: 2
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
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
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: {size}
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 2
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def max_size(w, h):
    """Unity's max size choices are powers of two from 32 to 16384: the smallest that holds the picture whole."""
    size = 32
    while size < max(w, h) and size < 16384:
        size *= 2
    return size


def ensure_folder(path):
    """Creates the folder and every missing parent inside Assets, each with a folder .meta."""
    path = os.path.abspath(path)
    if os.path.isdir(path):
        return
    ensure_folder(os.path.dirname(path))
    os.mkdir(path)
    if in_assets(path):
        write_new(path + '.meta', FOLDER_META.format(guid=uuid.uuid4().hex).encode('utf-8'))


def in_assets(path):
    parts = os.path.abspath(path).replace('\\', '/').split('/')
    return 'Assets' in parts[:-1]


def write_new(path, data):
    with open(path, 'xb') as f:
        f.write(data)


def write_picture(dst, w, h, data, replace):
    meta = dst + '.meta'
    if os.path.exists(dst) and not replace:
        raise SystemExit('already there (use --replace to write it again): %s' % dst)
    ensure_folder(os.path.dirname(dst))
    if in_assets(dst) and not os.path.exists(meta):
        write_new(meta, TEXTURE_META.format(guid=uuid.uuid4().hex, size=max_size(w, h)).encode('utf-8'))
    with open(dst, 'wb') as f:
        f.write(data)
    if open(dst, 'rb').read() != data:
        raise SystemExit('written bytes differ: %s' % dst)
    print('%s  %dx%d' % (dst, w, h))


def convert(src, dst, frame, all_frames, replace):
    if src.lower().endswith('.png'):
        data = open(src, 'rb').read()
        w, h = png_size(data)
        write_picture(dst, w, h, data, replace)
        return
    vtf = Vtf(src)
    frames = range(vtf.frames) if all_frames else [frame]
    for f in frames:
        out = dst if not all_frames else '%s_%d.png' % (os.path.splitext(dst)[0], f)
        write_picture(out, vtf.width, vtf.height, png_bytes(vtf.width, vtf.height, vtf.pixels(f)), replace)


def walk(folder, exts):
    for root, _, names in os.walk(folder):
        for name in sorted(names):
            if name.lower().endswith(exts):
                yield os.path.join(root, name)


def main(argv):
    if not argv:
        print(__doc__)
        return 2
    if argv[0] == '--info':
        target = argv[1]
        for path in (walk(target, ('.vtf',)) if os.path.isdir(target) else [target]):
            print('%s: %s' % (path, Vtf(path).describe()))
        return 0
    replace = '--replace' in argv
    all_frames = '--frames' in argv
    frame = 0
    if '--frame' in argv:
        frame = int(argv[argv.index('--frame') + 1])
    plain = [a for i, a in enumerate(argv) if not a.startswith('--') and not (i > 0 and argv[i - 1] == '--frame')]
    if len(plain) != 2:
        print(__doc__)
        return 2
    src, dst = plain
    if os.path.isdir(src):
        for path in walk(src, ('.vtf', '.png')):
            rel = os.path.relpath(path, src)
            out = os.path.join(dst, os.path.splitext(rel)[0].lower() + '.png')
            convert(path, out, frame, all_frames, replace)
    else:
        convert(src, dst, frame, all_frames, replace)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
