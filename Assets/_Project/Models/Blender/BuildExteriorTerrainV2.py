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
#   dunes(x,z) = 1.5*sin(0.021x + 1.7)*sin(0.017z - 0.6)
#              + 1.1*sin(0.043x - 2.1)*sin(0.052z + 1.2)
#              + 0.6*sin(0.110x + 0.3)*sin(0.090z + 2.9)
#   highlands: 3 broad gaussian rises (table below).
#   craters: 16 analytic craters (table below): bowl
#     -depth*(0.5 + 0.5*cos(pi*q)) for q = dist/R < 1, rim
#     +rimH*sin(pi*(q-1)/0.7)^2 for 1 <= q < 1.7.
#   mask m(x,z): 0 inside each flat disc, smoothstep to 1 across
#     its blend ring; discs at the base (r 42->60), the 9 nodes
#     (10->17), the 15 camp slots (21->30), the lair (19->28),
#     all at the NEW x2 positions; border taper to 0 at the
#     mesh edge (|x| 258->280, |z| 218->240).
#   h = clamp((dunes + highlands + craters) * m, -3.6, 5.5)
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
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "ExteriorTerrainV2.fbx")

HX = 280.0
HZ = 240.0
STEP = 2.0

# Flat discs: (cx, cz, r_in, r_out) at the NEW (x2) positions.
DISCS = [
    (0.0, 0.0, 42.0, 60.0),          # base footprint
    (70.0, 50.0, 10.0, 17.0),        # Biomass 500
    (-110.0, 90.0, 10.0, 17.0),      # Biomass 1000
    (190.0, -110.0, 10.0, 17.0),     # Biomass 2000
    (-60.0, -70.0, 10.0, 17.0),      # Alloy 500
    (130.0, 110.0, 10.0, 17.0),      # Alloy 1000
    (-210.0, -130.0, 10.0, 17.0),    # Alloy 2000
    (90.0, -80.0, 10.0, 17.0),       # Helium-3 500
    (-140.0, -40.0, 10.0, 17.0),     # Helium-3 1000
    (220.0, 150.0, 10.0, 17.0),      # Helium-3 2000
    (110.0, 30.0, 21.0, 30.0),       # camp slots
    (-80.0, 160.0, 21.0, 30.0),
    (200.0, 80.0, 21.0, 30.0),
    (-170.0, 120.0, 21.0, 30.0),
    (40.0, -180.0, 21.0, 30.0),
    (-240.0, -20.0, 21.0, 30.0),
    (240.0, -160.0, 21.0, 30.0),
    (0.0, 200.0, 21.0, 30.0),
    (120.0, -200.0, 21.0, 30.0),
    (170.0, 20.0, 21.0, 30.0),
    (-190.0, 30.0, 21.0, 30.0),
    (30.0, 120.0, 21.0, 30.0),
    (-30.0, -190.0, 21.0, 30.0),
    (250.0, 60.0, 21.0, 30.0),
    (-120.0, -170.0, 21.0, 30.0),
    (-240.0, -200.0, 19.0, 28.0),    # boss lair (+ robot spot)
]

# Broad highland rises: (cx, cz, amp, sigma).
HIGHLANDS = [
    (-190.0, 150.0, 2.6, 55.0),
    (205.0, -160.0, 2.2, 48.0),
    (150.0, 170.0, 1.8, 42.0),
]

# Craters: (cx, cz, radius, depth, rim height).
CRATERS = [
    (-60.0, -120.0, 22.0, 3.1, 2.0),
    (60.0, 130.0, 18.0, 2.5, 1.6),
    (150.0, -60.0, 14.0, 2.0, 1.3),
    (-160.0, -90.0, 16.0, 2.2, 1.4),
    (230.0, 120.0, 20.0, 2.8, 1.8),
    (-230.0, 90.0, 15.0, 2.1, 1.4),
    (90.0, -150.0, 12.0, 1.7, 1.1),
    (-40.0, 40.0, 11.0, 1.5, 1.0),
    (180.0, 190.0, 16.0, 2.2, 1.4),
    (-110.0, -180.0, 19.0, 2.7, 1.7),
    (40.0, -40.0, 9.0, 1.3, 0.8),
    (260.0, -40.0, 13.0, 1.8, 1.2),
    (-260.0, -140.0, 17.0, 2.4, 1.5),
    (120.0, 60.0, 10.0, 1.4, 0.9),
    (-190.0, 190.0, 14.0, 2.0, 1.3),
    (10.0, -110.0, 13.0, 1.8, 1.2),
]


def smoothstep(a, b, x):
    if x <= a:
        return 0.0
    if x >= b:
        return 1.0
    t = (x - a) / (b - a)
    return t * t * (3.0 - 2.0 * t)


def height_at(x, z):
    """Terrain height at Unity (x, z). MIRRORED IN C# — keep the
    two implementations byte-for-byte equivalent in math."""
    d = (1.5 * math.sin(0.021 * x + 1.7) * math.sin(0.017 * z - 0.6)
         + 1.1 * math.sin(0.043 * x - 2.1) * math.sin(0.052 * z + 1.2)
         + 0.6 * math.sin(0.110 * x + 0.3) * math.sin(0.090 * z + 2.9))
    for (cx, cz, amp, sigma) in HIGHLANDS:
        dx = x - cx
        dz = z - cz
        d += amp * math.exp(-(dx * dx + dz * dz) / (2.0 * sigma * sigma))
    for (cx, cz, r, depth, rim) in CRATERS:
        dist = math.hypot(x - cx, z - cz)
        q = dist / r
        if q < 1.0:
            d += -depth * (0.5 + 0.5 * math.cos(math.pi * q))
        elif q < 1.7:
            s = math.sin(math.pi * (q - 1.0) / 0.7)
            d += rim * s * s
    m = 1.0
    for (cx, cz, r_in, r_out) in DISCS:
        dist = math.hypot(x - cx, z - cz)
        m = min(m, smoothstep(r_in, r_out, dist))
    m *= 1.0 - smoothstep(258.0, 280.0, abs(x))
    m *= 1.0 - smoothstep(218.0, 240.0, abs(z))
    h = d * m
    return max(-3.6, min(5.5, h))


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
    mesh = bpy.data.meshes.new("ExteriorTerrainV2")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for loop in mesh.loops:
        uv_layer.data[loop.index].uv = uvs[loop.vertex_index]
    mat = bpy.data.materials.new(name="ExteriorRegolith")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.56, 0.58, 0.61, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.95
    mesh.materials.append(mat)
    obj = bpy.data.objects.new("ExteriorTerrainV2", mesh)
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
    check(abs(min(xs) + HX) < 0.01 and abs(max(xs) - HX) < 0.01, "x extent +-280")
    check(abs(min(zs)) < 3.6 + 0.01 and max(zs) <= 5.5 + 0.01, "height clamp range")
    check(len(mesh.polygons) > 100000, "tri count %d" % len(mesh.polygons))
    # Sites sit at h == 0 (flat discs).
    worst = 0.0
    for (cx, cz, r_in, r_out) in DISCS:
        worst = max(worst, abs(height_at(cx, cz)))
    check(worst < 0.001, "all disc centres at h=0 (worst %.4f)" % worst)
    # Crater centres are real bowls (where the mask lets them be).
    c0 = CRATERS[0]
    h0 = height_at(c0[0], c0[1])
    check(h0 < -2.0, "crater 1 centre is a bowl (h=%.2f)" % h0)
    # Normals point up on average (Blender +Z == Unity +Y here).
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    avg = sum(f.normal.z for f in bm.faces) / max(1, len(bm.faces))
    bm.free()
    check(avg > 0.9, "average face normal up (%.3f)" % avg)
    print("VERIFY: " + ("ALL PASS" if ok else "FAILURES PRESENT"))


def main():
    clear_scene()
    obj, mesh = build_terrain()
    print("Built ExteriorTerrainV2: %d verts, %d tris" % (len(mesh.vertices), len(mesh.polygons)))
    verify(mesh)
    export_fbx(obj)
    print("Exported:", OUTPUT_FBX)


main()
