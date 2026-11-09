# BuildHydroponicsDomeV5.py — Lunar Dominion: Hydroponics Dome v5
# (graphics upgrade, second building after the Nexus v5).
#
# v5 = v2 + surface detail. Same drop-in contract as v2 (do not break
# it): footprint radius 1.0, height ~1.24, origin at ground centre,
# Blender Z-up. The v2 geometry is untouched; v5 only ADDS:
#   - more vent grilles + pipes + junction boxes on the foundation,
#     a small antenna + sensor pods on the rim, and extra leaf tufts
#     on the plant mounds.
#   - textures: finer panel grid (32 px cells, plus a faint 16 px
#     sub-grid) so the metal reads more detailed up close.
# The FBX + the PNGs travel together into
# Assets/_Project/Resources/Models/ as HydroponicsDome.fbx +
# HydroponicsDomeV5_*.png.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildHydroponicsDomeV5.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Written/tested against Blender 4.2.3 LTS here; uses no deprecated APIs,
# so it should also run on Blender 5.2 LTS.
# NOTE: the script exports the FBX + PNGs next to wherever the script file
# was opened from (SCRIPT_DIR). If you open it from F:\ or Downloads, the
# files land there — check the "exported:" lines in the console / report.
#
# DROP-IN CONTRACT (from v1 — do not break it): footprint radius 1.0,
# height ~1.24, origin at ground centre, Blender Z-up. v1 parts: base
# platform r 1.0 z 0-0.22, rim ring r 0.94 z 0.22-0.28, dome sphere
# r 0.86 centre z 0.68 squashed 0.55 (top 1.153), frame ring torus
# 0.84/0.045 z 0.62, vent r 0.11 top z 1.24, 3 plant mounds r 0.13 at
# radius 0.88. v2 keeps the same outer envelope: base radius 1.0,
# dome radius 0.86 at the same centre/squash, vent top at ~1.23.
# Green identity kept: green glass dome, green plants, pale-green rim
# light; the frame is spec-palette titanium/gunmetal/dark alloy.
#
# GLASS: the dome is Principled BSDF with Alpha 0.55 (blend), so the
# interior grow beds show through. Deepened 2026-11-06: the first glass
# (pale green 0.45/0.85/0.55 at Alpha 0.35) read washed-out white-mint in
# Unity, so the tint is now a deeper green and the alpha higher. Unity's
# material opacity and sets the URP material to Transparent — the
# standard Blender-to-Unity glass route. If the dome ever imports
# opaque in Unity, the interior is hidden but the model still reads
# fine (ribs + rings carry the silhouette); if it imports invisible,
# set that material's Surface Type to Transparent in the inspector.
# The glass is NOT textured (a panel grid on glass would read wrong);
# its detail is the geometry (8 ribs + 4 rings).
#
# Emission stays procedural (emissive colour inputs, not baked), so the
# rim light and grow lights keep glowing.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildHydroponicsDomeV2.py
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
from mathutils import Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# Draft outputs next to this script. When installed in the repo, rename the
# exported FBX to HydroponicsDome.fbx and move the PNGs next to it in
# Assets/_Project/Resources/Models/.
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "HydroponicsDomeV5.fbx")
ATLAS_SIZE = 1024
TEXTURE_NAMES = {
    "HydroTitanium": "HydroponicsDomeV5_Titanium.png",
    "HydroGunmetal": "HydroponicsDomeV5_Gunmetal.png",
    "HydroDarkAlloy": "HydroponicsDomeV5_DarkAlloy.png",
}

# Palette: v1 hydro colours + spec section 4 metals (linear RGB).
TITANIUM = (0.78, 0.82, 0.85)      # #C6D0DA primary armor
GUNMETAL = (0.27, 0.33, 0.39)      # #465363
DARK_ALLOY = (0.09, 0.15, 0.22)    # #172638 recesses
HYDRO_GREEN = (0.25, 0.75, 0.35)   # v1 dome green (plants, accents)
HYDRO_DARK = (0.20, 0.28, 0.22)    # v1 base dark
HYDRO_LIGHT = (0.55, 0.90, 0.60)   # v1 pale rim green
HYDRO_PLANT = (0.18, 0.60, 0.28)   # v1 plant mounds
GLASS_GREEN = (0.12, 0.62, 0.30)   # dome glass tint (deepened 2026-11-06; was pale 0.45/0.85/0.55)
GROW_WARM = (1.00, 0.95, 0.75)     # grow-light warm white

# Layout constants (Blender units; 1 unit = 1 Unity unit).
DOME_R = 0.86
DOME_Z = 0.68
DOME_SQUASH = 0.55
DOME_TOP = DOME_Z + DOME_R * DOME_SQUASH  # 1.153
CAMERA_FACE = math.radians(315)  # face nearest the game camera axis


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
        # Transparent glass: FBX carries the opacity, Unity maps it to a
        # Transparent URP material. See the header note.
        bsdf.inputs["Alpha"].default_value = alpha
        mat.blend_method = "BLEND"
    return mat


def add_cylinder(name, vertices, radius, depth, z, material, x=0.0, y=0.0):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth,
        location=(x, y, z),
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    return obj


def add_cone(name, vertices, radius_bottom, radius_top, depth, z, material,
             x=0.0, y=0.0):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=radius_bottom, radius2=radius_top,
        depth=depth, location=(x, y, z),
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
              z, material, rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius, minor_radius=minor_radius,
        major_segments=major_segments, minor_segments=minor_segments,
        location=(0.0, 0.0, z), rotation=rotation,
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    return obj


def add_dome_rib(name, angle, material):
    """Vertical rib hugging the dome: a torus scaled in Y (0.55) so the
    ring becomes an ellipse matching the squashed dome profile (semi-axes
    0.87 horizontal / 0.4785 vertical), then rotated to stand in the
    vertical plane at `angle` (Euler XYZ: Rx(90) first, then Rz(angle)).
    The rib emerges from the rim ring and converges under the vent cap."""
    bpy.ops.mesh.primitive_torus_add(
        major_radius=0.87, minor_radius=0.025,
        major_segments=20, minor_segments=4,
        location=(0.0, 0.0, 0.0))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (1.0, 0.55, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.rotation_euler = (math.radians(90), 0.0, angle)
    obj.location = (0.0, 0.0, DOME_Z)
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

    titanium = make_material("HydroTitanium", TITANIUM, metallic=0.8, roughness=0.30)
    gunmetal = make_material("HydroGunmetal", GUNMETAL, metallic=0.9, roughness=0.40)
    dark = make_material("HydroDarkAlloy", DARK_ALLOY, metallic=0.85, roughness=0.35)
    glass = make_material("HydroGlass", GLASS_GREEN, metallic=0.0, roughness=0.20,
                          alpha=0.55)
    green = make_material("HydroGreen", HYDRO_GREEN, metallic=0.10, roughness=0.45)
    plant = make_material("HydroPlant", HYDRO_PLANT, metallic=0.05, roughness=0.70)
    light = make_material("HydroLight", HYDRO_LIGHT, metallic=0.10, roughness=0.40,
                          emission=1.0)
    grow = make_material("HydroGrowLight", GROW_WARM, metallic=0.05, roughness=0.30,
                         emission=3.0)

    # ---- Foundation: plinth + two tiers + rim ring (v1 envelope).
    # z spans: plinth 0-0.06, base 0.06-0.16, mid 0.16-0.24, rim 0.22-0.28.
    add_cylinder("Hydro_Plinth", 24, 1.00, 0.06, 0.03, dark)
    add_cylinder("Hydro_BaseTier", 24, 0.97, 0.10, 0.11, titanium)
    add_cylinder("Hydro_MidTier", 24, 0.90, 0.08, 0.20, gunmetal)
    # Recessed seam rings between the tiers.
    add_torus("Hydro_Seam_01", 0.972, 0.012, 24, 4, 0.06, dark)
    add_torus("Hydro_Seam_02", 0.902, 0.012, 24, 4, 0.16, dark)
    # Pale rim ring on top of the foundation (v1's accent, now emissive).
    add_cylinder("Hydro_RimRing", 24, 0.94, 0.06, 0.25, light)
    # Interior floor plate the grow beds sit on (inside the dome base).
    add_cylinder("Hydro_Floor", 24, 0.38, 0.03, 0.285, gunmetal)
    # Gunmetal trim ring around the base tier.
    add_torus("Hydro_TrimRing", 0.972, 0.015, 24, 4, 0.11, gunmetal)
    # Vent grilles on the base tier (proud of the 24-gon face, apothem 0.962).
    for i, a_deg in enumerate((90, 210)):
        a = math.radians(a_deg)
        add_box("Hydro_VentGrille_%d" % (i + 1),
                (0.96 * math.cos(a), 0.96 * math.sin(a), 0.11),
                a, (0.01, 0.05, 0.02), dark)
    # Vertical pipes on the base tier.
    for i, a_deg in enumerate((60, 240)):
        a = math.radians(a_deg)
        add_cylinder("Hydro_Pipe_%d" % (i + 1), 6, 0.025, 0.18, 0.15,
                     gunmetal, x=0.95 * math.cos(a), y=0.95 * math.sin(a))
    # Access door + frame on the camera-facing 315-degree face.
    da = CAMERA_FACE
    add_box("Hydro_DoorFrame",
            (0.958 * math.cos(da), 0.958 * math.sin(da), 0.11),
            da, (0.008, 0.10, 0.085), light)
    add_box("Hydro_Door",
            (0.96 * math.cos(da), 0.96 * math.sin(da), 0.11),
            da, (0.015, 0.08, 0.07), dark)
    # Crates on the rim-ring top (z 0.28).
    add_box("Hydro_Crate_01",
            (0.82 * math.cos(math.radians(120)), 0.82 * math.sin(math.radians(120)), 0.315),
            math.radians(120) + math.pi / 2.0, (0.045, 0.045, 0.035), gunmetal)
    add_box("Hydro_Crate_Stack",
            (0.82 * math.cos(math.radians(120)), 0.82 * math.sin(math.radians(120)), 0.37),
            math.radians(120) + math.pi / 2.0, (0.025, 0.025, 0.02), dark)
    add_box("Hydro_Crate_02",
            (0.82 * math.cos(math.radians(180)), 0.82 * math.sin(math.radians(180)), 0.305),
            math.pi / 2.0, (0.03, 0.03, 0.025), dark)
    # Bollard lights on the rim-ring top, clear of the door and mounds.
    for i, a_deg in enumerate((100, 220, 340)):
        a = math.radians(a_deg)
        add_cone("Hydro_Bollard_%d" % (i + 1), 6, 0.03, 0.012, 0.09, 0.325,
                 light, x=0.88 * math.cos(a), y=0.88 * math.sin(a))

    # ---- Glass dome: same sphere as v1 (r 0.86, centre z 0.68, squash
    # 0.55, top 1.153), higher segment count, transparent green glass.
    add_sphere("Hydro_Dome", 40, 20, DOME_R, (0.0, 0.0, DOME_Z), DOME_SQUASH, glass)
    # 8 longitudinal ribs (elliptical tori hugging the dome profile).
    for i in range(8):
        add_dome_rib("Hydro_Rib_%02d" % (i + 1), i * math.pi / 4.0, titanium)
    # Latitudinal rings at heights matching the dome surface radius:
    # z 0.45 -> r 0.752, z 0.62 -> r 0.853, z 0.75 -> r 0.851, z 0.95 -> r 0.706.
    add_torus("Hydro_Ring_01", 0.76, 0.02, 24, 4, 0.45, titanium)
    add_torus("Hydro_Ring_02", 0.855, 0.045, 24, 6, 0.62, dark)
    add_torus("Hydro_Ring_03", 0.855, 0.02, 24, 4, 0.75, titanium)
    add_torus("Hydro_Ring_04", 0.71, 0.02, 24, 4, 0.95, titanium)
    # Top vent: base + cap + grille slats (top ~1.232, inside the v1 1.24).
    add_cylinder("Hydro_VentBase", 8, 0.13, 0.06, 1.15, gunmetal)
    add_cylinder("Hydro_VentCap", 8, 0.15, 0.04, 1.20, dark)
    for i, a_deg in enumerate((0, 60, 120)):
        add_box("Hydro_VentSlat_%d" % (i + 1), (0.0, 0.0, 1.225),
                math.radians(a_deg), (0.12, 0.01, 0.0075), dark)

    # ---- Interior grow beds: 3 trays with plant rows, visible through
    # the glass. Tray rows at y -0.28 / 0 / +0.28, tops at z 0.35.
    for i, (y, length) in enumerate(((-0.28, 0.60), (0.0, 0.85), (0.28, 0.60))):
        add_box("Hydro_BedTray_%d" % (i + 1), (0.0, y, 0.325),
                0.0, (length / 2.0, 0.07, 0.025), dark)
    # Plant tufts: small cones on the trays (5 middle, 4 per outer row).
    tuft = 0
    for y, xs in ((-0.28, (-0.21, -0.07, 0.07, 0.21)),
                  (0.0, (-0.32, -0.16, 0.0, 0.16, 0.32)),
                  (0.28, (-0.21, -0.07, 0.07, 0.21))):
        for x in xs:
            tuft += 1
            add_cone("Hydro_Crop_%02d" % tuft, 6, 0.045, 0.008, 0.09, 0.395,
                     plant, x=x, y=y)
    # Grow lights: central mast + glowing ring + 3 hanging bulbs.
    add_cylinder("Hydro_LightMast", 6, 0.02, 0.65, 0.625, dark)
    add_torus("Hydro_GrowRing", 0.40, 0.02, 24, 4, 0.85, grow)
    for i, a_deg in enumerate((30, 150, 270)):
        a = math.radians(a_deg)
        add_sphere("Hydro_GrowBulb_%d" % (i + 1), 6, 4, 0.03,
                   (0.40 * math.cos(a), 0.40 * math.sin(a), 0.82), 1.0, grow)

    # ---- Exterior plant mounds on the rim (v1: 3 mounds at 40/160/280),
    # each with 3 leaf tufts.
    for i, a_deg in enumerate((40, 160, 280)):
        a = math.radians(a_deg)
        x, y = 0.88 * math.cos(a), 0.88 * math.sin(a)
        add_sphere("Hydro_Plant%d" % (i + 1), 8, 4, 0.13, (x, y, 0.30), 0.6, plant)
        for j, (dx, dy) in enumerate(((-0.05, 0.0), (0.05, 0.03), (0.0, -0.06))):
            add_cone("Hydro_Leaf_%d_%d" % (i + 1, j + 1), 6, 0.04, 0.008, 0.10,
                     0.36, plant, x=x + dx, y=y + dy)

    # ---- V5 surface detail (graphics upgrade): greebles on the v2
    # geometry. Nothing here moves the footprint or the v2 parts.
    # More vent grilles on the base tier (v2 has 90/210; add 30/150/270).
    for i, a_deg in enumerate((30, 150, 270)):
        a = math.radians(a_deg)
        add_box("V5_VentGrille_%d" % (i + 1),
                (0.96 * math.cos(a), 0.96 * math.sin(a), 0.11),
                a, (0.01, 0.05, 0.02), dark)
    # More vertical pipes on the base tier (v2 has 60/240; add 120/300).
    for i, a_deg in enumerate((120, 300)):
        a = math.radians(a_deg)
        add_cylinder("V5_Pipe_%d" % (i + 1), 6, 0.025, 0.18, 0.15,
                     gunmetal, x=0.95 * math.cos(a), y=0.95 * math.sin(a))
    # Junction boxes on the base tier (0/180), above the vent line.
    for i, a_deg in enumerate((0, 180)):
        a = math.radians(a_deg)
        add_box("V5_JunctionBox_%d" % (i + 1),
                (0.96 * math.cos(a), 0.96 * math.sin(a), 0.14),
                a, (0.01, 0.04, 0.02), dark)
    # Small antenna on the rim-ring top (0 degrees — clear of the door
    # at 315, crates at 120/180, bollards at 100/220/340, mounds at
    # 40/160/280) + a glowing tip.
    add_cylinder("V5_Antenna", 6, 0.015, 0.30, 0.43, dark,
                 x=0.88 * math.cos(0.0), y=0.88 * math.sin(0.0))
    add_sphere("V5_Antenna_Tip", 6, 4, 0.025,
               (0.88, 0.0, 0.59), 1.0, light)
    # Sensor pods on the rim-ring top (60/300 degrees).
    for i, a_deg in enumerate((60, 300)):
        a = math.radians(a_deg)
        add_sphere("V5_SensorPod_%d" % (i + 1), 6, 4, 0.035,
                   (0.88 * math.cos(a), 0.88 * math.sin(a), 0.30), 1.0, gunmetal)
    # One extra leaf tuft per plant mound (a 4th leaf, offset outward).
    for i, a_deg in enumerate((40, 160, 280)):
        a = math.radians(a_deg)
        x, y = 0.88 * math.cos(a), 0.88 * math.sin(a)
        add_cone("V5_Leaf_%d" % (i + 1), 6, 0.04, 0.008, 0.10,
                 0.36, plant, x=x + 0.06, y=y - 0.03)

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
    panel grid (v5: 32 px cells, 3 px lines — finer than v2's 48 px —
    plus a faint 16 px sub-grid), rivets at the cell corners,
    per-cell brightness variation, and soft grime blotches. Returns the
    Blender image, saved as a PNG next to the script. Turned-up
    settings (same as the Nexus textures after the faint first pass)."""
    S = ATLAS_SIZE
    CELL = 32
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    # Panel grid: lines where the within-cell position is near a border.
    cx = np.mod(xx, CELL)
    cy = np.mod(yy, CELL)
    line = ((cx < 3) | (cy < 3)).astype(np.float32)
    # Faint sub-grid at half the cell size (16 px), for finer detail.
    sx = np.mod(xx, CELL // 2)
    sy = np.mod(yy, CELL // 2)
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
    of (material name, filepath, bytes)."""
    seeds = {"HydroTitanium": 41, "HydroGunmetal": 53, "HydroDarkAlloy": 67}
    base_colors = {"HydroTitanium": TITANIUM, "HydroGunmetal": GUNMETAL,
                   "HydroDarkAlloy": DARK_ALLOY}
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
        path_mode="COPY",
        embed_textures=False,
    )


def report(objects):
    total_tris = 0
    print("Hydroponics Dome v2 build:")
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
    # Bounding box check: footprint must stay within radius 1.0, height ~1.24.
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
                  if m.name in ("HydroTitanium", "HydroGunmetal", "HydroDarkAlloy")]
    make_uv_atlas(built)
    TEXTURE_INFO = apply_metal_textures(metal_mats)
    export(built)
    report(built)
