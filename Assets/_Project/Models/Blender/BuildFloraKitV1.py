# BuildFloraKitV1.py - Lunar Dominion flora asset kit (TEST KIT)
#
# Why this exists: the terrain's flora (BuildExteriorTerrainV9) is
# procedural primitives - plain cones / cylinders / spheres merged
# into the terrain mesh - and reads flimsy in game. This kit models
# each flora type as a real standalone asset with a sturdier
# silhouette: buttress roots, segmented knobbly trunks, displaced
# multi-tier canopies, drooping fronds, clustered crystal shards on
# a rock mound. Same pure-python geometry approach as the terrain
# builders (verts/faces templates + a transform placer), but every
# asset is its OWN single mesh with the shared Flora* materials, so
# the kit can later be scattered into a V10 terrain bake or placed
# by code without changes.
#
# Assets (local space, base at z=0, origin at base centre):
#   FloraTreeMushroom - mushroom tree, ~7.5 tall
#   FloraTreeFrond    - drooping-frond tree, ~8.5 tall
#   FloraShrub        - dense berry shrub, ~1.9 tall
#   FloraCrystal      - crystal cluster on a rock mound, ~5 tall
#   FloraRock         - chunky rock cluster, ~2.2 tall
#   FloraTuft         - spore-grass tuft, ~3 tall
#   FloraGiantTree    - landmark mushroom tree, ~24 tall
#
# Tri budgets are enforced by the verify pass - the kit must stay
# in the same total budget class as the V9 flora it would replace.
#
# House rules (Patrick's Blender 5.2.3 GUI): exports use
# use_selection=False with ONLY the asset in the scene; nothing
# reads bpy.context.active_object / selected_objects; no
# transform_apply - all transforms are baked into the geometry by
# the placer. Outputs land next to this script.
#
# Set FLORA_KIT_PREVIEW=1 in the environment to also render the
# lineup + grove preview PNGs (headless runs only, takes a while).

import bpy
import math
import os
import random

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

# Material slots (shared with the terrain flora palette).
MAT_TRUNK = 0
MAT_CAP = 1
MAT_GLOW = 2
MAT_CRYSTAL = 3
MAT_SHRUB = 4
MAT_GRASS = 5
MAT_ROCK = 6
MAT_CAPV = 7
MAT_FROND = 8

SMOOTH_MATS = {MAT_TRUNK, MAT_CAP, MAT_SHRUB, MAT_GRASS, MAT_CAPV, MAT_FROND}

BUDGETS = {
    "FloraTreeMushroom": 1150,
    "FloraTreeFrond": 450,
    "FloraShrub": 420,
    "FloraCrystal": 220,
    "FloraRock": 220,
    "FloraTuft": 120,
    "FloraGiantTree": 1700,
}

# How far below z=0 an asset may reach. Crystals are buried on
# purpose (shards grow out of the mound, no floating tips when the
# asset sits on uneven ground); everything else stays near 0.
BASE_TOL = {"FloraCrystal": -2.6}

HEIGHTS = {  # (min, max) expected asset height for the verify pass
    "FloraTreeMushroom": (6.0, 9.0),
    "FloraTreeFrond": (6.5, 9.5),
    "FloraShrub": (1.2, 2.6),
    "FloraCrystal": (3.5, 6.0),
    "FloraRock": (1.2, 3.0),
    "FloraTuft": (1.8, 3.6),
    "FloraGiantTree": (19.0, 28.0),
}


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


# ----------------------------------------------------------------
# Geometry templates - (verts, faces) in LOCAL space, base z=0.
# All faces are triangles.
# ----------------------------------------------------------------

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


def _noisy_sphere(r, n, rings, rng, amp):
    """A sphere with per-vertex radial noise - the organic lumps
    the plain V9 primitives lack. Poles stay near r so the shape
    keeps a clean top and bottom."""
    verts, faces = _sphere(r, n, rings)
    out = []
    for idx, (x, y, z) in enumerate(verts):
        if idx == 0 or idx == len(verts) - 1:
            out.append((x, y, z))
            continue
        s = 1.0 + rng.uniform(-amp, amp)
        out.append((x * s, y * s, z * (1.0 + rng.uniform(-amp * 0.5, amp * 0.5))))
    return out, faces


def _place(acc, template, mat, yaw, tilt, tilt_dir, sx, sy, sz, tx, ty, tz):
    """Append a transformed copy of template into acc =
    (verts, faces, face_mats). Rotation: tilt toward tilt_dir,
    then yaw around Z. Scale per-axis. Same placer as the terrain
    builders."""
    verts, faces = template
    cy, sy = math.cos(yaw), math.sin(yaw)
    ct, st = math.cos(tilt), math.sin(tilt)
    ax, ay = math.cos(tilt_dir), math.sin(tilt_dir)
    av, af, am = acc
    base = len(av)
    for (x, y, z) in verts:
        x, y, z = x * sx, y * sy, z * sz
        d = x * ax + y * ay
        p = -x * ay + y * ax
        d2 = d * ct - z * st
        z2 = d * st + z * ct
        x, y, z = d2 * ax - p * ay, d2 * ay + p * ax, z2
        x, y = x * cy - y * sy, x * sy + y * cy
        av.append((x + tx, y + ty, z + tz))
    for f in faces:
        af.append(tuple(base + i for i in f))
        am.append(mat)


def _trunk(acc, rng, r_base, height, lean_x=0.0, mat=MAT_TRUNK, n=8):
    """A segmented, slightly wandering trunk: stacked tapered
    cylinders with per-segment radius jitter and lateral drift.
    Returns the (x, y, z) of the trunk top."""
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
# Asset builders - each returns (verts, faces, face_mats).
# ----------------------------------------------------------------

def build_tree_mushroom():
    rng = random.Random(11)
    acc = ([], [], [])
    _roots(acc, rng, 5, 0.34, 1.15)
    tx, ty, tz = _trunk(acc, rng, 0.85, 5.2, lean_x=0.35)
    # branch stubs
    for (h, a) in ((3.3, 0.7), (4.2, 2.6)):
        br = _cyl(0.20, 0.07, 1.35, 6)
        _place(acc, br, MAT_TRUNK, 0.0, 0.85, a, 1.0, 1.0, 1.0,
               tx * h / 5.2, ty * h / 5.2, h)
    # main canopy: two displaced squashed spheres (fuller than the
    # V9 cone) + a violet satellite canopy
    cap = _noisy_sphere(3.1, 14, 7, rng, 0.16)
    _place(acc, cap, MAT_CAP, 0.4, 0.0, 0.0, 1.0, 1.0, 0.42, tx, ty, 6.05)
    cap2 = _noisy_sphere(2.45, 12, 6, rng, 0.18)
    _place(acc, cap2, MAT_CAP, 1.7, 0.0, 0.0, 1.0, 1.0, 0.34, tx, ty, 5.55)
    sat = _noisy_sphere(1.55, 10, 5, rng, 0.18)
    _place(acc, sat, MAT_CAPV, 0.9, 0.0, 0.0, 1.0, 1.0, 0.45,
           tx + 1.9, ty - 1.2, 4.55)
    # glowing underside + hanging pods
    glow = _disc(2.45, 14, 0.0)
    _place(acc, glow, MAT_GLOW, 0.0, 0.0, 0.0, 1.0, 1.0, 1.0, tx, ty, 5.52)
    for k in range(4):
        a = rng.uniform(0, 6.28)
        px, py = tx + math.cos(a) * 1.55, ty + math.sin(a) * 1.55
        stem = _cyl(0.05, 0.05, 0.65, 5)
        _place(acc, stem, MAT_TRUNK, 0.0, 0.0, 0.0, 1.0, 1.0, -1.0, px, py, 5.5)
        pod = _sphere(0.24, 8, 5)
        _place(acc, pod, MAT_GLOW, 0.0, 0.0, 0.0, 1.0, 1.0, 1.25, px, py, 4.72)
    # crown sprout
    spr = _cone(0.30, 0.85, 7)
    _place(acc, spr, MAT_FROND, 0.0, 0.12, 1.0, 1.0, 1.0, 1.0, tx, ty, 7.15)
    tip = _octa(0.16, 0.16, 0.30)
    _place(acc, tip, MAT_GLOW, 0.0, 0.0, 0.0, 1.0, 1.0, 1.0, tx, ty, 8.15)
    return acc


def build_tree_frond():
    rng = random.Random(23)
    acc = ([], [], [])
    _roots(acc, rng, 4, 0.26, 0.9)
    tx, ty, tz = _trunk(acc, rng, 0.55, 6.3, lean_x=0.8, n=7)
    # drooping fronds: flattened octahedra angled down from crown
    for k in range(8):
        a = 2.0 * math.pi * k / 8 + rng.uniform(-0.15, 0.15)
        fr = _octa(0.55, 0.15, 2.35)
        _place(acc, fr, MAT_FROND, a, 1.02, a, 1.0, 1.0, 1.0,
               tx + math.cos(a) * 1.45, ty + math.sin(a) * 1.45, tz + 0.55)
    # glow berries at the crown
    for k in range(5):
        a = rng.uniform(0, 6.28)
        b = _octa(0.17, 0.17, 0.24)
        _place(acc, b, MAT_GLOW, 0.0, 0.0, 0.0, 1.0, 1.0, 1.0,
               tx + math.cos(a) * 0.42, ty + math.sin(a) * 0.42, tz - 0.15)
    return acc


def build_shrub():
    rng = random.Random(37)
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


def build_crystal():
    rng = random.Random(41)
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


def build_rock():
    rng = random.Random(53)
    acc = ([], [], [])
    lumps = ((1.45, 0.0, 0.0, 0.62), (1.0, 1.05, 0.35, 0.55), (0.8, -0.95, -0.4, 0.7))
    for (r, ox, oy, sq) in lumps:
        s = _noisy_sphere(r, 7, 4, rng, 0.26)
        _place(acc, s, MAT_ROCK, rng.uniform(0, 6.28), 0.0, 0.0,
               1.0, rng.uniform(0.8, 1.0), sq, ox, oy, r * sq * 0.75)
    return acc


def build_tuft():
    rng = random.Random(67)
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


def build_giant_tree():
    rng = random.Random(71)
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


BUILDERS = {
    "FloraTreeMushroom": build_tree_mushroom,
    "FloraTreeFrond": build_tree_frond,
    "FloraShrub": build_shrub,
    "FloraCrystal": build_crystal,
    "FloraRock": build_rock,
    "FloraTuft": build_tuft,
    "FloraGiantTree": build_giant_tree,
}


# ----------------------------------------------------------------
# Mesh assembly / export / verify
# ----------------------------------------------------------------

def _make_mat(name, colour, roughness, emission=None, emission_strength=0.0):
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (colour[0], colour[1], colour[2], 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (emission[0], emission[1], emission[2], 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission_strength
    return mat


def _materials():
    return [
        _make_mat("FloraTrunk", (0.10, 0.14, 0.11), 0.9),
        _make_mat("FloraCap", (0.02, 0.21, 0.25), 0.55,
                  emission=(0.06, 0.38, 0.34), emission_strength=0.45),
        _make_mat("FloraGlow", (0.35, 0.95, 0.85), 0.4,
                  emission=(0.30, 0.95, 0.85), emission_strength=2.2),
        _make_mat("FloraCrystal", (0.15, 0.75, 0.50), 0.25,
                  emission=(0.12, 0.70, 0.45), emission_strength=0.9),
        _make_mat("FloraShrub", (0.26, 0.15, 0.36), 0.8),
        _make_mat("FloraGrass", (0.20, 0.56, 0.38), 0.85),
        _make_mat("FloraRock", (0.32, 0.28, 0.38), 0.95),
        _make_mat("FloraCapViolet", (0.30, 0.16, 0.42), 0.6,
                  emission=(0.22, 0.08, 0.38), emission_strength=0.5),
        _make_mat("FloraFrond", (0.05, 0.30, 0.22), 0.7,
                  emission=(0.03, 0.25, 0.18), emission_strength=0.35),
    ]


def build_asset(name):
    clear_scene()
    verts, faces, face_mats = BUILDERS[name]()
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    for idx, poly in enumerate(mesh.polygons):
        poly.material_index = face_mats[idx]
        poly.use_smooth = face_mats[idx] in SMOOTH_MATS
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for loop in mesh.loops:
        v = verts[loop.vertex_index]
        uv_layer.data[loop.index].uv = (v[0] * 0.05, v[1] * 0.05)
    for mat in _materials():
        mesh.materials.append(mat)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj, mesh


def export_asset(name):
    # use_selection=False on purpose (Blender 5.2 GUI context
    # quirk, 2026-11-12): the scene holds ONLY this asset.
    path = os.path.join(SCRIPT_DIR, name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=False,
        object_types={"MESH"},
        use_mesh_modifiers=True,
        apply_scale_options="FBX_SCALE_ALL",
        apply_unit_scale=True,
        axis_forward="-Z",
        axis_up="Y",
    )
    return path


def verify_asset(name):
    ok = True

    def check(cond, msg):
        nonlocal ok
        print(("  PASS " if cond else "  FAIL ") + msg)
        if not cond:
            ok = False

    clear_scene()
    bpy.ops.import_scene.fbx(filepath=os.path.join(SCRIPT_DIR, name + ".fbx"))
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    check(len(meshes) == 1, "%s: exactly 1 mesh object (%d)" % (name, len(meshes)))
    if not meshes:
        return False
    mesh = meshes[0].data
    tris = sum(len(p.vertices) - 2 for p in mesh.polygons)
    check(tris <= BUDGETS[name], "%s: %d tris <= budget %d" % (name, tris, BUDGETS[name]))
    zs = [v.co.z for v in mesh.vertices]
    lo, hi = HEIGHTS[name]
    tol = BASE_TOL.get(name, -0.45)
    check(min(zs) >= tol, "%s: base at z=%.2f (>= %.2f)" % (name, min(zs), tol))
    check(lo <= max(zs) <= hi, "%s: height %.2f in [%.1f, %.1f]" % (name, max(zs), lo, hi))
    check(len(mesh.materials) >= 2, "%s: %d material slots" % (name, len(mesh.materials)))
    return ok


# ----------------------------------------------------------------
# Previews (FLORA_KIT_PREVIEW=1, headless): a lineup of all seven
# assets and a small planted grove, Cycles renders.
# ----------------------------------------------------------------

def _look_at(obj, target):
    from mathutils import Vector
    loc = Vector(obj.location)
    direction = Vector(target) - loc
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def _preview_scene(ground_r, ground_colour):
    clear_scene()
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 48
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.55, 0.70, 0.90, 1.0)
    bg.inputs[1].default_value = 0.7
    scene.world = world
    sun_data = bpy.data.lights.new("Sun", "SUN")
    sun_data.energy = 3.0
    sun_data.angle = 0.12
    sun = bpy.data.objects.new("Sun", sun_data)
    sun.rotation_euler = (math.radians(52), 0.0, math.radians(-35))
    bpy.context.collection.objects.link(sun)
    gverts, gfaces = _disc(ground_r, 40, 0.0)
    gmesh = bpy.data.meshes.new("Ground")
    gmesh.from_pydata(gverts, [], gfaces)
    gmesh.update()
    gmat = bpy.data.materials.new(name="GroundMat")
    gmat.use_nodes = True
    gmat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (
        ground_colour[0], ground_colour[1], ground_colour[2], 1.0)
    gmesh.materials.append(gmat)
    ground = bpy.data.objects.new("Ground", gmesh)
    bpy.context.collection.objects.link(ground)
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = 42
    cam_data.clip_end = 600
    cam = bpy.data.objects.new("Cam", cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam
    return scene, cam


def _import(name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(SCRIPT_DIR, name + ".fbx"))
    new = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    return new[0] if new else None


def render_previews():
    # --- lineup ---
    scene, cam = _preview_scene(70, (0.16, 0.34, 0.13))
    order = ["FloraTuft", "FloraRock", "FloraShrub", "FloraCrystal",
             "FloraTreeFrond", "FloraTreeMushroom", "FloraGiantTree"]
    x = -30.0
    for name in order:
        obj = _import(name)
        if obj is None:
            continue
        obj.location.x = x
        x += 15.0 if name == "FloraTreeMushroom" else 9.5
    cam.location = (5.0, -88.0, 17.0)
    _look_at(cam, (5.0, 0.0, 5.5))
    scene.render.filepath = os.path.join(SCRIPT_DIR, "flora_kit_lineup.png")
    bpy.ops.render.render(write_still=True)
    print("Preview:", scene.render.filepath)

    # --- grove ---
    scene, cam = _preview_scene(46, (0.15, 0.36, 0.13))
    rng = random.Random(99)
    spots = [
        ("FloraGiantTree", -14.0, -7.0, 1.0),
        ("FloraTreeMushroom", 6.0, -3.0, 1.05), ("FloraTreeMushroom", -4.0, 7.0, 0.85),
        ("FloraTreeFrond", 13.0, 5.0, 1.1), ("FloraTreeFrond", -11.0, 3.0, 0.9),
        ("FloraCrystal", 3.0, 3.5, 1.0), ("FloraCrystal", -7.5, -4.5, 0.8),
        ("FloraRock", 9.5, -6.5, 1.2), ("FloraRock", -2.0, -8.0, 0.9),
    ]
    for (name, gx, gz, s) in spots:
        obj = _import(name)
        if obj is None:
            continue
        obj.location = (gx, -gz, 0.0)
        obj.rotation_euler = (0.0, 0.0, rng.uniform(0, 6.28))
        obj.scale = (s, s, s)
    shrubs = _import("FloraShrub")
    tufts = _import("FloraTuft")
    for base, count, rad in ((shrubs, 6, 15.0), (tufts, 9, 17.0)):
        if base is None:
            continue
        for k in range(count):
            dup = base.copy()
            bpy.context.collection.objects.link(dup)
            a = rng.uniform(0, 6.28)
            d = rng.uniform(3.0, rad)
            dup.location = (math.cos(a) * d, math.sin(a) * d, 0.0)
            dup.rotation_euler = (0.0, 0.0, rng.uniform(0, 6.28))
            s = rng.uniform(0.8, 1.3)
            dup.scale = (s, s, s)
        base.location = (2.5, -1.5, 0.0)
    cam.location = (17.0, -21.0, 4.6)
    _look_at(cam, (-2.0, 0.0, 5.0))
    scene.render.filepath = os.path.join(SCRIPT_DIR, "flora_kit_grove.png")
    bpy.ops.render.render(write_still=True)
    print("Preview:", scene.render.filepath)


def main():
    all_ok = True
    for name in BUILDERS:
        obj, mesh = build_asset(name)
        tris = len(mesh.polygons)
        path = export_asset(name)
        print("Built %s: %d tris -> %s" % (name, tris, path))
    for name in BUILDERS:
        if not verify_asset(name):
            all_ok = False
    print("VERIFY ALL PASS" if all_ok else "VERIFY FAILURES - see above")
    if os.environ.get("FLORA_KIT_PREVIEW") == "1":
        render_previews()


main()
