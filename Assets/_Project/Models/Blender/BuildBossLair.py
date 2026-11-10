# BuildBossLair.py — Lunar Dominion: first boss lair for the
# exterior Map view.
#
# WHAT IT IS: a dark, foreboding structure — a rocky cave mouth /
# ruined bunker with a big dark opening (the War Robot's home). A
# squashed rock mound with a portal frame (two rock pillars + lintel)
# around a near-black doorway, a faint RED glow from inside the
# opening (matching the robot's red eyes), hazard stripes on the
# entrance threshold, broken metal plating, and debris/rubble around
# the base. It must read as "dangerous place" from the far exterior
# camera (50,80,50), so the opening is big and dark, the red glow is
# saturated (the Hydroponics lesson: pale tints read washed-out white
# in Unity), and the spikes break the silhouette.
#
# DESIGN (Blender coords: x = Unity x, y = -Unity z, z = Unity y/up):
# footprint ~3.0 across (x/y +/-1.5), height ~1.74. A low, wide
# structure — the robot stands in front of it, not on top. The map
# renders the lair at 2x. The doorway faces +X (the camera side,
# like the Brood Tyrant's front).
#
# Textures: 3 PNGs at 1024 (Rock, Metal, Hazard) via the v5 numpy
# pipeline — pure-Python zlib PNG encoder (never Blender image.save()),
# Lightmap Pack UVs. The red glow is a solid saturated material with
# emission 2.5 (the Brood Tyrant core pattern — no texture needed).
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildBossLair.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Written/tested against Blender 4.2.3 LTS here; uses no deprecated APIs,
# so it should also run on Blender 5.2 LTS.
# NOTE: the script exports the FBX + PNGs next to wherever the script file
# was opened from (SCRIPT_DIR). If you open it from F:\ or Downloads, the
# files land there — check the "exported:" lines in the console / report.
# Install: copy BossLair.fbx + the 3 PNGs into
# Assets/_Project/Resources/Models/ (ignore the .fbm sidecar, never
# rename the PNGs — the FBX references them by name).

import math
import os
import struct
import zlib

import bpy
import numpy as np
from mathutils import Quaternion, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "BossLair.fbx")
ATLAS_SIZE = 1024
TEXTURE_NAMES = {
    "LairRock": "BossLair_Rock.png",
    "LairMetal": "BossLair_Metal.png",
    "LairHazard": "BossLair_Hazard.png",
}

# Palette (linear RGB).
ROCK_DARK = (0.19, 0.17, 0.19)   # dark rock mound / pillars
LAIR_METAL = (0.20, 0.22, 0.26)  # dark metal plates / strips
DARK_OPENING = (0.03, 0.03, 0.04)  # near-black cave mouth
GLOW_RED = (0.95, 0.08, 0.08)    # red glow from inside (saturated)
HAZARD_YELLOW = (0.95, 0.75, 0.08)  # hazard stripe yellow
HAZARD_BLACK = (0.04, 0.04, 0.04)   # hazard stripe black
GROUND_DARK = (0.10, 0.10, 0.11)  # ground disc

# Envelope: footprint +/-1.5, height 0-1.8.
MAX_HEIGHT = 1.8


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


def add_box(name, location, rotation_z, scale, material):
    """Box with `scale` as half-extents (the cube primitive is 2 units).
    Kept axis-aligned (rotation_z 0.0) for wide boxes: a rotated wide
    box's corners reach half-extent x (cos + sin) and can silently
    exceed the envelope (the Nova v5 lesson)."""
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=(0.0, 0.0, rotation_z))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def add_box_rot(name, location, rotation, scale, material):
    """Box with a full Euler rotation. Only for small pieces (the
    broken metal plate): the half-extents are small, so the rotated
    corners stay well inside the envelope."""
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def add_cone_tilted(name, vertices, radius_bottom, radius_top, depth,
                    location, material, tilt_deg=0.0, tilt_dir=(1.0, 0.0)):
    """Cone tilted outward by tilt_deg toward tilt_dir (x, y). The tilt
    axis is horizontal and perpendicular to the tilt direction, so the
    cone's +Z axis leans toward (dx, dy) by exactly tilt_deg."""
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=radius_bottom, radius2=radius_top,
        depth=depth, location=location,
    )
    obj = bpy.context.active_object
    obj.name = name
    if tilt_deg != 0.0:
        dx, dy = tilt_dir
        length = math.hypot(dx, dy)
        dx, dy = dx / length, dy / length
        axis = Vector((-dy, dx, 0.0))
        obj.rotation_mode = 'QUATERNION'
        obj.rotation_quaternion = Quaternion(axis, math.radians(tilt_deg))
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
    """Safety net (the Helium-3 dish lesson): force every face normal
    outward so Unity never culls a flipped surface."""
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

    rock = make_material("LairRock", ROCK_DARK, metallic=0.05, roughness=0.95)
    metal = make_material("LairMetal", LAIR_METAL, metallic=0.85, roughness=0.45)
    dark = make_material("LairDark", DARK_OPENING, roughness=0.95)
    glow = make_material("LairGlow", GLOW_RED, metallic=0.10, roughness=0.30,
                         emission=2.5)
    hazard = make_material("LairHazard", HAZARD_YELLOW, metallic=0.2, roughness=0.60)
    ground = make_material("LairGround", GROUND_DARK, roughness=0.90)

    objects = []

    # ---- Ground disc: flat base at z 0, the model sits on it. ----
    objects.append(add_cylinder("Lair_Ground", 12, 1.45, 0.08, 0.04, ground))

    # ---- Rock mound: squashed sphere, bottom exactly at z 0
    # (centre 0.81 = radius 1.35 x z-scale 0.60), top at 1.62. ----
    objects.append(add_sphere("Lair_Mound", 12, 6, 1.35,
                              (0.0, 0.0, 0.81), rock, (1.0, 1.0, 0.60)))

    # ---- Portal frame around the doorway (front +X): two rock
    # pillars + a lintel. Pillars are grounded (z 0-1.00) and stand
    # proud of the mound bulge; the lintel bridges them. ----
    for i, py in enumerate((0.55, -0.55)):
        objects.append(add_box("Lair_Pillar_%02d" % (i + 1),
                               (1.30, py, 0.50), 0.0, (0.18, 0.14, 0.50),
                               rock))
    objects.append(add_box("Lair_Lintel", (1.30, 0.0, 1.05), 0.0,
                           (0.18, 0.70, 0.10), rock))

    # ---- Dark cave mouth: near-black box recessed 0.13 behind the
    # pillar fronts (1.48) but proud of the mound bulge (1.25). ----
    objects.append(add_box("Lair_Opening", (1.25, 0.0, 0.50), 0.0,
                           (0.10, 0.42, 0.48), dark))
    # ---- Red glow deep inside the opening: small and low, so the
    # doorway reads as a dark cave mouth with a glow inside, not a
    # glowing door. Straddles the dark panel front (1.35). ----
    objects.append(add_box("Lair_Glow", (1.34, 0.0, 0.42), 0.0,
                           (0.02, 0.16, 0.18), glow))
    # ---- Two ember stones flanking the doorway, grounded at z 0. ----
    for i, ey in enumerate((0.85, -0.85)):
        objects.append(add_sphere("Lair_Ember_%02d" % (i + 1), 6, 3, 0.05,
                                  (1.20, ey, 0.05), glow))

    # ---- Hazard stripes on the entrance threshold (flat on the disc). ----
    for i, hy in enumerate((0.16, -0.16)):
        objects.append(add_box("Lair_HazardBar_%02d" % (i + 1),
                               (1.40, hy, 0.09), 0.0, (0.08, 0.10, 0.02),
                               hazard))
    # ---- Thin metal strips on the pillar fronts (ruined bunker). ----
    for i, my in enumerate((0.55, -0.55)):
        objects.append(add_box("Lair_MetalStrip_%02d" % (i + 1),
                               (1.48, my, 0.50), 0.0, (0.02, 0.10, 0.40),
                               metal))

    # ---- Five rock spikes ringed around the mound (not the front,
    # where the portal is), tilted outward 25 deg. ----
    for i, (sx, sy) in enumerate(((-0.70, 0.70), (-1.05, 0.0),
                                  (-0.70, -0.70), (0.30, 1.05),
                                  (0.30, -1.05))):
        objects.append(add_cone_tilted("Lair_Spike_%02d" % (i + 1),
                                       6, 0.16, 0.0, 1.10,
                                       (sx, sy, 0.70), rock,
                                       tilt_deg=25.0, tilt_dir=(sx, sy)))

    # ---- Debris / rubble around the base: squashed spheres, bottoms
    # exactly at z 0 (centre z = radius x z-scale 0.6). ----
    for i, (dx, dy, dr) in enumerate(((1.30, 0.85, 0.12), (-1.30, 0.40, 0.14),
                                      (-0.90, -1.15, 0.12), (0.60, 1.30, 0.10),
                                      (0.90, -1.05, 0.13))):
        objects.append(add_sphere("Lair_Debris_%02d" % (i + 1), 6, 3, dr,
                                  (dx, dy, dr * 0.6), rock, (1.0, 1.0, 0.6)))

    # ---- Broken metal plate half-buried at the mound base, leaning. ----
    objects.append(add_box_rot("Lair_BrokenPlate", (-0.65, 0.60, 0.12),
                               (0.3, 0.0, 0.5), (0.175, 0.015, 0.10), metal))

    # ---- Antenna on the mound top + glowing tip (tallest point, 1.74). ----
    objects.append(add_cylinder("Lair_Antenna", 6, 0.02, 0.25, 1.58, metal,
                                x=-0.45, y=0.35))
    objects.append(add_sphere("Lair_AntennaTip", 6, 4, 0.02,
                              (-0.45, 0.35, 1.72), glow))

    recalc_normals(objects)
    return objects


def make_uv_atlas(objects):
    """Pack every mesh object's UV islands into one 0-1 atlas (Lightmap
    Pack). The panel detail is defined in object space, so the UVs only
    decide WHERE in the atlas each surface lands, not the pattern."""
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


def make_metal_texture(rgb, seed, filename):
    """Generate one 1024 metal texture with numpy: flat spec colour,
    panel grid (32 px cells + a faint 16 px sub-grid, 4 px lines),
    rivets at the cell corners, per-cell brightness variation, and
    soft grime blotches. Returns the Blender image, saved as a PNG
    next to the script. v5: finer cells than v2's 48 px."""
    S = ATLAS_SIZE
    CELL = 32
    SUB = 16
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    cx = np.mod(xx, CELL)
    cy = np.mod(yy, CELL)
    line = ((cx < 4) | (cy < 4)).astype(np.float32)
    sx = np.mod(xx, SUB)
    sy = np.mod(yy, SUB)
    subline = ((sx < 2) | (sy < 2)).astype(np.float32)
    cell_x = (xx // CELL).astype(np.int32)
    cell_y = (yy // CELL).astype(np.int32)
    h = np.sin(cell_x * 127.1 + cell_y * 311.7) * 43758.5453
    h = h - np.floor(h)
    cell_bright = 0.90 + 0.20 * h
    dx = np.minimum(cx, CELL - cx)
    dy = np.minimum(cy, CELL - cy)
    dist_corner = np.sqrt(dx * dx + dy * dy)
    rivet = (dist_corner < 3.5).astype(np.float32)
    grime = np.zeros((S, S), dtype=np.float32)
    for _ in range(48):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(40, 140)
        strength = rng.uniform(0.08, 0.25)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        grime += strength * np.exp(-d2 / (2.0 * (r / 2.5) ** 2))
    grime = np.clip(grime, 0.0, 0.45)
    base = np.array(rgb, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= cell_bright[..., None]
    arr *= (1.0 - line[..., None] * 0.75)
    arr *= (1.0 - subline[..., None] * 0.25)
    arr *= (1.0 + rivet[..., None] * 0.25)
    arr *= (1.0 - grime[..., None])
    arr = np.clip(arr, 0.0, 1.0)
    rgba = np.ones((S, S, 4), dtype=np.float32)
    rgba[..., :3] = arr
    img = bpy.data.images.new(filename, S, S, alpha=False)
    img.pixels.foreach_set(rgba.ravel())
    path = os.path.join(SCRIPT_DIR, filename)
    write_png(path, arr)
    img.filepath = path
    img.file_format = "PNG"
    return img


def make_rock_texture(rgb, seed, filename):
    """Generate one 1024 dark rock texture with numpy: flat spec
    colour, large soft mottled blotches, fine speckle, and a few dark
    pits. No panel grid — rock is not plated."""
    S = ATLAS_SIZE
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    # Large soft blotches (random circles, radial falloff).
    blotch = np.zeros((S, S), dtype=np.float32)
    for _ in range(60):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(30, 120)
        strength = rng.uniform(-0.15, 0.15)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        blotch += strength * np.exp(-d2 / (2.0 * (r / 2.5) ** 2))
    # Fine speckle.
    speckle = rng.uniform(0.88, 1.12, (S, S)).astype(np.float32)
    # Dark pits (small, deep).
    pits = np.zeros((S, S), dtype=np.float32)
    for _ in range(20):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(5, 15)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        pits += 0.30 * np.exp(-d2 / (2.0 * (r / 2.5) ** 2))
    pits = np.clip(pits, 0.0, 0.35)
    base = np.array(rgb, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= (1.0 + blotch[..., None])
    arr *= speckle[..., None]
    arr *= (1.0 - pits[..., None])
    arr = np.clip(arr, 0.0, 1.0)
    rgba = np.ones((S, S, 4), dtype=np.float32)
    rgba[..., :3] = arr
    img = bpy.data.images.new(filename, S, S, alpha=False)
    img.pixels.foreach_set(rgba.ravel())
    path = os.path.join(SCRIPT_DIR, filename)
    write_png(path, arr)
    img.filepath = path
    img.file_format = "PNG"
    return img


def make_hazard_texture(filename):
    """Diagonal yellow/black hazard stripes (64 px bands) with slight
    noise and grime, for the entrance threshold bars."""
    S = ATLAS_SIZE
    rng = np.random.default_rng(301)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    band = (((xx + yy) // 64) % 2).astype(np.float32)  # 0 yellow, 1 black
    yellow = np.array(HAZARD_YELLOW, dtype=np.float32)
    black = np.array(HAZARD_BLACK, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * yellow
    arr = np.where(band[..., None] > 0.5, black, arr)
    noise = rng.uniform(0.90, 1.05, (S, S)).astype(np.float32)
    arr *= noise[..., None]
    grime = np.zeros((S, S), dtype=np.float32)
    for _ in range(24):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(40, 120)
        strength = rng.uniform(0.05, 0.18)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        grime += strength * np.exp(-d2 / (2.0 * (r / 2.5) ** 2))
    grime = np.clip(grime, 0.0, 0.35)
    arr *= (1.0 - grime[..., None])
    arr = np.clip(arr, 0.0, 1.0)
    rgba = np.ones((S, S, 4), dtype=np.float32)
    rgba[..., :3] = arr
    img = bpy.data.images.new(filename, S, S, alpha=False)
    img.pixels.foreach_set(rgba.ravel())
    path = os.path.join(SCRIPT_DIR, filename)
    write_png(path, arr)
    img.filepath = path
    img.file_format = "PNG"
    return img


def apply_textures(textured_materials):
    """Generate one texture per textured material, plug it into the
    material's Base Color, and set the base colour to white so Unity's
    (map x colour) multiply returns exactly the texture. Returns a list
    of (material name, filepath, bytes)."""
    seeds = {"LairRock": 311, "LairMetal": 313}
    base_colors = {"LairRock": ROCK_DARK, "LairMetal": LAIR_METAL}
    info = []
    for mat in textured_materials:
        filename = TEXTURE_NAMES[mat.name]
        if mat.name == "LairHazard":
            img = make_hazard_texture(filename)
        elif mat.name == "LairRock":
            img = make_rock_texture(base_colors[mat.name], seeds[mat.name],
                                    filename)
        else:
            img = make_metal_texture(base_colors[mat.name], seeds[mat.name],
                                     filename)
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
    print("Boss Lair build:")
    for obj in objects:
        if obj.type != "MESH":
            continue
        mesh = obj.data
        mesh.calc_loop_triangles()
        total_tris += len(mesh.loop_triangles)
        print("  %-20s tris=%4d  materials=%s"
              % (obj.name, len(mesh.loop_triangles),
                 [m.name if m else "?" for m in mesh.materials]))
    print("  objects=%d  total_tris=%d" % (len(objects), total_tris))
    pts = [o.matrix_world @ v.co for o in objects if o.type == "MESH"
           for v in o.data.vertices]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    print("  bounds x=[%.2f, %.2f] y=[%.2f, %.2f] z=[%.2f, %.2f]"
          % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    print("  exported: %s" % OUTPUT_FBX)
    print("  fbx bytes: %d" % os.path.getsize(OUTPUT_FBX))
    for mat_name, path, size in TEXTURE_INFO:
        print("  texture %s: %s (%d bytes)" % (mat_name, path, size))


def verify(objects):
    """Checks on the built mesh (Blender coords)."""
    ok = True

    def check(cond, msg):
        nonlocal ok
        print("  [%s] %s" % ("PASS" if cond else "FAIL", msg))
        if not cond:
            ok = False

    total_tris = 0
    for obj in objects:
        if obj.type != "MESH":
            continue
        obj.data.calc_loop_triangles()
        total_tris += len(obj.data.loop_triangles)
    check(300 <= total_tris <= 3000,
          "total tris %d (spec 300-3000)" % total_tris)
    pts = [o.matrix_world @ v.co for o in objects if o.type == "MESH"
           for v in o.data.vertices]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    check(abs(min(xs)) <= 1.55 and abs(max(xs)) <= 1.55,
          "footprint x in +/-1.55 (got [%.2f, %.2f])" % (min(xs), max(xs)))
    check(abs(min(ys)) <= 1.55 and abs(max(ys)) <= 1.55,
          "footprint y in +/-1.55 (got [%.2f, %.2f])" % (min(ys), max(ys)))
    check(abs(min(zs)) < 1e-4, "flat-based at z=0 (min z %.3f)" % min(zs))
    check(max(zs) <= MAX_HEIGHT + 0.01,
          "height %.2f (spec <= %.1f)" % (max(zs), MAX_HEIGHT))
    check(max(zs) >= 1.5, "height %.2f (spec >= 1.5, reads from far)"
          % max(zs))
    # The dark opening must face +X (the camera side).
    opening = [o for o in objects if o.name == "Lair_Opening"]
    check(len(opening) == 1 and opening[0].location.x > 1.0,
          "opening on the +X face (x %.2f)"
          % opening[0].location.x if opening else "no opening")
    # The red glow must sit inside the opening, proud of the dark panel.
    glow_obj = [o for o in objects if o.name == "Lair_Glow"]
    check(len(glow_obj) == 1 and glow_obj[0].location.x > 1.3,
          "glow inside the opening (x %.2f)"
          % glow_obj[0].location.x if glow_obj else "no glow")
    # The portal frame must have two pillars + a lintel.
    pillars = [o for o in objects if o.name.startswith("Lair_Pillar_")]
    lintel = [o for o in objects if o.name == "Lair_Lintel"]
    check(len(pillars) == 2 and len(lintel) == 1,
          "portal frame: 2 pillars + lintel (got %d + %d)"
          % (len(pillars), len(lintel)))
    print("  verification: %s" % ("ALL PASS" if ok else "FAILURES ABOVE"))
    return ok


if __name__ == "__main__":
    built = build()
    textured_mats = [m for m in bpy.data.materials
                     if m.name in ("LairRock", "LairMetal", "LairHazard")]
    make_uv_atlas(built)
    TEXTURE_INFO = apply_textures(textured_mats)
    export(built)
    report(built)
    print("Verification:")
    verify(built)
