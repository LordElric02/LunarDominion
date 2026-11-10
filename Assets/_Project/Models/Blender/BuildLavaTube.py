# BuildLavaTube.py — Lunar Dominion: the sub-lunar lava tube
# cavern arena for the real-time boss fight (design doc:
# ARES-09-Boss-Encounter-Design.md, Part 2).
#
# WHAT IT IS: one static FBX set — floor, rock wall ring, ceiling
# slabs with 3 jagged cracks, stalactites, 6 cover boulders, 4
# emergency beacon poles, 3 steam vent grates, and wall pipes —
# built at REAL scale (the arena is 26 units in radius in Unity;
# the boss model is separate and placed at 3x inside it).
# Code finds gameplay pieces by name prefix: "CoverBoulder_"
# (sweep-blocking cover — colliders/layer set in Unity),
# "Beacon_" (emissive domes the lighting rig pulses), "Vent_"
# (steam particle anchors). Everything else is scenery.
#
# COORDS (Blender: x = Unity x, y = -Unity z, z = up):
# boss start is Unity (0, +10z) = Blender (0, -10); the heroes
# enter at Unity (0, -20z) = Blender (0, +20). The 3 ceiling
# cracks sit above Blender (-8, -4), (10, 8), (0, -16) — the
# Unity light shafts land there, so the ceiling slabs are placed
# to keep those points open.
#
# Textures: 3 PNGs at 1024 via the v5 numpy pipeline — pure-Python
# zlib PNG encoder (never Blender image.save()), Lightmap Pack UVs:
#   LavaTube_Rock.png (basalt walls/ceiling/boulders),
#   LavaTube_Floor.png (cracked basalt plates + regolith dust),
#   LavaTube_GlowAmber.png (beacon domes, emission material).
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildLavaTube.py
# From inside Blender: open the Scripting workspace, open this
# file, Run Script. Written/tested against Blender 4.2.3 LTS;
# should also run on Blender 5.2 LTS.
# NOTE: outputs land next to the script file (SCRIPT_DIR) — check
# the "exported:" lines. Install: copy LavaTube.fbx + the 3 PNGs
# into Assets/_Project/Resources/Models/ (ignore the .fbm sidecar,
# never rename the PNGs — the FBX references them by name).

import math
import os
import struct
import zlib

import bpy
import numpy as np

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "LavaTube.fbx")
ATLAS_SIZE = 1024
TEXTURE_NAMES = {
    "LavaRock": "LavaTube_Rock.png",
    "LavaFloor": "LavaTube_Floor.png",
    "LavaGlowAmber": "LavaTube_GlowAmber.png",
}

# Palette (linear RGB).
BASALT = (0.135, 0.145, 0.170)     # near-black blue-grey rock
FLOOR_BASE = (0.160, 0.165, 0.180)  # cracked plate floor
DARK_METAL = (0.10, 0.10, 0.12)     # poles / grates / pipes
GLOW_AMBER = (1.00, 0.62, 0.12)     # emergency beacon domes
SCORCH = (0.03, 0.03, 0.03)         # boss-start scorch ring

ARENA_R = 26.0
CRACKS = [(-8.0, -4.0), (10.0, 8.0), (0.0, -16.0)]  # Blender (x, y)


def make_material(name, rgb, metallic=0.0, roughness=0.8, emission=0.0,
                  emission_rgb=None):
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission > 0.0:
        ergb = emission_rgb if emission_rgb is not None else rgb
        bsdf.inputs["Emission Color"].default_value = (ergb[0], ergb[1], ergb[2], 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission
    return mat


def add_box(name, location, rotation_z, scale, material):
    """Box with `scale` as half-extents (the cube primitive is 2
    units). Wide boxes stay axis-aligned (the Nova v5 lesson)."""
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=(0.0, 0.0, rotation_z))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def add_cylinder(name, vertices, radius, depth, z, material, x=0.0, y=0.0,
                 rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth,
        location=(x, y, z), rotation=rotation,
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    return obj


def add_cone(name, vertices, radius_bottom, radius_top, depth, location,
             material, rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=radius_bottom, radius2=radius_top,
        depth=depth, location=location, rotation=rotation,
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    return obj


def add_sphere(name, segments, rings, radius, location, material,
               scale=(1.0, 1.0, 1.0)):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=radius, location=location
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def add_rock(name, location, scale, material, seed, subdivisions=3):
    """A displaced icosahedron chunk: per-vertex random radial
    scaling makes jagged basalt; flat shading sells the facets.
    (Blender's operator counts subdivisions=3 as 320 faces.)"""
    bpy.ops.mesh.primitive_ico_sphere_add(
        subdivisions=subdivisions, radius=1.0, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    rng = np.random.default_rng(seed)
    mesh = obj.data
    co = np.empty(len(mesh.vertices) * 3, dtype=np.float32)
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    jitter = 1.0 + rng.uniform(-0.22, 0.22, len(co)).astype(np.float32)
    co = co * jitter[:, None]
    mesh.vertices.foreach_set("co", co.ravel())
    for poly in mesh.polygons:
        poly.use_smooth = False
    mesh.update()
    obj.data.materials.append(material)
    return obj


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in list(bpy.data.meshes):
        bpy.data.meshes.remove(block)
    for block in list(bpy.data.materials):
        bpy.data.materials.remove(block)


def recalc_normals(objects):
    """Safety net (the Helium-3 dish lesson): force every face
    normal outward so Unity never culls a flipped surface."""
    for obj in objects:
        if obj.type != "MESH":
            continue
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.normals_make_consistent(inside=False)
        bpy.ops.object.mode_set(mode="OBJECT")
        obj.select_set(False)


def build():
    clear_scene()

    rock = make_material("LavaRock", BASALT, metallic=0.05, roughness=0.95)
    floor_mat = make_material("LavaFloor", FLOOR_BASE, metallic=0.05,
                              roughness=0.92)
    dark = make_material("LavaDark", DARK_METAL, metallic=0.4, roughness=0.6)
    amber = make_material("LavaGlowAmber", GLOW_AMBER, metallic=0.1,
                          roughness=0.3, emission=2.5)
    scorch = make_material("LavaScorch", SCORCH, metallic=0.0, roughness=1.0)

    objects = []
    rng = np.random.default_rng(777)

    # ---- Floor: one big disc, top surface exactly at z=0. ----
    objects.append(add_cylinder("Lava_Floor", 48, ARENA_R + 1.0, 0.5,
                                -0.25, floor_mat))
    # Scorch ring under the boss start (Blender (0, -10)).
    objects.append(add_cylinder("Lava_Scorch", 24, 4.5, 0.04, 0.021,
                                scorch, x=0.0, y=-10.0))

    # ---- Wall ring: 12 displaced rock chunks at r ~29.5, leaning
    # slightly inward, tall enough to close the tube visually. ----
    for i in range(12):
        ang = math.radians(i * 30.0 + 8.0)
        r = 29.5 + rng.uniform(-1.0, 1.0)
        loc = (math.cos(ang) * r, math.sin(ang) * r, 4.2 + rng.uniform(-0.6, 0.6))
        chunk = add_rock("Lava_Wall_%02d" % (i + 1), loc,
                         (8.6, 4.6, 6.4), rock, seed=1000 + i)
        chunk.rotation_euler = (0.0, 0.0, ang + math.pi / 2.0)
        objects.append(chunk)

    # ---- Ceiling slabs: 7 flattened chunks placed to keep the 3
    # crack points open (checked in verify). ----
    slab_centres = [(0.0, 2.0), (16.0, -6.0), (-17.0, -8.0), (-14.0, 10.0),
                    (16.0, 16.0), (-4.0, 22.0), (4.0, -25.0)]
    for i, (sx, sy) in enumerate(slab_centres):
        slab = add_rock("Lava_Ceiling_%02d" % (i + 1),
                        (sx, sy, 14.8 + rng.uniform(-0.4, 0.4)),
                        (10.2, 8.4, 2.4), rock, seed=2000 + i)
        slab.rotation_euler = (0.0, 0.0, rng.uniform(0.0, math.pi))
        objects.append(slab)

    # ---- Stalactites: cones hanging apex-down. Clusters ring each
    # crack; the rest scatter over the ceiling. ----
    stal_points = []
    for (cx, cy) in CRACKS:
        for _ in range(5):
            a = rng.uniform(0.0, math.pi * 2.0)
            rr = rng.uniform(1.5, 4.0)
            stal_points.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    for _ in range(20):
        a = rng.uniform(0.0, math.pi * 2.0)
        rr = rng.uniform(5.0, 23.0)
        stal_points.append((math.cos(a) * rr, math.sin(a) * rr))
    for i, (px, py) in enumerate(stal_points):
        depth = rng.uniform(2.0, 5.5)
        rad = rng.uniform(0.35, 0.85)
        tilt = rng.uniform(-0.10, 0.10)
        objects.append(add_cone(
            "Lava_Stalactite_%02d" % (i + 1), 6, rad, 0.02, depth,
            (px, py, 13.6 - depth / 2.0 + 0.8), rock,
            rotation=(math.pi + tilt, 0.0, rng.uniform(0.0, 0.2))))

    # ---- Cover boulders (gameplay): the doc's arena positions,
    # converted to Blender coords (y = -Unity z). ----
    cover_unity = [(9.0, -2.0), (-9.0, -2.0), (14.0, 8.0), (-14.0, 8.0),
                   (5.0, 14.0), (-5.0, 14.0)]
    for i, (ux, uz) in enumerate(cover_unity):
        sc = rng.uniform(1.5, 1.9)
        boulder = add_rock("CoverBoulder_%02d" % (i + 1),
                           (ux, -uz, sc * 0.55),
                           (sc, sc * rng.uniform(0.85, 1.05),
                            sc * rng.uniform(0.70, 0.95)),
                           rock, seed=3000 + i)
        boulder.rotation_euler = (0.0, 0.0, rng.uniform(0.0, math.pi))
        objects.append(boulder)

    # ---- Beacon poles x4 at Unity (+/-22, +/-14z). ----
    for i, (ux, uz) in enumerate(((22.0, 14.0), (22.0, -14.0),
                                  (-22.0, 14.0), (-22.0, -14.0))):
        bx, by = ux, -uz
        objects.append(add_cylinder("Beacon_%02d_Pole" % (i + 1), 8,
                                    0.09, 3.2, 1.6, dark, x=bx, y=by))
        objects.append(add_sphere("Beacon_%02d" % (i + 1), 10, 6, 0.17,
                                  (bx, by, 3.32), amber))

    # ---- Steam vent grates x3 (particle anchors for the code). ----
    vent_unity = [(-16.0, -12.0), (17.0, -6.0), (3.0, 19.0)]
    for i, (ux, uz) in enumerate(vent_unity):
        vx, vy = ux, -uz
        objects.append(add_cylinder("Vent_%02d" % (i + 1), 16, 1.1,
                                    0.07, 0.035, dark, x=vx, y=vy))
        for j, off in enumerate((-0.5, 0.0, 0.5)):
            objects.append(add_box("Vent_%02d_Slat_%d" % (i + 1, j + 1),
                                   (vx + off, vy, 0.09), 0.0,
                                   (0.09, 0.95, 0.02), rock))

    # ---- Broken pipe run along the east wall (+X). ----
    for j, pz in enumerate((0.45, 0.90)):
        objects.append(add_cylinder(
            "Lava_Pipe_%d" % (j + 1), 10, 0.20, 13.0, pz, dark,
            x=24.5, y=2.0, rotation=(math.radians(90.0), 0.0, 0.0)))
    objects.append(add_sphere("Lava_Pipe_Elbow", 8, 5, 0.24,
                              (24.5, -4.5, 0.45), dark))

    # ---- Backdrop shell: one big dark sphere around everything,
    # normals flipped inward (below), so no gap between rocks can
    # ever show through to the void behind the set. ----
    shell = add_sphere("Lava_Shell", 24, 16, 40.0, (0.0, 0.0, 2.0), rock)
    objects.append(shell)

    recalc_normals(objects)
    # Flip the shell's normals inward (recalc forced them outward).
    bpy.ops.object.select_all(action="DESELECT")
    shell.select_set(True)
    bpy.context.view_layer.objects.active = shell
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.flip_normals()
    bpy.ops.object.mode_set(mode="OBJECT")
    shell.select_set(False)
    return objects


def make_uv_atlas(objects):
    """Pack every mesh object's UV islands into one 0-1 atlas
    (Lightmap Pack), as with the other project models."""
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        if obj.type == "MESH":
            obj.select_set(True)
    bpy.context.view_layer.objects.active = next(
        o for o in objects if o.type == "MESH")
    bpy.ops.uv.lightmap_pack(
        PREF_CONTEXT="ALL_FACES", PREF_PACK_IN_ONE=True,
        PREF_BOX_DIV=12, PREF_MARGIN_DIV=0.1)
    bpy.ops.object.select_all(action="DESELECT")


def write_png(path, rgb_float):
    """Write an (H, W, 3) float 0-1 array as an 8-bit RGB PNG. Pure
    Python (zlib) — Blender's image.save() blackens generated
    pixels, so the PNG is written directly. Rows are flipped to
    match Blender's bottom-to-top pixel order."""
    h, w, _ = rgb_float.shape
    rgb = (np.clip(rgb_float, 0.0, 1.0) * 255.0).astype(np.uint8)
    rgb = rgb[::-1]
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


def _save_image(arr, filename):
    S = arr.shape[0]
    rgba = np.ones((S, S, 4), dtype=np.float32)
    rgba[..., :3] = arr
    img = bpy.data.images.new(filename, S, S, alpha=False)
    img.pixels.foreach_set(rgba.ravel())
    path = os.path.join(SCRIPT_DIR, filename)
    write_png(path, arr)
    img.filepath = path
    img.file_format = "PNG"
    return img


def _blotches(S, xx, yy, rng, count, strength_max, clip_max, r_lo=40, r_hi=160):
    out = np.zeros((S, S), dtype=np.float32)
    for _ in range(count):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(r_lo, r_hi)
        strength = rng.uniform(0.08, strength_max)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        out += strength * np.exp(-d2 / (2.0 * (r / 2.5) ** 2))
    return np.clip(out, 0.0, clip_max)


def make_basalt_texture(filename):
    """Dark basalt: near-black blue-grey, coarse tonal blotches,
    fine grain speckle, long vertical fracture streaks, and sparse
    pale dust settled in patches. Airless lunar rock."""
    S = ATLAS_SIZE
    rng = np.random.default_rng(811)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    base = np.array(BASALT, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    tone = _blotches(S, xx, yy, rng, 60, 0.22, 0.40)
    arr *= (1.0 - tone[..., None] * 0.7)
    light = _blotches(S, xx, yy, rng, 30, 0.16, 0.30)
    arr *= (1.0 + light[..., None] * 0.8)
    # Vertical fracture streaks (columnar jointing hints).
    for _ in range(44):
        x = int(rng.uniform(0, S))
        y0 = int(rng.uniform(0, S * 0.5))
        length = int(rng.uniform(120, S - y0))
        wdt = int(rng.uniform(2, 4))
        drift = 0
        for y in range(y0, min(S, y0 + length), 8):
            drift += int(rng.integers(-2, 3))
            xs = max(0, min(S - wdt, x + drift))
            arr[y:y + 8, xs:xs + wdt] *= 0.5
    speckle = rng.uniform(0.88, 1.12, (S, S)).astype(np.float32)
    arr *= speckle[..., None]
    dust = _blotches(S, xx, yy, rng, 20, 0.20, 0.30, 30, 90)
    pale = np.array((0.30, 0.31, 0.33), dtype=np.float32)
    arr = arr * (1.0 - dust[..., None] * 0.5) + pale * (dust[..., None] * 0.5)
    arr = np.clip(arr, 0.0, 1.0)
    return _save_image(arr, filename)


def make_floor_texture(filename):
    """Cracked basalt plates over regolith: big polygonal-feel
    plates (256 px cells, 8 px cracks; 128 px sub-cracks), per-
    plate brightness, pale dust drifted along the cracks, small
    impact pocks, fine speckle."""
    S = ATLAS_SIZE
    rng = np.random.default_rng(823)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    CELL, SUB = 256, 128
    cx = np.mod(xx, CELL); cy = np.mod(yy, CELL)
    line = ((cx < 8) | (cy < 8)).astype(np.float32)
    sx = np.mod(xx, SUB); sy = np.mod(yy, SUB)
    subline = ((sx < 3) | (sy < 3)).astype(np.float32)
    cell_x = (xx // CELL).astype(np.int32)
    cell_y = (yy // CELL).astype(np.int32)
    h = np.sin(cell_x * 127.1 + cell_y * 311.7) * 43758.5453
    h = h - np.floor(h)
    cell_bright = 0.85 + 0.25 * h
    base = np.array(FLOOR_BASE, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= cell_bright[..., None]
    arr *= (1.0 - subline[..., None] * 0.30)
    # Dust halo along the cracks: blur the line mask, lighten the
    # ring next to (not on) the crack.
    soft = line.copy()
    for _ in range(3):
        soft = (soft + np.roll(soft, 2, 0) + np.roll(soft, -2, 0)
                + np.roll(soft, 2, 1) + np.roll(soft, -2, 1)) / 5.0
    halo = np.clip(soft - line, 0.0, 1.0)
    pale = np.array((0.42, 0.43, 0.45), dtype=np.float32)
    arr = arr * (1.0 - halo[..., None] * 0.45) + pale * (halo[..., None] * 0.45)
    arr *= (1.0 - line[..., None] * 0.85)
    # Impact pocks: small dark gaussian dots.
    for _ in range(130):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(3, 10)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        arr *= (1.0 - 0.5 * np.exp(-d2 / (2.0 * (r / 2.0) ** 2))[..., None])
    speckle = rng.uniform(0.90, 1.10, (S, S)).astype(np.float32)
    arr *= speckle[..., None]
    arr = np.clip(arr, 0.0, 1.0)
    return _save_image(arr, filename)


def make_glow_texture(rgb, seed, filename):
    """Saturated glow texture with fine speckle (the emission
    strength on the material does the glowing)."""
    S = ATLAS_SIZE
    rng = np.random.default_rng(seed)
    speckle = rng.uniform(0.88, 1.06, (S, S)).astype(np.float32)
    base = np.array(rgb, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= speckle[..., None]
    arr = np.clip(arr, 0.0, 1.0)
    return _save_image(arr, filename)


def apply_textures(textured_materials):
    """Generate one texture per textured material and plug it into
    Base Color (base colour set to white so Unity's multiply
    returns exactly the texture)."""
    info = []
    for mat in textured_materials:
        filename = TEXTURE_NAMES[mat.name]
        if mat.name == "LavaRock":
            img = make_basalt_texture(filename)
        elif mat.name == "LavaFloor":
            img = make_floor_texture(filename)
        elif mat.name == "LavaGlowAmber":
            img = make_glow_texture(GLOW_AMBER, 829, filename)
        else:
            continue
        nt = mat.node_tree
        node = nt.nodes.new("ShaderNodeTexImage")
        node.name = "BakedTexture"
        node.image = img
        bsdf = nt.nodes["Principled BSDF"]
        nt.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Base Color"].default_value = (1.0, 1.0, 1.0, 1.0)
        path = os.path.join(SCRIPT_DIR, filename)
        info.append((mat.name, path, os.path.getsize(path)))
    return info


def export(objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        if obj.type == "MESH":
            obj.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=OUTPUT_FBX,
        use_selection=True,
        object_types={"MESH"},
        use_mesh_modifiers=True,
        apply_scale_options="FBX_SCALE_ALL",
        apply_unit_scale=True,
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,
        add_leaf_bones=False,
        path_mode="STRIP",
        embed_textures=False,
    )


def report(objects):
    total_tris = 0
    for obj in objects:
        if obj.type != "MESH":
            continue
        obj.data.calc_loop_triangles()
        total_tris += len(obj.data.loop_triangles)
    print("Lava Tube build:")
    print("  objects=%d  total_tris=%d" % (len(objects), total_tris))
    pts = [o.matrix_world @ v.co for o in objects
           if o.type == "MESH" and o.name != "Lava_Shell"
           for v in o.data.vertices]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    print("  bounds x=[%.2f, %.2f] y=[%.2f, %.2f] z=[%.2f, %.2f]"
          % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    print("  exported: %s" % OUTPUT_FBX)
    print("  fbx bytes: %d" % os.path.getsize(OUTPUT_FBX))
    for mat_name, path, size in TEXTURE_INFO:
        print("  texture %s: %s (%d bytes)" % (mat_name, path, size))


def verify(objects):
    """Checks on the built set (Blender coords)."""
    ok = True

    def check(cond, msg):
        nonlocal ok
        print("  [%s] %s" % ("PASS" if cond else "FAIL", msg))
        if not cond:
            ok = False

    by_name = {o.name: o for o in objects}
    total_tris = 0
    for obj in objects:
        if obj.type != "MESH":
            continue
        obj.data.calc_loop_triangles()
        total_tris += len(obj.data.loop_triangles)
    check(total_tris <= 25000, "total tris %d (spec <= 25000)" % total_tris)
    check("Lava_Shell" in by_name, "backdrop shell present")
    pts = [o.matrix_world @ v.co for o in objects
           if o.type == "MESH" and o.name != "Lava_Shell"
           for v in o.data.vertices]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    check(max(xs) <= 36.0 and min(xs) >= -36.0,
          "bounds x in +/-36 (got [%.2f, %.2f])" % (min(xs), max(xs)))
    check(max(ys) <= 36.0 and min(ys) >= -36.0,
          "bounds y in +/-36 (got [%.2f, %.2f])" % (min(ys), max(ys)))
    check(max(zs) <= 18.5, "ceiling height %.2f (spec <= 18.5)" % max(zs))
    # The floor's top surface must sit exactly at z=0 (gameplay plane).
    floor = by_name.get("Lava_Floor")
    if floor is not None:
        fz = [floor.matrix_world @ v.co for v in floor.data.vertices]
        fmax = max(p.z for p in fz)
        check(abs(fmax) < 1e-4, "floor top at z=0 (got %.4f)" % fmax)
    # Gameplay pieces by name.
    boulders = [o for o in objects if o.name.startswith("CoverBoulder_")]
    check(len(boulders) == 6, "cover boulders: %d (spec 6)" % len(boulders))
    domes = [o for o in objects
             if len(o.name) == 9 and o.name.startswith("Beacon_")]
    check(len(domes) == 4, "beacon domes: %d (spec 4)" % len(domes))
    vents = [o for o in objects
             if len(o.name) == 7 and o.name.startswith("Vent_")]
    check(len(vents) == 3, "vent grates: %d (spec 3)" % len(vents))
    # Boulders inside the arena, clear of the boss start and gate.
    for b in boulders:
        bx, by = b.location.x, b.location.y
        r = math.hypot(bx, by)
        check(6.0 <= r <= ARENA_R, "%s at r=%.1f inside the arena"
              % (b.name, r))
    # Ceiling slabs keep the crack points open.
    slabs = [o for o in objects if o.name.startswith("Lava_Ceiling_")]
    for (cx, cy) in CRACKS:
        nearest = min(math.hypot(o.location.x - cx, o.location.y - cy)
                      for o in slabs)
        check(nearest >= 6.5, "crack (%.0f, %.0f) open (nearest slab %.1f)"
              % (cx, cy, nearest))
    # Walls ring the arena, not inside it.
    walls = [o for o in objects if o.name.startswith("Lava_Wall_")]
    check(len(walls) == 12 and all(
        math.hypot(o.location.x, o.location.y) >= 27.0 for o in walls),
        "12 wall chunks ringing at r >= 27")
    bare = [o.name for o in objects
            if o.type == "MESH" and len(o.data.materials) == 0]
    check(not bare, "every mesh has a material (bare: %s)"
          % (", ".join(bare) if bare else "none"))
    print("  verification: %s" % ("ALL PASS" if ok else "FAILURES ABOVE"))
    return ok


if __name__ == "__main__":
    built = build()
    textured_mats = [m for m in bpy.data.materials
                     if m.name in TEXTURE_NAMES]
    make_uv_atlas(built)
    TEXTURE_INFO = apply_textures(textured_mats)
    export(built)
    report(built)
    print("Verification:")
    verify(built)
