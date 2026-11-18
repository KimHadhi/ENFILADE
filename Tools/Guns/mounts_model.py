"""The mount model as the game has it: Assets/_Project/Scripts/Weapons/FPSBody/GunMounts.cs mirrored in Python, so that
the tables can be checked without Unity. Change the two together.
A socket is a place on the gun (origin, rotation, span along its own forward); a part sits on it by one rule.
  sight   : slid along the rail until its own eye point is level with the gun's eye place, kept on the rail by its foot
            (GunMounts.SeatSight)
  under   : at the rail's front end, where a light looks past whatever else hangs there (GunMounts.SeatFront)
  grip    : at the rail's rear end until the hand that holds it says where (GunAttachments.Seat, FPSGun.SeatGrips)
  muzzle  : at the socket
Rows are dictionaries as the csv has them."""
import numpy as np


def euler_to_matrix(e):
    """Unity's Quaternion.Euler(x, y, z): z, then x, then y (degrees) -> 3x3."""
    x, y, z = np.radians(e)
    cx, sx, cy, sy, cz, sz = np.cos(x), np.sin(x), np.cos(y), np.sin(y), np.cos(z), np.sin(z)
    rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    return ry @ rx @ rz


def matrix_to_euler(m):
    """The inverse, as Unity gives eulerAngles (degrees)."""
    sx = -m[1, 2]
    sx = min(1.0, max(-1.0, sx))
    x = np.arcsin(sx)
    if abs(sx) < .99999:
        y = np.arctan2(m[0, 2], m[2, 2])
        z = np.arctan2(m[1, 0], m[1, 1])
    else:
        y = np.arctan2(-m[2, 0], m[0, 0])
        z = 0.0
    return np.degrees([x, y, z])


def pose(row):
    """A socket row's origin and axes in the gun's root space."""
    origin = np.array([float(row.get("x") or 0), float(row.get("y") or 0), float(row.get("z") or 0)])
    axes = euler_to_matrix([float(row.get("rx") or 0), float(row.get("ry") or 0), float(row.get("rz") or 0)])
    return origin, axes


def seat_sight(socket, eye, aim_local, foot_back, foot_front):
    """How far along the rail a sight sits (m from the socket's origin): where its own eye point (aim_local, in the part's
    frame) is level with the gun's eye place along the rail, kept on the rail by its foot."""
    origin, axes = pose(socket)
    eye_m = (np.asarray(eye, float) - origin) @ axes        # the gun's eye place in the socket's frame
    return kept(socket, eye_m[2] - aim_local[2], foot_back, foot_front)


def seat_front(socket, foot_front):
    return max(0.0, span_of(socket) - foot_front)


def seat_grip(socket, foot_back, foot_front):
    """A grip before any hand has said where: at the rail's rear end (GunAttachments.Seat)."""
    return kept(socket, 0.0, foot_back, foot_front)


def kept(socket, along, foot_back, foot_front):
    """A place along the rail kept where the part's foot stays on it; a rail shorter than the foot (a point) holds the
    part at its middle (GunMounts.Kept)."""
    span = span_of(socket)
    lo, hi = foot_back, span - foot_front
    return span * .5 if hi < lo else float(min(max(along, lo), hi))


def span_of(socket):
    try:
        return max(0.0, float(socket.get("span") or 0))
    except ValueError:
        return 0.0


def place(socket, t):
    """World (gun root) position and axes of a part's origin sitting t along the socket."""
    origin, axes = pose(socket)
    return origin + axes[:, 2] * t, axes


def aimed_fov(magnification, zooms, hip_fov, own_fov=None):
    """The vertical field of view (degrees) fully aimed through a sight (GunMounts.AimedFov): a sight that zooms narrows
    the hip field that many times; any other leaves the gun's own aimed field."""
    own = hip_fov if own_fov is None else own_fov
    if not zooms:
        return own
    return min(own, 2 * np.degrees(np.arctan(np.tan(np.radians(hip_fov) / 2) / magnification)))
