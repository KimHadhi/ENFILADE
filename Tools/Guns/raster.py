"""A small z-buffer rasteriser (numpy): triangles in camera space onto a picture, orthographic or perspective.
Used to look at a gun and its sights without Unity: silhouettes from the side and the top, and the view from the eye."""
import numpy as np


class Canvas:
    def __init__(self, width, height, background=(82, 87, 97)):
        self.w, self.h = width, height
        self.depth = np.full((height, width), np.inf)
        self.color = np.zeros((height, width, 3), np.uint8)
        self.color[:] = background
        self.ident = np.full((height, width), -1, np.int32)

    def image(self):
        from PIL import Image
        return Image.fromarray(self.color, "RGB")


def _raster(canvas, sx, sy, sz, tris, colors, ident):
    """sx, sy in pixels (y down), sz depth (smaller = nearer); tris (n,3) indices; colors (n,3) uint8."""
    w, h = canvas.w, canvas.h
    depth, color, idb = canvas.depth, canvas.color, canvas.ident
    ax, ay, az = sx[tris[:, 0]], sy[tris[:, 0]], sz[tris[:, 0]]
    bx, by, bz = sx[tris[:, 1]], sy[tris[:, 1]], sz[tris[:, 1]]
    cx, cy, cz = sx[tris[:, 2]], sy[tris[:, 2]], sz[tris[:, 2]]
    minx = np.floor(np.minimum(np.minimum(ax, bx), cx)).astype(int)
    maxx = np.ceil(np.maximum(np.maximum(ax, bx), cx)).astype(int)
    miny = np.floor(np.minimum(np.minimum(ay, by), cy)).astype(int)
    maxy = np.ceil(np.maximum(np.maximum(ay, by), cy)).astype(int)
    area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax)
    for i in range(len(tris)):
        x0, x1, y0, y1 = max(minx[i], 0), min(maxx[i], w - 1), max(miny[i], 0), min(maxy[i], h - 1)
        if x0 > x1 or y0 > y1 or abs(area[i]) < 1e-12:
            continue
        xs = np.arange(x0, x1 + 1) + .5
        ys = np.arange(y0, y1 + 1) + .5
        px, py = np.meshgrid(xs, ys)
        w0 = ((bx[i] - px) * (cy[i] - py) - (by[i] - py) * (cx[i] - px)) / area[i]
        w1 = ((cx[i] - px) * (ay[i] - py) - (cy[i] - py) * (ax[i] - px)) / area[i]
        w2 = 1.0 - w0 - w1
        inside = (w0 >= -1e-9) & (w1 >= -1e-9) & (w2 >= -1e-9)
        if not inside.any():
            continue
        z = w0 * az[i] + w1 * bz[i] + w2 * cz[i]
        sub = depth[y0:y1 + 1, x0:x1 + 1]
        hit = inside & (z < sub)
        if not hit.any():
            continue
        sub[hit] = z[hit]
        color[y0:y1 + 1, x0:x1 + 1][hit] = colors[i]
        idb[y0:y1 + 1, x0:x1 + 1][hit] = ident


def shade(verts, tris, base, light=(0.35, 0.8, -0.5)):
    """Flat colours per triangle: the base colour by how the face looks at the light (two-sided)."""
    a, b, c = verts[tris[:, 0]], verts[tris[:, 1]], verts[tris[:, 2]]
    n = np.cross(b - a, c - a)
    ln = np.linalg.norm(n, axis=1)
    ln[ln == 0] = 1
    n = n / ln[:, None]
    l = np.array(light, float)
    l = l / np.linalg.norm(l)
    k = .35 + .65 * np.abs(n @ l)
    return np.clip(np.array(base, float)[None, :] * k[:, None], 0, 255).astype(np.uint8)


def ortho(canvas, verts, tris, base, right, up, forward, centre, span, ident=0):
    """Orthographic: right/up/forward are the picture's axes in world space, centre the world point at the picture's
    middle, span the world width the picture covers."""
    if len(tris) == 0:
        return
    rel = verts - np.array(centre, float)
    scale = canvas.w / span
    sx = rel @ np.array(right, float) * scale + canvas.w / 2
    sy = canvas.h / 2 - rel @ np.array(up, float) * scale
    sz = rel @ np.array(forward, float)
    _raster(canvas, sx, sy, sz, tris, shade(verts, tris, base), ident)


def perspective(canvas, verts, tris, base, eye, forward, up, fov_v_deg, near=.01, ident=0):
    """Perspective from eye looking along forward; vertical field of view in degrees. Triangles crossing the near plane
    are dropped (the gun's parts behind the eye)."""
    if len(tris) == 0:
        return
    f = np.array(forward, float)
    f /= np.linalg.norm(f)
    r = np.cross(np.array(up, float), f)
    r /= np.linalg.norm(r)
    u = np.cross(f, r)
    rel = verts - np.array(eye, float)
    x, y, z = rel @ r, rel @ u, rel @ f
    ok = (z[tris] > near).all(axis=1)
    tris = tris[ok]
    if len(tris) == 0:
        return
    zs = np.where(z > near, z, near)
    t = np.tan(np.radians(fov_v_deg) / 2)
    sx = canvas.w / 2 + (x / zs) / t * (canvas.h / 2)
    sy = canvas.h / 2 - (y / zs) / t * (canvas.h / 2)
    _raster(canvas, sx, sy, zs, tris, shade(verts, tris, base), ident)


def dot(canvas, px, py, colour, r=2):
    x0, x1, y0, y1 = int(max(px - r, 0)), int(min(px + r + 1, canvas.w)), int(max(py - r, 0)), int(min(py + r + 1, canvas.h))
    if x0 < x1 and y0 < y1:
        canvas.color[y0:y1, x0:x1] = colour


def hline(canvas, py, colour, x0=0, x1=None):
    py = int(round(py))
    if 0 <= py < canvas.h:
        canvas.color[py, int(max(x0, 0)):int(canvas.w if x1 is None else min(x1, canvas.w))] = colour


def vline(canvas, px, colour, y0=0, y1=None):
    px = int(round(px))
    if 0 <= px < canvas.w:
        canvas.color[int(max(y0, 0)):int(canvas.h if y1 is None else min(y1, canvas.h)), px] = colour
