"""Prints the solid boxes of a built scene whose name contains a word: place, size, yaw.
usage: python find_boxes.py <scene.unity> <word> [<word> ...]"""
import sys
sys.path.insert(0, __import__("os").path.dirname(__file__))
import unity_scene as us

objs, docs = us.load(sys.argv[1])
words = sys.argv[2:]
for o in sorted(objs.values(), key=lambda o: o.name):
    if not any(w in o.name for w in words):
        continue
    b = us.box_of(o)
    if b is None:
        print("%-40s at (%.2f, %.2f, %.2f) no box; comps %s" % (o.name, o.wpos[0], o.wpos[1], o.wpos[2], [c[0] for c in o.comps]))
        continue
    c, d, yaw, up = b
    body = o.comp("BoxCollider")
    print("%-40s at (%.2f, %.2f, %.2f) size %.2fx%.2fx%.2f yaw %.0f%s%s%s  parent %s" % (
        o.name, c[0], c[1] - d[1] / 2, c[2], d[0], d[1], d[2], yaw, "" if up else " tilted",
        " trigger" if us.is_trigger(body) else "", "" if us.enabled(body) else " OFF", o.parent.name if o.parent else "-"))
