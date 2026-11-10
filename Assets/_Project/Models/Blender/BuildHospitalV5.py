# BuildHospitalV5.py — Lunar Dominion: Hospital v5
# (new 9th building, 2026-11-09; built at v5 standard from the start).
#
# White medical building: white/titanium main body, red crosses on the
# roof and on the front (+X) faces, cyan glowing windows, entrance
# canopy over the front door, helipad ring on the roof, vents, pipes,
# junction boxes, hazard bars and bollards as v5 surface detail.
# Drop-in contract (matches BaseView TryAddModelHospital): footprint
# 2.2 x 2.2, height ~2.12 (antenna tip). Textures are v5-fine: 32 px
# panel cells + a faint 16 px sub-grid.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildHospitalV5.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Written/tested against Blender 4.2.3 LTS here; uses no deprecated APIs,
# so it should also run on Blender 5.2 LTS.
# NOTE: the script exports the FBX + PNGs next to wherever the script file
# was opened from (SCRIPT_DIR). If you open it from F:\ or Downloads, the
# files land there — check the "exported:" lines in the console / report.

import math
import os
import struct
import zlib

import bpy
import numpy as np

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# Draft outputs next to this script. When installed in the repo, rename the
# exported FBX to Hospital.fbx and move the PNGs next to it in
# Assets/_Project/Resources/Models/.
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "HospitalV5.fbx")
ATLAS_SIZE = 1024
TEXTURE_NAMES = {
    "HospitalWhite": "HospitalV5_White.png",
    "HospitalTitanium": "HospitalV5_Titanium.png",
    "HospitalGunmetal": "HospitalV5_Gunmetal.png",
    "HospitalRed": "HospitalV5_Red.png",
}

# Palette: white medical body + spec section 4 metals (linear RGB).
HOSP_WHITE = (0.95, 0.95, 0.95)     # main body / medical white
TITANIUM = (0.78, 0.82, 0.85)       # #C6D0DA primary armor
GUNMETAL = (0.27, 0.33, 0.39)       # #465363
DARK_ALLOY = (0.09, 0.15, 0.22)     # #172638 recesses
HOSP_RED = (0.85, 0.15, 0.15)       # saturated red cross (reads at distance)
CYAN_GLOW = (0.20, 0.80, 1.00)      # saturated cyan windows / tips


def make_material(name, rgb, metallic=0.1, roughness=0.6, emission=0.0,
                  emission_rgb=None, alpha=1.0):
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
    if alpha < 1.0:
        bsdf.inputs["Alpha"].default_value = alpha
        mat.blend_method = "BLEND"
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


def add_sphere(name, segments, rings, radius, location, z_scale, material):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=radius, location=location
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (1.0, 1.0, z_scale)
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


def add_torus(name, major_radius, minor_radius, major_segments, minor_segments,
              z, material, rotation=(0.0, 0.0, 0.0), x=0.0, y=0.0):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius, minor_radius=minor_radius,
        major_segments=major_segments, minor_segments=minor_segments,
        location=(x, y, z), rotation=rotation,
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    return obj


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in list(bpy.data.meshes):
        bpy.data.meshes.remove(block)
    for block in list(bpy.data.materials):
        bpy.data.materials.remove(block)


def build():
    clear_scene()

    white = make_material("HospitalWhite", HOSP_WHITE, metallic=0.15, roughness=0.50)
    titanium = make_material("HospitalTitanium", TITANIUM, metallic=0.8, roughness=0.30)
    gunmetal = make_material("HospitalGunmetal", GUNMETAL, metallic=0.9, roughness=0.40)
    dark = make_material("HospitalDarkAlloy", DARK_ALLOY, metallic=0.85, roughness=0.35)
    red = make_material("HospitalRed", HOSP_RED, metallic=0.15, roughness=0.50)
    glow = make_material("HospitalGlow", CYAN_GLOW, metallic=0.10, roughness=0.30,
                         emission=2.5)

    # ---- Foundation: stepped platform (envelope 2.2 x 2.2).
    # Plinth 2.2 x 2.2 z 0-0.06, tier 2.0 x 2.0 z 0.06-0.12.
    add_box("Hosp_Plinth", (0.0, 0.0, 0.03), 0.0, (1.10, 1.10, 0.03), dark)
    add_box("Hosp_Tier", (0.0, 0.0, 0.09), 0.0, (1.00, 1.00, 0.03), titanium)
    # Vent grilles on the plinth faces (camera-facing -Y, and +X).
    add_box("Hosp_VentGrille_01", (0.30, -1.11, 0.04), 0.0, (0.15, 0.01, 0.04), dark)
    add_box("Hosp_VentGrille_02", (1.11, 0.30, 0.04), 0.0, (0.01, 0.15, 0.04), dark)

    # ---- Main body: white box x +-0.80, y +-0.70, z 0.12-1.30.
    add_box("Hosp_Body", (0.0, 0.0, 0.71), 0.0, (0.80, 0.70, 0.59), white)
    # Upper block, stepped in: x +-0.65, y +-0.55, z 1.30-1.62.
    add_box("Hosp_Upper", (0.0, 0.0, 1.46), 0.0, (0.65, 0.55, 0.16), white)
    # Flat roof slab (dark, slight eave over the upper block), z 1.62-1.66.
    add_box("Hosp_Roof", (0.0, 0.0, 1.64), 0.0, (0.75, 0.65, 0.02), dark)
    # Titanium trim band around the body top (z 1.28-1.32), where the
    # upper block steps in.
    add_box("Hosp_TrimFront", (0.0, -0.705, 1.30), 0.0, (0.80, 0.01, 0.02), titanium)
    add_box("Hosp_TrimBack", (0.0, 0.705, 1.30), 0.0, (0.80, 0.01, 0.02), titanium)
    add_box("Hosp_TrimLeft", (-0.805, 0.0, 1.30), 0.0, (0.01, 0.70, 0.02), titanium)
    add_box("Hosp_TrimRight", (0.805, 0.0, 1.30), 0.0, (0.01, 0.70, 0.02), titanium)
    # Corner trim on the upper block (proud of its faces by 0.01).
    for i, (x, y) in enumerate(((0.63, 0.53), (-0.63, 0.53),
                               (0.63, -0.53), (-0.63, -0.53))):
        add_box("Hosp_UpperTrim_%02d" % (i + 1), (x, y, 1.46), 0.0,
                (0.03, 0.03, 0.16), titanium)

    # ---- Red cross on the roof (lying flat on the roof slab, z 1.66-1.70).
    add_box("Hosp_RoofCross_A", (0.0, 0.0, 1.68), 0.0, (0.30, 0.10, 0.02), red)
    add_box("Hosp_RoofCross_B", (0.0, 0.0, 1.68), 0.0, (0.10, 0.30, 0.02), red)

    # ---- Red cross on the front (-Y) face, centred z 0.95.
    add_box("Hosp_FrontCross_V", (0.0, -0.705, 0.95), 0.0, (0.10, 0.01, 0.20), red)
    add_box("Hosp_FrontCross_H", (0.0, -0.705, 0.95), 0.0, (0.30, 0.01, 0.10), red)
    # ---- Red cross on the +X side face, same centre height.
    add_box("Hosp_SideCross_V", (0.805, 0.0, 0.95), 0.0, (0.01, 0.10, 0.20), red)
    add_box("Hosp_SideCross_H", (0.805, 0.0, 0.95), 0.0, (0.01, 0.30, 0.10), red)

    # ---- Cyan glowing windows: front pair (flanking the red cross),
    # back band, side bands, and a strip on the upper block front.
    add_box("Hosp_WindowFront_L", (-0.45, -0.705, 0.95), 0.0, (0.20, 0.01, 0.06), glow)
    add_box("Hosp_WindowFront_R", (0.45, -0.705, 0.95), 0.0, (0.20, 0.01, 0.06), glow)
    add_box("Hosp_WindowBack", (0.0, 0.705, 0.95), 0.0, (0.60, 0.01, 0.06), glow)
    add_box("Hosp_WindowLeft", (-0.805, 0.0, 0.95), 0.0, (0.01, 0.40, 0.06), glow)
    add_box("Hosp_WindowRight", (0.805, 0.0, 0.95), 0.0, (0.01, 0.40, 0.06), glow)
    add_box("Hosp_WindowUpper", (0.0, -0.555, 1.46), 0.0, (0.40, 0.01, 0.04), glow)
    add_box("Hosp_WindowUpper_L", (-0.655, 0.0, 1.46), 0.0, (0.01, 0.30, 0.04), glow)
    add_box("Hosp_WindowUpper_R", (0.655, 0.0, 1.46), 0.0, (0.01, 0.30, 0.04), glow)

    # ---- Entrance on the camera-facing (-Y) face: titanium frame, dark
    # door, step, canopy on two posts, glowing sign above the door.
    add_box("Hosp_DoorFrame", (0.0, -0.71, 0.40), 0.0, (0.28, 0.02, 0.28), titanium)
    add_box("Hosp_Door", (0.0, -0.715, 0.37), 0.0, (0.20, 0.02, 0.25), dark)
    add_box("Hosp_DoorStep", (0.0, -0.83, 0.145), 0.0, (0.30, 0.08, 0.025), gunmetal)
    add_box("Hosp_Canopy", (0.0, -0.82, 0.72), 0.0, (0.35, 0.15, 0.03), titanium)
    for i, x in enumerate((-0.30, 0.30)):
        add_cylinder("Hosp_CanopyPost_%02d" % (i + 1), 6, 0.02, 0.60, 0.42,
                     gunmetal, x=x, y=-0.90)
    add_box("Hosp_DoorSign", (0.0, -0.72, 0.68), 0.0, (0.12, 0.01, 0.03), glow)

    # ---- Vertical pipes at the four body corners (r 0.025, z 0.12-1.30).
    for i, (x, y) in enumerate(((0.81, 0.71), (-0.81, 0.71),
                               (0.81, -0.71), (-0.81, -0.71))):
        add_cylinder("Hosp_Pipe_%02d" % (i + 1), 8, 0.025, 1.18, 0.71,
                     gunmetal, x=x, y=y)

    # ---- Roof plant: two vent boxes, a vent stack with cap, a helipad
    # ring (titanium, flat on the roof), a sensor pod with glowing tip.
    add_box("Hosp_RoofVent_01", (0.52, 0.30, 1.70), 0.0, (0.08, 0.08, 0.04), dark)
    add_box("Hosp_RoofVent_02", (-0.52, -0.30, 1.70), 0.0, (0.08, 0.08, 0.04), dark)
    add_cylinder("Hosp_VentStack", 8, 0.05, 0.20, 1.76, gunmetal, x=0.52, y=-0.42)
    add_cylinder("Hosp_VentCap", 8, 0.06, 0.03, 1.875, dark, x=0.52, y=-0.42)
    add_torus("Hosp_Helipad", 0.16, 0.015, 16, 3, 1.67, titanium, x=-0.52, y=0.42)
    add_cylinder("Hosp_SensorPod", 6, 0.05, 0.10, 1.71, gunmetal, x=0.30, y=-0.50)
    add_sphere("Hosp_SensorTip", 6, 4, 0.02, (0.30, -0.50, 1.77), 1.0, glow)

    # ---- Antennae on the roof: main antenna + glowing tip (tallest
    # point, ~2.12), and a shorter whip on the opposite corner.
    add_cylinder("Hosp_Antenna", 8, 0.02, 0.40, 1.86, gunmetal, x=0.55, y=0.48)
    add_sphere("Hosp_AntennaTip", 8, 6, 0.035, (0.55, 0.48, 2.08), 1.0, glow)
    add_cylinder("Hosp_Whip", 6, 0.012, 0.20, 1.76, gunmetal, x=-0.55, y=-0.48)
    add_sphere("Hosp_WhipTip", 6, 4, 0.02, (-0.55, -0.48, 1.87), 1.0, glow)

    # ---- Medical supply crates + a white canister with a red cap on
    # the platform (clear of the door, canopy posts and bollards).
    add_box("Hosp_Crate_01", (0.88, 0.70, 0.18), 0.4, (0.09, 0.09, 0.06), white)
    add_box("Hosp_Crate_02", (0.88, 0.70, 0.30), 0.4, (0.06, 0.06, 0.06), gunmetal)
    add_box("Hosp_Crate_03", (-0.88, 0.52, 0.16), -0.3, (0.07, 0.07, 0.04), dark)
    add_box("Hosp_Crate_04", (0.70, -0.88, 0.16), 0.2, (0.07, 0.07, 0.04), white)
    add_box("Hosp_Crate_05", (-0.70, -0.88, 0.16), -0.5, (0.08, 0.08, 0.04), gunmetal)
    add_cylinder("Hosp_Canister", 12, 0.09, 0.14, 0.19, white, x=-0.88, y=-0.70)
    add_cylinder("Hosp_CanisterCap", 12, 0.095, 0.03, 0.275, red, x=-0.88, y=-0.70)

    # ---- Bollard lights on the platform corners (cyan glow).
    for i, (x, y) in enumerate(((0.95, 0.95), (-0.95, 0.95),
                               (0.95, -0.95), (-0.95, -0.95))):
        add_cone("Hosp_Bollard_%02d" % (i + 1), 6, 0.03, 0.012, 0.09, 0.165,
                 glow, x=x, y=y)

    # ---- v5 greebles (graphics upgrade): surface detail inside the
    # 2.2 x 2.2 envelope, z <= 2.12. Two more vent grilles on the
    # plinth (base has -Y and +X; these add +Y and -X).
    add_box("Hosp_VentGrille_03", (-0.30, 1.11, 0.04), 0.0, (0.15, 0.01, 0.04), dark)
    add_box("Hosp_VentGrille_04", (-1.11, -0.30, 0.04), 0.0, (0.01, 0.15, 0.04), dark)
    # Junction boxes on the back (+Y) and -X faces.
    add_box("Hosp_JunctionBox_01", (0.45, 0.705, 0.60), 0.0, (0.12, 0.02, 0.10), gunmetal)
    add_box("Hosp_JunctionBox_02", (-0.45, 0.705, 1.00), 0.0, (0.12, 0.02, 0.10), gunmetal)
    add_box("Hosp_JunctionBox_03", (-0.805, 0.30, 0.60), 0.0, (0.02, 0.12, 0.10), gunmetal)
    # A second, lower glowing window strip (base band is at z 0.95).
    add_box("Hosp_WindowBand2_Back", (0.0, 0.705, 0.45), 0.0, (0.50, 0.01, 0.05), glow)
    add_box("Hosp_WindowBand2_L", (-0.805, 0.0, 0.45), 0.0, (0.01, 0.30, 0.05), glow)
    add_box("Hosp_WindowBand2_R", (0.805, 0.0, 0.45), 0.0, (0.01, 0.30, 0.05), glow)
    # Hazard bars on the tier front (-Y) face (tier face at y = -1.00).
    add_box("Hosp_HazardBar_01", (-0.55, -1.005, 0.09), 0.0, (0.15, 0.01, 0.02), glow)
    add_box("Hosp_HazardBar_02", (0.55, -1.005, 0.09), 0.0, (0.15, 0.01, 0.02), glow)

    return list(bpy.context.scene.objects)


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
    # Panel grid: lines where the within-cell position is near a border.
    cx = np.mod(xx, CELL)
    cy = np.mod(yy, CELL)
    line = ((cx < 4) | (cy < 4)).astype(np.float32)
    # Faint sub-grid: a finer 16 px panel read inside each 32 px cell.
    sx = np.mod(xx, SUB)
    sy = np.mod(yy, SUB)
    subline = ((sx < 2) | (sy < 2)).astype(np.float32)
    # Per-cell brightness variation (deterministic hash of cell indices).
    cell_x = (xx // CELL).astype(np.int32)
    cell_y = (yy // CELL).astype(np.int32)
    h = np.sin(cell_x * 127.1 + cell_y * 311.7) * 43758.5453
    h = h - np.floor(h)
    cell_bright = 0.90 + 0.20 * h
    # Rivets: small dots at the cell corners.
    dx = np.minimum(cx, CELL - cx)
    dy = np.minimum(cy, CELL - cy)
    dist_corner = np.sqrt(dx * dx + dy * dy)
    rivet = (dist_corner < 3.5).astype(np.float32)
    # Grime: soft dark blotches (random circles, radial falloff).
    grime = np.zeros((S, S), dtype=np.float32)
    for _ in range(48):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(40, 140)
        strength = rng.uniform(0.08, 0.25)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        grime += strength * np.exp(-d2 / (2.0 * (r / 2.5) ** 2))
    grime = np.clip(grime, 0.0, 0.45)
    # Combine into RGB (float 0-1).
    base = np.array(rgb, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= cell_bright[..., None]
    arr *= (1.0 - line[..., None] * 0.75)
    arr *= (1.0 - subline[..., None] * 0.25)
    arr *= (1.0 + rivet[..., None] * 0.25)
    arr *= (1.0 - grime[..., None])
    arr = np.clip(arr, 0.0, 1.0)
    # Blender images are RGBA; add an opaque alpha channel.
    rgba = np.ones((S, S, 4), dtype=np.float32)
    rgba[..., :3] = arr
    img = bpy.data.images.new(filename, S, S, alpha=False)
    img.pixels.foreach_set(rgba.ravel())
    path = os.path.join(SCRIPT_DIR, filename)
    # Write the PNG directly (see write_png): image.save() would blacken it.
    write_png(path, arr)
    img.filepath = path
    img.file_format = "PNG"
    return img


def apply_metal_textures(metal_materials):
    """Generate one texture per metal material, plug it into the
    material's Base Color, and set the base colour to white so Unity's
    (map x colour) multiply returns exactly the texture. Returns a list
    of (material name, filepath, bytes). Four textures here: the white
    body is the Hospital's hero surface, so it gets its own panel
    texture, as does the red cross."""
    seeds = {"HospitalWhite": 113, "HospitalTitanium": 127,
             "HospitalGunmetal": 131, "HospitalRed": 137}
    base_colors = {"HospitalWhite": HOSP_WHITE, "HospitalTitanium": TITANIUM,
                   "HospitalGunmetal": GUNMETAL, "HospitalRed": HOSP_RED}
    info = []
    for mat in metal_materials:
        filename = TEXTURE_NAMES[mat.name]
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
    print("Hospital v5 build:")
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
    # Bounding box check: footprint must stay 2.2 x 2.2, height ~2.12.
    pts = [o.matrix_world @ v.co for o in objects if o.type == "MESH"
           for v in o.data.vertices]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    print("  bounds x=[%.2f, %.2f] y=[%.2f, %.2f] z=[%.2f, %.2f]"
          % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    print("  exported: %s" % OUTPUT_FBX)
    print("  fbx bytes: %d" % os.path.getsize(OUTPUT_FBX))
    for mat_name, path, size in TEXTURE_INFO:
        print("  texture %s: %s (%d bytes)" % (mat_name, path, size))


if __name__ == "__main__":
    built = build()
    metal_mats = [m for m in bpy.data.materials
                  if m.name in ("HospitalWhite", "HospitalTitanium",
                                "HospitalGunmetal", "HospitalRed")]
    make_uv_atlas(built)
    TEXTURE_INFO = apply_metal_textures(metal_mats)
    export(built)
    report(built)
