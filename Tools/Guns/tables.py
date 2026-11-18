"""The two tables of the mount model (Tables/Weapons/GunMounts.csv, GunParts.csv), read as the game reads them and
written so that nothing else in them changes: the file's own line endings and byte-order mark stay, every line that is
not touched stays byte for byte, and a file another program may hold open (Unity keeps tables mapped) is never written
shorter in place (a shorter text written in place leaves the old tail behind it)."""
import csv
import io
import os
import sys
import time

import numpy as np

import gunmesh as G

FOLDER = "Assets/_Project/Data/Resources/Tables/Weapons"
MOUNTS, PARTS = "GunMounts", "GunParts"
SOCKETS = ("eye", "sight", "muzzle", "grip", "under")
# What measuring a gun's shape says of a socket; fits and starts are a person's choice and are never measured.
MEASURED = ("x", "y", "z", "rx", "ry", "rz", "span", "folds", "hides")
BOM = "﻿"


def path(name):
    return os.path.join(G.REPO, FOLDER, name + ".csv")


def table(name):
    """id -> {column: cell}, the rows the game reads (no '#' rows)."""
    rows = {}
    data = list(csv.reader(io.StringIO(open(path(name), encoding="utf-8-sig").read())))
    head = data[0]
    for r in data[1:]:
        if not r or not r[0] or r[0].startswith("#"):
            continue
        rows[r[0]] = {head[i]: (r[i] if i < len(r) else "") for i in range(len(head))}
    return rows


def number(row, key, fallback=0.0):
    try:
        return float((row or {}).get(key) or fallback)
    except ValueError:
        return fallback


def boxes_of(cell):
    """The boxes of a folds or hides cell as GunMounts.Boxes reads them: (low corner, high corner), its slack added."""
    out = []
    for one in (cell or "").split("|"):
        c = one.split()
        if len(c) == 6:
            v = [float(x) for x in c]
            out.append((np.minimum(v[:3], v[3:]) - .0002, np.maximum(v[:3], v[3:]) + .0002))
    return out


def offers(row, part):
    """GunMounts.Socket.Offers: a row that lists nothing offers whatever the prefab carries."""
    fits = [f for f in ((row or {}).get("fits") or "").split("|") if f.strip()]
    return not fits or part.lower() in (f.strip().lower() for f in fits)


def cell_of(x, places=4):
    """A number as the tables write it: no trailing zeros, never '-0'."""
    if x is None or x == "":
        return ""
    s = ("%." + str(places) + "f") % float(x)
    s = s.rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


def _quote(cell):
    cell = str(cell)
    return '"' + cell.replace('"', '""') + '"' if any(ch in cell for ch in ',"\n\r') else cell


def _cells(line):
    return next(csv.reader(io.StringIO(line)))


def _write(file, text):
    new = text.encode("utf-8")
    size = os.path.getsize(file) if os.path.exists(file) else 0
    for _ in range(40):
        try:
            if len(new) >= size and size > 0:
                with open(file, "r+b") as f:
                    f.write(new)
            else:
                with open(file, "wb") as f:                 # truncates: fails while another program maps the file
                    f.write(new)
            break
        except OSError as error:
            print("retry write", file, error)
            time.sleep(1.5)
    else:
        sys.exit("COULD NOT WRITE " + file)
    if open(file, "rb").read() != new:
        sys.exit("WRITE NOT VERIFIED " + file)
    print("wrote %s (%d -> %d bytes)" % (os.path.relpath(file, G.REPO), size, len(new)))


def put(name, rows):
    """rows: {row id: {column: cell text}}. A row that is there gets those cells; one that is not is added after the last
    row of the same gun (the id's part before the dot), else at the table's end, its other cells empty."""
    file = path(name)
    text = open(file, "rb").read().decode("utf-8")
    eol = "\r\n" if "\r\n" in text else "\n"
    lines = text.split(eol)
    header = _cells(lines[0].lstrip(BOM))
    todo = {k: dict(v) for k, v in rows.items()}
    last_of = {}
    for i in range(1, len(lines)):
        if not lines[i].strip():
            continue
        row = _cells(lines[i])
        key = row[0]
        last_of[key.split(".")[0]] = i
        if key not in todo:
            continue
        for column, value in todo.pop(key).items():
            at = header.index(column)
            while len(row) <= at:
                row.append("")
            row[at] = value
        lines[i] = ",".join(_quote(c) for c in row)
    added = 0
    for key, cells in todo.items():
        row = [""] * len(header)
        row[0] = key
        for column, value in cells.items():
            row[header.index(column)] = value
        gun = key.split(".")[0]
        end = len(lines) - 1 if lines and not lines[-1].strip() else len(lines)
        at = last_of[gun] + 1 if gun in last_of else end
        lines.insert(at, ",".join(_quote(c) for c in row))
        # rows below the new line moved down by one
        last_of = {g: (i + 1 if i >= at else i) for g, i in last_of.items()}
        last_of[gun] = at
        added += 1
    _write(file, eol.join(lines))
    return added
