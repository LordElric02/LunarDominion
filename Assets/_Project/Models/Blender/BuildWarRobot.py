# BuildWarRobot.py — Lunar Dominion: first boss, giant War Robot
# for the exterior Map view (Boss Lair).
#
# WHAT IT IS: a giant war machine — bulky torso, heavy armoured legs,
# big arms (one with a cannon, one with a claw), a head with glowing
# RED eyes (hostile boss — red reads as danger). Dark gunmetal armour
# with hazard stripes and red accent panels, plus v5-style greebles
# (vents, pipes, junction boxes, antennae). It must read as a boss
# from the far exterior camera (50,80,50), so the mass is broad, the
# cannon and claw break the silhouette, and the red glow is saturated
# (the Hydroponics lesson: pale tints read washed-out white in Unity).
#
# DESIGN (Blender coords: x = Unity x, y = -Unity z, z = Unity y/up):
# footprint ~2.0 across (x/y +/-1.0), height ~2.17. The map renders
# the robot at 4x, so it reads giant on the map — do NOT build it 4x
# larger here. Single upright model: the fight animation tips the
# whole model over, so there is no separate rig or pose.
#
# Textures: 4 PNGs at 1024 (Gunmetal, Red, Hazard, Glow) via the v5
# numpy pipeline — pure-Python zlib PNG encoder (never Blender
# image.save()), Lightmap Pack UVs, saturated emission for the glow.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildWarRobot.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Written/tested against Blender 4.2.3 LTS here; uses no deprecated APIs,
# so it should also run on Blender 5.2 LTS.
# NOTE: the script exports the FBX + PNGs next to wherever the script file
# was opened from (SCRIPT_DIR). If you open it from F:\ or Downloads, the
# files land there — check the "exported:" lines in the console / report.
# Install: copy WarRobot.fbx + the 4 PNGs into
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
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "WarRobot.fbx")
ATLAS_SIZE = 1024
TEXTURE_NAMES = {
    "RobotGunmetal": "WarRobot_Gunmetal.png",
    "RobotRed": "WarRobot_Red.png",
    "RobotHazard": "WarRobot_Hazard.png",
    "RobotGlow": "WarRobot_Glow.png",
}

# Palette (linear RGB).
GUNMETAL = (0.27, 0.30, 0.35)   # dark gunmetal armour
DARK_JOINT = (0.13, 0.14, 0.17)  # joints / legs / head / vents
ROBOT_RED = (0.85, 0.15, 0.15)   # saturated red accent panels
HAZARD_YELLOW = (0.95, 0.75, 0.08)  # hazard stripe yellow
HAZARD_BLACK = (0.04, 0.04, 0.04)   # hazard stripe black
GLOW_RED = (0.95, 0.08, 0.08)   # glowing eyes / core / muzzle (saturated)
GROUND_DARK = (0.10, 0.10, 0.11)  # ground disc

# Envelope: footprint +/-1.0, height 0-2.2.
MAX_HEIGHT = 2.2


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


def add_cone(name, vertices, radius_bottom, radius_top, depth, z, material,
             x=0.0, y=0.0, rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=radius_bottom, radius2=radius_top,
        depth=depth, location=(x, y, z), rotation=rotation,
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


def add_cylinder_tilted(name, vertices, radius, depth, location, material,
                        tilt_deg=0.0, tilt_dir=(1.0, 0.0)):
    """Cylinder tilted by tilt_deg toward tilt_dir (x, y) — the tilt
    axis is horizontal and perpendicular to the tilt direction, so the
    cylinder's +Z axis leans toward (dx, dy) by exactly tilt_deg."""
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth, location=location,
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

    gunmetal = make_material("RobotGunmetal", GUNMETAL, metallic=0.9, roughness=0.40)
    joint = make_material("RobotDark", DARK_JOINT, metallic=0.3, roughness=0.65)
    red = make_material("RobotRed", ROBOT_RED, metallic=0.15, roughness=0.50)
    hazard = make_material("RobotHazard", HAZARD_YELLOW, metallic=0.2, roughness=0.60)
    glow = make_material("RobotGlow", GLOW_RED, metallic=0.10, roughness=0.30,
                         emission=2.5)
    ground = make_material("RobotGround", GROUND_DARK, roughness=0.90)

    objects = []

    # ---- Ground disc: flat base at z 0, the model sits on it. ----
    objects.append(add_cylinder("Robot_Ground", 12, 1.00, 0.06, 0.03, ground))

    # ---- Feet: wide flat stompers, front edge at x 0.35. ----
    for i, fy in enumerate((0.42, -0.42)):
        objects.append(add_box("Robot_Foot_%02d" % (i + 1),
                               (0.05, fy, 0.12), 0.0, (0.30, 0.20, 0.06),
                               gunmetal))

    # ---- Two heavy legs, tilted outward 6 deg. ----
    for i, ly in enumerate((0.42, -0.42)):
        objects.append(add_cylinder_tilted(
            "Robot_Leg_%02d" % (i + 1), 8, 0.16, 0.55,
            (0.0, ly, 0.45), joint, tilt_deg=6.0, tilt_dir=(0.0, ly)))

    # ---- Knee pads (red) on the leg fronts. ----
    for i, ky in enumerate((0.42, -0.42)):
        objects.append(add_box("Robot_Knee_%02d" % (i + 1),
                               (0.14, ky, 0.55), 0.0, (0.06, 0.14, 0.10), red))

    # ---- Hazard stripes on the shin fronts. ----
    for i, hy in enumerate((0.42, -0.42)):
        objects.append(add_box("Robot_HazardShin_%02d" % (i + 1),
                               (0.17, hy, 0.45), 0.0, (0.02, 0.12, 0.08),
                               hazard))

    # ---- Hip / pelvis. ----
    objects.append(add_box("Robot_Hip", (0.0, 0.0, 0.85), 0.0,
                           (0.35, 0.45, 0.12), gunmetal))
    # ---- Abdomen (dark, between hip and chest). ----
    objects.append(add_box("Robot_Abdomen", (0.0, 0.0, 1.05), 0.0,
                           (0.38, 0.42, 0.15), joint))

    # ---- Chest: the big bulky torso. ----
    objects.append(add_box("Robot_Chest", (0.0, 0.0, 1.45), 0.0,
                           (0.48, 0.55, 0.25), gunmetal))
    # ---- Red chest plate on the front (+X) face. ----
    objects.append(add_box("Robot_ChestPlate", (0.49, 0.0, 1.45), 0.0,
                           (0.02, 0.40, 0.18), red))
    # ---- Glowing red core, half-buried in the chest front below the plate. ----
    objects.append(add_sphere("Robot_Core", 8, 3, 0.09,
                              (0.50, 0.0, 1.28), glow, (0.5, 1.0, 1.0)))
    # ---- Hazard bars on the chest front, below the plate. ----
    for i, hy in enumerate((0.35, -0.35)):
        objects.append(add_box("Robot_HazardChest_%02d" % (i + 1),
                               (0.49, hy, 1.15), 0.0, (0.02, 0.12, 0.06),
                               hazard))

    # ---- Shoulder plates: flattened boxes rising above the chest. ----
    for i, sy in enumerate((0.68, -0.68)):
        objects.append(add_box("Robot_Shoulder_%02d" % (i + 1),
                               (0.05, sy, 1.68), 0.0, (0.30, 0.12, 0.22),
                               gunmetal))

    # ---- Head: dark box on top of the chest, front at x 0.27. ----
    objects.append(add_box("Robot_Head", (0.05, 0.0, 1.85), 0.0,
                           (0.22, 0.20, 0.14), joint))
    # ---- Two glowing red eyes, proud of the head front. ----
    for i, ey in enumerate((0.08, -0.08)):
        objects.append(add_box("Robot_Eye_%02d" % (i + 1),
                               (0.28, ey, 1.88), 0.0, (0.02, 0.06, 0.04),
                               glow))
    # ---- Head antenna + glowing tip (tallest point, ~2.17). ----
    objects.append(add_cylinder("Robot_Antenna", 8, 0.015, 0.16, 2.06, gunmetal))
    objects.append(add_sphere("Robot_AntennaTip", 6, 4, 0.02,
                              (0.0, 0.0, 2.15), glow))

    # ---- Cannon arm (+Y): upper arm, forearm, barrel, muzzle, glow. ----
    objects.append(add_cylinder_tilted(
        "Robot_ArmUpper_L", 6, 0.13, 0.45,
        (0.05, 0.68, 1.35), joint, tilt_deg=10.0, tilt_dir=(0.0, 1.0)))
    objects.append(add_cylinder("Robot_ArmFore_L", 8, 0.16, 0.40, 1.05,
                                gunmetal, x=0.05, y=0.75))
    # Cannon barrel: horizontal along +X (cylinder axis rotated 90 deg
    # about Y so its axis lies along X).
    objects.append(add_cylinder("Robot_Cannon", 8, 0.10, 0.55, 0.95,
                                gunmetal, x=0.35, y=0.78,
                                rotation=(0.0, math.radians(90.0), 0.0)))
    objects.append(add_cylinder("Robot_Muzzle", 8, 0.13, 0.08, 0.95,
                                joint, x=0.62, y=0.78,
                                rotation=(0.0, math.radians(90.0), 0.0)))
    objects.append(add_sphere("Robot_MuzzleGlow", 6, 2, 0.06,
                              (0.66, 0.78, 0.95), glow, (0.5, 1.0, 1.0)))

    # ---- Claw arm (-Y): upper arm, forearm, hub, three prongs. ----
    objects.append(add_cylinder_tilted(
        "Robot_ArmUpper_R", 6, 0.13, 0.45,
        (0.05, -0.68, 1.35), joint, tilt_deg=10.0, tilt_dir=(0.0, -1.0)))
    objects.append(add_cylinder("Robot_ArmFore_R", 8, 0.16, 0.40, 1.05,
                                gunmetal, x=0.05, y=-0.75))
    objects.append(add_box("Robot_ClawHub", (0.10, -0.78, 0.80), 0.0,
                           (0.10, 0.12, 0.08), joint))
    # Three claw prongs: cones whose apex points forward-down (+X, -Z),
    # i.e. rotated 135 deg about Y from the default +Z apex direction.
    for i, cy in enumerate((-0.70, -0.78, -0.86)):
        objects.append(add_cone("Robot_Claw_%02d" % (i + 1),
                                6, 0.06, 0.0, 0.35, 0.73, joint,
                                x=0.17, y=cy,
                                rotation=(0.0, math.radians(135.0), 0.0)))

    # ---- Back vents on the chest rear (-X) face. ----
    for i, vy in enumerate((0.25, -0.25)):
        objects.append(add_box("Robot_VentBack_%02d" % (i + 1),
                               (-0.49, vy, 1.45), 0.0, (0.02, 0.18, 0.15),
                               joint))
    # ---- Exhaust stacks on the chest top rear, with caps. ----
    for i, sy in enumerate((0.45, -0.45)):
        objects.append(add_cylinder("Robot_Stack_%02d" % (i + 1),
                                    8, 0.06, 0.25, 1.80, joint,
                                    x=-0.30, y=sy))
        objects.append(add_cylinder("Robot_StackCap_%02d" % (i + 1),
                                    8, 0.07, 0.03, 1.94, gunmetal,
                                    x=-0.30, y=sy))

    # ---- Vertical pipes on the chest front corners. ----
    for i, py in enumerate((0.56, -0.56)):
        objects.append(add_cylinder("Robot_Pipe_%02d" % (i + 1),
                                    8, 0.025, 0.50, 1.30, gunmetal,
                                    x=0.30, y=py))
    # ---- Junction boxes on the abdomen front. ----
    for i, jy in enumerate((0.30, -0.30)):
        objects.append(add_box("Robot_Junction_%02d" % (i + 1),
                               (0.39, jy, 1.05), 0.0, (0.02, 0.10, 0.08),
                               joint))

    # ---- Shoulder antenna + glowing tip. ----
    objects.append(add_cylinder("Robot_ShoulderAntenna", 6, 0.012, 0.25, 2.00,
                                gunmetal, x=0.0, y=0.70))
    objects.append(add_sphere("Robot_ShoulderTip", 6, 4, 0.02,
                              (0.0, 0.70, 2.14), glow))

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


def make_hazard_texture(filename):
    """Diagonal yellow/black hazard stripes (64 px bands) with slight
    noise and grime, for the shin/chest stripe bars."""
    S = ATLAS_SIZE
    rng = np.random.default_rng(201)
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


def make_glow_texture(rgb, seed, filename):
    """Saturated red glow texture: flat spec colour with fine speckle
    variation. The emission strength on the material does the glowing;
    the texture keeps the base colour saturated (pale tints wash out)."""
    S = ATLAS_SIZE
    rng = np.random.default_rng(seed)
    speckle = rng.uniform(0.88, 1.06, (S, S)).astype(np.float32)
    base = np.array(rgb, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= speckle[..., None]
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
    seeds = {"RobotGunmetal": 211, "RobotRed": 223, "RobotGlow": 229}
    base_colors = {"RobotGunmetal": GUNMETAL, "RobotRed": ROBOT_RED,
                   "RobotGlow": GLOW_RED}
    info = []
    for mat in textured_materials:
        filename = TEXTURE_NAMES[mat.name]
        if mat.name == "RobotHazard":
            img = make_hazard_texture(filename)
        elif mat.name == "RobotGlow":
            img = make_glow_texture(base_colors[mat.name], seeds[mat.name],
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
    print("War Robot build:")
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
    check(abs(min(xs)) <= 1.05 and abs(max(xs)) <= 1.05,
          "footprint x in +/-1.05 (got [%.2f, %.2f])" % (min(xs), max(xs)))
    check(abs(min(ys)) <= 1.05 and abs(max(ys)) <= 1.05,
          "footprint y in +/-1.05 (got [%.2f, %.2f])" % (min(ys), max(ys)))
    check(abs(min(zs)) < 1e-4, "flat-based at z=0 (min z %.3f)" % min(zs))
    check(max(zs) <= MAX_HEIGHT + 0.01,
          "height %.2f (spec <= %.1f)" % (max(zs), MAX_HEIGHT))
    check(max(zs) >= 2.0, "height %.2f (spec >= 2.0, boss presence)"
          % max(zs))
    # The eyes must sit on the head front (+X).
    eyes = [o for o in objects if o.name.startswith("Robot_Eye_")]
    check(len(eyes) == 2 and all(o.location.x > 0.2 for o in eyes),
          "two eyes on the head front (x %.2f)"
          % eyes[0].location.x if eyes else "no eyes")
    # The cannon must point forward (+X) past the chest.
    muzzle = [o for o in objects if o.name == "Robot_Muzzle"]
    check(len(muzzle) == 1 and muzzle[0].location.x > 0.5,
          "cannon muzzle forward (x %.2f)"
          % muzzle[0].location.x if muzzle else "no muzzle")
    # The claw must have three prongs.
    claws = [o for o in objects if o.name.startswith("Robot_Claw_")
             and not o.name.startswith("Robot_ClawHub")]
    check(len(claws) == 3, "three claw prongs (got %d)" % len(claws))
    print("  verification: %s" % ("ALL PASS" if ok else "FAILURES ABOVE"))
    return ok


if __name__ == "__main__":
    built = build()
    textured_mats = [m for m in bpy.data.materials
                     if m.name in ("RobotGunmetal", "RobotRed",
                                   "RobotHazard", "RobotGlow")]
    make_uv_atlas(built)
    TEXTURE_INFO = apply_textures(textured_mats)
    export(built)
    report(built)
    print("Verification:")
    verify(built)
