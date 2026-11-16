# BuildExteriorTerrainV13.py - SMALL DIALS (Patrick
# 2026-11-16, from the polish list after V12 closed).
# Fork of V12: heightfield math, discs, landforms, road
# network, scatter positions/counts and the cobblestone
# roads are IDENTICAL. Two dials only:
#   * Crystals read large in V10-V12: the scatter scale
#     range 1.0-1.6 is trimmed to 0.9-1.22 (uniform draws
#     consume the same RNG sequence, so every placement
#     is unchanged - only crystal sizes shrink).
#   * The violet heath read strongly: the tint's violet
#     field factor 0.70 -> 0.50. Violet stays as patches,
#     just quieter.
# Install: rename ExteriorTerrainV13.fbx ->
# ExteriorTerrain.fbx in Resources/Models AND overwrite
# ExteriorTerrainTint.png in Resources/Textures.
# The V12 notes follow below.
#
# BuildExteriorTerrainV12.py - COBBLESTONE ROADS (Patrick
# 2026-11-16, approved from the mockup renders: "can make
# the road cobblestones like?"). Fork of V11: heightfield
# math, discs, landforms, road network, scatter, flora and
# the biome tint are IDENTICAL - the terrain GEOMETRY does
# not change at all, so the V11 FBX stays valid. Only the
# road painting in the tint changes:
#   * The dirt track (ruts and all) is replaced by stone
#     setts: staggered cobble cells in world space (1.25u),
#     a per-stone shade hash, dark mortar at the cell
#     borders, and a light bevel across each stone. The
#     V11 dark edge line and crisp falloff are kept.
# Install: overwrite ExteriorTerrainTint.png in
# Resources/Textures with this script's tint output. No
# FBX reinstall needed if V11's terrain is already in.
# The V11 notes follow below.
#
# BuildExteriorTerrainV11.py - FROND + ROAD DEFINITION
# (Patrick 2026-11-16, from the V10 play-check video: the
# frond trees "read as starbursts" and the roads "could use
# some more definition - they almost look out of focus").
# Fork of V10: heightfield math, discs, landforms, road
# network, scatter and the biome tint are IDENTICAL (no C#
# mirror change). What is new:
#   * Frond trees rebuilt: the flat octahedron fans are
#     replaced by arched blade fronds - a shallow Lambda
#     cross-section (raised centre rib) swept along a pitch
#     arc that starts upright and droops to the tip, built
#     as a thin solid so it reads from below too. Two tiers
#     (6 upright + 4 longer drooping) per tree.
#   * Roads get definition in the tint: resolution 2048 ->
#     4096, a crisper edge (0.7-unit falloff, was 2.2), a
#     dark worn edge line, twin wheel ruts at |lateral|
#     ~1.05, and a lighter, warmer dirt so the track stands
#     off both the green and the golden fields.
# Install: rename ExteriorTerrainV11.fbx -> ExteriorTerrain.fbx
# in Resources/Models AND overwrite ExteriorTerrainTint.png
# in Resources/Textures (the tint changed this time).
# The V10 notes follow below.
#
# BuildExteriorTerrainV10.py - FLORA KIT SWAP (Patrick
# 2026-11-16: the V9 generated flora "look a bit flimsy").
# Fork of V9: heightfield math, discs, landforms, road network
# and the bright-day tint are IDENTICAL (no C# mirror change,
# tint PNG byte-identical - only the FBX changes). What is new:
#   * The procedural-primitive flora (cone-canopy trees, sphere
#     shrubs, octa crystals) is replaced by the modeled assets
#     from BuildFloraKitV1.py, copied in as seeded template
#     builders and scattered whole: mushroom + frond trees,
#     berry shrubs, crystal clusters on rock mounds, rock
#     clusters, spore tufts, and the 3-tier giant tree at the
#     searched giant spots. Rock spires stay primitive (they
#     are landform dressing). Two new material slots: 8
#     FloraCapViolet, 9 FloraFrond.
#   * Counts drop (kit pieces cost 2-3x the tris each): trees
#     500 -> ~380, shrubs 2003 -> ~500, tufts 7281 -> ~3200,
#     crystals ~300, plus ~160 rock clusters. Total stays in
#     the phone-proven class (~1.18M tris; the S24 ran V9's
#     1.10M "very well" 2026-11-16).
# Install: rename ExteriorTerrainV10.fbx -> ExteriorTerrain.fbx
# in Resources/Models. The tint PNG is unchanged from V9 - no
# need to recopy it.
# The V9 notes follow below.
#
# BuildExteriorTerrainV9.py - BRIGHT DAY (Exterior Round 3,
# Phase 4, Patrick 2026-11-16). Fork of V8: the heightfield
# math, discs, landforms and road network are IDENTICAL (no
# C# mirror change). What is new:
#   * The tint is rebaked in the bright-day palette: green
#     fields with golden patches and violet heath, lighter
#     warmer dirt on the roads, fine grain for the close
#     camera. (The matching relight - sky, sun, fog, post
#     grade - lives in ExteriorView's ambience block.)
#   * Flora density roughly triples, and three GIANT
#     mushroom-canopy trees (the mockup's signature) are
#     placed at searched clear spots.
# Install: rename ExteriorTerrainV9.fbx -> ExteriorTerrain.fbx
# in Resources/Models and overwrite ExteriorTerrainTint.png
# in Resources/Textures.
# The V8 notes follow below.
#
# BuildExteriorTerrainV8.py - ROADS (Exterior Round 3,
# Phase 2, Patrick 2026-11-15). Fork of V7: the biome, flora,
# landforms and discs are unchanged, and the heightfield math
# is IDENTICAL (no C# mirror change). What is new:
#   * A polar road network (16 rays x 3 rings + 4 corner
#     nodes; tables below, generated by gen_roads.py, which
#     emitted the same tables for ExteriorView's pathing):
#     junctions nudged clear of every site disc and landform;
#     sites whose network route ran >1.45x the straight line
#     got a direct driveway from the base (ROAD_DRIVEWAYS).
#   * The tint paints the network as worn dirt tracks (soft
#     edges, darker centre rut, noise breakup), clipped at
#     the site discs; site pads still paint over the tips.
#   * Flora keeps off the tracks (per-kind margins in
#     scatter), so trees line the roads instead of blocking
#     them.
# Install: rename ExteriorTerrainV8.fbx -> ExteriorTerrain.fbx
# in Resources/Models and overwrite ExteriorTerrainTint.png
# in Resources/Textures.
# The V7 notes follow below.
#
# BuildExteriorTerrainV7.py - LANDFORM REPLACEMENT
# (Patrick 2026-11-15, after the V5 sites landed: "We have
# the moon craters left over from the earlier model, I think
# we should replace those."). Fork of V6: the biome tint, the
# flora system and every site disc are unchanged, but the 44
# entries of the CRATERS table - same positions, radii and
# depth/rim parameters - now describe ALIEN LANDFORMS instead
# of impact craters:
#   0 moss saucer  - a gentle dip (0.35x depth), no rim,
#                    dressed with a fairy ring of tufts
#   1 geode basin  - flat floor, smooth wall, soft lip;
#                    crystal clusters grow from the floor
#   2 rolling hollow - a wide, very shallow dip (0.20x),
#                    tint carries it (deep moss)
#   3 geyser cone  - a RAISED mound (the old "fresh" spots)
#                    with a vent dip on top, a glowing vent
#                    disc, crystals on its slopes; the tint
#                    paints the vent bright cyan and the
#                    ejecta rays are GONE.
# The height profiles changed, so ExteriorView's C# mirror
# (TerrainCraterHeight) changed in the same commit - the two
# must never drift. Install: rename ExteriorTerrainV7.fbx ->
# ExteriorTerrain.fbx in Resources/Models and overwrite
# ExteriorTerrainTint.png in Resources/Textures.
# The V6 notes follow below.
#
import bpy
import math
import os
from mathutils import Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "ExteriorTerrainV13.fbx")

HX = 560.0
HZ = 480.0
STEP = 2.0

# Flat discs: (cx, cz, r_in, r_out) at the NEW (x2) positions.
DISCS = [
    (0.0, 0.0, 42.0, 60.0),
    (140.0, 100.0, 10.0, 17.0),
    (-220.0, 180.0, 10.0, 17.0),
    (380.0, -220.0, 10.0, 17.0),
    (-120.0, -140.0, 10.0, 17.0),
    (260.0, 220.0, 10.0, 17.0),
    (-420.0, -260.0, 10.0, 17.0),
    (180.0, -160.0, 10.0, 17.0),
    (-280.0, -80.0, 10.0, 17.0),
    (440.0, 300.0, 10.0, 17.0),
    (-380.0, 320.0, 10.0, 17.0),
    (300.0, 380.0, 10.0, 17.0),
    (-180.0, -420.0, 10.0, 17.0),
    (480.0, -60.0, 10.0, 17.0),
    (-480.0, 60.0, 10.0, 17.0),
    (120.0, 430.0, 10.0, 17.0),
    (-460.0, -340.0, 10.0, 17.0),
    (500.0, 240.0, 10.0, 17.0),
    (60.0, -440.0, 10.0, 17.0),
    (-480.0, -400.0, 19.0, 28.0),
    (220.0, 60.0, 21.0, 30.0),
    (-160.0, 320.0, 21.0, 30.0),
    (400.0, 160.0, 21.0, 30.0),
    (-340.0, 240.0, 21.0, 30.0),
    (80.0, -360.0, 21.0, 30.0),
    (-480.0, -40.0, 21.0, 30.0),
    (480.0, -320.0, 21.0, 30.0),
    (0.0, 400.0, 21.0, 30.0),
    (240.0, -400.0, 21.0, 30.0),
    (340.0, 40.0, 21.0, 30.0),
    (-380.0, 60.0, 21.0, 30.0),
    (60.0, 240.0, 21.0, 30.0),
    (-60.0, -380.0, 21.0, 30.0),
    (500.0, 120.0, 21.0, 30.0),
    (-240.0, -340.0, 21.0, 30.0),
    (150.0, -60.0, 21.0, 30.0),
    (-150.0, -220.0, 21.0, 30.0),
    (-260.0, 20.0, 21.0, 30.0),
    (90.0, 300.0, 21.0, 30.0),
    (-320.0, -180.0, 21.0, 30.0),
    (420.0, -380.0, 21.0, 30.0),
    (-440.0, 380.0, 21.0, 30.0),
    (520.0, 340.0, 21.0, 30.0),
    (200.0, -260.0, 21.0, 30.0),
    (-80.0, 120.0, 21.0, 30.0),
    (-400.0, -420.0, 21.0, 30.0),
    (-180.0, 430.0, 21.0, 30.0),
    (500.0, 430.0, 21.0, 30.0),
    (280.0, -80.0, 21.0, 30.0),
    (-520.0, -180.0, 21.0, 30.0),
    (40.0, -160.0, 21.0, 30.0),
    (-40.0, 260.0, 21.0, 30.0),
    (430.0, 60.0, 21.0, 30.0),
    (-520.0, 200.0, 21.0, 30.0),
    (170.0, 190.0, 21.0, 30.0),
    (-300.0, -450.0, 21.0, 30.0),
]

# Broad highland rises: (cx, cz, amp, sigma).
HIGHLANDS = [
    (-380.0, 300.0, 3.9, 88.0),
    (410.0, -320.0, 3.3, 77.0),
    (300.0, 340.0, 2.7, 67.0),
    (-120.0, -300.0, 3.0, 70.0),
    (60.0, 180.0, 2.4, 60.0),
]

# Craters: (cx, cz, radius, depth, rim height, type).
# Types (2026-11-14, Patrick: more realistic craters, different
# kinds): 0 simple (the legacy cosine bowl + rim), 1 complex
# (flat floor, central peak, terraced walls - the big ones),
# V7 landforms: 0 moss saucer, 1 geode basin, 2 rolling
# hollow, 3 geyser cone (see crater_height). One old entry
# became a 5-pit secondary chain (type 0, small).
CRATERS = [
(-120, -240, 27.5, 5.0, 3.2, 1),
    (120, 260, 22.5, 4.0, 2.6, 3),
    (300, -120, 17.5, 3.2, 2.1, 0),
    (-320, -180, 20.0, 3.5, 2.2, 2),
    (460, 240, 25.0, 4.5, 2.9, 1),
    (-460, 180, 18.8, 3.4, 2.2, 0),
    (180, -300, 15.0, 2.7, 1.8, 2),
    (-80, 80, 13.8, 2.4, 1.6, 0),
    (360, 380, 20.0, 3.5, 2.2, 3),
    (-220, -360, 23.8, 4.3, 2.7, 1),
    (56, -104, 5.2, 1.1, 0.7, 0),
    (68, -94, 6.5, 1.4, 0.9, 0),
    (80, -84, 5.8, 1.2, 0.8, 0),
    (92, -74, 4.8, 1.0, 0.6, 0),
    (104, -64, 4.0, 0.8, 0.5, 0),
    (520, -80, 16.2, 2.9, 1.9, 0),
    (-520, -280, 21.2, 3.8, 2.4, 2),
    (240, 120, 12.5, 2.2, 1.4, 0),
    (-380, 380, 17.5, 3.2, 2.1, 0),
    (20, -220, 16.2, 2.9, 1.9, 2),
    (5, -312, 9.4, 1.9, 1.3, 3),
    (154, 34, 15.0, 3.0, 1.8, 0),
    (345, 351, 14.9, 3.4, 2.2, 2),
    (-80, 327, 11.1, 2.2, 1.3, 0),
    (425, -302, 21.1, 4.2, 2.8, 0),
    (316, -230, 9.9, 2.1, 1.3, 2),
    (-147, 221, 20.8, 5.0, 3.2, 1),
    (-346, -6, 10.9, 2.4, 1.6, 3),
    (-246, 90, 16.4, 3.9, 2.5, 2),
    (325, 168, 19.7, 4.6, 3.0, 0),
    (-243, 265, 14.6, 2.8, 1.8, 3),
    (-375, -222, 22.0, 5.0, 3.2, 2),
    (-471, -113, 15.7, 3.0, 2.0, 0),
    (-397, -11, 15.8, 3.6, 2.3, 2),
    (-126, -382, 13.5, 3.0, 2.0, 2),
    (-324, -266, 12.9, 3.0, 1.8, 3),
    (-462, -278, 9.8, 2.0, 1.2, 0),
    (-108, -107, 11.4, 2.5, 1.6, 0),
    (391, -64, 15.2, 3.0, 2.0, 0),
    (-161, -117, 20.1, 4.0, 2.6, 0),
    (-378, 110, 12.9, 2.7, 1.6, 3),
    (-203, 95, 22.8, 4.5, 2.7, 2),
    (342, 323, 10.3, 2.1, 1.4, 0),
    (118, 9, 14.8, 2.9, 1.8, 0),
]


# ----------------------------------------------------------------
# Roads (V8): the network tables. ROAD_JUNCTIONS[0] is the base;
# 1..48 are the polar grid (ray * 3 + ring), 49..52 the corner
# nodes. Generated by gen_roads.py - do not hand-edit; the same
# numbers live in ExteriorView.cs for pathing.
ROAD_JUNCTIONS = [
    (0.0, 0.0),
    (160.0, 0.0),
    (320.0, 0.0),
    (470.0, 0.0),
    (147.8, 61.2),
    (295.6, 122.5),
    (434.2, 179.9),
    (113.1, 113.1),
    (226.3, 226.3),
    (320.5, 343.7),
    (61.2, 147.8),
    (132.7, 291.2),
    (171.3, 413.6),
    (0.0, 160.0),
    (0.0, 320.0),
    (-50.4, 410.5),
    (-56.0, 149.9),
    (-122.5, 295.6),
    (-142.1, 424.5),
    (-117.0, 109.1),
    (-226.3, 226.3),
    (-332.3, 332.3),
    (-147.8, 61.2),
    (-295.6, 122.5),
    (-427.7, 194.9),
    (-160.0, 0.0),
    (-320.0, 0.0),
    (-470.0, 0.0),
    (-147.8, -61.2),
    (-295.6, -122.5),
    (-434.2, -179.9),
    (-89.5, -132.6),
    (-226.3, -226.3),
    (-332.3, -332.3),
    (-61.2, -147.8),
    (-122.5, -295.6),
    (-142.1, -424.5),
    (-0.0, -160.0),
    (-22.3, -319.2),
    (-0.0, -413.6),
    (78.8, -139.3),
    (122.5, -295.6),
    (171.3, -413.6),
    (113.1, -113.1),
    (226.3, -226.3),
    (332.3, -332.3),
    (159.0, -18.1),
    (278.5, -157.6),
    (434.2, -179.9),
    (452.0, 379.2),
    (-477.3, 346.8),
    (-438.5, -394.8),
    (464.9, -363.2),
]
ROAD_EDGES = [
    (0, 1),
    (0, 4),
    (0, 7),
    (0, 10),
    (0, 13),
    (0, 16),
    (0, 19),
    (0, 22),
    (0, 25),
    (0, 28),
    (0, 31),
    (0, 34),
    (0, 37),
    (0, 40),
    (0, 43),
    (0, 46),
    (1, 2),
    (1, 4),
    (1, 46),
    (2, 3),
    (2, 5),
    (2, 47),
    (3, 6),
    (3, 48),
    (4, 5),
    (4, 7),
    (5, 6),
    (5, 8),
    (6, 9),
    (6, 49),
    (7, 8),
    (7, 10),
    (8, 9),
    (8, 11),
    (9, 12),
    (9, 49),
    (10, 11),
    (10, 13),
    (11, 12),
    (11, 14),
    (12, 15),
    (13, 14),
    (13, 16),
    (14, 15),
    (14, 17),
    (15, 18),
    (16, 17),
    (16, 19),
    (17, 18),
    (17, 20),
    (18, 21),
    (19, 20),
    (19, 22),
    (20, 21),
    (20, 23),
    (21, 24),
    (21, 50),
    (22, 23),
    (22, 25),
    (23, 24),
    (23, 26),
    (24, 27),
    (24, 50),
    (25, 26),
    (25, 28),
    (26, 27),
    (26, 29),
    (27, 30),
    (28, 29),
    (28, 31),
    (29, 30),
    (29, 32),
    (30, 33),
    (30, 51),
    (31, 32),
    (31, 34),
    (32, 33),
    (32, 35),
    (33, 36),
    (33, 51),
    (34, 35),
    (34, 37),
    (35, 36),
    (35, 38),
    (36, 39),
    (37, 38),
    (37, 40),
    (38, 39),
    (38, 41),
    (39, 42),
    (40, 41),
    (40, 43),
    (41, 42),
    (41, 44),
    (42, 45),
    (43, 44),
    (43, 46),
    (44, 45),
    (44, 47),
    (45, 48),
    (45, 52),
    (46, 47),
    (47, 48),
    (48, 52),
]
ROAD_DRIVEWAYS = [7, 31, 36, 37, 51, 54]

_ROAD_MARGIN = {"tree": 5.0, "spire": 5.0, "crystal": 4.5, "shrub": 3.5, "tuft": 2.2, "rock": 3.5}


def _site_nearest_junction(x, z):
    best, bd = -1, 1e18
    for j, (jx, jz) in enumerate(ROAD_JUNCTIONS):
        d = (x - jx) ** 2 + (z - jz) ** 2
        if d < bd:
            bd, best = d, j
    return best


def _clip_from(a, b, from_a):
    """The point from_a units along a->b."""
    d = math.hypot(b[0] - a[0], b[1] - a[1])
    if d <= from_a:
        return b
    t = from_a / d
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)


def _road_draw_segments():
    """Segments as painted in the tint and kept clear of flora:
    network edges (spokes start at the base apron, r=50) plus
    one spur per site, clipped at the site disc edge; driveway
    sites run straight from the base apron instead."""
    segs = []
    for (a, b) in ROAD_EDGES:
        pa, pb = ROAD_JUNCTIONS[a], ROAD_JUNCTIONS[b]
        if a == 0:
            pa = _clip_from(pa, pb, 50.0)
        segs.append((pa, pb))
    for si, (sx, sz, r_in, r_out) in enumerate(DISCS):
        if si == 0:
            continue
        j = _site_nearest_junction(sx, sz)
        if si in ROAD_DRIVEWAYS:
            # The driveway runs straight base -> site, so its end
            # clips along THAT line (toward the origin). Clipping
            # toward the nearest junction instead veered the painted
            # segment off the marched line (the Phase 2 tint bug
            # found 2026-11-16: armies marched the true straight
            # line across grass while the paint bent away).
            start = _clip_from((0.0, 0.0), (sx, sz), 50.0)
            end = _clip_from((sx, sz), (0.0, 0.0), r_out + 3.0)
            segs.append((start, end))
        else:
            end = _clip_from((sx, sz), ROAD_JUNCTIONS[j], r_out + 3.0)
            segs.append((ROAD_JUNCTIONS[j], end))
    return segs


_ROAD_SEGS = None


def _near_road(x, z, dist):
    global _ROAD_SEGS
    if _ROAD_SEGS is None:
        _ROAD_SEGS = _road_draw_segments()
    for ((ax, az), (bx, bz)) in _ROAD_SEGS:
        dx, dz = bx - ax, bz - az
        L2 = dx * dx + dz * dz
        t = 0.0 if L2 == 0 else max(0.0, min(1.0, ((x - ax) * dx + (z - az) * dz) / L2))
        if math.hypot(x - (ax + t * dx), z - (az + t * dz)) < dist:
            return True
    return False


def smoothstep(a, b, x):
    if x <= a:
        return 0.0
    if x >= b:
        return 1.0
    t = (x - a) / (b - a)
    return t * t * (3.0 - 2.0 * t)


def crater_height(q, depth, rim, ctype):
    """One landform's height contribution at q = dist / radius.
    MIRRORED IN C# (ExteriorView.TerrainCraterHeight) - keep
    the math identical. V7 types: 0 moss saucer, 1 geode
    basin, 2 rolling hollow, 3 geyser cone. The rim column is
    only used by the basin's soft lip now."""
    if ctype == 1:  # geode basin: flat floor, smooth wall, soft lip
        if q < 0.45:
            return -0.80 * depth
        if q < 1.0:
            t = (q - 0.45) / 0.55
            return -0.80 * depth * (1.0 - t) ** 1.2
        if q < 1.5:
            sn = math.sin(math.pi * (q - 1.0) / 0.5)
            return 0.30 * rim * sn * sn
        return 0.0
    if ctype == 2:  # rolling hollow: wide, very shallow, no rim
        if q < 1.6:
            return -0.20 * depth * (0.5 + 0.5 * math.cos(math.pi * q / 1.6)) ** 1.3
        return 0.0
    if ctype == 3:  # geyser cone: raised mound, vent dip on top
        if q >= 2.2:
            return 0.0
        cone = 1.40 * depth / (1.0 + (q / 0.40) ** 2) ** 1.5
        if q < 0.14:
            cone -= 0.36 * depth * (1.0 - q / 0.14)
        return cone
    # moss saucer (0): a gentle dip, no rim
    if q < 1.0:
        return -0.35 * depth * (0.5 + 0.5 * math.cos(math.pi * q)) ** 1.2
    return 0.0


def height_at(x, z):
    """Terrain height at Unity (x, z). MIRRORED IN C# — keep the
    two implementations byte-for-byte equivalent in math."""
    d = (2.4 * math.sin(0.021 * x + 1.7) * math.sin(0.017 * z - 0.6)
         + 1.75 * math.sin(0.043 * x - 2.1) * math.sin(0.052 * z + 1.2)
         + 0.95 * math.sin(0.110 * x + 0.3) * math.sin(0.090 * z + 2.9))
    for (cx, cz, amp, sigma) in HIGHLANDS:
        dx = x - cx
        dz = z - cz
        d += amp * math.exp(-(dx * dx + dz * dz) / (2.0 * sigma * sigma))
    for (cx, cz, r, depth, rim, ctype) in CRATERS:
        dist = math.hypot(x - cx, z - cz)
        d += crater_height(dist / r, depth, rim, ctype)
    m = 1.0
    for (cx, cz, r_in, r_out) in DISCS:
        dist = math.hypot(x - cx, z - cz)
        m = min(m, smoothstep(r_in, r_out, dist))
    m *= 1.0 - smoothstep(516.0, 560.0, abs(x))
    m *= 1.0 - smoothstep(436.0, 480.0, abs(z))
    h = d * m
    return max(-5.2, min(8.0, h))


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


# ----------------------------------------------------------------
# FLORA - merged into the terrain mesh under material slots
# 1..9 (slot 0 = regolith). V10: the scatter plants whole
# modeled assets from the flora kit (see _kit_* builders).
# Every helper returns (verts, faces) in LOCAL space, base at
# z=0; _xform applies yaw / tilt / scale / translation into
# Blender world coords (x, -z, h).
# ----------------------------------------------------------------
import random

# Material slots in the merged mesh.
MAT_TRUNK = 1
MAT_CAP = 2
MAT_GLOW = 3
MAT_CRYSTAL = 4
MAT_SHRUB = 5
MAT_GRASS = 6
MAT_ROCK = 7
MAT_CAPV = 8
MAT_FROND = 9

FLORA_STATS = {}


def _cyl(r_bot, r_top, h, n, cap_bottom=False):
    verts = []
    for i in range(n):
        a = 2.0 * math.pi * i / n
        verts.append((r_bot * math.cos(a), r_bot * math.sin(a), 0.0))
    for i in range(n):
        a = 2.0 * math.pi * i / n
        verts.append((r_top * math.cos(a), r_top * math.sin(a), h))
    faces = []
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j))
        faces.append((i, n + j, n + i))
    if cap_bottom:
        c = len(verts)
        verts.append((0.0, 0.0, 0.0))
        for i in range(n):
            j = (i + 1) % n
            faces.append((c, j, i))
    return verts, faces


def _cone(r, h, n):
    verts = []
    for i in range(n):
        a = 2.0 * math.pi * i / n
        verts.append((r * math.cos(a), r * math.sin(a), 0.0))
    apex = len(verts)
    verts.append((0.0, 0.0, h))
    faces = [(i, (i + 1) % n, apex) for i in range(n)]
    return verts, faces


def _disc(r, n, z):
    verts = [(0.0, 0.0, z)]
    for i in range(n):
        a = 2.0 * math.pi * i / n
        verts.append((r * math.cos(a), r * math.sin(a), z))
    faces = [(0, 1 + (i + 1) % n, 1 + i) for i in range(n)]
    return verts, faces


def _octa(sx, sy, sz):
    verts = [(sx, 0, 0), (-sx, 0, 0), (0, sy, 0), (0, -sy, 0), (0, 0, sz), (0, 0, -sz)]
    faces = [(0, 2, 4), (2, 1, 4), (1, 3, 4), (3, 0, 4),
             (2, 0, 5), (1, 2, 5), (3, 1, 5), (0, 3, 5)]
    return verts, faces


def _sphere(r, n=8, rings=5):
    verts = [(0.0, 0.0, r)]
    for j in range(1, rings):
        phi = math.pi * j / rings
        for i in range(n):
            th = 2.0 * math.pi * i / n
            verts.append((r * math.sin(phi) * math.cos(th),
                          r * math.sin(phi) * math.sin(th),
                          r * math.cos(phi)))
    bot = len(verts)
    verts.append((0.0, 0.0, -r))
    faces = []
    for i in range(n):
        faces.append((0, 1 + (i + 1) % n, 1 + i))
    for j in range(rings - 2):
        r0 = 1 + j * n
        r1 = r0 + n
        for i in range(n):
            k = (i + 1) % n
            faces.append((r0 + i, r1 + k, r1 + i))
            faces.append((r0 + i, r0 + k, r1 + k))
    rl = 1 + (rings - 2) * n
    for i in range(n):
        faces.append((rl + i, bot, rl + (i + 1) % n))
    return verts, faces


def _place(acc, template, mat, yaw, tilt, tilt_dir, sx, sy, sz, tx, ty, tz):
    """Append a transformed copy of template (verts, faces) into
    acc = (verts, faces, face_mats). Rotation: yaw around Z, then
    a small tilt toward tilt_dir. Scale per-axis. Translation in
    Blender world coords."""
    verts, faces = template
    cy, sy = math.cos(yaw), math.sin(yaw)
    ct, st = math.cos(tilt), math.sin(tilt)
    ax, ay = math.cos(tilt_dir), math.sin(tilt_dir)  # tilt leans toward (ax, ay)
    av, af, am = acc
    base = len(av)
    for (x, y, z) in verts:
        x, y, z = x * sx, y * sy, z * sz
        # tilt: rotate around the horizontal axis (-ay, ax, 0)
        d = x * ax + y * ay            # component along lean dir
        p = -x * ay + y * ax           # perpendicular component
        d2 = d * ct - z * st
        z2 = d * st + z * ct
        x, y, z = d2 * ax - p * ay, d2 * ay + p * ax, z2
        # yaw
        x, y = x * cy - y * sy, x * sy + y * cy
        av.append((x + tx, y + ty, z + tz))
    for f in faces:
        af.append(tuple(base + i for i in f))
        am.append(mat)


def _noisy_sphere(r, n, rings, rng, amp):
    """A sphere with per-vertex radial noise (from the flora
    kit) - organic lumps the plain primitives lack."""
    verts, faces = _sphere(r, n, rings)
    out = []
    for idx, (x, y, z) in enumerate(verts):
        if idx == 0 or idx == len(verts) - 1:
            out.append((x, y, z))
            continue
        s = 1.0 + rng.uniform(-amp, amp)
        out.append((x * s, y * s, z * (1.0 + rng.uniform(-amp * 0.5, amp * 0.5))))
    return out, faces


def _trunk(acc, rng, r_base, height, lean_x=0.0, mat=MAT_TRUNK, n=8):
    """Segmented, slightly wandering trunk (flora kit). Returns
    the (x, y, z) of the trunk top."""
    segs = 3
    seg_h = height / segs
    x = y = 0.0
    r = r_base
    z = 0.0
    for s in range(segs):
        r_top = r * rng.uniform(0.68, 0.78)
        seg = _cyl(r, r_top, seg_h + 0.25, n, cap_bottom=(s == 0))
        _place(acc, seg, mat, rng.uniform(0, 6.28), 0.0, 0.0,
               rng.uniform(0.94, 1.06), rng.uniform(0.94, 1.06), 1.0, x, y, z)
        x += lean_x / segs + rng.uniform(-0.07, 0.07)
        y += rng.uniform(-0.07, 0.07)
        z += seg_h
        r = r_top
    return x, y, height


def _roots(acc, rng, count, r_root, len_root, mat=MAT_TRUNK):
    for k in range(count):
        a = 2.0 * math.pi * k / count + rng.uniform(-0.2, 0.2)
        root = _cyl(r_root, r_root * 0.25, len_root, 6)
        _place(acc, root, mat, 0.0, 0.85, a, 1.0, 1.0, 1.0,
               math.cos(a) * 0.22, math.sin(a) * 0.22, 0.32)


# ----------------------------------------------------------------
# FLORA KIT assets (V10) - the modeled replacements from
# BuildFloraKitV1.py, copied in as seeded template builders.
# Each returns a whole asset (verts, faces, face_mats) in local
# space, base at z=0, using the terrain material slots directly.
# ----------------------------------------------------------------

def _kit_tree_mushroom(seed):
    rng = random.Random(seed)
    acc = ([], [], [])
    _roots(acc, rng, 5, 0.34, 1.15)
    tx, ty, tz = _trunk(acc, rng, 0.85, 5.2, lean_x=0.35)
    for (h, a) in ((3.3, 0.7), (4.2, 2.6)):
        br = _cyl(0.20, 0.07, 1.35, 6)
        _place(acc, br, MAT_TRUNK, 0.0, 0.85, a, 1.0, 1.0, 1.0,
               tx * h / 5.2, ty * h / 5.2, h)
    cap = _noisy_sphere(3.1, 14, 7, rng, 0.16)
    _place(acc, cap, MAT_CAP, 0.4, 0.0, 0.0, 1.0, 1.0, 0.42, tx, ty, 6.05)
    cap2 = _noisy_sphere(2.45, 12, 6, rng, 0.18)
    _place(acc, cap2, MAT_CAP, 1.7, 0.0, 0.0, 1.0, 1.0, 0.34, tx, ty, 5.55)
    sat = _noisy_sphere(1.55, 10, 5, rng, 0.18)
    _place(acc, sat, MAT_CAPV, 0.9, 0.0, 0.0, 1.0, 1.0, 0.45,
           tx + 1.9, ty - 1.2, 4.55)
    glow = _disc(2.45, 14, 0.0)
    _place(acc, glow, MAT_GLOW, 0.0, 0.0, 0.0, 1.0, 1.0, 1.0, tx, ty, 5.52)
    for k in range(4):
        a = rng.uniform(0, 6.28)
        px, py = tx + math.cos(a) * 1.55, ty + math.sin(a) * 1.55
        stem = _cyl(0.05, 0.05, 0.65, 5)
        _place(acc, stem, MAT_TRUNK, 0.0, 0.0, 0.0, 1.0, 1.0, -1.0, px, py, 5.5)
        pod = _sphere(0.24, 8, 5)
        _place(acc, pod, MAT_GLOW, 0.0, 0.0, 0.0, 1.0, 1.0, 1.25, px, py, 4.72)
    spr = _cone(0.30, 0.85, 7)
    _place(acc, spr, MAT_FROND, 0.0, 0.12, 1.0, 1.0, 1.0, 1.0, tx, ty, 7.15)
    tip = _octa(0.16, 0.16, 0.30)
    _place(acc, tip, MAT_GLOW, 0.0, 0.0, 0.0, 1.0, 1.0, 1.0, tx, ty, 8.15)
    return acc


def _blade(length, w0, rib, thick, pitch0):
    """One arched frond blade (V11): a shallow Lambda
    cross-section (raised centre rib) swept along a pitch
    arc - starts at pitch0, droops to -0.68 rad at the tip -
    as a thin solid (top, underside, side walls, tip cap).
    Local space: base at origin, extends +X, width on Y,
    up is +Z. 40 tris."""
    ts = [0.0, 0.34, 0.67, 1.0]
    pts = []
    x = z = 0.0
    for i, t in enumerate(ts):
        if i > 0:
            tm = 0.5 * (ts[i - 1] + t)
            th = pitch0 - (pitch0 + 0.68) * (tm ** 1.25)
            seg = length * (t - ts[i - 1])
            x += seg * math.cos(th)
            z += seg * math.sin(th)
        pts.append((x, z))
    verts = []
    for i, t in enumerate(ts):
        cx, cz = pts[i]
        w = w0 * (1.0 - 0.62 * t) + 0.05
        r = rib * (1.0 - 0.55 * t)
        verts += [(cx, -w, cz), (cx, 0.0, cz + r), (cx, w, cz)]
        verts += [(cx, -w, cz - thick), (cx, 0.0, cz + r - thick),
                  (cx, w, cz - thick)]
    faces = []
    for i in range(len(ts) - 1):
        a = i * 6
        b = (i + 1) * 6
        faces += [(a + 0, b + 0, b + 1), (a + 0, b + 1, a + 1)]
        faces += [(a + 1, b + 1, b + 2), (a + 1, b + 2, a + 2)]
        faces += [(a + 3, b + 4, b + 3), (a + 3, a + 4, b + 4)]
        faces += [(a + 4, b + 5, b + 4), (a + 4, a + 5, b + 5)]
        faces += [(a + 0, a + 3, b + 3), (a + 0, b + 3, b + 0)]
        faces += [(a + 2, b + 2, b + 5), (a + 2, b + 5, a + 5)]
    l = (len(ts) - 1) * 6
    faces += [(l + 0, l + 1, l + 4), (l + 0, l + 4, l + 3),
              (l + 1, l + 2, l + 5), (l + 1, l + 5, l + 4)]
    return verts, faces


def _kit_tree_frond(seed):
    rng = random.Random(seed)
    acc = ([], [], [])
    _roots(acc, rng, 4, 0.26, 0.9)
    tx, ty, tz = _trunk(acc, rng, 0.55, 6.3, lean_x=0.8, n=7)
    # V11: arched blade fronds in two tiers replace the flat
    # octa fans (starburst read at close zoom). Tier 1 upright,
    # tier 2 longer and droopier, set half a step around.
    for tier, (count, drop, L, p0) in enumerate(
            ((6, 0.0, 2.35, 0.50), (4, 0.5, 2.6, 0.20))):
        for k in range(count):
            a = 2.0 * math.pi * (k + 0.5 * tier) / count \
                + rng.uniform(-0.18, 0.18)
            blade = _blade(L * rng.uniform(0.9, 1.12), 0.58, 0.12,
                           0.06, p0)
            _place(acc, blade, MAT_FROND, a,
                   rng.uniform(-0.06, 0.10), a, 1.0, 1.0, 1.0,
                   tx, ty, tz - drop)
    # Crown core: a flattened frond-green blob under the blades
    # fills the middle gaps so the crown reads as one canopy.
    core = _noisy_sphere(0.85, 8, 5, rng, 0.16)
    _place(acc, core, MAT_FROND, 0.0, 0.0, 0.0, 1.15, 1.15, 0.62,
           tx, ty, tz - 0.18)
    for k in range(5):
        a = rng.uniform(0, 6.28)
        b = _octa(0.17, 0.17, 0.24)
        _place(acc, b, MAT_GLOW, 0.0, 0.0, 0.0, 1.0, 1.0, 1.0,
               tx + math.cos(a) * 0.42, ty + math.sin(a) * 0.42, tz - 0.15)
    return acc


def _kit_shrub(seed):
    rng = random.Random(seed)
    acc = ([], [], [])
    blobs = ((1.25, 0.0, 0.0, 0.72, MAT_SHRUB), (0.9, 0.85, 0.3, 0.55, MAT_SHRUB),
             (0.8, -0.8, -0.25, 0.5, MAT_FROND), (0.62, 0.15, -0.8, 0.42, MAT_SHRUB),
             (0.55, -0.3, 0.75, 0.4, MAT_CAPV))
    for (r, ox, oy, sq, m) in blobs:
        s = _noisy_sphere(r, 8, 5, rng, 0.20)
        _place(acc, s, m, rng.uniform(0, 6.28), 0.0, 0.0, 1.0, 1.0, sq,
               ox, oy, r * sq * 0.82)
    for k in range(6):
        a = rng.uniform(0, 6.28)
        d = rng.uniform(0.4, 1.15)
        b = _octa(0.11, 0.11, 0.17)
        _place(acc, b, MAT_GLOW, 0.0, 0.0, 0.0, 1.0, 1.0, 1.0,
               math.cos(a) * d, math.sin(a) * d, rng.uniform(0.75, 1.35))
    return acc


def _kit_crystal(seed):
    rng = random.Random(seed)
    acc = ([], [], [])
    mound = _noisy_sphere(1.55, 9, 5, rng, 0.22)
    _place(acc, mound, MAT_ROCK, 0.3, 0.0, 0.0, 1.0, 1.0, 0.34, 0.0, 0.0, 0.30)
    hero = _octa(0.62, 0.62, 3.30)
    _place(acc, hero, MAT_CRYSTAL, 0.2, 0.06, 1.0, 1.0, 1.0, 1.0, 0.0, 0.0, 2.05)
    for k in range(4):
        a = 2.0 * math.pi * k / 4 + rng.uniform(-0.3, 0.3)
        ln = rng.uniform(1.7, 2.5)
        o = _octa(rng.uniform(0.36, 0.48), rng.uniform(0.36, 0.48), ln)
        _place(acc, o, MAT_CRYSTAL, 0.0, rng.uniform(0.22, 0.40), a,
               1.0, 1.0, 1.0, math.cos(a) * 0.85, math.sin(a) * 0.85, ln * 0.52)
    for k in range(3):
        a = rng.uniform(0, 6.28)
        o = _octa(0.18, 0.18, 0.85)
        _place(acc, o, MAT_CRYSTAL, 0.0, rng.uniform(0.3, 0.55), a,
               1.0, 1.0, 1.0, math.cos(a) * 1.25, math.sin(a) * 1.25, 0.35)
    return acc


def _kit_rock(seed):
    rng = random.Random(seed)
    acc = ([], [], [])
    lumps = ((1.45, 0.0, 0.0, 0.62), (1.0, 1.05, 0.35, 0.55), (0.8, -0.95, -0.4, 0.7))
    for (r, ox, oy, sq) in lumps:
        s = _noisy_sphere(r, 7, 4, rng, 0.26)
        _place(acc, s, MAT_ROCK, rng.uniform(0, 6.28), 0.0, 0.0,
               1.0, rng.uniform(0.8, 1.0), sq, ox, oy, r * sq * 0.75)
    return acc


def _kit_tuft(seed):
    rng = random.Random(seed)
    acc = ([], [], [])
    for k in range(7):
        h = rng.uniform(1.8, 3.0)
        blade = _cone(0.17, h, 5)
        a = rng.uniform(0, 6.28)
        d = rng.uniform(0.0, 0.55)
        _place(acc, blade, MAT_GRASS, 0.0, rng.uniform(0.10, 0.34), a,
               1.0, 0.55, 1.0, math.cos(a) * d, math.sin(a) * d, 0.0)
    for k in range(2):
        a = rng.uniform(0, 6.28)
        tip = _octa(0.13, 0.13, 0.27)
        _place(acc, tip, MAT_GLOW, 0.0, 0.0, 0.0, 1.0, 1.0, 1.0,
               math.cos(a) * 0.5, math.sin(a) * 0.5, rng.uniform(1.1, 1.8))
    return acc


def _kit_giant(seed):
    rng = random.Random(seed)
    acc = ([], [], [])
    _roots(acc, rng, 6, 0.95, 3.3)
    tx, ty, tz = _trunk(acc, rng, 2.25, 13.5, lean_x=0.9)
    for (h, a) in ((7.5, 0.5), (9.8, 2.4), (11.5, 4.4)):
        br = _cyl(0.5, 0.16, 3.1, 7)
        _place(acc, br, MAT_TRUNK, 0.0, 0.8, a, 1.0, 1.0, 1.0,
               tx * h / 13.5, ty * h / 13.5, h)
    tiers = ((8.6, 0.40, 14.6, MAT_CAP), (6.1, 0.38, 17.8, MAT_CAPV),
             (4.3, 0.36, 20.6, MAT_CAP))
    for (r, sq, z, m) in tiers:
        c = _noisy_sphere(r, 16, 8, rng, 0.15)
        _place(acc, c, m, rng.uniform(0, 6.28), 0.0, 0.0, 1.0, 1.0, sq, tx, ty, z)
        g = _disc(r * 0.78, 16, 0.0)
        _place(acc, g, MAT_GLOW, 0.0, 0.0, 0.0, 1.0, 1.0, 1.0, tx, ty, z - r * sq - 0.12)
    for k in range(8):
        a = rng.uniform(0, 6.28)
        px, py = tx + math.cos(a) * 4.6, ty + math.sin(a) * 4.6
        stem = _cyl(0.09, 0.09, 1.6, 5)
        _place(acc, stem, MAT_TRUNK, 0.0, 0.0, 0.0, 1.0, 1.0, -1.0, px, py, 11.4)
        pod = _octa(0.42, 0.42, 0.62)
        _place(acc, pod, MAT_GLOW, 0.0, 0.0, 0.0, 1.0, 1.0, 1.0, px, py, 9.45)
    return acc


_KIT_BUILDERS = {
    "mushroom": (_kit_tree_mushroom, (11, 12, 13)),
    "frond": (_kit_tree_frond, (23, 24, 25)),
    "shrub": (_kit_shrub, (37, 38, 39)),
    "crystal": (_kit_crystal, (41, 42)),
    "rock": (_kit_rock, (53, 54)),
    "tuft": (_kit_tuft, (67, 68)),
    "giant": (_kit_giant, (71,)),
}

_TEMPLATES = {}


def _tpl(kind):
    if kind not in _TEMPLATES:
        fn, seeds = _KIT_BUILDERS[kind]
        _TEMPLATES[kind] = [fn(s) for s in seeds]
    return _TEMPLATES[kind]


def _place_asset(acc, tpl, yaw, tilt, tilt_dir, sc, tx, ty, tz):
    """Append a whole kit asset (verts, faces, face_mats) into
    acc with a uniform scale - the same transform math as
    _place, but each face keeps its own material."""
    verts, faces, face_mats = tpl
    cy, sy = math.cos(yaw), math.sin(yaw)
    ct, st = math.cos(tilt), math.sin(tilt)
    ax, ay = math.cos(tilt_dir), math.sin(tilt_dir)
    av, af, am = acc
    base = len(av)
    for (x, y, z) in verts:
        x, y, z = x * sc, y * sc, z * sc
        d = x * ax + y * ay
        p = -x * ay + y * ax
        d2 = d * ct - z * st
        z2 = d * st + z * ct
        x, y, z = d2 * ax - p * ay, d2 * ay + p * ax, z2
        x, y = x * cy - y * sy, x * sy + y * cy
        av.append((x + tx, y + ty, z + tz))
    for i, f in enumerate(faces):
        af.append(tuple(base + j for j in f))
        am.append(face_mats[i])
def _spire(rng):
    rock = _cyl(1.5, 0.25, rng.uniform(6.0, 11.0), 7, cap_bottom=True)
    return [(rock, MAT_ROCK, 1.0, rng.uniform(0.8, 1.1), 1.0, 0.0, 0.0, 0.0)]


def _disc_clearance(x, z, margin):
    for (cx, cz, r_in, r_out) in DISCS:
        if math.hypot(x - cx, z - cz) < r_out + margin:
            return False
    return True


def _crater_q(x, z):
    worst = 99.0
    for (cx, cz, r, depth, rim, ctype) in CRATERS:
        worst = min(worst, math.hypot(x - cx, z - cz) / r)
    return worst


def build_flora():
    """Scatter the alien flora. Returns (verts, faces, face_mats)
    in Blender world coords, plus fills FLORA_STATS. Placement
    rules: hard clearance from every site disc; trees and spires
    stay out of crater bowls; everything stays inside the edge
    fade; groves cluster the growth so the field has regions."""
    rng = random.Random(20261115)
    acc = ([], [], [])
    placed_trees = []
    counts = {"tree": 0, "frond": 0, "crystal": 0, "shrub": 0, "tuft": 0, "rock": 0, "spire": 0}

    def scatter(kind, x, z):
        # V8: flora keeps off the road tracks.
        if _near_road(x, z, _ROAD_MARGIN.get(kind, 3.0)):
            return
        h = height_at(x, z)
        yaw = rng.uniform(0.0, 2.0 * math.pi)
        tilt = rng.uniform(0.0, 0.07)
        tdir = rng.uniform(0.0, 2.0 * math.pi)
        if kind == "spire":
            # The rock spires stay primitive (landform dressing).
            parts = _spire(rng)
            sc = rng.uniform(0.9, 1.5)
            tilt = rng.uniform(0.02, 0.10)
            for (tpl, mat, psx, psy, psz, ox, oy, oz) in parts:
                rx = (ox * math.cos(yaw) - oy * math.sin(yaw)) * sc
                ry = (ox * math.sin(yaw) + oy * math.cos(yaw)) * sc
                _place(acc, tpl, mat, yaw, tilt, tdir, psx * sc, psy * sc, psz * sc,
                       x + rx, -(z) + ry, h - 0.18 + oz * sc)
            counts[kind] += 1
            return
        # V10: whole-asset placement from the flora kit.
        if kind == "tree":
            if rng.random() < 0.62:
                tpl = rng.choice(_tpl("mushroom"))
                counts["tree"] += 1
            else:
                tpl = rng.choice(_tpl("frond"))
                counts["frond"] += 1
            sc = rng.uniform(0.85, 1.45)
        elif kind == "crystal":
            tpl = rng.choice(_tpl("crystal"))
            sc = rng.uniform(0.9, 1.22)  # V13 trim (was 1.0-1.6)
            counts[kind] += 1
        elif kind == "shrub":
            tpl = rng.choice(_tpl("shrub"))
            sc = rng.uniform(0.8, 1.4)
            counts[kind] += 1
        elif kind == "rock":
            tpl = rng.choice(_tpl("rock"))
            sc = rng.uniform(0.9, 1.6)
            counts[kind] += 1
        else:  # tuft
            tpl = rng.choice(_tpl("tuft"))
            sc = rng.uniform(0.85, 1.5)
            counts[kind] += 1
        _place_asset(acc, tpl, yaw, tilt, tdir, sc, x, -(z), h - 0.18)
    # Groves: cluster centres, rejection-sampled off the discs.
    groves = []
    tries = 0
    while len(groves) < 120 and tries < 9000:
        tries += 1
        x = rng.uniform(-520.0, 520.0)
        z = rng.uniform(-440.0, 440.0)
        if not _disc_clearance(x, z, 12.0):
            continue
        groves.append((x, z))
    FLORA_STATS["groves"] = groves

    for (gx, gz) in groves:
        for _ in range(rng.randint(3, 5)):
            x = gx + rng.gauss(0.0, 10.0)
            z = gz + rng.gauss(0.0, 10.0)
            if abs(x) > 535.0 or abs(z) > 455.0:
                continue
            if not _disc_clearance(x, z, 7.0) or _crater_q(x, z) < 1.15:
                continue
            if any(math.hypot(x - tx, z - tz) < 8.0 for (tx, tz) in placed_trees):
                continue
            placed_trees.append((x, z))
            scatter("tree", x, z)
        for _ in range(rng.randint(3, 5)):
            x = gx + rng.gauss(0.0, 13.0)
            z = gz + rng.gauss(0.0, 13.0)
            if abs(x) > 540.0 or abs(z) > 460.0 or not _disc_clearance(x, z, 3.5):
                continue
            scatter("shrub", x, z)
        for _ in range(rng.randint(14, 22)):
            x = gx + rng.gauss(0.0, 15.0)
            z = gz + rng.gauss(0.0, 15.0)
            if abs(x) > 545.0 or abs(z) > 465.0 or not _disc_clearance(x, z, 2.5):
                continue
            scatter("tuft", x, z)
        if rng.random() < 0.8:
            x = gx + rng.gauss(0.0, 12.0)
            z = gz + rng.gauss(0.0, 12.0)
            if abs(x) < 540.0 and abs(z) < 460.0 and _disc_clearance(x, z, 4.0):
                scatter("crystal", x, z)

    # Global scatter: lone trees, crystals (craters included -
    # crystal growths in the bowls read as mineral deposits),
    # tufts and shrubs to break up the empty flats.
    for _ in range(120):
        x = rng.uniform(-530.0, 530.0)
        z = rng.uniform(-450.0, 450.0)
        if _disc_clearance(x, z, 7.0) and _crater_q(x, z) >= 1.15 \
                and not any(math.hypot(x - tx, z - tz) < 8.0 for (tx, tz) in placed_trees):
            placed_trees.append((x, z))
            scatter("tree", x, z)
    for _ in range(240):
        x = rng.uniform(-540.0, 540.0)
        z = rng.uniform(-460.0, 460.0)
        if _disc_clearance(x, z, 4.0):
            scatter("crystal", x, z)
    for _ in range(1300):
        x = rng.uniform(-545.0, 545.0)
        z = rng.uniform(-465.0, 465.0)
        if _disc_clearance(x, z, 2.5):
            scatter("tuft", x, z)
    for _ in range(140):
        x = rng.uniform(-540.0, 540.0)
        z = rng.uniform(-460.0, 460.0)
        if _disc_clearance(x, z, 3.5):
            scatter("shrub", x, z)
    for _ in range(26):
        x = rng.uniform(-530.0, 530.0)
        z = rng.uniform(-450.0, 450.0)
        if _disc_clearance(x, z, 8.0) and _crater_q(x, z) >= 1.3:
            scatter("spire", x, z)

    for _ in range(200):
        x = rng.uniform(-540.0, 540.0)
        z = rng.uniform(-460.0, 460.0)
        if _disc_clearance(x, z, 4.0):
            scatter("rock", x, z)

    # Landform dressing (V7): growth tied to the landforms -
    # fairy rings on the saucers, crystal clusters in the
    # geode basins, vent discs and crystals on the geysers.
    # Positions stay inside each landform's radius, which the
    # crater placement rules already keep clear of every
    # site disc (camps >= r+34, nodes >= r+21, lair >= r+32).
    def _lf_scatter(kind, x, z, margin):
        # A few original craters sit close to camp discs; keep
        # landform growth out of the discs like all other flora
        # (a fairy ring with a gap on the camp side is fine).
        if _disc_clearance(x, z, margin):
            scatter(kind, x, z)

    for (cx, cz, r, depth, rimh, ctype) in CRATERS:
        if ctype == 0:
            for i in range(9):
                a = i * (2.0 * math.pi / 9.0) + rng.uniform(-0.15, 0.15)
                _lf_scatter("tuft", cx + math.cos(a) * r * 0.82, cz + math.sin(a) * r * 0.82, 2.5)
            for i in range(2):
                a = rng.uniform(0.0, 2.0 * math.pi)
                _lf_scatter("crystal", cx + math.cos(a) * r * 0.82, cz + math.sin(a) * r * 0.82, 4.0)
        elif ctype == 1:
            for i in range(4):
                a = rng.uniform(0.0, 2.0 * math.pi)
                rr = r * rng.uniform(0.05, 0.22)
                _lf_scatter("crystal", cx + math.cos(a) * rr, cz + math.sin(a) * rr, 4.0)
            for i in range(3):
                a = rng.uniform(0.0, 2.0 * math.pi)
                _lf_scatter("tuft", cx + math.cos(a) * r * 0.5, cz + math.sin(a) * r * 0.5, 2.5)
        elif ctype == 3:
            if _disc_clearance(cx, cz, r * 0.16):
                dv, df = _disc(r * 0.16, 12, 0.0)
                df = [(i0, i2, i1) for (i0, i1, i2) in df]  # flip to face up
                _place(acc, (dv, df), MAT_GLOW, 0.0, 0.0, 0.0,
                       1.0, 1.0, 1.0, cx, -cz, height_at(cx, cz) + 0.05)
            for i in range(3):
                a = rng.uniform(0.0, 2.0 * math.pi)
                _lf_scatter("crystal", cx + math.cos(a) * r * 0.55, cz + math.sin(a) * r * 0.55, 4.0)
            for i in range(4):
                a = i * 1.7 + rng.uniform(-0.2, 0.2)
                _lf_scatter("tuft", cx + math.cos(a) * r * 0.9, cz + math.sin(a) * r * 0.9, 2.5)

    # GIANT TREES (V9, the mockup's signature): three fungal
    # trees at 3.1x scale (~25 tall, canopy ~30 across) placed
    # by a seeded search - well clear of every site disc, off
    # the road tracks, on gentle ground, far from each other.
    giants = []
    tries = 0
    while len(giants) < 3 and tries < 60000:
        tries += 1
        x = rng.uniform(-500.0, 500.0)
        z = rng.uniform(-420.0, 420.0)
        if not _disc_clearance(x, z, 26.0):
            continue
        if _near_road(x, z, 24.0):
            continue
        if abs(height_at(x, z)) > 2.5:
            continue
        # Spread them across the map (one per region, not a
        # cluster): at least 300 from each other and not all
        # in the same half.
        if any(math.hypot(x - gx, z - gz) < 300.0 for (gx, gz) in giants):
            continue
        if giants and all((x > 0) == (gx > 0) for (gx, gz) in giants) and len(giants) >= 2:
            continue
        # V10: same spread rule on z - the first V10 run put
        # two giants on the southern rim; landmarks should
        # cover the map, not share an edge.
        if giants and all((z > 0) == (gz > 0) for (gx, gz) in giants) and len(giants) >= 2:
            continue
        giants.append((x, z))
    for (gx, gz) in giants:
        h = height_at(gx, gz)
        yaw = rng.uniform(0.0, 2.0 * math.pi)
        _place_asset(acc, _tpl("giant")[0], yaw, 0.0, 0.0, 1.0, gx, -(gz), h - 0.18)
    FLORA_STATS["giants"] = giants
    print("Giant trees at: %s" % ([(round(x, 1), round(z, 1)) for (x, z) in giants],))

    FLORA_STATS["counts"] = counts
    FLORA_STATS["tris"] = len(acc[1])
    print("Flora: %s = %d tris" % (counts, len(acc[1])))
    return acc


TERRAIN_VERT_COUNT = [0]


def build_terrain():
    nx = int(2 * HX / STEP) + 1
    nz = int(2 * HZ / STEP) + 1
    verts = []
    uvs = []
    for j in range(nz):
        z = -HZ + j * STEP
        for i in range(nx):
            x = -HX + i * STEP
            h = height_at(x, z)
            # Blender (x, -z, h) -> Unity (x, h, z).
            verts.append((x, -z, h))
            uvs.append(((x + HX) / (2 * HX), (z + HZ) / (2 * HZ)))
    faces = []
    face_mats = []
    for j in range(nz - 1):
        for i in range(nx - 1):
            a = j * nx + i
            b = a + 1
            c = a + nx
            e = c + 1
            faces.append((a, c, b))
            faces.append((b, c, e))
            face_mats.append(0)
            face_mats.append(0)
    terrain_faces = len(faces)
    TERRAIN_VERT_COUNT[0] = len(verts)

    # V6: merge the flora into the same mesh (material slots 1+).
    fv, ff, fm = build_flora()
    base = len(verts)
    verts.extend(fv)
    for f in ff:
        faces.append(tuple(base + i for i in f))
    face_mats.extend(fm)
    for _ in range(len(fv)):
        uvs.append((0.0, 0.0))

    mesh = bpy.data.meshes.new("ExteriorTerrainV6")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    for idx, poly in enumerate(mesh.polygons):
        poly.material_index = face_mats[idx]
        poly.use_smooth = idx < terrain_faces
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for loop in mesh.loops:
        uv_layer.data[loop.index].uv = uvs[loop.vertex_index]

    def make_mat(name, colour, roughness, emission=None, emission_strength=0.0):
        mat = bpy.data.materials.new(name=name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (colour[0], colour[1], colour[2], 1.0)
        bsdf.inputs["Roughness"].default_value = roughness
        if emission is not None:
            bsdf.inputs["Emission Color"].default_value = (emission[0], emission[1], emission[2], 1.0)
            bsdf.inputs["Emission Strength"].default_value = emission_strength
        return mat

    mesh.materials.append(make_mat("ExteriorRegolith", (0.135, 0.145, 0.175), 0.95))
    mesh.materials.append(make_mat("FloraTrunk", (0.09, 0.13, 0.11), 0.9))
    mesh.materials.append(make_mat("FloraCap", (0.02, 0.21, 0.25), 0.55,
                                   emission=(0.06, 0.38, 0.34), emission_strength=0.45))
    mesh.materials.append(make_mat("FloraGlow", (0.35, 0.95, 0.85), 0.4,
                                   emission=(0.30, 0.95, 0.85), emission_strength=2.2))
    mesh.materials.append(make_mat("FloraCrystal", (0.15, 0.75, 0.50), 0.25,
                                   emission=(0.12, 0.70, 0.45), emission_strength=0.9))
    mesh.materials.append(make_mat("FloraShrub", (0.26, 0.15, 0.36), 0.8))
    mesh.materials.append(make_mat("FloraGrass", (0.20, 0.56, 0.38), 0.85))
    mesh.materials.append(make_mat("FloraRock", (0.32, 0.28, 0.38), 0.95))
    mesh.materials.append(make_mat("FloraCapViolet", (0.30, 0.16, 0.42), 0.6,
                                   emission=(0.22, 0.08, 0.38), emission_strength=0.5))
    mesh.materials.append(make_mat("FloraFrond", (0.05, 0.30, 0.22), 0.7,
                                   emission=(0.03, 0.25, 0.18), emission_strength=0.35))
    obj = bpy.data.objects.new("ExteriorTerrainV6", mesh)
    bpy.context.collection.objects.link(obj)
    return obj, mesh


def export_fbx(obj):
    # use_selection=False on purpose: in Blender 5.2's GUI the
    # exporter's use_selection path reads context.selected_objects,
    # which raises AttributeError when the script built its mesh
    # through bpy.data (no operator context) — hit by Patrick
    # 2026-11-12. The scene holds ONLY the terrain at this point
    # (read_factory_settings emptied it), so exporting everything
    # exports exactly the terrain.
    bpy.ops.export_scene.fbx(
        filepath=OUTPUT_FBX,
        use_selection=False,
        object_types={"MESH"},
        use_mesh_modifiers=True,
        apply_scale_options="FBX_SCALE_ALL",
        apply_unit_scale=True,
        axis_forward="-Z",
        axis_up="Y",
    )


def verify(mesh):
    ok = True

    def check(cond, msg):
        nonlocal ok
        print(("  PASS " if cond else "  FAIL ") + msg)
        if not cond:
            ok = False

    xs = [v.co.x for v in mesh.vertices]
    zs = [v.co.z for v in mesh.vertices]
    check(abs(min(xs) + HX) < 0.01 and abs(max(xs) - HX) < 0.01, "x extent +-560")
    tzv = TERRAIN_VERT_COUNT[0]
    check(abs(min(zs[:tzv])) < 5.2 + 0.01 and max(zs[:tzv]) <= 8.0 + 0.01, "terrain height clamp range")
    check(len(mesh.polygons) > 100000, "tri count %d" % len(mesh.polygons))
    # V10: kit flora costs more per piece but counts dropped;
    # the total stays in the class the S24 already proved
    # (V9's 1.10M ran "very well" on the phone, 2026-11-16).
    check(len(mesh.polygons) < 1220000, "total tris under V13 budget (%d)" % len(mesh.polygons))
    counts = FLORA_STATS.get("counts", {})
    for kind in ("tree", "frond", "crystal", "shrub", "tuft", "rock", "spire"):
        check(counts.get(kind, 0) > 0, "flora placed: %s x%d" % (kind, counts.get(kind, 0)))
    check(max(zs) <= 30.0, "flora max height %.1f <= 30 (giants reach ~26)" % max(zs))
    giants = FLORA_STATS.get("giants", [])
    check(len(giants) == 3, "giant trees placed (%d)" % len(giants))
    for (gx, gz) in giants:
        check(_disc_clearance(gx, gz, 20.0), "giant at (%.0f, %.0f) clear of discs" % (gx, gz))
        check(not _near_road(gx, gz, 18.0), "giant at (%.0f, %.0f) clear of roads" % (gx, gz))
    # Flora clearance re-check: no flora vertex inside any disc's
    # r_out (instances were placed with margins beyond it).
    fv = [(v.co.x, -v.co.y) for v in mesh.vertices][tzv:]
    worst = 0
    for (vx, vz) in fv[::37]:
        for (cx, cz, r_in, r_out) in DISCS:
            if math.hypot(vx - cx, vz - cz) < r_out - 0.5:
                worst += 1
    check(worst == 0, "flora vertices clear of site discs (sampled)")
    # Sites sit at h == 0 (flat discs).
    worst = 0.0
    for (cx, cz, r_in, r_out) in DISCS:
        worst = max(worst, abs(height_at(cx, cz)))
    check(worst < 0.001, "all disc centres at h=0 (worst %.4f)" % worst)
    # Crater centres are real bowls (where the mask lets them be).
    c0 = CRATERS[0]
    h0 = height_at(c0[0], c0[1])
    check(crater_height(0.3, CRATERS[0][3], CRATERS[0][4], CRATERS[0][5]) < -3.5, "crater 1 floor is a deep bowl (profile)")
    # Normals point up on average (Blender +Z == Unity +Y here).
    # V6: average over the TERRAIN faces only - the merged flora
    # legitimately has side/down faces (trunks, cap undersides).
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(mesh)
    # V7 landform profile spot checks.
    by_type = {}
    for (cx, cz, r, dep, rimh, ct) in CRATERS:
        by_type.setdefault(ct, (dep, rimh))
    d0, _r0 = by_type[0]
    check(abs(crater_height(0.0, d0, 0.0, 0) + 0.35 * d0) < 1e-6, "saucer centre depth")
    d1, r1 = by_type[1]
    check(abs(crater_height(0.0, d1, r1, 1) + 0.80 * d1) < 1e-6
          and crater_height(0.0, d1, r1, 1) <= crater_height(0.1, d1, r1, 1) + 1e-9,
          "basin flat floor, no central peak")
    d2, _r2 = by_type[2]
    check(abs(crater_height(0.0, d2, 0.0, 2) + 0.20 * d2) < 1e-6, "hollow centre depth")
    d3, _r3 = by_type[3]
    check(crater_height(0.0, d3, 0.0, 3) > 0.0
          and crater_height(0.0, d3, 0.0, 3) < crater_height(0.14, d3, 0.0, 3),
          "geyser cone raised with vent dip")
    import numpy as _np
    _rngp = random.Random(7)
    _worst = 0.0
    for _ in range(400):
        (cx, cz, r, dep, rimh, ct) = CRATERS[_rngp.randrange(len(CRATERS))]
        q = _rngp.uniform(0.0, 2.6)
        _worst = max(_worst, abs(crater_height(q, dep, rimh, ct)
                                 - float(crater_h_np(_np.array([q]), dep, rimh, ct)[0])))
    check(_worst < 1e-9, "crater_h_np parity (worst %.2e)" % _worst)
    bm.faces.ensure_lookup_table()
    terrain_faces = int(2 * HX / STEP) * int(2 * HZ / STEP) * 2
    avg = sum(bm.faces[i].normal.z for i in range(terrain_faces)) / terrain_faces
    bm.free()
    check(avg > 0.9, "average terrain face normal up (%.3f)" % avg)
    # V8 roads: junctions clear, graph connected, driveways set.
    worst_j = 1e9
    for (jx, jz) in ROAD_JUNCTIONS[1:]:
        for (cx, cz, r_in, r_out) in DISCS:
            worst_j = min(worst_j, math.hypot(jx - cx, jz - cz) - (r_out + 8.0))
        for (cx, cz, r, dep, rimh, ct) in CRATERS:
            worst_j = min(worst_j, math.hypot(jx - cx, jz - cz) - (r + 10.0))
    check(worst_j > -0.25, "road junctions clear of discs/landforms (worst margin %.2f)" % worst_j)
    seen = {0}
    frontier = [0]
    while frontier:
        u = frontier.pop()
        for (a, b) in ROAD_EDGES:
            v = b if a == u else (a if b == u else None)
            if v is not None and v not in seen:
                seen.add(v)
                frontier.append(v)
    check(len(seen) == len(ROAD_JUNCTIONS),
          "road graph connected (%d/%d junctions)" % (len(seen), len(ROAD_JUNCTIONS)))
    check(len(ROAD_DRIVEWAYS) == 6, "driveway sites: %s" % (ROAD_DRIVEWAYS,))
    print("VERIFY: " + ("ALL PASS" if ok else "FAILURES PRESENT"))

def _smoothstep_np(a, b, x):
    import numpy as np
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def crater_h_np(q, depth, rim, ctype):
    """numpy twin of crater_height (identical math, vectorised)."""
    import numpy as np
    out = np.zeros_like(q)
    if ctype == 1:
        t = np.clip((q - 0.45) / 0.55, 0.0, 1.0)
        wall = -0.80 * depth * (1.0 - t) ** 1.2
        out = np.where(q < 1.0, np.where(q < 0.45, -0.80 * depth, wall), out)
        band = (q >= 1.0) & (q < 1.5)
        out = np.where(band, 0.30 * rim * np.sin(np.pi * (q - 1.0) / 0.5) ** 2, out)
    elif ctype == 2:
        qc = np.clip(q / 1.6, 0.0, 1.0)
        out = np.where(q < 1.6, -0.20 * depth * (0.5 + 0.5 * np.cos(np.pi * qc)) ** 1.3, out)
    elif ctype == 3:
        cone = 1.40 * depth / (1.0 + (q / 0.40) ** 2) ** 1.5
        dip = np.where(q < 0.14, 0.36 * depth * (1.0 - q / 0.14), 0.0)
        out = np.where(q < 2.2, cone - dip, out)
    else:
        qc = np.clip(q, 0.0, 1.0)
        out = np.where(q < 1.0, -0.35 * depth * (0.5 + 0.5 * np.cos(np.pi * qc)) ** 1.2, out)
    return out


def build_tint_texture():
    """Bake the soft light/shadow tint for the terrain: crater
    bowls darken, rims lighten, broad mottling, a blue-grey in
    deep hollows, faint grain. Written as ExteriorTerrainTint.png
    next to this script; the PNG's top row is v=1 (z=+240),
    matching the mesh UVs (u=(x+280)/560, v=(z+240)/480). The
    heightfield math below mirrors height_at exactly."""
    import numpy as np
    import struct
    import zlib

    W = H = 4096
    xs = np.linspace(-HX, HX, W, dtype=np.float64)
    row_z = HZ - (np.arange(H, dtype=np.float64) / (H - 1)) * (2 * HZ)
    # V6->V9: the flat base colour is replaced by a per-pixel
    # biome blend in the chunk loop (bright-day fields since V9) -
    # teal moss plains, rust flats, violet patches.
    img = np.zeros((H, W, 3), dtype=np.float64)
    rng = np.random.default_rng(20261112)
    for r0 in range(0, H, 256):
        r1 = min(H, r0 + 256)
        zz, xx = np.meshgrid(row_z[r0:r1], xs, indexing="ij")
        d = (2.4 * np.sin(0.021 * xx + 1.7) * np.sin(0.017 * zz - 0.6)
             + 1.75 * np.sin(0.043 * xx - 2.1) * np.sin(0.052 * zz + 1.2)
             + 0.95 * np.sin(0.110 * xx + 0.3) * np.sin(0.090 * zz + 2.9))
        for (cx, cz, amp, sigma) in HIGHLANDS:
            d = d + amp * np.exp(-((xx - cx) ** 2 + (zz - cz) ** 2) / (2.0 * sigma * sigma))
        ejec = np.zeros_like(xx)
        vent = np.zeros_like(xx)
        basin = np.zeros_like(xx)
        for ci, (cx, cz, r, depth, rimh, ctype) in enumerate(CRATERS):
            q = np.hypot(xx - cx, zz - cz) / r
            d = d + crater_h_np(q, depth, rimh, ctype)
            if ctype == 3:
                # Geyser: bright vent glow at the cone's heart.
                vent = np.maximum(vent, np.exp(-((q / 0.16) ** 2)))
            if ctype == 1:
                # Geode basin: pale mineral floor.
                basin = np.maximum(basin, (q < 0.45) * 0.30)
        m = np.ones_like(xx)
        for (cx, cz, r_in, r_out) in DISCS:
            m = np.minimum(m, _smoothstep_np(r_in, r_out, np.hypot(xx - cx, zz - cz)))
        m = m * (1.0 - _smoothstep_np(516.0, 560.0, np.abs(xx)))
        m = m * (1.0 - _smoothstep_np(436.0, 480.0, np.abs(zz)))
        h = np.clip(d * m, -5.2, 8.0)
        # Shading derived from the height itself, so every crater
        # type shades consistently (bowls darken, rims and central
        # peaks lighten, degraded craters shade weakly because
        # they ARE shallow).
        factor = (1.0
                  - 0.36 * np.clip(-h / 5.0, 0.0, 1.0)
                  + 0.15 * np.clip(h / 3.2, 0.0, 1.0)
                  + 0.05 * np.sin(0.011 * xx + 2.0) * np.sin(0.013 * zz - 1.0)
                  + 0.055 * np.sin(0.055 * xx + 0.8) * np.sin(0.047 * zz - 2.2)
                  + 0.03 * np.sin(0.031 * xx - 0.5) * np.sin(0.027 * zz + 1.7))
        # V6 biome blend: two low-frequency patch fields pick
        # between teal moss and rust flats; a third field lays
        # violet patches over both.
        n1 = 0.5 + 0.5 * np.sin(0.0085 * xx + 1.3) * np.sin(0.0075 * zz - 0.4)
        n2 = 0.5 + 0.5 * np.sin(0.0031 * xx - 2.0) * np.sin(0.0043 * zz + 0.8)
        bfield = 0.6 * n1 + 0.4 * n2
        rt = _smoothstep_np(0.32, 0.72, bfield)[:, :, None]
        # V9 BRIGHT DAY palette (was Alien Twilight): meadow
        # green patchworked with warm golden grass, violet
        # heath patches, soft green shading in the hollows.
        biome = np.array([0.150, 0.360, 0.130])[None, None, :] * (1.0 - rt) \
            + np.array([0.400, 0.350, 0.140])[None, None, :] * rt
        vf = (0.50 * _smoothstep_np(0.66, 0.90,  # V13: quieter heath (was 0.70)
                0.5 + 0.5 * np.sin(0.0058 * xx + 4.0) * np.sin(0.0066 * zz + 2.2)))[:, :, None]
        biome = biome * (1.0 - vf) + np.array([0.340, 0.250, 0.480])[None, None, :] * vf
        col = biome * factor[:, :, None]
        hollow = (np.clip(-h / 5.0, 0.0, 1.0) * 0.34)[:, :, None]
        col = col * (1.0 - hollow) + np.array([0.070, 0.170, 0.110])[None, None, :] * hollow
        vn = np.clip(vent, 0.0, 1.0)[:, :, None] * 0.85
        col = col * (1.0 - vn) + np.array([0.55, 0.95, 0.90])[None, None, :] * vn
        bs = np.clip(basin, 0.0, 1.0)[:, :, None]
        col = col * (1.0 - bs) + np.array([0.50, 0.52, 0.40])[None, None, :] * bs
        # Warm sun-kissed high ground, then dusty pads on the
        # site discs so structures sit on cleared ground.
        hw = (np.clip(h / 6.0, 0.0, 1.0) * 0.12)[:, :, None]
        col = col * (1.0 - hw) + np.array([0.55, 0.46, 0.26])[None, None, :] * hw
        # V8 roads: worn dirt tracks along the network. Distance
        # to the nearest segment (junctions get a wider pad)
        # drives a soft-edged blend; a darker centre rut and a
        # sin-hash breakup keep it from reading as a vector
        # line. The site pads below still win on the discs.
        rdist = np.full(xx.shape, 1e9)
        for ((ax, az), (bx, bz)) in _road_draw_segments():
            if (max(ax, bx) < xx.min() - 8.0 or min(ax, bx) > xx.max() + 8.0
                    or max(az, bz) < zz.min() - 8.0 or min(az, bz) > zz.max() + 8.0):
                continue
            dx, dz = bx - ax, bz - az
            L2 = dx * dx + dz * dz
            tt = np.clip(((xx - ax) * dx + (zz - az) * dz) / L2, 0.0, 1.0)
            rdist = np.minimum(rdist, np.hypot(xx - (ax + tt * dx), zz - (az + tt * dz)))
        for (jx, jz) in ROAD_JUNCTIONS:
            if jx < xx.min() - 10.0 or jx > xx.max() + 10.0 or jz < zz.min() - 10.0 or jz > zz.max() + 10.0:
                continue
            rdist = np.minimum(rdist, np.hypot(xx - jx, zz - jz) - 2.5)
        # V12: cobblestone roads - staggered stone setts in
        # world space: per-stone shade hash, dark mortar at
        # the cell borders, a light bevel across each stone.
        # The V11 crisp falloff and dark edge line are kept.
        ralpha = (1.0 - _smoothstep_np(2.35, 3.05, rdist)) * 0.97
        cs = 1.25
        row = np.floor(zz / cs)
        cu = xx / cs + 0.5 * np.mod(row, 2.0)
        cv = zz / cs
        cellx = np.floor(cu)
        cellz = np.floor(cv)
        fx = cu - cellx
        fz = cv - cellz
        bord = np.minimum(np.minimum(fx, 1.0 - fx),
                          np.minimum(fz, 1.0 - fz)) * cs
        mortar = 1.0 - _smoothstep_np(0.02, 0.16, bord)
        hsh = np.sin(cellx * 127.1 + cellz * 311.7) * 43758.5453
        shade = 0.80 + 0.34 * (hsh - np.floor(hsh))
        bevel = 0.10 * (fx - 0.5) + 0.06 * (fz - 0.5)
        stone = (np.array([0.56, 0.53, 0.48])[None, None, :]
                 * (shade + bevel)[:, :, None])
        cobble = stone * (1.0 - 0.55 * mortar[:, :, None]) \
            + np.array([0.29, 0.27, 0.24])[None, None, :] \
            * (0.55 * mortar[:, :, None])
        col = col * (1.0 - ralpha[:, :, None]) + cobble * ralpha[:, :, None]
        edge = (_smoothstep_np(2.30, 2.75, rdist)
                * (1.0 - _smoothstep_np(3.05, 3.90, rdist))) * 0.55
        col = col * (1.0 - edge[:, :, None]) \
            + np.array([0.24, 0.22, 0.19])[None, None, :] * edge[:, :, None]
        padw = ((1.0 - _smoothstep_np(0.0, 0.6, m)) * 0.22)[:, :, None]
        col = col * (1.0 - padw) + np.array([0.46, 0.39, 0.26])[None, None, :] * padw
        # V9: fine speckle so the ground holds up at the Phase 1
        # camera's closest zoom, plus the per-pixel grain.
        col = col + (0.018 * np.sin(0.85 * xx + 1.0) * np.sin(0.78 * zz - 0.6))[:, :, None]
        col = col + rng.normal(0.0, 0.012, col.shape)
        img[r0:r1] = np.clip(col, 0.0, 1.0)
    data = (img * 255.0).astype(np.uint8)
    raw = b"".join(b"\x00" + data[r].tobytes() for r in range(H))

    def chunk(tag, payload):
        c = struct.pack(">I", len(payload)) + tag + payload
        return c + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 2, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 6))
           + chunk(b"IEND", b""))
    path = os.path.join(SCRIPT_DIR, "ExteriorTerrainTint.png")
    with open(path, "wb") as f:
        f.write(png)
    print("Wrote tint texture:", path)



def main():
    clear_scene()
    obj, mesh = build_terrain()
    print("Built ExteriorTerrainV13: %d verts, %d tris" % (len(mesh.vertices), len(mesh.polygons)))
    verify(mesh)
    build_tint_texture()
    export_fbx(obj)
    print("Exported:", OUTPUT_FBX)


main()
