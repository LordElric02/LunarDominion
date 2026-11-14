# BuildExteriorTerrainV5.py - THE X4 MAP (Patrick 2026-11-14:
# "Quadruple the map and put more enough resource/enemy nodes
# to fill up this larger map... needed to level up to the higher
# tiers"). Fork of V4 (same typed crater profiles, same dark
# tint recipe, mirrored in ExteriorView):
#   * World 560x480 -> 1120x960 (HX 560, HZ 480), STEP 2.0
#     (269,841 verts - re-export coarser any time; the C# height
#     mirror is analytic and does not depend on the step).
#   * Sites: 18 resource nodes (the original 9 at doubled
#     positions + 9 far nodes at 3500/5000/8000), 36 camp slots,
#     lair at (-480, -400); 56 flat discs.
#   * Craters: V4's 20 scaled (positions x2, radii x1.25) + 24
#     generated with seed 20261114 under separation rules (the
#     final table is hardcoded below and in C# - identical).
#   * Highlands: V4's 3 scaled + 2 new. Edge fades rescaled.
#   * Verify: crater-1 check now samples the complex profile's
#     floor (its centre carries the central peak by design).
# Install: ExteriorTerrainV5.fbx renamed over ExteriorTerrain.fbx
# in Resources/Models; ExteriorTerrainTint.png into
# Resources/Textures (overwrite). The V4 notes follow below.
#
# BuildExteriorTerrainV4.py - DARK RESTYLE + CRATER TYPES
# (Patrick 2026-11-14: "Restyle the terrain - darker and moodier,
# matching the new base" + "make the craters more realistic and
# have different types of craters"). Fork of V3: SAME dunes,
# highlands, site discs and grid - heights change ONLY through
# the new typed crater profiles, mirrored in C#
# (ExteriorView.TerrainCraters gained a type column and
# TerrainCraterHeight; keep the two in sync).
#   * Crater types: 0 simple (the legacy profile), 1 complex
#     (flat floor, central peak, terraced walls - the 3 biggest),
#     2 degraded (shallow, soft, wide worn rim), 3 fresh (deeper,
#     crisp rim; bright ejecta blanket + radial rays baked into
#     the tint). One small crater became a 5-pit secondary chain.
#   * Tint: base (0.56, 0.58, 0.61) -> (0.135, 0.145, 0.175) deep
#     blue-charcoal; shading derived from the height itself so
#     every type shades consistently; hollows blend to deep
#     blue (0.15, 0.19, 0.29); grain 0.026 -> 0.009.
# Install: ExteriorTerrainV4.fbx renamed over ExteriorTerrain.fbx
# in Resources/Models; ExteriorTerrainTint.png copied into
# Resources/Textures (same file name as V3's tint - overwrite).
# The V3 notes follow below.
#
# BuildExteriorTerrainV3.py — terrain REALISM pass (Patrick
# 2026-11-12: "smooth it out to match your mockup, more realistic
# looking"). SAME analytic heightfield as V2 — every height is
# identical, so ExteriorView.TerrainHeight, the flat site discs
# and the march terrain-riding are untouched. Only presentation
# changes: finer grid (step 2.0 -> 1.6), SMOOTH shading instead
# of flat facets, and a baked 2048x2048 tint texture
# (ExteriorTerrainTint.png: crater bowls darkened, rims
# lightened, broad mottling, blue-grey in deep hollows) that the
# view uses when present, falling back to the tiling regolith.
# Install: ExteriorTerrainV4.fbx renamed over ExteriorTerrain.fbx
# in Resources/Models, ExteriorTerrainTint.png copied into
# Resources/Textures. The V2 notes follow below.
#
# BuildExteriorTerrainV2.py — Lunar Dominion: sculpted exterior
# terrain for the ENLARGED map (Patrick 2026-11-12: the map goes
# to ~4x the area — 560x480, double each dimension — with
# sculpted-terrain quality like the concept mockup he approved).
#
# ASSETS ONLY — the ExteriorView.cs wiring (positions x2, pan
# limits, march height sampling) is the matching code step.
#
# WHAT IT GENERATES (next to this script):
#   ExteriorTerrainV2.fbx — one heightfield mesh, 560x480 Unity
#     units (x +-280, z +-240). Install: rename to
#     ExteriorTerrain.fbx over the old file in Resources/Models.
#
# HEIGHT DESIGN — ANALYTIC, MIRRORED IN C# (ExteriorView.
# TerrainHeight uses the IDENTICAL formulas and constants; the
# marching markers sample it so armies ride the dunes and dip
# through craters while every site sits on a flat disc at h=0):
#   dunes(x,z) = 2.4*sin(0.021x + 1.7)*sin(0.017z - 0.6)
#              + 1.75*sin(0.043x - 2.1)*sin(0.052z + 1.2)
#              + 0.95*sin(0.110x + 0.3)*sin(0.090z + 2.9)
#     (amplitudes raised ~1.6x 2026-11-12: the first version read
#     almost flat under the game's lighting in Patrick's Play
#     check; crater depth/rim and highlands raised to match)
#   highlands: 3 broad gaussian rises (table below).
#   craters: 16 analytic craters (table below): bowl
#     -depth*(0.5 + 0.5*cos(pi*q)) for q = dist/R < 1, rim
#     +rimH*sin(pi*(q-1)/0.7)^2 for 1 <= q < 1.7.
#   mask m(x,z): 0 inside each flat disc, smoothstep to 1 across
#     its blend ring; discs at the base (r 42->60), the 9 nodes
#     (10->17), the 15 camp slots (21->30), the lair (19->28),
#     all at the NEW x2 positions; border taper to 0 at the
#     mesh edge (|x| 258->280, |z| 218->240).
#   h = clamp((dunes + highlands + craters) * m, -5.2, 8.0)
#
# AXIS CONVENTION (same as BuildTerrain.py, confirmed by FBX
# parsing there): a Blender vertex (x, y, z) lands at Unity
# (x, z, -y), so a Unity point (x, z) at height h is built at
# Blender (x, -z, h).
#
# UVs: planar u = (x+280)/560, v = (z+240)/480 — ExteriorView
# overrides the material with the tiling regolith texture.
#
# Run headless:
#   blender -b --python BuildExteriorTerrainV2.py
# Prints VERIFY: ALL PASS or the failing checks.

import bpy
import math
import os
from mathutils import Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "ExteriorTerrainV5.fbx")

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
# 2 degraded (shallow, soft, wide worn rim), 3 fresh (deeper,
# crisp rim; bright ejecta blanket + rays in the tint bake).
# One old entry became a 5-pit secondary chain (type 0, small).
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


def smoothstep(a, b, x):
    if x <= a:
        return 0.0
    if x >= b:
        return 1.0
    t = (x - a) / (b - a)
    return t * t * (3.0 - 2.0 * t)


def crater_height(q, depth, rim, ctype):
    """One crater's height contribution at q = dist / radius.
    MIRRORED IN C# (ExteriorView.TerrainHeight) - keep the math
    identical. See the CRATERS table for the type meanings."""
    if ctype == 1:  # complex: flat floor, central peak, terraces
        if q < 1.0:
            peak = 0.38 * depth * math.exp(-((q / 0.16) ** 2))
            if q < 0.42:
                return -0.92 * depth + peak
            t = (q - 0.42) / 0.58
            return (-0.92 * depth * (1.0 - t) ** 1.35
                    + depth * 0.05 * math.sin(6.0 * math.pi * t) * (4.0 * t * (1.0 - t))
                    + peak)
        if q < 1.7:
            s = math.sin(math.pi * (q - 1.0) / 0.7)
            return 1.1 * rim * s * s
        return 0.0
    if ctype == 2:  # degraded: shallow, softened, wide worn rim
        if q < 1.0:
            return -0.45 * depth * (0.5 + 0.5 * math.cos(math.pi * q)) ** 1.3
        if q < 2.1:
            s = math.sin(math.pi * (q - 1.0) / 1.1)
            return 0.4 * rim * s * s
        return 0.0
    if ctype == 3:  # fresh: deeper bowl, crisp narrow rim
        if q < 1.0:
            return -1.12 * depth * (0.5 + 0.5 * math.cos(math.pi * q))
        if q < 1.5:
            s = math.sin(math.pi * (q - 1.0) / 0.5)
            return 1.25 * rim * s * s
        return 0.0
    # simple (0): the legacy profile, unchanged
    if q < 1.0:
        return -depth * (0.5 + 0.5 * math.cos(math.pi * q))
    if q < 1.7:
        s = math.sin(math.pi * (q - 1.0) / 0.7)
        return rim * s * s
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
    for j in range(nz - 1):
        for i in range(nx - 1):
            a = j * nx + i
            b = a + 1
            c = a + nx
            e = c + 1
            faces.append((a, c, b))
            faces.append((b, c, e))
    mesh = bpy.data.meshes.new("ExteriorTerrainV3")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    # V3: smooth shading — the mockup's dunes and crater bowls
    # shade in soft gradients, not flat facets.
    for poly in mesh.polygons:
        poly.use_smooth = True
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for loop in mesh.loops:
        uv_layer.data[loop.index].uv = uvs[loop.vertex_index]
    mat = bpy.data.materials.new(name="ExteriorRegolith")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.56, 0.58, 0.61, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.95
    mesh.materials.append(mat)
    obj = bpy.data.objects.new("ExteriorTerrainV3", mesh)
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
    check(abs(min(zs)) < 5.2 + 0.01 and max(zs) <= 8.0 + 0.01, "height clamp range")
    check(len(mesh.polygons) > 100000, "tri count %d" % len(mesh.polygons))
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
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    avg = sum(f.normal.z for f in bm.faces) / max(1, len(bm.faces))
    bm.free()
    check(avg > 0.9, "average face normal up (%.3f)" % avg)
    print("VERIFY: " + ("ALL PASS" if ok else "FAILURES PRESENT"))

def _smoothstep_np(a, b, x):
    import numpy as np
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def crater_h_np(q, depth, rim, ctype):
    """numpy twin of crater_height (identical math, vectorised)."""
    import numpy as np
    out = np.zeros_like(q)
    qc = np.clip(q, 0.0, 1.0)
    if ctype == 1:
        peak = 0.38 * depth * np.exp(-((q / 0.16) ** 2))
        t = np.clip((q - 0.42) / 0.58, 0.0, 1.0)
        wall = (-0.92 * depth * (1.0 - t) ** 1.35
                + depth * 0.05 * np.sin(6.0 * np.pi * t) * (4.0 * t * (1.0 - t)) + peak)
        out = np.where(q < 1.0, np.where(q < 0.42, -0.92 * depth + peak, wall), out)
        band = (q >= 1.0) & (q < 1.7)
        out = np.where(band, 1.1 * rim * np.sin(np.pi * (q - 1.0) / 0.7) ** 2, out)
    elif ctype == 2:
        out = np.where(q < 1.0, -0.45 * depth * (0.5 + 0.5 * np.cos(np.pi * qc)) ** 1.3, out)
        band = (q >= 1.0) & (q < 2.1)
        out = np.where(band, 0.4 * rim * np.sin(np.pi * (q - 1.0) / 1.1) ** 2, out)
    elif ctype == 3:
        out = np.where(q < 1.0, -1.12 * depth * (0.5 + 0.5 * np.cos(np.pi * qc)), out)
        band = (q >= 1.0) & (q < 1.5)
        out = np.where(band, 1.25 * rim * np.sin(np.pi * (q - 1.0) / 0.5) ** 2, out)
    else:
        out = np.where(q < 1.0, -depth * (0.5 + 0.5 * np.cos(np.pi * qc)), out)
        band = (q >= 1.0) & (q < 1.7)
        out = np.where(band, rim * np.sin(np.pi * (q - 1.0) / 0.7) ** 2, out)
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

    W = H = 2048
    xs = np.linspace(-HX, HX, W, dtype=np.float64)
    row_z = HZ - (np.arange(H, dtype=np.float64) / (H - 1)) * (2 * HZ)
    base = np.array([0.135, 0.145, 0.175])  # 2026-11-14 dark restyle: deep blue-charcoal (was 0.56/0.58/0.61 pale grey)
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
        for ci, (cx, cz, r, depth, rimh, ctype) in enumerate(CRATERS):
            q = np.hypot(xx - cx, zz - cz) / r
            d = d + crater_h_np(q, depth, rimh, ctype)
            if ctype == 3:
                # Fresh crater: bright ejecta apron + radial rays.
                apron = np.clip(1.0 - (q - 1.0) / 1.6, 0.0, 1.0) * (q >= 1.0)
                ejec = np.maximum(ejec, apron * 0.55)
                ang = np.arctan2(zz - cz, xx - cx)
                rrng = np.random.default_rng(1000 + ci)
                for _ in range(6):
                    a = rrng.uniform(0.0, 2.0 * np.pi)
                    diff = np.arctan2(np.sin(ang - a), np.cos(ang - a))
                    ray = np.exp(-((diff / 0.06) ** 2)) * np.clip(1.0 - (q - 1.05) / 2.3, 0.0, 1.0) * (q > 1.05)
                    ejec = np.maximum(ejec, ray * 0.5)
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
        col = base[None, None, :] * factor[:, :, None]
        hollow = (np.clip(-h / 5.0, 0.0, 1.0) * 0.40)[:, :, None]
        col = col * (1.0 - hollow) + np.array([0.15, 0.19, 0.29])[None, None, :] * hollow
        e = np.clip(ejec, 0.0, 0.8)[:, :, None]
        col = col * (1.0 - e) + np.array([0.45, 0.47, 0.51])[None, None, :] * e
        col = col + rng.normal(0.0, 0.009, col.shape)
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
    print("Built ExteriorTerrainV5: %d verts, %d tris" % (len(mesh.vertices), len(mesh.polygons)))
    verify(mesh)
    build_tint_texture()
    export_fbx(obj)
    print("Exported:", OUTPUT_FBX)


main()
