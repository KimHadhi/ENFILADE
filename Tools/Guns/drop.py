"""A part let down onto a gun until it touches: the height its origin comes to rest at.
Both shapes are height fields over the same x-z grid (the gun's top, the part's underside); the part rests where the
two first meet, small bumps aside (a screw head under a sight's foot is pressed in, not stood on)."""
import numpy as np
import gunshape as GS

HALF, CELL, BUMPS = .024, .001, 97.0


def rest(body, body_tris, body_mask, part, part_tris):
    """body, part: vertices in one frame (y up, z along the rail), the part with its origin's height at 0.
    Returns how high the part's origin rests (the frame's y), or None when nothing of the gun is under it."""
    z0, z1 = float(part[:, 2].min()), float(part[:, 2].max())
    top, _, _ = GS.height_field(body, body_tris, HALF, z0, z1, CELL, True, body_mask)
    low, _, _ = GS.height_field(part, part_tris, HALF, z0, z1, CELL, False, None)
    both = np.isfinite(top) & np.isfinite(low)
    if not both.any():
        return None
    return float(np.percentile((top - low)[both], BUMPS))


def gaps(body, body_tris, body_mask, part, part_tris):
    """With the part where it is: the least gap between its underside and the gun's top (m; under 0 = that deep in the
    gun), bumps aside, and the share of its footprint within 1.5 mm of the gun (what it rests on)."""
    z0, z1 = float(part[:, 2].min()), float(part[:, 2].max())
    top, _, _ = GS.height_field(body, body_tris, HALF, z0, z1, CELL, True, body_mask)
    low, _, _ = GS.height_field(part, part_tris, HALF, z0, z1, CELL, False, None)
    both = np.isfinite(top) & np.isfinite(low)
    if not both.any():
        return None, 0.0
    d = (low - top)[both]
    return float(np.percentile(d, 100.0 - BUMPS)), float(np.mean(np.abs(d - np.percentile(d, 100.0 - BUMPS)) <= .0015))
