"""What the builder's rule for spawn points (GreyboxBuilder.Standing FreeSpawnPoints) does to the points of a built
scene: a point that stands inside a solid box is moved the shortest way out of it along its floor, at most
POINT_FARTHEST, as the build does it (standing.py is the same rule in Python: change both together). Prints the points
it moves or cannot free. The scene's props stand where that build put them: a rebuild may stand them elsewhere.
usage: python Tools/Maps/point_check.py [scene name ...]"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import flow
import standing as st

# GreyboxBuilder.Standing: PointHalf, PointHeight, PointFarthest, SettleTries, StandSunk, the spawn points' Lift.
POINT_HALF, POINT_HEIGHT, POINT_FARTHEST, SETTLE_TRIES, STAND_SUNK, LIFT = .35, 1.8, 2.0, 6, .1, .05


def floor_at(world, floor):
    """Whether a box has its top at this place's feet (the box's own size about its middle: a floor is not turned)."""
    for b in world.blocks:
        if abs(b.max_y - floor[1]) > STAND_SUNK + LIFT:
            continue
        if abs(floor[0] - b.c[0]) <= b.size[0] * .5 and abs(floor[2] - b.c[2]) <= b.size[2] * .5:
            return True
    return False


def main():
    names = sys.argv[1:] or flow.CHAPTERS
    for name in names:
        path = flow.SCENES + name + ".unity"
        spots, points = flow.campaign_points(path)
        world = st.World()
        for o, block in st.scene_blocks(path):
            world.blocks.append(block)
        moved = blocked = 0
        print("%s: 스폰 지점 %d개" % (name, len(points)))
        for point, pos in points:
            asked = (pos[0], pos[1] - LIFT, pos[2])
            floor = asked
            first = None
            free = False
            for _ in range(SETTLE_TRIES + 1):
                what, way = world.crosses(floor, (POINT_HALF, POINT_HEIGHT * .5, POINT_HALF), POINT_HEIGHT, 0.0, None, False)
                if what is None:
                    free = True
                    break
                first = first or what
                floor = (floor[0] + way[0], floor[1], floor[2] + way[1])
                if (floor[0] - asked[0]) ** 2 + (floor[2] - asked[2]) ** 2 > POINT_FARTHEST ** 2:
                    break
            if first is None:
                continue
            if not free or not floor_at(world, floor):
                blocked += 1
                print("  막힘 %-16s (%.1f, %.1f, %.1f): %s 속, %.0f m 안에 빈 바닥이 없음" % (point, asked[0], asked[1], asked[2], first, POINT_FARTHEST))
            else:
                moved += 1
                print("  옮김 %-16s (%.1f, %.1f, %.1f) -> (%.1f, %.1f, %.1f): %s 속이어서" % (point, asked[0], asked[1], asked[2], floor[0], floor[1], floor[2], first))
        print("  # 옮긴 것 %d, 막힌 것 %d" % (moved, blocked))


if __name__ == "__main__":
    main()
