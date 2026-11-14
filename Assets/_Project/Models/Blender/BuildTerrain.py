# BuildTerrain.py — Lunar Dominion: sculpted terrain (third
# ground-work piece, after the ground texture + debris and the
# roads + props, which are done and Play-verified / built).
#
# ASSETS ONLY — no Unity code. The BaseView.cs wiring to use this
# mesh is a separate step done later: it will REPLACE the flat
# ground plane with TerrainMesh.fbx.
#
# WHAT IT GENERATES (next to this script):
#   TerrainMesh.fbx — a single heightfield mesh covering the
#     full 132x132 ground (x/z +-66 in Unity coords; was 56x48
#     before the 2026-11-13 base redesign).
#
# HEIGHT DESIGN (Unity coords: x horizontal, z depth, y up).
# AXIS CONVENTION (confirmed by parsing the exported FBX):
# the exporter writes the mesh vertices in Blender coords and
# puts a -90 deg rotation about X in the FBX model node, so the
# effective Unity position of a Blender vertex (x, y, z) is
# (x, z, -y) — the same convention the building scripts use
# (detail on the Blender -Y face = Unity +Z). So a Unity point
# (x, z) is built at Blender (x, -z, h): Blender y = -Unity z.
# (Building it at Blender y = +Unity z mirrors the terrain in
# z in Unity: the mountain rise and the flat pads would land on
# the wrong side. That bug was caught from the FBX and fixed
# here before delivery; the orientation is now checked on the
# mesh and on the re-imported FBX.)
#   * FLAT (height 0) everywhere inside the wall, so the
#     buildings, roads, props and debris sit level exactly
#     as they do now. The wall is a rotated rectangle
#     (local x +-36, z +-22 since 2026-11-13, rotated 45 deg);
#     in world coords a point is inside when |x+z| <= 31.1127
#     AND |x-z| <= 50.9117.
#   * GENTLE rolling dunes OUTSIDE the wall only: three low
#     sinusoids (wavelengths ~6-12 units, total amplitude
#     ~0.85), ramping up smoothly from the wall line
#     (smoothstep over 3 units, zero slope at the wall, so
#     there is no step or gap where the terrain meets the
#     wall) and tapering back to 0 at the ground edges (so
#     the mesh meets the frame cleanly).
#   * A broad rise toward the mountains on the far -X and -Z
#     edges (up to ~1.1), so the terrain flows into the
#     mountain bases instead of ending abruptly. Max height
#     overall is ~1.95 (dunes + mountain rise), inside the
#     0.5-2.0 spec.
#   * FLAT PADS under the decor that sits outside the wall:
#     the 8 outer rocks are placed by the code at y 0 — if
#     the terrain rose under them they would be buried. So
#     the height is forced to 0 (smoothly, over a 2-unit
#     blend) in a disc around each outer rock. (All current
#     debris placements sit inside the redesigned wall, on
#     the flat interior, so they need no pads.) The pads are
#     small clearings; the dunes flow around them. The
#     mountains need no pads: their bases are meant to be
#     partly buried (they rise 5.5-8 units, so they still
#     tower over the terrain).
#   * Height is clamped to >= 0 everywhere (no dips below
#     the ground plane), so nothing floats and there is no
#     gap under the wall or the mountain bases.
#
# UVs: planar, u = (x+66)/132, v = (z+66)/132, so the regolith
# texture tiles exactly as on the flat plane (the
# code sets the same 19x19 tiling). The material references
# GroundRegolith.png BY NAME ONLY (path_mode STRIP on
# export, so the texture is NOT copied into this folder):
# the PNG lives in Resources/Textures/, and the wiring step
# loads it from there and sets it on the mesh material, the
# same way it does for the flat plane today. If Unity's
# importer also resolves the bare filename project-wide,
# the material works even before the code step.
#
# No colliders, no animation, no name dependencies. Grid
# 132x132 cells (133x133 vertices, 34848 tris), 1-unit spacing —
# plenty for gentle dunes (6+ samples per wavelength).
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildTerrain.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Written/tested against Blender 4.2.3 LTS here; uses no deprecated APIs,
# so it should also run on Blender 5.2 LTS.
# NOTE: the script exports next to wherever the script file was opened
# from (SCRIPT_DIR). If you open it from F:\ or Downloads, the file
# lands there — check the "exported:" line in the console / report.

import math
import os

import bpy
import numpy as np

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
TEXTURE_NAME = "GroundRegolith.png"

def find_texture():
    """Locate GroundRegolith.png. The script is run from different
    folders on different machines, so try the likely spots: next to
    the ground-texture build script (this workspace), next to this
    script, and the Unity Resources/Textures folder (found by walking
    up to an Assets folder). Returns the path or None. The texture is
    NOT required to build the mesh: the Unity wiring sets it on the
    material at runtime (BuildGround), so a missing texture only means
    the FBX material has no image reference — the mesh still builds."""
    candidates = [
        os.path.join(os.path.dirname(SCRIPT_DIR), "ground-texture-debris", TEXTURE_NAME),
        os.path.join(SCRIPT_DIR, TEXTURE_NAME),
    ]
    # Walk up from the script dir looking for an Assets folder, then
    # Resources/Textures/GroundRegolith.png under it.
    d = SCRIPT_DIR
    while True:
        assets = os.path.join(d, "Assets")
        if os.path.isdir(assets):
            candidates.append(os.path.join(
                assets, "_Project", "Resources", "Textures", TEXTURE_NAME))
            break
        parent = os.path.dirname(d)
        if parent == d:
            break
        d = parent
    for path in candidates:
        if os.path.isfile(path):
            return path
    return None

# --- ground / wall geometry (Unity coords) ---
HALF_X = 66.0
HALF_Z = 66.0
# Wall (rotated rectangle): inside when |x+z| <= 31.1127 and |x-z| <= 50.9117.
# 2026-11-13 redesigned base: wall x +-36, z +-22 (was x +-24, z +-13.5).
WALL_SUM = 31.1127   # local z +-22 * sqrt(2)
WALL_DIFF = 50.9117  # local x +-36 * sqrt(2)
RAMP_DIST = 5.0    # smoothstep ramp from the wall line to full height
# (3.0 until 2026-11-13; widened with the bigger compound so the
# dunes + mountain rise stay below the 0.8 wall top within 2 units
# of the wall, where the rotated corners reach into the rise band)
EDGE_TAPER = 2.0   # taper to 0 at the ground edges over this many units
PAD_BLEND = 2.0    # flat-pad blend distance

# --- dune sinusoids: (amplitude, fx, fz, phase) ---
# Wavelengths ~6-12 units; total amplitude 0.85.
DUNES = (
    (0.42, 0.085, 0.043, 1.3),
    (0.27, 0.052, -0.071, 4.1),
    (0.16, 0.121, 0.097, 2.6),
)
MOUNTAIN_RISE = 1.10  # broad rise toward the far -X / -Z mountain lines

# --- layout data (world space) ---
# 2026-11-13 redesigned base: world = layout rotated 45 deg,
# world x = (lx + lz) / sqrt(2), world z = (lz - lx) / sqrt(2).
# Nexus pad radius 1.75 (the Nexus model is scaled 1.75x in
# BaseView).
BUILDINGS = [
    ("Nexus", 0.0, 0.0, 1.75),
    ("Hydroponics", -4.95, 24.75, 1.0),
    ("Alloy", 24.75, -4.95, 1.0),
    ("Helium-3", 11.31, 11.31, 1.0),
    ("Marine", -24.75, 4.95, 1.0),
    ("Nova", 4.95, -24.75, 1.0),
    ("Aegis", -11.31, -11.31, 1.0),
    ("Lab", -21.21, 21.21, 1.1),
    ("Hospital", 21.21, -21.21, 1.1),
]
# Rocks (world, radius ~0.25) — the 8 BaseView rocks that sit
# outside the wall (world conversions of their layout spots);
# only these need pads.
ROCKS = [
    (35.36, -21.21), (32.53, 4.24), (36.06, -2.12), (41.01, -12.73),
    (43.13, -4.95), (30.41, 12.02), (0.0, 38.18), (31.11, -26.87),
]
# Debris placements (world, radius ~0.5) — all inside the
# redesigned wall, so the pad loop skips them; kept in sync with
# BaseView.AddDebris.
DEBRIS = [
    (0.0, -7.5), (-7.5, 0.0), (7.5, 0.0), (-0.5, 7.5),
    (14.0, -10.0), (-14.0, 20.0), (20.0, 8.0), (13.0, -9.0),
]
# Mountains (world) — the terrain rises toward the -X / -Z lines
# where BaseView rings the horizon models (2026-11-13: ring moved
# out x2.6 with the wider camera framing).
MOUNTAINS = [
    (-60.0, 26.0), (-55.0, 39.0), (-52.0, 13.0), (-55.0, -10.0),
    (-49.0, -34.0), (-36.0, -52.0), (-13.0, -60.0), (13.0, -60.0),
    (34.0, -52.0), (49.0, -42.0), (60.0, -21.0), (62.0, 3.0),
    (-44.0, 42.0),
]


def smoothstep(t):
    """Smoothstep on a numpy array (clamped 0..1, zero slope at 0 and 1)."""
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def dist_outside_wall(x, z):
    """Distance outside the wall rectangle (0 inside it)."""
    lx = np.abs(x - z) / math.sqrt(2.0)
    lz = np.abs(x + z) / math.sqrt(2.0)
    dx = np.maximum(0.0, lx - WALL_DIFF / math.sqrt(2.0))
    dz = np.maximum(0.0, lz - WALL_SUM / math.sqrt(2.0))
    return np.sqrt(dx * dx + dz * dz)


def inside_wall(x, z):
    return (np.abs(x + z) <= WALL_SUM) & (np.abs(x - z) <= WALL_DIFF)


def height_at(x, z):
    """Terrain height (Unity y) at world (x, z). Vectorized over numpy arrays."""
    x = np.asarray(x, dtype=np.float64)
    z = np.asarray(z, dtype=np.float64)
    # Ramp up from the wall line (0 inside, 1 at RAMP_DIST outside).
    ramp = smoothstep(dist_outside_wall(x, z) / RAMP_DIST)
    # Taper to 0 at the ground edges.
    edge = (smoothstep((HALF_X - np.abs(x)) / EDGE_TAPER)
            * smoothstep((HALF_Z - np.abs(z)) / EDGE_TAPER))
    # Rolling dunes (signed, mean 0).
    dunes = np.zeros_like(x)
    for (amp, fx, fz, ph) in DUNES:
        dunes = dunes + amp * np.sin(
            2.0 * math.pi * (fx * x + fz * z) + ph)
    # Broad rise toward the far -X / -Z mountain lines.
    rise_x = smoothstep((-x - 36.0) / 12.0)
    rise_z = smoothstep((-z - 36.0) / 12.0)
    mountain = MOUNTAIN_RISE * np.maximum(rise_x, rise_z)
    h = ramp * edge * (dunes + mountain)
    h = np.maximum(0.0, h)
    # Flat pads under the outer decor, so nothing placed at
    # y 0 gets buried. (The old E1 road-corridor pad went with
    # the road system removed in 2026-11; the 2026-11-13 roads
    # are painted inside the flat wall interior.)
    mask = np.ones_like(x)
    for (px, pz) in ROCKS + DEBRIS:
        if inside_wall(np.array([px]), np.array([pz]))[0]:
            continue
        r = 1.0 if (px, pz) in ROCKS else 1.2
        d = np.sqrt((x - px) ** 2 + (z - pz) ** 2)
        mask = np.minimum(mask, smoothstep((d - r) / PAD_BLEND))
    return h * mask


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in list(bpy.data.meshes):
        bpy.data.meshes.remove(block)
    for block in list(bpy.data.materials):
        bpy.data.materials.remove(block)
    for block in list(bpy.data.images):
        bpy.data.images.remove(block)


def build_terrain():
    """Build the 132x132-cell heightfield mesh. Blender coords:
    (x, y, z) = (Unity x, -Unity z, Unity y) — see the axis
    note at the top of the file. Rows are emitted in reverse
    Unity-z order so Blender y increases with the row and the
    standard (a,b,c,d) winding keeps the normals up."""
    nx, nz = 132, 132  # cells
    xs = np.linspace(-HALF_X, HALF_X, nx + 1)
    zs = np.linspace(-HALF_Z, HALF_Z, nz + 1)  # Unity z, increasing
    gx, gz = np.meshgrid(xs, zs)  # gz rows = z, gx cols = x
    h = height_at(gx, gz)  # Unity height grid, h[j, i] at (xs[i], zs[j])
    # Vertices: Blender (x, y=-z, z=h); row r is Unity z = zs[nz-r].
    verts = []
    uvs = []
    for r in range(nz + 1):
        z = zs[nz - r]          # Unity z, +24 -> -24
        yb = -z                 # Blender y, -24 -> +24 (increasing)
        for i, x in enumerate(xs):
            verts.append((float(x), float(yb), float(h[nz - r, i])))
            uvs.append(((float(x) + HALF_X) / (2.0 * HALF_X),
                        (float(z) + HALF_Z) / (2.0 * HALF_Z)))
    faces = []
    for j in range(nz):
        for i in range(nx):
            a = j * (nx + 1) + i
            b = a + 1
            c = a + (nx + 1) + 1
            d = a + (nx + 1)
            faces.append((a, b, c, d))
    mesh = bpy.data.meshes.new("TerrainMesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    # Planar UVs spanning 0-1 across the 132x132 (from the Unity
    # coords, so the regolith texture tiles exactly as on the
    # flat plane — NOT from the Blender y, which is -Unity z).
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for poly in mesh.polygons:
        for loop_idx in poly.loop_indices:
            uv_layer.data[loop_idx].uv = uvs[
                mesh.loops[loop_idx].vertex_index]
    # Smooth shading so the dunes read soft, not faceted.
    for poly in mesh.polygons:
        poly.use_smooth = True
    obj = bpy.data.objects.new("TerrainMesh", mesh)
    bpy.context.collection.objects.link(obj)
    # Material: GroundRegolith.png in Base Color, base colour white.
    # The texture is looked up (see find_texture); if it is not found
    # the material is built without it and the Unity wiring sets it at
    # runtime, so the mesh still builds.
    mat = bpy.data.materials.new(name="TerrainRegolithMat")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (1.0, 1.0, 1.0, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.95
    bsdf.inputs["Metallic"].default_value = 0.0
    texture_path = find_texture()
    if texture_path is not None:
        img = bpy.data.images.load(texture_path)
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = img
        nt.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
        print("  texture: %s" % texture_path)
    else:
        print("  WARNING: %s not found — material has no texture; "
              "the Unity wiring sets it at runtime (BuildGround)."
              % TEXTURE_NAME)
    mesh.materials.append(mat)
    return obj, mesh, h


def export_fbx(obj, filepath):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=filepath,
        use_selection=True,
        object_types={"MESH"},
        use_mesh_modifiers=True,
        apply_scale_options="FBX_SCALE_ALL",
        apply_unit_scale=True,
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,
        add_leaf_bones=False,
        path_mode="STRIP",  # reference GroundRegolith.png by name only;
        # do NOT copy it next to the FBX (it lives in Resources/Textures/).
        embed_textures=False,
    )


def report(obj, mesh, h):
    mesh.calc_loop_triangles()
    pts = [obj.matrix_world @ v.co for v in mesh.vertices]
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    zs = [p.z for p in pts]
    print("  TerrainMesh: objects=1 tris=%d bounds x=[%.2f, %.2f] "
          "y=[%.2f, %.2f] z=[%.2f, %.2f]"
          % (len(mesh.loop_triangles),
             min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    print("  height: min %.3f max %.3f mean %.3f"
          % (float(h.min()), float(h.max()), float(h.mean())))
    # Orientation check on the mesh (Blender coords): the mountain
    # rise is toward Unity -Z, i.e. Blender +Y, so the mean height
    # at Blender y >= 17 must clearly exceed Blender y <= -17
    # (both at Blender x >= 10, where only the -Z rise applies).
    far = [p for p in pts if p.x >= 25.0 and p.y >= 40.0]
    near = [p for p in pts if p.x >= 25.0 and p.y <= -40.0]
    mf = sum(p.z for p in far) / len(far)
    mn = sum(p.z for p in near) / len(near)
    print("  orientation: mean height Blender y>=40 (Unity -Z) %.3f "
          "vs y<=-40 (Unity +Z) %.3f — %s"
          % (mf, mn, "OK, rise toward Unity -Z"
             if mf > mn + 0.3 else "FAIL, mirrored?"))
    path = os.path.join(SCRIPT_DIR, "TerrainMesh.fbx")
    print("  exported: %s (%d bytes)" % (path, os.path.getsize(path)))
    return path


def verify(h):
    """Numerical checks on the height grid (133x133, x/z -66..66)."""
    xs = np.linspace(-HALF_X, HALF_X, 133)
    zs = np.linspace(-HALF_Z, HALF_Z, 133)
    gx, gz = np.meshgrid(xs, zs)
    ok = True

    def check(cond, msg):
        nonlocal ok
        print("  [%s] %s" % ("PASS" if cond else "FAIL", msg))
        if not cond:
            ok = False

    # 1. Flat (height 0) at every building centre and across each
    #    footprint (sampled on a 0.5 grid inside radius 1.1).
    for (name, bx, bz, r) in BUILDINGS:
        pts = [(bx + dx, bz + dz)
               for dx in (-1.0, -0.5, 0.0, 0.5, 1.0)
               for dz in (-1.0, -0.5, 0.0, 0.5, 1.0)
               if dx * dx + dz * dz <= r * r + 0.01]
        vals = height_at(np.array([p[0] for p in pts]),
                         np.array([p[1] for p in pts]))
        check(float(np.abs(vals).max()) < 1e-9,
              "flat at %s footprint (max |h| %.2e)" % (name, float(np.abs(vals).max())))
    # 2. Flat along the wall line (sampled every 1 unit on all 4 edges).
    # 2026-11-13 redesigned base: wall x +-36, z +-22.
    wall_pts = []
    for t in np.linspace(-36.0, 36.0, 73):
        # local (t, +-22) -> world ((t+lz)*0.707, (-t+lz)*0.707)
        for lz in (-22.0, 22.0):
            wall_pts.append(((t + lz) * math.sqrt(0.5),
                             (-t + lz) * math.sqrt(0.5)))
    for t in np.linspace(-22.0, 22.0, 45):
        for lx in (-36.0, 36.0):
            wall_pts.append(((lx + t) * math.sqrt(0.5),
                             (-lx + t) * math.sqrt(0.5)))
    vals = height_at(np.array([p[0] for p in wall_pts]),
                     np.array([p[1] for p in wall_pts]))
    # Tolerance 1e-6 (not 1e-9): the wall line sits on the smoothstep
    # boundary, so floating-point noise gives ~1e-6 there — effectively
    # flat, far below any visible height.
    check(float(np.abs(vals).max()) < 1e-6,
          "flat on the wall line, %d samples (max |h| %.2e)"
          % (len(wall_pts), float(np.abs(vals).max())))
    # 3. Height within 0.5-2.0 outside the wall (max), and below the
    #    wall top (0.8) within 2 units of the wall.
    outside = ~inside_wall(gx, gz)
    check(float(h[outside].max()) <= 2.0,
          "max height outside wall %.3f (spec <= 2.0)" % float(h[outside].max()))
    check(float(h[outside].max()) >= 0.5,
          "max height outside wall %.3f (spec >= 0.5, dunes must read)"
          % float(h[outside].max()))
    d = dist_outside_wall(gx, gz)
    near = outside & (d <= 2.0)
    check(float(h[near].max()) < 0.8,
          "height within 2 units of the wall %.3f (wall top 0.8, "
          "terrain must not bury it)" % float(h[near].max()))
    # 4. Mesh covers the full ground, edges at height 0.
    check(float(h[0, :].max()) == 0.0 and float(h[-1, :].max()) == 0.0
          and float(h[:, 0].max()) == 0.0 and float(h[:, -1].max()) == 0.0,
          "edges at height 0 (no cliff at the frame)")
    check(abs(float(h.min())) < 1e-9, "no dips below 0 (min %.3f)" % float(h.min()))
    # 5. Flat pads: h = 0 at every outer rock / debris centre.
    for (px, pz) in ROCKS + DEBRIS:
        if inside_wall(np.array([px]), np.array([pz]))[0]:
            continue
        v = float(height_at(np.array([px]), np.array([pz]))[0])
        check(v == 0.0, "flat pad at (%s, %s) h=%.3f" % (px, pz, v))
    # 6. Terrain rises toward the mountains (mean height in the
    #    mountain band is well above the outer-band mean).
    band = (np.abs(gx) >= 40.0) | (gz <= -40.0)
    band &= outside
    outer = outside & ~band
    check(float(h[band].mean()) > float(h[outer].mean()),
          "mountain band mean %.3f > outer-band mean %.3f"
          % (float(h[band].mean()), float(h[outer].mean())))
    print("  verification: %s" % ("ALL PASS" if ok else "FAILURES ABOVE"))
    return ok


if __name__ == "__main__":
    clear_scene()
    print("Sculpted terrain build:")
    obj, mesh, h = build_terrain()
    path = os.path.join(SCRIPT_DIR, "TerrainMesh.fbx")
    export_fbx(obj, path)
    report(obj, mesh, h)
    print("Verification (height grid 133x133):")
    verify(h)
