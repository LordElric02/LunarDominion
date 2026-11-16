# BuildTroopsV2.py — Lunar Dominion: hero-quality troop figures
# (Exterior Round 3, Phase 3 — replaces the Mini* figures on the
# map). One script builds all three class models:
#   TroopInfantry.fbx  (~3.25 tall, bulky marine, rifle across body)
#   TroopRanged.fbx    (~3.25 + antenna, lean gunner, long rail rifle)
#   TroopMech.fbx      (~4.35 tall, bipedal mech, arm cannons)
# Tier is shown by the GLOW TRIM: every model carries parts in a
# material named exactly "TroopTrim" (pauldron rims, chest stripe,
# knee/shin plates, belt, mech light bands). ExteriorView swaps
# that material at runtime for a per-tier colour, so a squad's tier
# reads at a glance (AoE Mobile style). Class identity lives in
# the painted armour/glow textures: Infantry steel-blue, Ranged
# burnt orange, Mechanized green.
#
# Pipeline forked from BuildHeroWard.py: same helper shape, same
# rig naming (root -> Hips -> Torso -> Head, Shoulder/Elbow,
# Hip/Knee), same parenting discipline (rig first, meshes second,
# pose LAST), same texture pipeline (numpy panels + pure-Python
# zlib PNG writer, Lightmap Pack UVs, 2 PNGs per model).
# Blender coords: x = Unity x (front is +X), y = -Unity z
# (lateral), z = up. Marching pose: one leg forward, one back.
#
# How to run: blender --background --python BuildTroopsV2.py
# Outputs land next to the script. Install: copy the 3 FBXs + 6
# PNGs into Assets/_Project/Resources/Models/ (ignore .fbm).

import math
import os
import struct
import zlib

import bpy
import numpy as np

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ATLAS_SIZE = 1024

TRIM_NAME = "TroopTrim"


# ----------------------------------------------------------------
# Shared helpers (the hero-script family pattern)
# ----------------------------------------------------------------

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
    """Box with `scale` as half-extents (the cube primitive is 2 units)."""
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


class Builder:
    """Collects objects and mesh->parent assignments for one model."""

    def __init__(self):
        self.objects = []
        self.parts = {}

    def B(self, *args, **kwargs):
        obj = add_box(*args, **kwargs)
        self.objects.append(obj)
        return obj

    def C(self, *args, **kwargs):
        obj = add_cylinder(*args, **kwargs)
        self.objects.append(obj)
        return obj

    def S(self, *args, **kwargs):
        obj = add_sphere(*args, **kwargs)
        self.objects.append(obj)
        return obj

    def E(self, name, location):
        obj = add_empty(name, location)
        self.objects.append(obj)
        return obj

    def P(self, obj, parent):
        self.parts[obj] = parent
        return obj

    def parent_all(self):
        for obj, parent in self.parts.items():
            parent_to(obj, parent)


def make_rig(b, root_name, hips_z, torso_z, head_z, shoulder_y,
             shoulder_z, hip_y, hip_z, knee_y, knee_z):
    """The hero-family rig. Returns a dict of joint empties.
    Elbow build position: straight down 0.52 from the shoulder."""
    rig = {}
    rig["root"] = b.E(root_name, (0.0, 0.0, 0.0))
    rig["hips"] = b.E("Hips", (0.0, 0.0, hips_z))
    rig["torso"] = b.E("Torso", (0.0, 0.0, torso_z))
    rig["head"] = b.E("Head", (0.0, 0.0, head_z))
    for side, s in (("L", 1.0), ("R", -1.0)):
        rig["shoulder" + side] = b.E("Shoulder" + side,
                                     (0.0, s * shoulder_y, shoulder_z))
        rig["elbow" + side] = b.E("Elbow" + side,
                                  (0.0, s * shoulder_y, shoulder_z - 0.52))
        rig["hip" + side] = b.E("Hip" + side, (0.0, s * hip_y, hip_z))
        rig["knee" + side] = b.E("Knee" + side, (0.0, s * knee_y, knee_z))
    # Parent the rig FIRST, while every joint is unrotated.
    for side in ("L", "R"):
        parent_to(rig["knee" + side], rig["hip" + side])
        parent_to(rig["hip" + side], rig["hips"])
        parent_to(rig["elbow" + side], rig["shoulder" + side])
        parent_to(rig["shoulder" + side], rig["torso"])
    parent_to(rig["head"], rig["torso"])
    parent_to(rig["torso"], rig["hips"])
    parent_to(rig["hips"], rig["root"])
    return rig


def march_pose(rig, swing=0.34):
    """One leg forward, one back (rotation about Blender Y swings
    a hanging leg in the forward/back plane; negative = forward).
    Arms get a slight counter-swing unless a caller re-poses them
    onto a weapon afterwards."""
    rig["hipL"].rotation_euler = (0.0, -swing, 0.0)
    rig["kneeL"].rotation_euler = (0.0, swing * 0.85, 0.0)
    rig["hipR"].rotation_euler = (0.0, swing * 0.88, 0.0)
    rig["kneeR"].rotation_euler = (0.0, swing * 0.35, 0.0)


# ----------------------------------------------------------------
# Texture pipeline (numpy + pure-Python PNG writer; from the
# hero scripts — never Blender image.save()).
# ----------------------------------------------------------------

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


def apply_textures(jobs):
    """jobs: list of (material, kind, rgb, seed, filename).
    kind is 'metal' (panel texture) or 'glow' (speckle)."""
    info = []
    for mat, kind, rgb, seed, filename in jobs:
        if kind == "metal":
            img = make_metal_texture(rgb, seed, filename, ATLAS_SIZE, 64, 32)
        else:
            img = make_glow_texture(rgb, seed, filename)
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


def export_model(objects, filename):
    path = os.path.join(SCRIPT_DIR, filename)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=path,
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
    return path


def model_stats(objects):
    total_tris = 0
    mesh_count = 0
    for obj in objects:
        if obj.type != "MESH":
            continue
        mesh_count += 1
        obj.data.calc_loop_triangles()
        total_tris += len(obj.data.loop_triangles)
    pts = [o.matrix_world @ v.co for o in objects if o.type == "MESH"
           for v in o.data.vertices]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    return mesh_count, total_tris, (min(xs), max(xs), min(ys), max(ys),
                                    min(zs), max(zs))


def verify_model(label, objects, tex_info, fbx_path, root_name,
                 height_range, max_tris):
    fails = []

    def check(cond, msg):
        print(("  PASS " if cond else "  FAIL ") + msg)
        if not cond:
            fails.append(msg)

    names = {o.name for o in objects}
    for req in (root_name, "Hips", "Torso", "Head", "ShoulderL",
                "ShoulderR", "ElbowL", "ElbowR", "HipL", "HipR",
                "KneeL", "KneeR"):
        check(req in names, "rig node present: %s" % req)
    mesh_count, total_tris, bounds = model_stats(objects)
    min_z = bounds[4]
    height = bounds[5] - bounds[4]
    check(abs(min_z) < 0.03, "base sits at z=0 (min z %.3f)" % min_z)
    check(height_range[0] <= height <= height_range[1],
          "height %.2f in [%.2f, %.2f]" % (height, height_range[0],
                                           height_range[1]))
    check(total_tris <= max_tris, "tris %d <= %d" % (total_tris, max_tris))
    trim_meshes = 0
    for obj in objects:
        if obj.type != "MESH":
            continue
        for slot in obj.data.materials:
            if slot is not None and slot.name == TRIM_NAME:
                trim_meshes += 1
    check(trim_meshes >= 3, "TroopTrim on %d meshes (>= 3)" % trim_meshes)
    for mat_name, path, size in tex_info:
        check(size > 1000, "texture written: %s" % os.path.basename(path))
    check(os.path.getsize(fbx_path) > 5000, "fbx written: %s"
          % os.path.basename(fbx_path))
    print("%s VERIFY: %s" % (label, "ALL PASS" if not fails else
                             "%d FAILURES" % len(fails)))
    return not fails


def finish_model(label, b, jobs, fbx_name, root_name, height_range,
                 max_tris=6500):
    make_uv_atlas(b.objects)
    tex_info = apply_textures(jobs)
    fbx_path = export_model(b.objects, fbx_name)
    mesh_count, total_tris, bounds = model_stats(b.objects)
    print("%s build: objects=%d (meshes=%d) tris=%d bounds "
          "x=[%.2f, %.2f] y=[%.2f, %.2f] z=[%.2f, %.2f] fbx=%d bytes"
          % (label, len(b.objects), mesh_count, total_tris,
             bounds[0], bounds[1], bounds[2], bounds[3], bounds[4],
             bounds[5], os.path.getsize(fbx_path)))
    return verify_model(label, b.objects, tex_info, fbx_path, root_name,
                        height_range, max_tris)


# ----------------------------------------------------------------
# Model 1 — TroopInfantry: bulky marine, rifle across the body
# ----------------------------------------------------------------

def build_infantry():
    clear_scene()
    b = Builder()
    armor_rgb = (0.40, 0.48, 0.60)
    glow_rgb = (0.35, 0.62, 1.0)
    armor = make_material("InfArmor", armor_rgb, metallic=0.55, roughness=0.48)
    glow = make_material("InfGlow", glow_rgb, metallic=0.1, roughness=0.3,
                         emission=2.5)
    accent = make_material("InfAccent", (0.25, 0.35, 0.55), metallic=0.3,
                           roughness=0.5)
    dark = make_material("InfDark", (0.12, 0.13, 0.16), metallic=0.2,
                         roughness=0.7)
    chrome = make_material("InfChrome", (0.62, 0.65, 0.70), metallic=1.0,
                           roughness=0.2)
    visor = make_material("InfVisor", (0.02, 0.04, 0.07), metallic=0.9,
                          roughness=0.15)
    trim = make_material(TRIM_NAME, (0.55, 0.58, 0.62), metallic=0.7,
                         roughness=0.3, emission=0.7,
                         emission_rgb=(0.45, 0.70, 1.0))

    rig = make_rig(b, "TroopInfantry", 1.52, 1.66, 2.62, 0.52, 2.42,
                   0.20, 1.48, 0.21, 0.78)
    B, C, S, P = b.B, b.C, b.S, b.P

    for side, s in (("L", 1.0), ("R", -1.0)):
        hip, knee = rig["hip" + side], rig["knee" + side]
        P(B("Inf_Thigh" + side, (0.0, s * 0.21, 1.13), 0.0,
            (0.135, 0.155, 0.34), armor), hip)
        P(B("Inf_ThighPlate" + side, (0.145, s * 0.21, 1.16), 0.0,
            (0.018, 0.10, 0.25), accent), hip)
        P(S("Inf_Knee" + side, 10, 8, 0.125, (0.05, s * 0.22, 0.78),
            dark, scale=(1.1, 1.15, 0.95)), knee)
        P(B("Inf_KneeTrim" + side, (0.165, s * 0.22, 0.78), 0.0,
            (0.02, 0.075, 0.075), trim), knee)
        P(B("Inf_Shin" + side, (0.0, s * 0.22, 0.44), 0.0,
            (0.115, 0.13, 0.32), armor), knee)
        P(B("Inf_ShinGlow" + side, (0.12, s * 0.22, 0.44), 0.0,
            (0.012, 0.03, 0.20), glow), knee)
        P(B("Inf_Boot" + side, (0.06, s * 0.22, 0.07), 0.0,
            (0.18, 0.12, 0.07), dark), knee)
        P(B("Inf_BootToe" + side, (0.22, s * 0.22, 0.045), 0.0,
            (0.05, 0.11, 0.045), accent), knee)
        P(B("Inf_HipPouch" + side, (0.02, s * 0.38, 1.34), 0.0,
            (0.11, 0.06, 0.13), accent), hip)

    hips, torso, head = rig["hips"], rig["torso"], rig["head"]
    P(B("Inf_Belt", (0.0, 0.0, 1.52), 0.0, (0.21, 0.32, 0.10), dark), hips)
    P(B("Inf_Pelvis", (0.0, 0.0, 1.63), 0.0, (0.22, 0.32, 0.10), armor), hips)
    P(B("Inf_BuckleTrim", (0.225, 0.0, 1.54), 0.0, (0.018, 0.09, 0.045),
        trim), hips)

    P(B("Inf_Abdomen", (0.0, 0.0, 1.78), 0.0, (0.19, 0.28, 0.09), dark),
      torso)
    P(B("Inf_Chest", (0.0, 0.0, 2.08), 0.0, (0.25, 0.35, 0.30), armor),
      torso)
    P(B("Inf_ChestPlate", (0.255, 0.0, 2.12), 0.0, (0.02, 0.22, 0.19),
        accent), torso)
    P(B("Inf_ChestTrim", (0.28, 0.0, 2.28), 0.0, (0.014, 0.16, 0.028),
        trim), torso)
    P(B("Inf_ChestCore", (0.28, 0.0, 2.02), 0.0, (0.012, 0.045, 0.045),
        glow), torso)
    P(C("Inf_Neck", 12, 0.10, 0.20, 2.47, dark), torso)
    P(B("Inf_Pack", (-0.34, 0.0, 2.08), 0.0, (0.15, 0.27, 0.30), dark),
      torso)
    P(B("Inf_PackTrim", (-0.495, 0.0, 2.20), 0.0, (0.014, 0.18, 0.03),
        trim), torso)
    for s in (1.0, -1.0):
        tag = "L" if s > 0 else "R"
        P(C("Inf_Canister_" + tag, 12, 0.055, 0.46, 2.08, chrome,
            x=-0.50, y=s * 0.14), torso)
        P(C("Inf_CanRing_" + tag, 12, 0.06, 0.035, 2.20, glow,
            x=-0.50, y=s * 0.14), torso)

    for side, s in (("L", 1.0), ("R", -1.0)):
        sh, el = rig["shoulder" + side], rig["elbow" + side]
        P(S("Inf_Pauldron" + side, 12, 8, 0.23, (0.0, s * 0.55, 2.47),
            armor, scale=(1.0, 1.08, 0.88)), sh)
        P(B("Inf_PauldronTrim" + side, (0.0, s * 0.56, 2.665), 0.0,
            (0.13, 0.15, 0.022), trim), sh)
        P(B("Inf_UpperArm" + side, (0.0, s * 0.53, 2.16), 0.0,
            (0.095, 0.105, 0.26), armor), sh)
        P(S("Inf_ElbowPad" + side, 10, 8, 0.09, (0.0, s * 0.53, 1.90),
            dark), el)
        P(B("Inf_Forearm" + side, (0.0, s * 0.53, 1.68), 0.0,
            (0.085, 0.095, 0.21), armor), el)
        P(B("Inf_Gauntlet" + side, (0.02, s * 0.53, 1.59), 0.0,
            (0.09, 0.10, 0.07), accent), el)
        P(S("Inf_Hand" + side, 10, 8, 0.08, (0.0, s * 0.53, 1.44),
            dark), el)

    rifle = b.E("Rifle", (0.40, -0.10, 1.95))
    P(B("Inf_RifleBody", (0.40, -0.10, 1.98), 0.0, (0.34, 0.055, 0.075),
        dark), rifle)
    P(C("Inf_RifleBarrel", 10, 0.028, 0.34, 2.02, chrome, x=0.86, y=-0.10,
        rotation=(0.0, math.radians(90.0), 0.0)), rifle)
    P(B("Inf_RifleMuzzle", (1.04, -0.10, 2.02), 0.0, (0.035, 0.045, 0.045),
        dark), rifle)
    P(B("Inf_RifleStock", (-0.02, -0.10, 1.94), 0.0, (0.09, 0.05, 0.11),
        accent), rifle)
    P(B("Inf_RifleMag", (0.38, -0.10, 1.80), 0.0, (0.05, 0.045, 0.10),
        accent), rifle)
    P(B("Inf_RifleSight", (0.45, -0.10, 2.13), 0.0, (0.07, 0.035, 0.045),
        dark), rifle)
    P(B("Inf_RifleSightGlow", (0.45, -0.10, 2.185), 0.0,
        (0.03, 0.02, 0.012), glow), rifle)
    P(B("Inf_RifleCell", (0.20, -0.10, 2.06), 0.0, (0.05, 0.058, 0.02),
        glow), rifle)

    P(S("Inf_Helmet", 16, 12, 0.235, (0.01, 0.0, 2.84), armor,
        scale=(1.05, 0.97, 1.12)), head)
    P(B("Inf_Visor", (0.205, 0.0, 2.84), 0.0, (0.035, 0.14, 0.08), visor),
      head)
    P(B("Inf_VisorGlow", (0.242, 0.0, 2.86), 0.0, (0.012, 0.11, 0.018),
        glow), head)
    P(B("Inf_Jaw", (0.13, 0.0, 2.65), 0.0, (0.09, 0.14, 0.08), armor), head)
    P(B("Inf_CrestTrim", (0.0, 0.0, 3.10), 0.0, (0.11, 0.028, 0.03), trim),
      head)
    for s in (1.0, -1.0):
        tag = "L" if s > 0 else "R"
        P(C("Inf_EarPod_" + tag, 12, 0.055, 0.045, 2.87, dark, x=0.0,
            y=s * 0.215, rotation=(math.radians(90.0), 0.0, 0.0)), head)

    b.parent_all()
    parent_to(rifle, torso)

    march_pose(rig)
    rig["shoulderR"].rotation_euler = (0.30, -0.42, 0.0)
    rig["elbowR"].rotation_euler = (0.0, -0.95, 0.0)
    rig["shoulderL"].rotation_euler = (-0.30, -0.28, 0.0)
    rig["elbowL"].rotation_euler = (0.0, -0.50, 0.0)

    recalc_normals(b.objects)
    jobs = [(armor, "metal", armor_rgb, 2001, "TroopInfantry_Armor.png"),
            (glow, "glow", glow_rgb, 2007, "TroopInfantry_Glow.png")]
    return b, jobs


# ----------------------------------------------------------------
# Model 2 — TroopRanged: lean gunner, long rail rifle, antenna
# ----------------------------------------------------------------

def build_ranged():
    clear_scene()
    b = Builder()
    armor_rgb = (0.46, 0.42, 0.40)
    glow_rgb = (1.0, 0.55, 0.18)
    armor = make_material("RngArmor", armor_rgb, metallic=0.55, roughness=0.48)
    glow = make_material("RngGlow", glow_rgb, metallic=0.1, roughness=0.3,
                         emission=2.5)
    accent = make_material("RngAccent", (0.70, 0.38, 0.16), metallic=0.3,
                           roughness=0.5)
    dark = make_material("RngDark", (0.13, 0.13, 0.15), metallic=0.2,
                         roughness=0.7)
    chrome = make_material("RngChrome", (0.62, 0.65, 0.70), metallic=1.0,
                           roughness=0.2)
    visor = make_material("RngVisor", (0.02, 0.04, 0.07), metallic=0.9,
                          roughness=0.15)
    trim = make_material(TRIM_NAME, (0.55, 0.58, 0.62), metallic=0.7,
                         roughness=0.3, emission=0.7,
                         emission_rgb=(0.45, 0.70, 1.0))

    rig = make_rig(b, "TroopRanged", 1.52, 1.66, 2.62, 0.52, 2.42,
                   0.20, 1.48, 0.21, 0.78)
    B, C, S, P = b.B, b.C, b.S, b.P

    for side, s in (("L", 1.0), ("R", -1.0)):
        hip, knee = rig["hip" + side], rig["knee" + side]
        P(B("Rng_Thigh" + side, (0.0, s * 0.21, 1.13), 0.0,
            (0.11, 0.125, 0.34), armor), hip)
        P(B("Rng_ThighPlate" + side, (0.12, s * 0.21, 1.16), 0.0,
            (0.016, 0.085, 0.25), accent), hip)
        P(S("Rng_Knee" + side, 10, 8, 0.115, (0.05, s * 0.22, 0.78),
            dark, scale=(1.1, 1.15, 0.95)), knee)
        P(B("Rng_KneeTrim" + side, (0.15, s * 0.22, 0.78), 0.0,
            (0.018, 0.068, 0.068), trim), knee)
        P(B("Rng_Shin" + side, (0.0, s * 0.22, 0.44), 0.0,
            (0.095, 0.11, 0.32), armor), knee)
        P(B("Rng_ShinGlow" + side, (0.10, s * 0.22, 0.44), 0.0,
            (0.012, 0.026, 0.20), glow), knee)
        P(B("Rng_Boot" + side, (0.06, s * 0.22, 0.07), 0.0,
            (0.18, 0.11, 0.07), dark), knee)
        P(B("Rng_BootToe" + side, (0.22, s * 0.22, 0.045), 0.0,
            (0.05, 0.10, 0.045), accent), knee)
    # Ammo cells stacked on the outer left thigh.
    for i, z in enumerate((1.30, 1.15, 1.00)):
        P(B("Rng_Cell%d" % i, (0.0, 0.345, z), 0.0, (0.07, 0.035, 0.06),
            accent), rig["hipL"])
        P(B("Rng_CellGlow%d" % i, (0.075, 0.345, z), 0.0,
            (0.012, 0.02, 0.02), glow), rig["hipL"])

    hips, torso, head = rig["hips"], rig["torso"], rig["head"]
    P(B("Rng_Belt", (0.0, 0.0, 1.52), 0.0, (0.20, 0.29, 0.10), dark), hips)
    P(B("Rng_Pelvis", (0.0, 0.0, 1.63), 0.0, (0.21, 0.29, 0.10), armor), hips)
    P(B("Rng_BuckleTrim", (0.215, 0.0, 1.54), 0.0, (0.018, 0.08, 0.045),
        trim), hips)

    P(B("Rng_Abdomen", (0.0, 0.0, 1.78), 0.0, (0.18, 0.25, 0.09), dark),
      torso)
    P(B("Rng_Chest", (0.0, 0.0, 2.08), 0.0, (0.215, 0.295, 0.29), armor),
      torso)
    P(B("Rng_ChestPlate", (0.22, 0.0, 2.10), 0.0, (0.018, 0.18, 0.17),
        accent), torso)
    P(B("Rng_ChestTrim", (0.24, 0.0, 2.28), 0.0, (0.012, 0.13, 0.024),
        trim), torso)
    P(C("Rng_Neck", 12, 0.10, 0.20, 2.47, dark), torso)
    P(B("Rng_Pack", (-0.30, 0.0, 2.06), 0.0, (0.13, 0.22, 0.27), dark),
      torso)
    P(C("Rng_Antenna", 6, 0.008, 0.55, 3.05, dark, x=-0.36, y=0.16), torso)
    P(S("Rng_AntennaTip", 8, 6, 0.02, (-0.36, 0.16, 3.34), glow), torso)

    for side, s in (("L", 1.0), ("R", -1.0)):
        sh, el = rig["shoulder" + side], rig["elbow" + side]
        P(S("Rng_Pauldron" + side, 12, 8, 0.175, (0.0, s * 0.54, 2.46),
            armor, scale=(0.95, 1.05, 0.85)), sh)
        P(B("Rng_PauldronTrim" + side, (0.0, s * 0.545, 2.585), 0.0,
            (0.105, 0.125, 0.02), trim), sh)
        P(B("Rng_UpperArm" + side, (0.0, s * 0.53, 2.16), 0.0,
            (0.08, 0.09, 0.26), armor), sh)
        P(S("Rng_ElbowPad" + side, 10, 8, 0.08, (0.0, s * 0.53, 1.90),
            dark), el)
        P(B("Rng_Forearm" + side, (0.0, s * 0.53, 1.68), 0.0,
            (0.07, 0.08, 0.21), armor), el)
        P(B("Rng_Gauntlet" + side, (0.02, s * 0.53, 1.59), 0.0,
            (0.075, 0.085, 0.07), accent), el)
        P(S("Rng_Hand" + side, 10, 8, 0.07, (0.0, s * 0.53, 1.44),
            dark), el)

    rail = b.E("RailRifle", (0.80, -0.30, 2.16))
    P(B("Rng_RailBody", (0.80, -0.30, 2.16), 0.0, (0.58, 0.045, 0.055),
        dark), rail)
    P(C("Rng_RailBarrel", 10, 0.024, 0.50, 2.16, chrome, x=1.55, y=-0.30,
        rotation=(0.0, math.radians(90.0), 0.0)), rail)
    P(C("Rng_RailMuzzle", 10, 0.035, 0.03, 2.16, glow, x=1.80, y=-0.30,
        rotation=(0.0, math.radians(90.0), 0.0)), rail)
    for i, x in enumerate((0.95, 1.15, 1.35)):
        P(C("Rng_Coil%d" % i, 12, 0.065, 0.05, 2.16, chrome, x=x, y=-0.30,
            rotation=(0.0, math.radians(90.0), 0.0)), rail)
    P(B("Rng_Scope", (0.70, -0.30, 2.30), 0.0, (0.11, 0.035, 0.04), dark),
      rail)
    P(B("Rng_ScopeLens", (0.815, -0.30, 2.30), 0.0, (0.012, 0.025, 0.025),
        glow), rail)
    P(B("Rng_RailStock", (0.10, -0.30, 2.10), 0.0, (0.10, 0.05, 0.10),
        accent), rail)
    P(B("Rng_RailGrip", (0.55, -0.30, 1.98), 0.0, (0.045, 0.04, 0.09),
        accent), rail)
    P(B("Rng_RailCell", (0.35, -0.30, 2.225), 0.0, (0.06, 0.048, 0.018),
        glow), rail)

    P(S("Rng_Helmet", 16, 12, 0.225, (0.01, 0.0, 2.83), armor,
        scale=(1.05, 0.95, 1.12)), head)
    P(B("Rng_Visor", (0.195, 0.0, 2.83), 0.0, (0.035, 0.13, 0.075), visor),
      head)
    P(B("Rng_VisorGlow", (0.232, 0.0, 2.85), 0.0, (0.012, 0.10, 0.018),
        glow), head)
    P(B("Rng_Jaw", (0.12, 0.0, 2.64), 0.0, (0.08, 0.13, 0.075), armor),
      head)
    P(B("Rng_BrowTrim", (0.19, 0.0, 2.99), 0.0, (0.02, 0.09, 0.02), trim),
      head)
    for s in (1.0, -1.0):
        tag = "L" if s > 0 else "R"
        P(C("Rng_EarPod_" + tag, 12, 0.052, 0.045, 2.86, dark, x=0.0,
            y=s * 0.205, rotation=(math.radians(90.0), 0.0, 0.0)), head)

    b.parent_all()
    parent_to(rail, torso)

    march_pose(rig)
    rig["shoulderR"].rotation_euler = (0.15, -0.30, 0.0)
    rig["elbowR"].rotation_euler = (0.0, -0.70, 0.0)
    rig["shoulderL"].rotation_euler = (-0.42, -0.20, 0.0)
    rig["elbowL"].rotation_euler = (0.0, -0.35, 0.0)

    recalc_normals(b.objects)
    jobs = [(armor, "metal", armor_rgb, 2101, "TroopRanged_Armor.png"),
            (glow, "glow", glow_rgb, 2107, "TroopRanged_Glow.png")]
    return b, jobs


# ----------------------------------------------------------------
# Model 3 — TroopMech: bipedal mech, arm cannons, ~4.4 tall
# ----------------------------------------------------------------

def build_mech():
    clear_scene()
    b = Builder()
    armor_rgb = (0.38, 0.46, 0.36)
    glow_rgb = (0.45, 0.95, 0.45)
    armor = make_material("MechArmor", armor_rgb, metallic=0.55, roughness=0.48)
    glow = make_material("MechGlow", glow_rgb, metallic=0.1, roughness=0.3,
                         emission=2.5)
    accent = make_material("MechAccent", (0.30, 0.36, 0.24), metallic=0.3,
                           roughness=0.5)
    dark = make_material("MechDark", (0.13, 0.14, 0.13), metallic=0.2,
                         roughness=0.7)
    chrome = make_material("MechChrome", (0.62, 0.65, 0.70), metallic=1.0,
                           roughness=0.2)
    visor = make_material("MechVisor", (0.02, 0.04, 0.07), metallic=0.9,
                          roughness=0.15)
    trim = make_material(TRIM_NAME, (0.55, 0.58, 0.62), metallic=0.7,
                         roughness=0.3, emission=0.7,
                         emission_rgb=(0.45, 0.70, 1.0))

    rig = make_rig(b, "TroopMech", 2.30, 2.56, 3.86, 0.84, 3.58,
                   0.36, 2.26, 0.38, 1.16)
    B, C, S, P = b.B, b.C, b.S, b.P

    for side, s in (("L", 1.0), ("R", -1.0)):
        hip, knee = rig["hip" + side], rig["knee" + side]
        P(B("Mech_Thigh" + side, (0.02, s * 0.38, 1.74), 0.0,
            (0.18, 0.21, 0.52), armor), hip)
        P(B("Mech_ThighTrim" + side, (0.21, s * 0.38, 1.84), 0.0,
            (0.018, 0.12, 0.34), trim), hip)
        P(S("Mech_Knee" + side, 12, 8, 0.19, (0.06, s * 0.40, 1.16),
            dark, scale=(1.1, 1.1, 1.0)), knee)
        P(B("Mech_Shin" + side, (0.0, s * 0.40, 0.60), 0.0,
            (0.15, 0.17, 0.54), armor), knee)
        P(B("Mech_ShinPlate" + side, (0.155, s * 0.40, 0.60), 0.0,
            (0.016, 0.11, 0.34), accent), knee)
        P(B("Mech_Foot" + side, (0.08, s * 0.40, 0.09), 0.0,
            (0.32, 0.18, 0.05), dark), knee)

    hips, torso, head = rig["hips"], rig["torso"], rig["head"]
    P(B("Mech_Pelvis", (0.0, 0.0, 2.36), 0.0, (0.36, 0.52, 0.26), armor),
      hips)
    P(B("Mech_WaistTrim", (0.365, 0.0, 2.48), 0.0, (0.016, 0.36, 0.04),
        trim), hips)
    for s in (1.0, -1.0):
        tag = "L" if s > 0 else "R"
        P(B("Mech_HipSkirt_" + tag, (0.0, s * 0.56, 2.18), 0.0,
            (0.22, 0.06, 0.20), accent), hips)

    P(B("Mech_Chest", (0.0, 0.0, 3.24), 0.0, (0.46, 0.66, 0.56), armor),
      torso)
    P(B("Mech_ChestPlate", (0.47, 0.0, 3.28), 0.0, (0.02, 0.42, 0.36),
        accent), torso)
    P(C("Mech_CoreTrim", 16, 0.165, 0.02, 3.28, trim, x=0.488, y=0.0,
        rotation=(0.0, math.radians(90.0), 0.0)), torso)
    P(C("Mech_CoreGlow", 16, 0.12, 0.03, 3.28, glow, x=0.495, y=0.0,
        rotation=(0.0, math.radians(90.0), 0.0)), torso)
    P(B("Mech_ChestBandTrim", (0.472, 0.0, 3.66), 0.0, (0.014, 0.46, 0.032),
        trim), torso)
    for s in (1.0, -1.0):
        tag = "L" if s > 0 else "R"
        P(B("Mech_Vent_" + tag, (0.475, s * 0.30, 2.92), 0.0,
            (0.012, 0.09, 0.11), dark), torso)
    P(C("Mech_NeckBase", 12, 0.17, 0.20, 3.86, dark), torso)
    P(B("Mech_Reactor", (-0.56, 0.0, 3.24), 0.0, (0.17, 0.42, 0.48), dark),
      torso)
    for s in (1.0, -1.0):
        tag = "L" if s > 0 else "R"
        P(C("Mech_Stack_" + tag, 12, 0.075, 0.55, 3.95, chrome,
            x=-0.58, y=s * 0.24), torso)
        P(C("Mech_StackGlow_" + tag, 12, 0.08, 0.035, 4.10, glow,
            x=-0.58, y=s * 0.24), torso)

    for side, s in (("L", 1.0), ("R", -1.0)):
        sh, el = rig["shoulder" + side], rig["elbow" + side]
        P(B("Mech_ShoulderBlock" + side, (0.0, s * 0.86, 3.58), 0.0,
            (0.24, 0.16, 0.24), armor), sh)
        P(B("Mech_ShoulderTrim" + side, (0.0, s * 0.87, 3.845), 0.0,
            (0.16, 0.12, 0.02), trim), sh)
        P(B("Mech_Pod" + side, (-0.30, s * 0.86, 3.62), 0.0,
            (0.14, 0.15, 0.18), dark), sh)
        for i, dy in enumerate((-0.07, 0.0, 0.07)):
            P(C("Mech_PodTube%s_%d" % (side, i), 10, 0.035, 0.30, 3.62,
                chrome, x=-0.10, y=s * 0.86 + dy,
                rotation=(0.0, math.radians(90.0), 0.0)), sh)
        P(B("Mech_UpperArm" + side, (0.0, s * 0.88, 3.10), 0.0,
            (0.15, 0.15, 0.42), armor), sh)
        P(S("Mech_Elbow" + side, 10, 8, 0.13, (0.0, s * 0.88, 3.06), dark),
          el)
        P(C("Mech_Cannon" + side, 14, 0.125, 0.95, 2.88, dark, x=0.30,
            y=s * 0.88, rotation=(0.0, math.radians(90.0), 0.0)), el)
        P(C("Mech_MuzzleTrim" + side, 14, 0.145, 0.06, 2.88, trim, x=0.72,
            y=s * 0.88, rotation=(0.0, math.radians(90.0), 0.0)), el)
        P(C("Mech_MuzzleGlow" + side, 12, 0.09, 0.02, 2.88, glow, x=0.79,
            y=s * 0.88, rotation=(0.0, math.radians(90.0), 0.0)), el)
        P(B("Mech_Drum" + side, (-0.28, s * 0.88, 2.88), 0.0,
            (0.16, 0.19, 0.19), accent), el)
        P(B("Mech_ForearmGuard" + side, (0.05, s * 0.88, 3.02), 0.0,
            (0.20, 0.17, 0.10), armor), el)

    P(S("Mech_SensorPod", 14, 10, 0.21, (0.06, 0.0, 4.02), armor,
        scale=(1.15, 0.9, 0.8)), head)
    P(B("Mech_VisorPlate", (0.26, 0.0, 4.04), 0.0, (0.02, 0.13, 0.05),
        visor), head)
    P(B("Mech_EyeSlit", (0.285, 0.0, 4.04), 0.0, (0.014, 0.10, 0.022),
        glow), head)
    P(C("Mech_Antenna", 6, 0.01, 0.34, 4.28, dark, x=-0.12, y=0.12), head)
    P(S("Mech_AntennaTip", 8, 6, 0.022, (-0.12, 0.12, 4.46), glow), head)

    b.parent_all()

    march_pose(rig, swing=0.20)
    rig["shoulderL"].rotation_euler = (0.0, -0.10, 0.0)
    rig["shoulderR"].rotation_euler = (0.0, -0.10, 0.0)

    recalc_normals(b.objects)
    jobs = [(armor, "metal", armor_rgb, 2201, "TroopMech_Armor.png"),
            (glow, "glow", glow_rgb, 2207, "TroopMech_Glow.png")]
    return b, jobs


def main():
    results = []
    b, jobs = build_infantry()
    results.append(finish_model("TroopInfantry", b, jobs,
                                "TroopInfantry.fbx", "TroopInfantry",
                                (3.0, 3.45)))
    b, jobs = build_ranged()
    results.append(finish_model("TroopRanged", b, jobs,
                                "TroopRanged.fbx", "TroopRanged",
                                (3.0, 3.75)))
    b, jobs = build_mech()
    results.append(finish_model("TroopMech", b, jobs,
                                "TroopMech.fbx", "TroopMech",
                                (4.05, 4.65)))
    print("BUILD RESULT:", "OK" if all(results) else "FAILED")


main()
