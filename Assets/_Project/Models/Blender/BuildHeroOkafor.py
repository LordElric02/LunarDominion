# BuildHeroOkafor.py — Lunar Dominion: Lieutenant Ada Okafor,
# Mechanized hero (one of the three named heroes). Replaces the
# primitive capsule figure in the boss fight; portrait renders of
# this model are the Heroes-menu icon source.
#
# Rig: named Empty hierarchy for possible code animation —
#   HeroOkafor (root) -> Hips -> Torso -> Head
#                               Torso -> ShoulderL/R -> ElbowL/R
#                        Hips -> HipL/R -> KneeL/R
# Pose is set by rotating the joint empties (arms bent, rifle in a
# one-hand upright carry in the right hand). All other meshes use
# the Okafor_ prefix.
#
# DESIGN (Blender coords: x = Unity x, y = -Unity z, z = up):
# front is +X (visor/rifle face +X), same convention as the War
# Robot and A.R.E.S.-09. Height 3.23 (matches the ~3.3 hero
# figures), footprint +/-0.55. No ground disc.
#
# Look: heavy powered armour (1024 panel texture, the bulkiest
# of the three heroes) with green accents — her colour chip in
# the UI is (0.25, 0.85, 0.45) — an exoframe with chrome leg
# pistons, a backpack power unit with two glowing green cells,
# a forearm-mounted cannon on the right arm, and a wide glowing
# green visor band.
#
# Textures: 2 PNGs via the v5 numpy pipeline — pure-Python zlib
# PNG encoder (never Blender image.save()), Lightmap Pack UVs.
#   HeroOkafor_Armor.png, HeroOkafor_Glow.png.
#
# How to run: blender --background --python BuildHeroOkafor.py
# (or Blender's Scripting workspace -> Run Script). Outputs land
# next to the script file (SCRIPT_DIR). Install: copy
# HeroOkafor.fbx + the 2 PNGs into Assets/_Project/Resources/
# Models/ (ignore the .fbm sidecar, never rename the PNGs).
# The script also renders HeroIcon_Okafor.png — copy that one
# into Assets/_Project/Resources/Textures/ for the Heroes menu.

import math
import os
import struct
import zlib

import bpy
import numpy as np
from mathutils import Quaternion, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "HeroOkafor.fbx")
ATLAS_SIZE = 1024
TEXTURE_NAMES = {
    "OkaforArmor": "HeroOkafor_Armor.png",
    "OkaforGlow": "HeroOkafor_Glow.png",
}

# Palette (linear RGB).
ARMOR = (0.47, 0.52, 0.49)        # warm grey-green heavy plating
ACCENT = (0.25, 0.85, 0.45)       # her UI colour — green
DARK_SUIT = (0.13, 0.15, 0.19)    # under-suit / joints
CHROME = (0.62, 0.65, 0.70)
VISOR = (0.02, 0.04, 0.07)        # dark glossy visor base
GLOW = (0.20, 0.95, 0.45)         # saturated green glow

MAX_HEIGHT = 3.35


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
    Kept axis-aligned (rotation_z 0.0) for wide boxes — the Nova v5
    rotated-corner lesson."""
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=(0.0, 0.0, rotation_z))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def add_empty(name, location):
    bpy.ops.object.empty_add(type="PLAIN_AXES", location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.empty_display_size = 0.12
    return obj


def parent_to(child, parent):
    bpy.ops.object.select_all(action="DESELECT")
    child.select_set(True)
    parent.select_set(True)
    bpy.context.view_layer.objects.active = parent
    bpy.ops.object.parent_set(type="OBJECT", keep_transform=True)


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in list(bpy.data.meshes):
        bpy.data.meshes.remove(block)
    for block in list(bpy.data.materials):
        bpy.data.materials.remove(block)


def recalc_normals(objects):
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

    armor = make_material("OkaforArmor", ARMOR, metallic=0.55, roughness=0.48)
    glow = make_material("OkaforGlow", GLOW, metallic=0.1, roughness=0.3,
                         emission=2.5)
    accent = make_material("OkaforAccent", ACCENT, metallic=0.3, roughness=0.5)
    dark = make_material("OkaforDark", DARK_SUIT, metallic=0.2, roughness=0.7)
    chrome = make_material("OkaforChrome", CHROME, metallic=1.0, roughness=0.2)
    visor = make_material("OkaforVisor", VISOR, metallic=0.9, roughness=0.15)

    objects = []
    meshes = []

    def B(*args, **kwargs):
        obj = add_box(*args, **kwargs)
        objects.append(obj); meshes.append(obj)
        return obj

    def C(*args, **kwargs):
        obj = add_cylinder(*args, **kwargs)
        objects.append(obj); meshes.append(obj)
        return obj

    def S(*args, **kwargs):
        obj = add_sphere(*args, **kwargs)
        objects.append(obj); meshes.append(obj)
        return obj

    def E(name, location):
        obj = add_empty(name, location)
        objects.append(obj)
        return obj

    # ================= Rig empties =================
    root = E("HeroOkafor", (0.0, 0.0, 0.0))
    hips = E("Hips", (0.0, 0.0, 1.52))
    torso = E("Torso", (0.0, 0.0, 1.66))
    head_e = E("Head", (0.0, 0.0, 2.62))
    shoulder = {}
    elbow = {}
    for side, s in (("L", 1.0), ("R", -1.0)):
        shoulder[side] = E("Shoulder%s" % side, (0.0, s * 0.52, 2.42))
        # Elbow build position: straight down from the shoulder;
        # the pose comes from the joint rotations below.
        elbow[side] = E("Elbow%s" % side, (0.0, s * 0.52, 1.90))
    hip = {}
    knee = {}
    for side, s in (("L", 1.0), ("R", -1.0)):
        hip[side] = E("Hip%s" % side, (0.0, s * 0.20, 1.48))
        knee[side] = E("Knee%s" % side, (0.0, s * 0.21, 0.78))

    parts = {}   # mesh -> parent empty (or root)

    def P(obj, parent):
        parts[obj] = parent
        return obj

    # Parent the rig FIRST, while every joint is unrotated: mesh
    # locals then stay clean offsets, and the pose rotations set
    # afterwards swing the geometry rigidly. (Parenting meshes to
    # already-rotated joints with keep_transform would bake a
    # compensating local into every mesh and the pose would never
    # show — the first Okafor build did exactly that.)
    parent_to(knee["L"], hip["L"]); parent_to(knee["R"], hip["R"])
    parent_to(hip["L"], hips); parent_to(hip["R"], hips)
    parent_to(elbow["L"], shoulder["L"]); parent_to(elbow["R"], shoulder["R"])
    parent_to(shoulder["L"], torso); parent_to(shoulder["R"], torso)
    parent_to(head_e, torso)
    parent_to(torso, hips)
    parent_to(hips, root)

    # ================= Legs =================
    for side, s in (("L", 1.0), ("R", -1.0)):
        P(B("Okafor_Thigh%s" % side, (0.0, s * 0.21, 1.13), 0.0,
            (0.135, 0.155, 0.33), armor), hip[side])
        P(B("Okafor_ThighPlate%s" % side, (0.15, s * 0.21, 1.16), 0.0,
            (0.015, 0.10, 0.24), accent), hip[side])
        # Exoframe piston pair on the outer leg (sleeve on the
        # thigh, chrome rod on the shin — they telescope).
        P(C("Okafor_PistonSleeve%s" % side, 10, 0.048, 0.46, 1.06,
            dark, x=-0.02, y=s * 0.385), hip[side])
        P(S("Okafor_Knee%s" % side, 10, 8, 0.125, (0.05, s * 0.22, 0.78),
            dark, scale=(1.1, 1.15, 0.95)), knee[side])
        P(B("Okafor_Shin%s" % side, (0.0, s * 0.22, 0.45), 0.0,
            (0.12, 0.135, 0.31), armor), knee[side])
        P(C("Okafor_PistonRod%s" % side, 10, 0.028, 0.44, 0.58,
            chrome, x=-0.02, y=s * 0.395), knee[side])
        P(B("Okafor_ShinGlow%s" % side, (0.125, s * 0.22, 0.45), 0.0,
            (0.012, 0.032, 0.19), glow), knee[side])
        P(B("Okafor_Boot%s" % side, (0.05, s * 0.22, 0.08), 0.0,
            (0.20, 0.13, 0.08), dark), knee[side])
        P(B("Okafor_BootToe%s" % side, (0.23, s * 0.22, 0.05), 0.0,
            (0.05, 0.115, 0.05), accent), knee[side])

    # ================= Pelvis =================
    P(B("Okafor_Belt", (0.0, 0.0, 1.52), 0.0, (0.22, 0.34, 0.10), dark), hips)
    P(B("Okafor_Pelvis", (0.0, 0.0, 1.62), 0.0, (0.23, 0.34, 0.10), armor), hips)
    P(B("Okafor_BuckleGlow", (0.21, 0.0, 1.54), 0.0, (0.015, 0.07, 0.035), glow), hips)

    # ================= Torso =================
    P(B("Okafor_Abdomen", (0.0, 0.0, 1.76), 0.0, (0.20, 0.30, 0.09), dark), torso)
    P(B("Okafor_Chest", (0.0, 0.0, 2.07), 0.0, (0.26, 0.38, 0.31), armor), torso)
    P(B("Okafor_ChestPlate", (0.265, 0.0, 2.12), 0.0, (0.02, 0.24, 0.19), accent), torso)
    P(B("Okafor_ChestGlow", (0.29, 0.0, 2.22), 0.0, (0.012, 0.16, 0.03), glow), torso)
    P(C("Okafor_Neck", 12, 0.10, 0.20, 2.46, dark), torso)
    # Backpack power unit: frame + two glowing power cells.
    P(B("Okafor_Pack", (-0.36, 0.0, 2.06), 0.0, (0.17, 0.30, 0.33), dark), torso)
    for s in (1.0, -1.0):
        P(C("Okafor_PowerCell_%s" % ("L" if s > 0 else "R"), 14, 0.068, 0.50,
            2.06, glow, x=-0.50, y=s * 0.15), torso)
        P(C("Okafor_CellCapT_%s" % ("L" if s > 0 else "R"), 14, 0.075, 0.05,
            2.33, chrome, x=-0.50, y=s * 0.15), torso)
        P(C("Okafor_CellCapB_%s" % ("L" if s > 0 else "R"), 14, 0.075, 0.05,
            1.79, chrome, x=-0.50, y=s * 0.15), torso)

    # ================= Arms =================
    for side, s in (("L", 1.0), ("R", -1.0)):
        P(S("Okafor_Pauldron%s" % side, 12, 8, 0.23, (0.0, s * 0.55, 2.46),
            armor, scale=(0.95, 1.05, 0.85)), shoulder[side])
        P(B("Okafor_PauldronStripe%s" % side, (0.0, s * 0.57, 2.63), 0.0,
            (0.11, 0.14, 0.025), accent), shoulder[side])
        P(B("Okafor_UpperArm%s" % side, (0.0, s * 0.52, 2.16), 0.0,
            (0.105, 0.115, 0.26), armor), shoulder[side])
        P(S("Okafor_ElbowPad%s" % side, 10, 8, 0.10, (0.0, s * 0.52, 1.90),
            dark), elbow[side])
        P(B("Okafor_Forearm%s" % side, (0.0, s * 0.52, 1.69), 0.0,
            (0.095, 0.105, 0.21), armor), elbow[side])
        P(B("Okafor_Gauntlet%s" % side, (0.02, s * 0.52, 1.60), 0.0,
            (0.105, 0.115, 0.08), accent), elbow[side])
        P(S("Okafor_Hand%s" % side, 10, 8, 0.075, (0.0, s * 0.52, 1.44),
            dark), elbow[side])

    # ================= Forearm cannon (right arm) ===============
    # Built around the right forearm; parented to the elbow joint
    # so the pose swings it forward with the arm.
    cannon = E("Cannon", (0.0, -0.52, 1.90))
    P(B("Okafor_CannonBody", (0.04, -0.52, 1.66), 0.0, (0.11, 0.115, 0.17), dark), cannon)
    P(C("Okafor_CannonBarrel", 12, 0.048, 0.24, 1.70, chrome, x=0.22, y=-0.52,
        rotation=(0.0, math.radians(90.0), 0.0)), cannon)
    P(C("Okafor_CannonMuzzle", 12, 0.058, 0.04, 1.70, glow, x=0.33, y=-0.52,
        rotation=(0.0, math.radians(90.0), 0.0)), cannon)
    P(B("Okafor_CannonCell", (0.04, -0.52, 1.86), 0.0, (0.07, 0.07, 0.025), glow), cannon)

    # ================= Head / helmet =================
    P(S("Okafor_Helmet", 16, 12, 0.235, (0.01, 0.0, 2.83), armor,
        scale=(1.05, 0.98, 1.10)), head_e)
    P(B("Okafor_Visor", (0.21, 0.0, 2.83), 0.0, (0.035, 0.16, 0.08), visor), head_e)
    P(B("Okafor_VisorGlow", (0.245, 0.0, 2.85), 0.0, (0.012, 0.135, 0.022), glow), head_e)
    P(B("Okafor_Jaw", (0.14, 0.0, 2.62), 0.0, (0.09, 0.15, 0.085), armor), head_e)
    P(S("Okafor_BrowDot", 8, 6, 0.022, (0.225, 0.0, 2.98), glow), head_e)
    for s in (1.0, -1.0):
        P(B("Okafor_Cheek_%s" % ("L" if s > 0 else "R"), (0.10, s * 0.17, 2.70),
            0.0, (0.10, 0.045, 0.10), armor), head_e)
        P(C("Okafor_EarPod_%s" % ("L" if s > 0 else "R"), 12, 0.058, 0.05,
            2.86, dark, x=0.0, y=s * 0.215,
            rotation=(math.radians(90.0), 0.0, 0.0)), head_e)

    # ================= Parenting (meshes, joints still unposed) ==
    for obj, parent in parts.items():
        parent_to(obj, parent)
    parent_to(cannon, elbow["R"])

    # ================= Pose LAST =================
    # Joint rotations go on only after every mesh is parented —
    # parenting to an already-rotated joint bakes a compensating
    # local into the mesh and cancels the pose in the export.
    # Heavy relaxed stance; right elbow bent so the forearm
    # cannon points forward.
    shoulder["R"].rotation_euler = (0.12, -0.20, 0.0)
    elbow["R"].rotation_euler = (0.0, -0.55, 0.0)
    shoulder["L"].rotation_euler = (-0.12, -0.20, 0.0)
    elbow["L"].rotation_euler = (0.0, -0.35, 0.0)

    recalc_normals(objects)
    return objects, [armor, glow]


def make_uv_atlas(objects):
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


def _panel_base(S, cell, sub, rng):
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    cx = np.mod(xx, cell)
    cy = np.mod(yy, cell)
    line = ((cx < cell // 16 + 2) | (cy < cell // 16 + 2)).astype(np.float32)
    sx = np.mod(xx, sub)
    sy = np.mod(yy, sub)
    subline = ((sx < 1) | (sy < 1)).astype(np.float32)
    cells_x = (xx // cell).astype(int)
    cells_y = (yy // cell).astype(int)
    n = (S // cell + 2) ** 2
    table = rng.uniform(0.92, 1.06, n).astype(np.float32)
    cell_bright = table[cells_y * (S // cell + 2) + cells_x]
    rivet = np.zeros((S, S), dtype=np.float32)
    return yy, xx, line, subline, cell_bright, rivet


def _grime(S, xx, yy, rng, count, strength_max, clip_max):
    grime = np.zeros((S, S), dtype=np.float32)
    for _ in range(count):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(40, 140) * (S / 1024.0)
        strength = rng.uniform(0.08, strength_max)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        grime += strength * np.exp(-d2 / (2.0 * (r / 2.5) ** 2))
    return np.clip(grime, 0.0, clip_max)


def make_metal_texture(rgb, seed, filename, size, cell, sub):
    S = size
    rng = np.random.default_rng(seed)
    yy, xx, line, subline, cell_bright, rivet = _panel_base(S, cell, sub, rng)
    base = np.array(rgb, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= cell_bright[..., None]
    arr *= (1.0 - line[..., None] * 0.75)
    arr *= (1.0 - subline[..., None] * 0.25)
    for _ in range(160):
        gx, gy = rng.uniform(0, S, 2)
        length = rng.uniform(8, 60) * (S / 1024.0)
        if rng.uniform() < 0.5:
            x0, x1 = int(gx), int(min(S - 1, gx + length))
            arr[int(gy):int(gy) + 2, x0:x1] *= 1.30
        else:
            y0, y1 = int(gy), int(min(S - 1, gy + length))
            arr[y0:y1, int(gx):int(gx) + 2] *= 1.30
    grime = _grime(S, xx, yy, rng, 36, 0.22, 0.40)
    arr *= (1.0 - grime[..., None])
    arr = np.clip(arr, 0.0, 1.0)
    return _save_image(arr, filename)


def make_glow_texture(rgb, seed, filename):
    S = 1024
    rng = np.random.default_rng(seed)
    speckle = rng.uniform(0.88, 1.06, (S, S)).astype(np.float32)
    base = np.array(rgb, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= speckle[..., None]
    arr = np.clip(arr, 0.0, 1.0)
    return _save_image(arr, filename)


TEXTURE_INFO = []


def apply_textures(textured_materials):
    info = []
    for mat in textured_materials:
        filename = TEXTURE_NAMES[mat.name]
        if mat.name == "OkaforArmor":
            img = make_metal_texture(ARMOR, 3701, filename, ATLAS_SIZE, 64, 32)
        elif mat.name == "OkaforGlow":
            img = make_glow_texture(GLOW, 3707, filename)
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
        obj.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=OUTPUT_FBX,
        use_selection=True,
        object_types={"MESH", "EMPTY"},
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
    mesh_count = 0
    print("HeroOkafor build:")
    for obj in objects:
        if obj.type != "MESH":
            continue
        mesh_count += 1
        mesh = obj.data
        mesh.calc_loop_triangles()
        total_tris += len(mesh.loop_triangles)
    print("  objects=%d (meshes=%d)  total_tris=%d"
          % (len(objects), mesh_count, total_tris))
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
    fails = []

    def check(cond, msg):
        print(("  PASS " if cond else "  FAIL ") + msg)
        if not cond:
            fails.append(msg)

    names = {o.name for o in objects}
    for req in ("HeroOkafor", "Hips", "Torso", "Head", "ShoulderL",
                "ShoulderR", "ElbowL", "ElbowR", "HipL", "HipR",
                "KneeL", "KneeR", "Cannon"):
        check(req in names, "rig node present: %s" % req)
    pts = [o.matrix_world @ v.co for o in objects if o.type == "MESH"
           for v in o.data.vertices]
    zs = [p.z for p in pts]
    height = max(zs) - min(zs)
    check(abs(min(zs)) < 0.03, "base sits at z=0 (min z %.3f)" % min(zs))
    check(3.0 <= height <= MAX_HEIGHT, "height %.2f in [3.0, %.2f]"
          % (height, MAX_HEIGHT))
    total_tris = 0
    for obj in objects:
        if obj.type == "MESH":
            obj.data.calc_loop_triangles()
            total_tris += len(obj.data.loop_triangles)
    check(total_tris <= 6000, "tris %d <= 6000" % total_tris)
    for mat_name, path, size in TEXTURE_INFO:
        check(size > 1000, "texture written: %s" % os.path.basename(path))
    check(os.path.getsize(OUTPUT_FBX) > 5000, "fbx written")
    print("VERIFY: %s" % ("ALL PASS" if not fails else
                          "%d FAILURES" % len(fails)))
    return not fails


def render_icon():
    """Render the Heroes-menu icon from the finished model: a
    transparent-background bust portrait (Cycles), written next to
    the script as HeroIcon_<Hero>.png. The camera/lights are added
    after the FBX export and are never part of it. The rim light
    is tinted with the hero's glow colour."""
    from mathutils import Vector

    hero = os.path.basename(OUTPUT_FBX)[:-4]          # HeroOkafor
    icon_path = os.path.join(SCRIPT_DIR, "HeroIcon_%s.png" % hero[4:])
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 32
    scene.cycles.device = "CPU"
    scene.render.film_transparent = True
    scene.view_settings.view_transform = "Filmic"
    scene.view_settings.look = "Medium Contrast"
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.filepath = icon_path

    def look_at(obj, target):
        obj.rotation_euler = (Vector(target) - obj.location).to_track_quat(
            "-Z", "Y").to_euler()

    def light(name, loc, energy, size, color):
        d = bpy.data.lights.new(name, "AREA")
        d.energy = energy
        d.shape = "DISK"
        d.size = size
        d.color = color
        o = bpy.data.objects.new(name, d)
        o.location = loc
        scene.collection.objects.link(o)
        look_at(o, (0, 0, 1.6))

    light("IconKey", (3.5, -3.0, 4.5), 900, 3.0, (1.0, 0.95, 0.88))
    light("IconRim", (-3.5, 2.5, 3.5), 1200, 2.5, GLOW)
    light("IconFill", (-2.0, -3.5, 1.5), 250, 3.0, (0.8, 0.85, 1.0))
    cam_data = bpy.data.cameras.new("IconCam")
    cam = bpy.data.objects.new("IconCam", cam_data)
    cam.location = (2.35, -1.25, 2.95)
    cam_data.lens = 55
    scene.collection.objects.link(cam)
    look_at(cam, (0.10, 0, 2.30))
    scene.camera = cam
    bpy.ops.render.render(write_still=True)
    print("  icon: %s (%d bytes)" % (icon_path, os.path.getsize(icon_path)))
    return icon_path


def main():
    objects, textured = build()
    make_uv_atlas(objects)
    TEXTURE_INFO.extend(apply_textures(textured))
    export(objects)
    report(objects)
    ok = verify(objects)
    render_icon()
    print("BUILD RESULT:", "OK" if ok else "FAILED")


main()
