# BuildGroundTextureV2.py — Lunar Dominion: ground regolith texture v2
# (graphics upgrade: richer colour + a normal map).
#
# v2 = the v1 regolith texture (BuildGroundTextureDebris.py) with more
# detail, plus a NEW tangent-space normal map so the ground has real
# surface relief under the scene lighting instead of reading flat.
#
# WHAT IT GENERATES (next to this script):
#   GroundRegolithV2.png — 1024x1024 SEAMLESSLY TILING regolith
#     colour texture. Same base colour as v1 (sRGB 0.56/0.58/0.61,
#     the sRGB encoding of the ground's linear 0.27/0.29/0.33) and
#     the same mean, so the ground keeps its current colour — the
#     v1 look was liked brighter, do NOT darken it. More detail than
#     v1: ~1100 pebbles (was 700), ~350 pocks (was 220), more tonal
#     patches (20 dark + 8 light, was 14 + 6), finer grain, and a new
#     feature — ~150 tiny bright mineral flecks (1-2 px) that read as
#     glinting mineral in the dust.
#   GroundRegolithV2_Normal.png — 1024x1024 SEAMLESSLY TILING
#     tangent-space normal map (OpenGL convention, green = +V), built
#     from a heightmap that shares the colour texture's pebble/pock/
#     patch fields, so the relief matches the colour. The pebbles
#     read as bumps, the pocks as dents, the patches as gentle
#     undulation. Computed with wrapped central differences, so it
#     tiles exactly like the colour texture.
#
# DEBRIS: not regenerated here — the v1 debris models (Rubble/Rocks/
# Shards) are already in the project and placed. This script is
# textures only.
#
# INSTALL (after running the script):
#   Colour: copy GroundRegolithV2.png to
#     Assets/_Project/Resources/Textures/ and rename it to
#     GroundRegolith.png (replacing the v1 texture). The PNGs keep
#     the V2 name only until this step; the code loads it by the
#     plain name "Textures/GroundRegolith".
#   Normal map: copy GroundRegolithV2_Normal.png to
#     Assets/_Project/Resources/Textures/ and rename it to
#     GroundRegolith_Normal.png. Then in Unity select it in the
#     Project window and set Texture Type to "Normal Map" (Inspector
#     -> Texture Type -> Normal Map -> Apply) — a normal map must be
#     imported as a Normal Map or Unity unpacks it wrong. The
#     BaseView.cs wiring (added with this upgrade) loads it as
#     "Textures/GroundRegolith_Normal" and sets it as the ground
#     material's _BumpMap with the same (8,7) tiling as the colour.
#     If the bumps read inverted (pebbles look like dents), flip the
#     green channel: in the Inspector, check "Flip Green Channel"
#     under the Normal Map settings, or ask Ember to regenerate it
#     with the green channel flipped.
#
# HOW THE TEXTURES ARE MADE: pixel-generated with numpy, NOT a Cycles
# bake (a bake is unusable headless). Every feature is seamless by
# construction: integer-frequency sinusoids for the tonal/grain
# variation, per-pixel noise (independent pixels cannot seam), and
# wrapped stamps for the patches/pebbles/pocks/flecks (each stamp is
# split toroidally at the edges). Written with the pure-Python zlib
# PNG encoder — Blender's image.save()/save_render() blacken
# generated pixels.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildGroundTextureV2.py
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
TEXTURE_FILE = "GroundRegolithV2.png"
NORMAL_FILE = "GroundRegolithV2_Normal.png"
TEXTURE_PATH = os.path.join(SCRIPT_DIR, TEXTURE_FILE)
NORMAL_PATH = os.path.join(SCRIPT_DIR, NORMAL_FILE)

# Ground base colour — as sRGB DISPLAY values, not the linear values.
# BaseView.cs sets the ground material to linear (0.27, 0.29, 0.33);
# a texture PNG stores sRGB, and Unity decodes sRGB -> linear on
# sample, so the PNG must hold the sRGB encoding of that linear
# colour: (0.56, 0.58, 0.61). Authoring it at (0.27, 0.29, 0.33)
# would decode to linear ~0.06 and the ground would render nearly
# black. Same base and same mean as v1 — the ground keeps its colour.
REGOLITH = (0.56, 0.58, 0.61)

TEXTURE_SIZE = 1024
# Normal-map relief strength: multiplier on the height gradient.
# 2.0 gives visible but not exaggerated relief under the scene light.
# The bump scale can also be tuned in Unity (material _BumpScale).
NORMAL_STRENGTH = 2.0


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


def stamp(arr, cx, cy, kernel):
    """Add `kernel` (2D float array) to `arr` (H, W) centred at (cx, cy),
    wrapping toroidally at the edges so the result tiles seamlessly.
    The kernel is split into up to 4 rectangles at the wrap boundary."""
    H, W = arr.shape
    kh, kw = kernel.shape
    x0 = int(round(cx - kw / 2.0))
    y0 = int(round(cy - kh / 2.0))

    def intervals(start, length, size):
        s = start % size
        if s + length <= size:
            return [(s, 0, length)]
        first = size - s
        return [(s, 0, first), (0, first, length - first)]

    for (ax, kx, nx) in intervals(x0, kw, W):
        for (ay, ky, ny) in intervals(y0, kh, H):
            arr[ay:ay + ny, ax:ax + nx] += kernel[ky:ky + ny, kx:kx + nx]


def gauss_kernel(radius, sigma, amp):
    """2D Gaussian disc kernel, half-size `radius` px, peak `amp`."""
    r = int(math.ceil(radius))
    yy, xx = np.mgrid[-r:r + 1, -r:r + 1].astype(np.float32)
    return amp * np.exp(-(xx * xx + yy * yy) / (2.0 * sigma * sigma))


def make_regolith_v2():
    """Generate the v2 regolith colour texture and its normal map.
    The colour and the heightmap share the same feature fields, so
    the relief matches the colour. Returns (colour array, normal array).
    Both PNGs are written next to the script."""
    S = TEXTURE_SIZE
    rng = np.random.default_rng(7)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    base = np.array(REGOLITH, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    # Heightmap: same features as the colour, in height units. Pebbles
    # are bumps (+), pocks are dents (-), patches are gentle
    # undulation, flecks are tiny bumps. Built alongside the colour
    # so the two stay in register.
    height = np.zeros((S, S), dtype=np.float32)
    # Large tonal variation + fine grain: integer-frequency sinusoids,
    # so the pattern repeats exactly at the tile edge. v2 adds two
    # higher-frequency terms for a finer grain read.
    var = np.zeros((S, S), dtype=np.float32)
    for (fx, fy, amp, ph) in ((2, 1, 0.030, 0.0), (3, -2, 0.020, 1.7),
                              (5, 3, 0.015, 4.2), (-4, 5, 0.012, 2.3),
                              (37, 21, 0.015, 0.5), (53, -31, 0.012, 2.0),
                              (71, 47, 0.010, 3.1),
                              (97, 61, 0.008, 1.1), (113, -79, 0.007, 5.0)):
        var += amp * np.sin(2.0 * np.pi * (fx * xx + fy * yy) / S + ph)
    arr *= (1.0 + var[..., None])
    # Per-pixel noise (independent pixels cannot seam).
    arr *= (1.0 + rng.normal(0.0, 0.012, (S, S, 1)).astype(np.float32))
    # Soft darker patches (20) and a few lighter ones (8), wrapped.
    # v2: more patches than v1 (14 + 6), for more tonal variation.
    patch = np.zeros((S, S), dtype=np.float32)
    for _ in range(20):
        px, py = rng.uniform(0, S, 2)
        pr = rng.uniform(50, 200)
        a = -rng.uniform(0.05, 0.16)
        stamp(patch, px, py, gauss_kernel(pr, pr / 2.5, a))
        stamp(height, px, py, gauss_kernel(pr, pr / 2.5, a * 0.8))
    for _ in range(8):
        px, py = rng.uniform(0, S, 2)
        pr = rng.uniform(60, 180)
        a = rng.uniform(0.04, 0.08)
        stamp(patch, px, py, gauss_kernel(pr, pr / 2.5, a))
        stamp(height, px, py, gauss_kernel(pr, pr / 2.5, a * 0.8))
    arr *= (1.0 + patch[..., None])
    # Pebbles: ~1100 small discs (4-16 px radius), each a light bump
    # with a darker shadow offset by (2, 2) px, wrapped. v2: more and
    # slightly larger than v1 (700, 5-14 px).
    peb = np.zeros((S, S), dtype=np.float32)
    for _ in range(1100):
        px, py = rng.uniform(0, S, 2)
        pr = rng.uniform(4, 16)
        a = rng.uniform(0.08, 0.16)
        stamp(peb, px, py,
              gauss_kernel(2 * pr, pr / 2.0, a))
        stamp(peb, px + 2, py + 2,
              gauss_kernel(2 * pr, pr / 2.0, -rng.uniform(0.06, 0.12)))
        stamp(height, px, py,
              gauss_kernel(2 * pr, pr / 2.0, a * 1.2))
    arr *= (1.0 + peb[..., None])
    # Tiny pocks: ~350 small dark dots (2-4 px) with a light rim, wrapped.
    # v2: more than v1 (220).
    pock = np.zeros((S, S), dtype=np.float32)
    for _ in range(350):
        px, py = rng.uniform(0, S, 2)
        pr = rng.uniform(2, 4)
        a = -rng.uniform(0.08, 0.15)
        stamp(pock, px, py,
              gauss_kernel(2 * pr, pr / 2.0, a))
        stamp(pock, px - 1, py - 1,
              gauss_kernel(2 * pr, pr / 2.0, rng.uniform(0.03, 0.06)))
        stamp(height, px, py,
              gauss_kernel(2 * pr, pr / 2.0, a * 1.2))
    arr *= (1.0 + pock[..., None])
    # Mineral flecks (NEW in v2): ~150 tiny bright specks (1-2 px)
    # that read as glinting mineral in the dust. Small positive height
    # so they catch the light in the normal map too.
    fleck = np.zeros((S, S), dtype=np.float32)
    for _ in range(150):
        px, py = rng.uniform(0, S, 2)
        pr = rng.uniform(1, 2)
        a = rng.uniform(0.15, 0.30)
        stamp(fleck, px, py,
              gauss_kernel(2 * pr, pr / 2.0, a))
        stamp(height, px, py,
              gauss_kernel(2 * pr, pr / 2.0, a * 0.8))
    arr *= (1.0 + fleck[..., None])
    arr = np.clip(arr, 0.0, 1.0)
    # Restore the base mean (the features roughly balance, but correct
    # any drift so the ground keeps its current colour).
    mean = arr.reshape(-1, 3).mean(axis=0)
    arr = np.clip(arr + (base - mean)[None, None, :], 0.0, 1.0)
    write_png(TEXTURE_PATH, arr)
    print("  texture %s: %s (%d bytes) mean RGB (%.3f, %.3f, %.3f)"
          % (TEXTURE_FILE, TEXTURE_PATH, os.path.getsize(TEXTURE_PATH),
             arr[..., 0].mean(), arr[..., 1].mean(), arr[..., 2].mean()))
    # ---- Normal map from the heightmap. Central differences with
    # np.roll (toroidal wrap), so the normal map tiles exactly like
    # the colour texture. OpenGL convention: green = +V (up in UV
    # space). The row flip in write_png keeps each texel's normal
    # attached to its texel (Unity undoes the flip when sampling by V).
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
    print("Ground regolith texture v2 build:")
    make_regolith_v2()
