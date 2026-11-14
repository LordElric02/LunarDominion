# BuildGroundPaved.py — Lunar Dominion: paved base-interior texture
# (graphics upgrade follow-up: paved floor inside the walls, regolith
# outside, like the reference image's spaceport floor).
#
# WHAT IT GENERATES (next to this script):
#   GroundPaved.png — 2048x1024 paved-interior colour texture, ONE
#     TILE stretched across the whole base interior (69 x 42 units,
#     x +/-34.5, z +/-21 in the BaseView layout-root local space).
#     NOT a tiling texture — the markings are positioned to line up
#     with the actual building spots, so the roads lead to the
#     right doors. Contents:
#       * Concrete plates: 3-unit plates (23 x 14) with dark seams,
#         medium cool grey (sRGB 0.45/0.46/0.49) — darker than the
#         regolith (0.56/0.58/0.61) so the paved interior reads
#         distinct from the moon surface outside the walls.
#       * Nexus plaza: a lighter concrete disc (radius 6, since
#         the 2026-11-13 redesign; was 4.5) around the Nexus with a
#         cyan ring at its edge and a thinner inner ring at radius
#         2.2 (clears the 3.5-wide Nexus footprint).
#       * Roads (2026-11-13 redesign, replacing the painted
#         walkways): a 3-unit-wide asphalt network — a ring road
#         around the plaza, a main street from the Research Lab
#         through the ring and the Hospital to the east gate, and
#         spines to the Helium-3 Harvester and Aegis Motor Pool —
#         with pale edge lines and dashed yellow centre lines. The
#         same segment list is the route network the future
#         support vehicles will drive.
#       * Gate hazard stripes: diagonal yellow/dark stripes in a
#         2.2 x 4.4 unit panel at the east wall gate (local
#         x +34.5, z 0), the 3.4-wide gate gap.
#       * Wear: bolt dots at plate corners, a few soft stains, fine
#         grain — so the floor reads used, not freshly painted.
#   GroundPaved_Normal.png — 2048x1024 tangent-space normal map
#     (OpenGL convention, green = +V) from a heightmap sharing the
#     plate seams (grooves) and plaza disc, so the paving has real
#     relief under the scene light. Painted markings are flat (no
#     height change). Same install as the regolith normal map.
#
# HOW IT IS USED: BaseView.cs builds a flat 69x42 plane as a child
# of the layout root (so it rotates with the base), at local y 0.02
# (just above the terrain's flat interior), and loads this texture
# as its _BaseMap (and the normal PNG as _BumpMap). The TerrainMesh
# keeps the regolith outside the walls; this plane covers only the
# interior. If the PNG is missing, the interior stays regolith.
#
# INSTALL (after running the script):
#   Colour: copy GroundPaved.png to
#     Assets/_Project/Resources/Textures/ (no rename needed — the
#     code loads it by "Textures/GroundPaved").
#   Normal map: copy GroundPaved_Normal.png to
#     Assets/_Project/Resources/Textures/ and set its Texture Type
#     to "Normal Map" in the Unity Inspector (same as the regolith
#     normal map). If the seams read inverted (plates look raised
#     instead of grooved), flip the green channel in the Inspector.
#
# HOW THE TEXTURE IS MADE: pixel-generated with numpy, NOT a Cycles
# bake. Markings are computed in layout-root WORLD units (lx, lz)
# per pixel, so circles are circles and lines have uniform width
# despite the texture being stretched 48x27. Written with the
# pure-Python zlib PNG encoder — Blender's image.save() blackens
# generated pixels.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildGroundPaved.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Written/tested against Blender 4.2.3 LTS here; uses no deprecated APIs,
# so it should also run on Blender 5.2 LTS.
# NOTE: the script exports next to wherever the script file was opened
# from (SCRIPT_DIR). If you open it from F:\ or Downloads, the files
# land there — check the "exported:" lines in the console / report.

import math
import os
import struct
import zlib

import bpy
import numpy as np

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
TEXTURE_FILE = "GroundPaved.png"
NORMAL_FILE = "GroundPaved_Normal.png"
TEXTURE_PATH = os.path.join(SCRIPT_DIR, TEXTURE_FILE)
NORMAL_PATH = os.path.join(SCRIPT_DIR, NORMAL_FILE)

# Interior size in layout-root local units (redesigned base,
# 2026-11-13): wall at x +/-36, z +/-22; the paving stops a margin
# inside it.
HALF_X = 34.5
HALF_Z = 21.0
TEX_W = 2048
TEX_H = 1024
NORMAL_STRENGTH = 2.0

# Colours as sRGB DISPLAY values (the PNG stores sRGB; Unity decodes
# to linear on sample — same convention as the regolith texture).
CONCRETE = (0.45, 0.46, 0.49)
PLAZA_DISC = (0.53, 0.54, 0.57)
SEAM = (0.26, 0.27, 0.30)
ASPHALT = (0.215, 0.225, 0.25)
ROAD_EDGE = (0.52, 0.54, 0.58)
ROAD_DASH = (0.85, 0.79, 0.52)
PLAZA_RING = (0.35, 0.75, 0.85)
HAZARD_YELLOW = (0.90, 0.75, 0.20)
HAZARD_DARK = (0.15, 0.15, 0.16)

# Road network in layout-root local space (2026-11-13 redesign):
# segments as (ax, az, bx, bz), all 3 units wide. A ring road
# around the Nexus plaza (its east/west edges extend to serve the
# four corner buildings' driveways), the main street running
# Research Lab - ring - Hospital - east gate, and the two spines
# from the plaza to the Helium-3 Harvester and Aegis Motor Pool.
# Building positions (BaseView.cs): Nexus (0,0); Hydroponics
# (-21,14); Alloy (21,14); Helium-3 (0,16); Marine (-21,-14);
# Nova (21,-14); Aegis (0,-16); Lab (-30,0); Hospital (30,0).
ROAD_SEGMENTS = [
    (-21.0, 9.5, 21.0, 9.5),      # ring north
    (-21.0, -9.5, 21.0, -9.5),    # ring south
    (-21.0, -11.5, -21.0, 11.5),  # ring west (+ corner driveways)
    (21.0, -11.5, 21.0, 11.5),    # ring east (+ corner driveways)
    (-27.5, 0.0, 36.0, 0.0),      # main street: Lab to the gate
    (0.0, 6.0, 0.0, 13.5),        # north spine to Helium-3
    (0.0, -13.5, 0.0, -6.0),      # south spine to Aegis
]
ROAD_HALF_WIDTH = 1.5
ROAD_EDGE_IN = 1.26   # edge lines live between 1.26 and 1.5
DASH_PERIOD = 2.4     # centre-dash repeat along the road
DASH_ON = 1.3         # dashed portion of each repeat
PLAZA_RADIUS = 6.0
PLAZA_INNER_RADIUS = 2.2
PLATE = 3.0
SEAM_HALF_WIDTH = 0.06


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


def make_paved():
    """Generate the paved-interior colour texture and its normal map.
    Markings are computed in world units per pixel, so they line up
    with the building positions in the layout-root local space."""
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
    base = np.array(CONCRETE, dtype=np.float32)
    arr = np.ones((H, W, 3), dtype=np.float32) * base
    height = np.zeros((H, W), dtype=np.float32)
    # Subtle tonal variation + fine grain (concrete, not regolith).
    var = np.zeros((H, W), dtype=np.float32)
    for (fx, fy, amp, ph) in ((2, 1, 0.020, 0.0), (3, -2, 0.014, 1.7),
                              (5, 3, 0.010, 4.2), (29, 17, 0.010, 0.5),
                              (41, -23, 0.008, 2.0)):
        var += amp * np.sin(2.0 * np.pi * (fx * px[None, :] + fy * py[:, None]) + ph)
    arr *= (1.0 + var[..., None])
    arr *= (1.0 + rng.normal(0.0, 0.008, (H, W, 1)).astype(np.float32))
    # ---- Plate seams: 3-unit plates, grooves in the heightmap.
    # Seams at lx = -34.5 + 3k and lz = -21 + 3k (edges align
    # exactly: 69/3 = 23 plates across, 42/3 = 14 plates down).
    seam_x = np.minimum((lx + HALF_X) % PLATE, PLATE - (lx + HALF_X) % PLATE)
    seam_z = np.minimum((lz + HALF_Z) % PLATE, PLATE - (lz + HALF_Z) % PLATE)
    seam = np.minimum(seam_x, seam_z)
    seam_mask = seam < SEAM_HALF_WIDTH
    # Soft seam edge (1 px fade) so the lines are not razor-sharp.
    seam_w = np.clip((SEAM_HALF_WIDTH + 0.03 - seam) / 0.03, 0.0, 1.0)
    seam_w = np.where(seam_mask, 1.0, seam_w * 0.0)
    seam_col = np.array(SEAM, dtype=np.float32)
    arr = np.where(seam_mask[..., None], seam_col, arr)
    height -= np.where(seam_mask, 0.35, 0.0).astype(np.float32)
    # Bolt dots at plate corners (small dark dots, slight dents).
    for gx in np.arange(-HALF_X + PLATE, HALF_X, PLATE):
        for gz in np.arange(-HALF_Z + PLATE, HALF_Z, PLATE):
            d = np.sqrt((lx - gx) ** 2 + (lz - gz) ** 2)
            dot = d < 0.09
            arr = np.where(dot[..., None], seam_col * 0.8, arr)
            height -= np.where(dot, 0.15, 0.0).astype(np.float32)
    # ---- Roads: the 2026-11-13 network (see ROAD_SEGMENTS).
    # Asphalt with pale edge lines and dashed yellow centre lines.
    # Painted BEFORE the plaza so the main street passes cleanly
    # under the plaza disc instead of cutting across it. Roads are
    # flat: the plate seams are erased from the heightmap under
    # the asphalt, so the road surface has no grooves.
    road_any = np.zeros((H, W), dtype=bool)
    asph_col = np.array(ASPHALT, dtype=np.float32)
    edge_col = np.array(ROAD_EDGE, dtype=np.float32)
    dash_col = np.array(ROAD_DASH, dtype=np.float32)
    for (ax, ay, bx, by) in ROAD_SEGMENTS:
        d, s = dist_and_arclen(lx, lz, ax, ay, bx, by)
        m = d < ROAD_HALF_WIDTH
        road_any |= m
        arr = np.where(m[..., None], asph_col, arr)
        e = m & (d > ROAD_EDGE_IN)
        arr = np.where(e[..., None], edge_col, arr)
        dash = m & (d < 0.14) & ((s % DASH_PERIOD) < DASH_ON)
        arr = np.where(dash[..., None], dash_col, arr)
    height = np.where(road_any, 0.0, height).astype(np.float32)
    # ---- Nexus plaza: lighter disc + cyan rings.
    r = np.sqrt(lx * lx + lz * lz)
    disc = r < PLAZA_RADIUS
    plaza_col = np.array(PLAZA_DISC, dtype=np.float32)
    arr = np.where(disc[..., None], plaza_col, arr)
    ring_col = np.array(PLAZA_RING, dtype=np.float32)
    ring = np.abs(r - PLAZA_RADIUS) < 0.18
    arr = np.where(ring[..., None], ring_col, arr)
    inner = np.abs(r - PLAZA_INNER_RADIUS) < 0.12
    arr = np.where(inner[..., None], ring_col, arr)
    height += np.where(disc, 0.05, 0.0).astype(np.float32)
    # ---- Gate hazard stripes: diagonal yellow/dark panel at the
    # east wall gate (local x +34.5, z 0), 2.2 deep x 4.4 wide.
    gate = (lx > HALF_X - 2.2) & (np.abs(lz) < 2.2)
    diag = np.floor((lx + lz) / 0.8) % 2.0
    haz_y = np.array(HAZARD_YELLOW, dtype=np.float32)
    haz_d = np.array(HAZARD_DARK, dtype=np.float32)
    haz = np.where((diag < 1.0)[..., None], haz_y, haz_d)
    arr = np.where(gate[..., None], haz, arr)
    # ---- Wear: a few soft dark stains so the floor reads used.
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
    print("Paved interior texture build:")
    make_paved()
