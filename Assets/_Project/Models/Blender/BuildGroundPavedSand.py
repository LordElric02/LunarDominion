# BuildGroundPavedSand.py — Lunar Dominion: SAND variant of the
# paved base-interior texture (Option C, Patrick's pick 2026-11-18:
# regolith floor with the road network repainted on top, chosen
# because the roads are the future robot-vehicle routes).
#
# WHAT IT GENERATES (next to this script — SAME filenames as
# BuildGroundPaved.py, so installing these REPLACES the current
# floor textures; back the old PNGs up first):
#   GroundPaved.png — 2048x1024 interior colour texture, ONE TILE
#     across the whole base interior (103.5 x 63 units,
#     x +/-51.75, z +/-31.5 in BaseView layout-root local
#     space; the compound grew x1.25 then x1.5 on 2026-11-18).
#     Contents:
#       * Cracked lunar regolith base (Sand_01-style): a warm
#         grey-tan sand with a voronoi crack network, pebble
#         speckle and soft tonal patches. NO concrete plates.
#       * Roads — the SAME 2026-11-13 network as
#         BuildGroundPaved.py (same segment list: it is the route
#         network the future support vehicles will drive), drawn
#         as compacted darker tracks with pale painted edge lines
#         and dashed amber centre lines.
#       * Nexus plaza: a compacted lighter disc (radius 6) with
#         the cyan rings kept (diffuse + emission).
#       * Gate hazard stripes at the east gate, unchanged.
#     The illuminated-streak clusters of the 2026-11-14 jet-black
#     glass trial are GONE (they were light veins in glass; on
#     sand they would read as random glowing scratches).
#   GroundPaved_Emission.png — the glow layer: cyan road edges,
#     amber dashes, plaza rings, faint hazard glow. Identical
#     road/plaza geometry to the current floor, so the lit
#     network does not move.
#   GroundPaved_Normal.png — tangent-space normal map (OpenGL,
#     green = +V) from a heightmap of crack grooves + pebble
#     grain; roads and plaza are compacted flat.
#
# HOW IT IS USED: BaseView.cs BuildPavedInterior loads the three
# PNGs from Resources/Textures by the same names. No code change
# is needed for the textures themselves. (The material's
# smoothness 0.55 / metallic 0.1 in BaseView are glass values;
# a matte tweak for sand is a separate small code change.)
#
# INSTALL (after running the script): back up the current
# GroundPaved*.png in Assets/_Project/Resources/Textures/, then
# copy these three over them (no rename). GroundPaved_Normal.png
# must keep Texture Type "Normal Map" in the Inspector (the
# existing import setting carries over on overwrite).
#
# HOW THE TEXTURE IS MADE: pixel-generated with numpy, NOT a
# Cycles bake. Markings are computed in layout-root units (lx,
# lz) per pixel. Written with the pure-Python zlib PNG encoder —
# Blender's image.save() blackens generated pixels. Pure numpy:
# no bpy import, so this runs in Blender's Python AND in any
# plain Python with numpy.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildGroundPavedSand.py
# or plain: python3 BuildGroundPavedSand.py
# NOTE: the script exports next to wherever the script file was
# opened from (SCRIPT_DIR). If you open it from F:\ or Downloads,
# the files land there — check the "exported:" lines in the
# console / report.

import math
import os
import struct
import zlib

import numpy as np

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
TEXTURE_FILE = "GroundPaved.png"
NORMAL_FILE = "GroundPaved_Normal.png"
EMISSION_FILE = "GroundPaved_Emission.png"
TEXTURE_PATH = os.path.join(SCRIPT_DIR, TEXTURE_FILE)
NORMAL_PATH = os.path.join(SCRIPT_DIR, NORMAL_FILE)
EMISSION_PATH = os.path.join(SCRIPT_DIR, EMISSION_FILE)

# Interior size in layout-root local units (redesigned base,
# 2026-11-13): wall at x +/-36, z +/-22; the paving stops a margin
# inside it.
HALF_X = 51.75
HALF_Z = 31.5
TEX_W = 2048
TEX_H = 1024
NORMAL_STRENGTH = 2.0

# Colours as sRGB DISPLAY values (the PNG stores sRGB; Unity
# decodes to linear on sample — same convention as the regolith
# texture). Sand palette (2026-11-18): warm grey-tan regolith in
# the family of the kit's Sand_01 (pale cracked earth); roads are
# compacted darker tracks; painted markings are brighter than in
# the black-glass version so they read on the pale ground.
SAND = (0.520, 0.470, 0.400)
SAND_LIGHT = (0.630, 0.580, 0.500)
CRACK = (0.295, 0.255, 0.210)
PLAZA_DISC = (0.580, 0.530, 0.450)
TRACK = (0.395, 0.355, 0.295)
ROAD_EDGE = (0.720, 0.740, 0.750)
ROAD_DASH = (0.550, 0.380, 0.100)
PLAZA_RING = (0.150, 0.450, 0.520)
HAZARD_YELLOW = (0.90, 0.75, 0.20)
HAZARD_DARK = (0.15, 0.15, 0.16)

# Road network in layout-root local space (2026-11-13 redesign,
# grown with the compound — x1.25 then x1.5 on 2026-11-18):
# segments as (ax, az, bx, bz), all 3 units wide. A ring road
# around the Nexus plaza (its east/west edges extend to serve
# the four corner buildings' driveways), the main street
# running Research Lab - ring - Hospital - east gate, and the
# two spines from the plaza to the Helium-3 Harvester and
# Aegis Motor Pool. Building positions (BaseView.cs):
# Nexus (0,0); Hydroponics (-31.5,21); Alloy (31.5,21);
# Helium-3 (0,24); Marine (-31.5,-21); Nova (31.5,-21);
# Aegis (0,-24); Lab (-45,0); Hospital (45,0).
# (BuildGroundPaved.py keeps the pre-growth network — that
# generator retired with the black-glass floor.)
ROAD_SEGMENTS = [
    (-31.5, 14.25, 31.5, 14.25),      # ring north
    (-31.5, -14.25, 31.5, -14.25),    # ring south
    (-31.5, -17.25, -31.5, 17.25),    # ring west (+ corner driveways)
    (31.5, -17.25, 31.5, 17.25),      # ring east (+ corner driveways)
    (-41.25, 0.0, 54.0, 0.0),         # main street: Lab to the gate
    (0.0, 9.0, 0.0, 20.25),           # north spine to Helium-3
    (0.0, -20.25, 0.0, -9.0),         # south spine to Aegis
]
ROAD_HALF_WIDTH = 1.5
ROAD_EDGE_IN = 1.26   # edge lines live between 1.26 and 1.5
DASH_PERIOD = 2.4     # centre-dash repeat along the road
DASH_ON = 1.3         # dashed portion of each repeat
PLAZA_RADIUS = 6.0
PLAZA_INNER_RADIUS = 2.2
CRACK_CELL = 1.15     # voronoi cell size in units (fine cracks)
CRACK_CELL_BIG = 2.6  # second, larger crack scale (breaks uniformity)
CRACK_SOFT = 0.07     # F2-F1 falloff width for the crack blend


def write_png(path, rgb_float):
    """Write an (H, W, 3) float 0-1 array as an 8-bit RGB PNG. Pure
    Python (zlib) — Blender's image.save()/save_render() blacken a
    generated image's pixels on save, so the PNG is written directly
    instead. Rows are flipped so the file matches Blender's
    bottom-to-top pixel order."""
    h, w, _ = rgb_float.shape
    rgb = (np.clip(rgb_float, 0.0, 1.0) * 255.0).astype(np.uint8)
    rgb = rgb[::-1]  # Blender row 0 is the bottom; PNG row 0 is the top.
    raw = b"".join(b"\x00" + rgb[y].tobytes() for y in range(h))

    def chunk(typ, data):
        c = struct.pack(">I", len(data)) + typ + data
        return c + struct.pack(">I", zlib.crc32(typ + data) & 0xffffffff)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 6))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def dist_and_arclen(px, py, ax, ay, bx, by):
    """Distance from each point in (px, py) grids to segment A-B,
    plus the arc length along the segment of the closest point
    (for the road centre dashes). Vectorised; returns two
    (H, W) float arrays."""
    abx = bx - ax
    aby = by - ay
    ab2 = abx * abx + aby * aby
    t = ((px - ax) * abx + (py - ay) * aby) / ab2
    t = np.clip(t, 0.0, 1.0)
    cx = ax + t * abx
    cy = ay + t * aby
    dist = np.sqrt((px - cx) ** 2 + (py - cy) ** 2)
    return dist, t * math.sqrt(ab2)


def crack_metric(lx, lz, rng, cell):
    """Voronoi F2-F1 crack metric over a jittered grid with the
    given cell size: near a cell border F2-F1 approaches 0, which
    is where dry earth cracks. Only the 3x3 neighbouring cells can
    hold the two nearest sites, so nine vectorised passes suffice.
    Returns the metric in layout units (0 = on a crack line)."""
    x0, z0 = -HALF_X - cell, -HALF_Z - cell
    nx = int(math.ceil((2.0 * HALF_X + 2.0 * cell) / cell))
    nz = int(math.ceil((2.0 * HALF_Z + 2.0 * cell) / cell))
    jx = rng.uniform(-0.5, 0.5, (nz, nx)).astype(np.float32) * cell
    jz = rng.uniform(-0.5, 0.5, (nz, nx)).astype(np.float32) * cell
    cell_x = np.clip(((lx - x0) / cell).astype(np.int32), 0, nx - 1)
    cell_z = np.clip(((lz - z0) / cell).astype(np.int32), 0, nz - 1)
    d1 = np.full(lx.shape, 1e9, dtype=np.float32)
    d2 = np.full(lx.shape, 1e9, dtype=np.float32)
    for dz in (-1, 0, 1):
        for dx in (-1, 0, 1):
            cx = np.clip(cell_x + dx, 0, nx - 1)
            cz = np.clip(cell_z + dz, 0, nz - 1)
            sx = (x0 + (cx + 0.5) * cell + jx[cz, cx]).astype(np.float32)
            sz = (z0 + (cz + 0.5) * cell + jz[cz, cx]).astype(np.float32)
            dd = (lx - sx) ** 2 + (lz - sz) ** 2
            new_min = np.minimum(d1, dd)
            d2 = np.minimum(np.maximum(d1, dd), d2)
            d2 = np.where(dd < d1, d1, d2)
            d1 = new_min
    return np.sqrt(d2) - np.sqrt(d1)


def make_sand():
    """Generate the sand-interior colour texture, emission map and
    normal map. Markings are computed in world units per pixel, so
    they line up with the building positions in the layout-root
    local space — same geometry as BuildGroundPaved.py."""
    W, H = TEX_W, TEX_H
    rng = np.random.default_rng(11)
    # Pixel-centre coordinates in layout-root local units.
    # Array x -> lx (-34.5..+34.5), array y -> lz (-21..+21).
    # Array y=0 is the bottom (lz=-21); write_png flips the rows so
    # the PNG top is lz=+21 (north), matching the plane's UV v=1 at
    # local +Z.
    px = (np.arange(W, dtype=np.float32) + 0.5) / W
    py = (np.arange(H, dtype=np.float32) + 0.5) / H
    lx = (px * (2.0 * HALF_X) - HALF_X)[None, :].repeat(H, axis=0)
    lz = (py * (2.0 * HALF_Z) - HALF_Z)[:, None].repeat(W, axis=1)
    base = np.array(SAND, dtype=np.float32)
    arr = np.ones((H, W, 3), dtype=np.float32) * base
    height = np.zeros((H, W), dtype=np.float32)
    emi = np.zeros((H, W, 3), dtype=np.float32)
    # Soft tonal patches (broad warm/cool drift across the field).
    var = np.zeros((H, W), dtype=np.float32)
    for (fx, fy, amp, ph) in ((2, 1, 0.045, 0.0), (3, -2, 0.032, 1.7),
                              (5, 3, 0.022, 4.2), (29, 17, 0.012, 0.5),
                              (41, -23, 0.010, 2.0)):
        var += amp * np.sin(2.0 * np.pi * (fx * px[None, :] + fy * py[:, None]) + ph)
    arr *= (1.0 + var[..., None])
    arr *= (1.0 + rng.normal(0.0, 0.022, (H, W, 1)).astype(np.float32))
    # ---- Crack network: TWO voronoi scales (fine + coarse), so
    # the pattern does not read as uniform crazy paving. A
    # low-frequency fade field (from the tonal var) makes some
    # cracks die out entirely, like real dried ground. A faint
    # wide shoulder softens each crack's edge.
    fade = np.clip(0.65 + var * 9.0, 0.15, 1.0).astype(np.float32)
    metric = crack_metric(lx, lz, rng, CRACK_CELL)
    crack_a = np.clip((CRACK_SOFT - metric) / CRACK_SOFT, 0.0, 1.0) ** 1.2
    shoulder = np.clip((0.20 - metric) / 0.20, 0.0, 1.0) * 0.22
    metric_big = crack_metric(lx, lz, rng, CRACK_CELL_BIG)
    crack_big = np.clip((0.09 - metric_big) / 0.09, 0.0, 1.0) ** 1.2 * 0.7
    crack_a = np.clip((crack_a + shoulder + crack_big) * fade, 0.0, 1.0)
    crack_col = np.array(CRACK, dtype=np.float32)
    arr = arr * (1.0 - (crack_a * 0.85)[..., None]) + crack_col * (crack_a * 0.85)[..., None]
    height -= (crack_a * 0.55).astype(np.float32)
    # ---- Pebble speckle: sparse light stones (slight bumps) and
    # dark grit (flat), so the field has grain at building scale.
    stones = rng.random((H, W)) < 0.0035
    light_col = np.array(SAND_LIGHT, dtype=np.float32)
    arr = np.where(stones[..., None], light_col, arr)
    height += np.where(stones, 0.10, 0.0).astype(np.float32)
    grit = rng.random((H, W)) < 0.005
    arr = np.where(grit[..., None], crack_col * 1.15, arr)
    height += rng.normal(0.0, 0.02, (H, W)).astype(np.float32)
    # ---- Roads: the 2026-11-13 network (see ROAD_SEGMENTS).
    # Compacted tracks with pale painted edge lines and dashed
    # amber centre lines. Painted BEFORE the plaza so the main
    # street passes cleanly under the plaza disc. Roads are
    # compacted flat: cracks and grain are erased from the
    # heightmap under the track.
    road_any = np.zeros((H, W), dtype=bool)
    track_col = np.array(TRACK, dtype=np.float32)
    edge_col = np.array(ROAD_EDGE, dtype=np.float32)
    dash_col = np.array(ROAD_DASH, dtype=np.float32)
    for (ax, ay, bx, by) in ROAD_SEGMENTS:
        d, s = dist_and_arclen(lx, lz, ax, ay, bx, by)
        m = d < ROAD_HALF_WIDTH
        road_any |= m
        arr = np.where(m[..., None], track_col, arr)
        e = m & (d > ROAD_EDGE_IN)
        arr = np.where(e[..., None], edge_col, arr)
        dash = m & (d < 0.14) & ((s % DASH_PERIOD) < DASH_ON)
        arr = np.where(dash[..., None], dash_col, arr)
        emi = np.where(e[..., None], np.array((0.30, 0.75, 1.00), dtype=np.float32) * 0.85, emi)
        emi = np.where(dash[..., None], np.array((1.00, 0.70, 0.28), dtype=np.float32) * 0.55, emi)
    height = np.where(road_any, 0.0, height).astype(np.float32)
    # ---- Nexus plaza: compacted lighter disc + cyan rings.
    r = np.sqrt(lx * lx + lz * lz)
    disc = r < PLAZA_RADIUS
    plaza_col = np.array(PLAZA_DISC, dtype=np.float32)
    arr = np.where(disc[..., None], plaza_col, arr)
    ring_col = np.array(PLAZA_RING, dtype=np.float32)
    ring = np.abs(r - PLAZA_RADIUS) < 0.18
    arr = np.where(ring[..., None], ring_col, arr)
    inner = np.abs(r - PLAZA_INNER_RADIUS) < 0.12
    arr = np.where(inner[..., None], ring_col, arr)
    emi = np.where(ring[..., None], np.array((0.35, 0.85, 1.00), dtype=np.float32), emi)
    emi = np.where(inner[..., None], np.array((0.35, 0.85, 1.00), dtype=np.float32) * 0.9, emi)
    height = np.where(disc, 0.0, height).astype(np.float32)
    # ---- Gate hazard stripes: diagonal yellow/dark panel at the
    # east wall gate (local x +34.5, z 0), 2.2 deep x 4.4 wide.
    gate = (lx > HALF_X - 2.2) & (np.abs(lz) < 2.2)
    diag = np.floor((lx + lz) / 0.8) % 2.0
    haz_y = np.array(HAZARD_YELLOW, dtype=np.float32)
    haz_d = np.array(HAZARD_DARK, dtype=np.float32)
    haz = np.where((diag < 1.0)[..., None], haz_y, haz_d)
    arr = np.where(gate[..., None], haz, arr)
    emi = np.where((gate & (diag < 1.0))[..., None], np.array((0.90, 0.70, 0.20), dtype=np.float32) * 0.22, emi)
    # ---- Wear: a few soft dark stains so the ground reads used
    # (spills and scorch near where machines work).
    for _ in range(14):
        sx = rng.uniform(-HALF_X + 2, HALF_X - 2)
        sz = rng.uniform(-HALF_Z + 2, HALF_Z - 2)
        sr = rng.uniform(1.0, 3.0)
        sa = rng.uniform(0.04, 0.10)
        d2 = ((lx - sx) ** 2 + (lz - sz) ** 2) / (2.0 * (sr / 2.5) ** 2)
        stain = sa * np.exp(-d2)
        arr *= (1.0 - stain[..., None])
    arr = np.clip(arr, 0.0, 1.0)
    write_png(TEXTURE_PATH, arr)
    print("  texture %s: %s (%d bytes) mean RGB (%.3f, %.3f, %.3f)"
          % (TEXTURE_FILE, TEXTURE_PATH, os.path.getsize(TEXTURE_PATH),
             arr[..., 0].mean(), arr[..., 1].mean(), arr[..., 2].mean()))
    emi = np.clip(emi, 0.0, 1.0)
    write_png(EMISSION_PATH, emi)
    print("  emission %s: %s (%d bytes) mean RGB (%.3f, %.3f, %.3f)"
          % (EMISSION_FILE, EMISSION_PATH, os.path.getsize(EMISSION_PATH),
             emi[..., 0].mean(), emi[..., 1].mean(), emi[..., 2].mean()))
    # ---- Normal map from the heightmap. Central differences with
    # np.roll (toroidal wrap). The texture does not tile, so the wrap
    # is only wrong on the 1-px border at the walls — negligible.
    # OpenGL convention: green = +V. The row flip in write_png keeps
    # each texel's normal attached to its texel.
    dH_dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dH_dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    nx = -dH_dx * NORMAL_STRENGTH
    ny = -dH_dy * NORMAL_STRENGTH
    nz = np.ones_like(height)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    nx *= inv
    ny *= inv
    nz *= inv
    normal = np.stack([nx * 0.5 + 0.5,
                       ny * 0.5 + 0.5,
                       nz * 0.5 + 0.5], axis=-1).astype(np.float32)
    write_png(NORMAL_PATH, normal)
    print("  normal %s: %s (%d bytes) mean RGB (%.3f, %.3f, %.3f)"
          % (NORMAL_FILE, NORMAL_PATH, os.path.getsize(NORMAL_PATH),
             normal[..., 0].mean(), normal[..., 1].mean(),
             normal[..., 2].mean()))
    return arr, normal


if __name__ == "__main__":
    print("Sand interior texture build:")
    make_sand()
