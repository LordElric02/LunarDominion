# BuildMarineFoundryV2.py — Lunar Dominion: Marine Foundry v2
# (detailed geometry + baked panel textures).
#
# DRAFT — not yet in the Unity project and NOT pushed to GitHub.
# Rebuilds the Marine Foundry at the Nexus v4 standard: same footprint,
# height and position as the v1 Foundry (BuildMarineFoundry.py, 164 tris,
# the smallest v1 model), but with real detail — a fortified barracks
# block with a training yard, a watchtower, an armory wing, recruitment
# gates, barriers/sandbags, floodlights, ammo crates, vents and pipes,
# and a unit banner. Textures use the proven Nexus pipeline:
# pixel-generated with numpy (turned-up settings: 48 px cells, 4 px
# lines, 0.75 line darkening, +/-10% per-cell variation, stronger
# grime), Lightmap Pack UV atlas. No Cycles bake — it proved unusable
# headless (a flat-colour bake takes 5+ minutes on this VM).
#
# DROP-IN CONTRACT (from v1 — do not break it): footprint 2.0 x 2.0
# (base platform), height ~1.47 (flag top edge), origin at ground
# centre, Blender Z-up. v1 parts: dark base platform 2.0 x 2.0
# z 0-0.12, main green barracks block 1.5 x 1.2 x 0.9 z 0.12-1.02,
# flat dark roof slab 1.6 x 1.3 top z 1.10, annex on the +X side
# (0.6 x 0.8 x 0.55, x 0.35-0.95), thin antenna on the roof (top
# z 1.45), flag pole + small orange flag (top edge z 1.47), pale
# doorway + two pale windows on the camera-facing (-Y) face, dark
# vent on the +X face. v2 keeps the same outer envelope: base
# 2.0 x 2.0, block 1.5 x 1.2, roof top z 1.10, tallest point the
# flag top at z 1.46. Green identity kept: green barracks block and
# annex, dark roof/foundation, orange flag/banner, pale doorway;
# the frame is spec-palette titanium/gunmetal/dark alloy.
#
# CAMERA NOTE (from v1): the game camera sits at Unity +X/+Z, so
# Blender -Y and +X faces are the visible ones. The Foundry sits on
# the far side of the base from the camera, so details go on -Y / +X
# faces. The watchtower is at the back-right (+X/+Y) corner so it
# reads above the roof without occluding the front.
#
# EMISSION (lesson from the Hydroponics dome): pale tints read
# washed-out white in Unity. The glowing parts here use clearly
# saturated colours with strong emission — warm amber (1.00, 0.60,
# 0.15) at 2.5 for the windows, floodlights, door sign and antenna
# tip — not pale tints. There is no glass on the Foundry, so no
# transparency import caveat.
#
# Emission stays procedural (emissive colour inputs, not baked), so
# the windows, floodlights and sign keep glowing.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildMarineFoundryV2.py
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
# exported FBX to MarineFoundry.fbx and move the PNGs next to it in
# Assets/_Project/Resources/Models/.
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "MarineFoundryV2.fbx")
ATLAS_SIZE = 1024
TEXTURE_NAMES = {
    "MarineTitanium": "MarineFoundryV2_Titanium.png",
    "MarineGunmetal": "MarineFoundryV2_Gunmetal.png",
    "MarineDarkAlloy": "MarineFoundryV2_DarkAlloy.png",
    "MarineGreen": "MarineFoundryV2_Green.png",
}

# Palette: v1 Foundry colours + spec section 4 metals (linear RGB).
TITANIUM = (0.78, 0.82, 0.85)      # #C6D0DA primary armor
GUNMETAL = (0.27, 0.33, 0.39)      # #465363
DARK_ALLOY = (0.09, 0.15, 0.22)    # #172638 recesses
MARINE_GREEN = (0.35, 0.65, 0.25)  # v1 barracks block / annex green
MARINE_DARK = (0.20, 0.35, 0.18)   # v1 dark green (base, roof)
MARINE_LIGHT = (0.85, 0.88, 0.80)  # v1 pale doorway / windows
MARINE_ORANGE = (0.85, 0.45, 0.15)  # v1 flag orange
AMBER_GLOW = (1.00, 0.60, 0.15)    # saturated amber windows / floodlights

# Layout constants (Blender units; 1 unit = 1 Unity unit).
BASE_HALF = 1.00
BLOCK_X, BLOCK_Y, BLOCK_Z0, BLOCK_Z1 = 0.75, 0.60, 0.12, 1.02
ROOF_TOP = 1.10
MAX_Z = 1.47  # v1 flag top edge; nothing in v2 goes above this.


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

    titanium = make_material("MarineTitanium", TITANIUM, metallic=0.8, roughness=0.30)
    gunmetal = make_material("MarineGunmetal", GUNMETAL, metallic=0.9, roughness=0.40)
    dark = make_material("MarineDarkAlloy", DARK_ALLOY, metallic=0.85, roughness=0.35)
    green = make_material("MarineGreen", MARINE_GREEN, metallic=0.25, roughness=0.55)
    darkgreen = make_material("MarineDarkGreen", MARINE_DARK, metallic=0.40, roughness=0.55)
    light = make_material("MarineLight", MARINE_LIGHT, metallic=0.10, roughness=0.45)
    orange = make_material("MarineOrange", MARINE_ORANGE, metallic=0.10, roughness=0.50)
    glow = make_material("MarineGlow", AMBER_GLOW, metallic=0.10, roughness=0.30,
                         emission=2.5)

    # ---- Foundation: dark plinth 2.0 x 2.0 z 0-0.06, titanium tier
    # 1.9 x 1.9 z 0.06-0.12 (v1 envelope).
    add_box("Marine_Plinth", (0.0, 0.0, 0.03), 0.0, (1.00, 1.00, 0.03), darkgreen)
    add_box("Marine_Tier", (0.0, 0.0, 0.09), 0.0, (0.95, 0.95, 0.03), titanium)
    # Vent grilles on the plinth faces (camera-facing -Y, and +X).
    add_box("Marine_VentGrille_01", (0.30, -1.005, 0.035), 0.0, (0.15, 0.01, 0.035), dark)
    add_box("Marine_VentGrille_02", (1.005, 0.30, 0.035), 0.0, (0.01, 0.15, 0.035), dark)
    # Recessed seam ring between plinth and tier.
    add_torus("Marine_Seam", 0.97, 0.012, 16, 3, 0.06, dark)

    # ---- Main barracks block: 1.5 x 1.2 x 0.9, green, z 0.12-1.02 (as v1).
    add_box("Marine_Block", (0.0, 0.0, 0.57), 0.0, (0.75, 0.60, 0.45), green)
    # Corner pilasters (thin titanium boxes, proud of the block walls).
    for (x, y) in ((-0.72, -0.57), (0.72, -0.57), (-0.72, 0.57), (0.72, 0.57)):
        add_box("Marine_Pilaster_%+d%+d" % (int(x * 100), int(y * 100)),
                (x, y, 0.57), 0.0, (0.05, 0.05, 0.45), titanium)
    # Horizontal segmentation band around the block (dark, slightly proud).
    add_box("Marine_Band", (0.0, 0.0, 0.88), 0.0, (0.77, 0.62, 0.025), dark)
    # Vertical pipes on the block (-Y face left of the door, +X face).
    add_cylinder("Marine_Pipe_01", 8, 0.025, 0.83, 0.535,
                 gunmetal, x=-0.65, y=-0.615)
    add_cylinder("Marine_Pipe_02", 8, 0.025, 0.83, 0.535,
                 gunmetal, x=0.765, y=-0.45)
    # Dark vent grille on the +X face of the block (clear of the annex).
    add_box("Marine_Vent", (0.775, -0.45, 0.80), 0.0, (0.025, 0.15, 0.10), dark)

    # ---- Roof: dark slab 1.6 x 1.3 x 0.08, top z 1.10 (as v1), with a
    # parapet rim, two roof vents, a vent stack and a floodlight mast.
    add_box("Marine_Roof", (0.0, 0.0, 1.06), 0.0, (0.80, 0.65, 0.04), darkgreen)
    add_box("Marine_Parapet_F", (0.0, -0.63, 1.13), 0.0, (0.80, 0.02, 0.03), dark)
    add_box("Marine_Parapet_B", (0.0, 0.63, 1.13), 0.0, (0.80, 0.02, 0.03), dark)
    add_box("Marine_Parapet_L", (-0.78, 0.0, 1.13), 0.0, (0.02, 0.61, 0.03), dark)
    add_box("Marine_Parapet_R", (0.78, 0.0, 1.13), 0.0, (0.02, 0.61, 0.03), dark)
    add_box("Marine_RoofVent_01", (-0.40, 0.30, 1.13), 0.0, (0.09, 0.09, 0.03), dark)
    add_box("Marine_RoofVent_02", (0.35, -0.30, 1.13), 0.0, (0.09, 0.09, 0.03), dark)
    add_cylinder("Marine_VentStack", 8, 0.05, 0.16, 1.18, gunmetal, x=0.40, y=0.35)
    add_cylinder("Marine_VentCap", 8, 0.06, 0.03, 1.275, dark, x=0.40, y=0.35)
    # Floodlight mast on the roof (pole + glowing head, faces -Y).
    add_cylinder("Marine_FloodMast", 6, 0.015, 0.22, 1.21, gunmetal, x=-0.55, y=0.35)
    add_box("Marine_FloodHead", (-0.55, 0.32, 1.33), 0.0, (0.035, 0.025, 0.02), glow)
    # Thin antenna on the roof (v1 position, back-left), glowing tip.
    add_cylinder("Marine_Antenna", 8, 0.025, 0.32, 1.26, dark, x=-0.55, y=-0.35)
    add_sphere("Marine_AntennaTip", 8, 6, 0.035, (-0.55, -0.35, 1.42), 1.0, glow)
    # Flag pole + larger orange flag on the roof (+X side, v1 position).
    add_cylinder("Marine_FlagPole", 8, 0.02, 0.35, 1.275, dark, x=0.55, y=0.35)
    add_box("Marine_Flag", (0.68, 0.35, 1.38), 0.0, (0.12, 0.01, 0.075), orange)

    # ---- Armory wing (annex, +X side, v1 position, two storeys now):
    # lower x 0.35-0.95, y -0.30-0.50, z 0.12-0.67; upper x 0.40-0.90,
    # y -0.25-0.45, z 0.67-1.02; flat dark roof; warm window slits on
    # its -Y and +X faces; roof vent + exhaust stack.
    add_box("Marine_Annex", (0.65, 0.10, 0.395), 0.0, (0.30, 0.40, 0.275), green)
    add_box("Marine_AnnexTop", (0.65, 0.10, 0.845), 0.0, (0.25, 0.35, 0.175), green)
    add_box("Marine_AnnexRoof", (0.65, 0.10, 1.03), 0.0, (0.27, 0.37, 0.02), dark)
    add_box("Marine_AnnexWindow_01", (0.65, -0.315, 0.45), 0.0, (0.18, 0.01, 0.06), glow)
    add_box("Marine_AnnexWindow_02", (0.96, 0.15, 0.45), 0.0, (0.01, 0.18, 0.06), glow)
    add_box("Marine_AnnexVent", (0.50, 0.00, 1.07), 0.0, (0.06, 0.06, 0.03), dark)
    add_cylinder("Marine_ExhaustStack", 8, 0.04, 0.14, 1.11, gunmetal, x=0.80, y=0.30)
    add_cylinder("Marine_ExhaustCap", 8, 0.05, 0.03, 1.195, dark, x=0.80, y=0.30)
    # Vertical pipe on the annex +X face.
    add_cylinder("Marine_AnnexPipe", 8, 0.02, 0.50, 0.37,
                 gunmetal, x=0.965, y=0.45)

    # ---- Recruitment gate on the camera-facing (-Y) face: pale frame,
    # dark door, step, glowing sign above.
    add_box("Marine_DoorFrame", (0.10, -0.615, 0.42), 0.0, (0.23, 0.02, 0.33), light)
    add_box("Marine_Door", (0.10, -0.63, 0.42), 0.0, (0.18, 0.025, 0.29), dark)
    add_box("Marine_DoorStep", (0.10, -0.68, 0.135), 0.0, (0.23, 0.06, 0.015), gunmetal)
    add_box("Marine_DoorSign", (0.10, -0.64, 0.82), 0.0, (0.12, 0.01, 0.025), glow)
    # Warm glowing windows on the -Y face (v1 positions, now glowing).
    add_box("Marine_WindowLeft", (-0.45, -0.62, 0.75), 0.0, (0.10, 0.02, 0.10), glow)
    add_box("Marine_WindowRight", (0.55, -0.62, 0.75), 0.0, (0.10, 0.02, 0.10), glow)
    # Unit banner on the -Y face (dark cloth, orange chevron insignia).
    add_box("Marine_Banner", (-0.45, -0.625, 0.50), 0.0, (0.10, 0.01, 0.25), dark)
    add_box("Marine_Chevron_L", (-0.485, -0.64, 0.56), 0.7, (0.045, 0.008, 0.012), orange)
    add_box("Marine_Chevron_R", (-0.415, -0.64, 0.56), -0.7, (0.045, 0.008, 0.012), orange)

    # ---- Watchtower at the back-right (+X/+Y) corner: 4 legs, cross
    # braces, cylindrical cab with warm window slits, cone roof, and a
    # floodlight on top (top z 1.455, inside the 1.47 envelope). It
    # reads above the roof without occluding the front.
    for (lx, ly) in ((0.54, 0.54), (0.86, 0.54), (0.54, 0.86), (0.86, 0.86)):
        add_cylinder("Marine_TowerLeg_%+d%+d" % (int(lx * 100), int(ly * 100)),
                     8, 0.018, 0.83, 0.535, gunmetal, x=lx, y=ly)
    add_box("Marine_TowerBrace_01", (0.70, 0.54, 0.55), 0.0, (0.18, 0.01, 0.01), gunmetal)
    add_box("Marine_TowerBrace_02", (0.70, 0.86, 0.55), 0.0, (0.18, 0.01, 0.01), gunmetal)
    add_cylinder("Marine_WatchCab", 12, 0.20, 0.30, 1.10, green, x=0.70, y=0.70)
    for i, a_deg in enumerate((315, 45, 225)):
        a = math.radians(a_deg)
        add_box("Marine_CabSlit_%02d" % (i + 1),
                (0.70 + 0.205 * math.cos(a), 0.70 + 0.205 * math.sin(a), 1.10),
                a, (0.05, 0.008, 0.05), glow)
    add_cone("Marine_WatchRoof", 12, 0.24, 0.05, 0.10, 1.30, dark, x=0.70, y=0.70)
    add_cylinder("Marine_TowerLight", 6, 0.012, 0.08, 1.39, gunmetal, x=0.70, y=0.70)
    add_box("Marine_TowerLightHead", (0.70, 0.66, 1.44), 0.0, (0.025, 0.02, 0.015), glow)

    # ---- Training yard: barrier wall + sandbags on the front-left,
    # sandbag row along the back edge, ammo crates, training post.
    add_box("Marine_Barrier", (-0.55, -0.88, 0.21), 0.0, (0.30, 0.05, 0.09), dark)
    for i, x in enumerate((-0.75, -0.55, -0.35)):
        add_sphere("Marine_Sandbag_F%02d" % (i + 1), 8, 4, 0.06,
                   (x, -0.88, 0.33), 0.6, darkgreen)
    for i, x in enumerate((-0.60, -0.20, 0.20)):
        add_sphere("Marine_Sandbag_B%02d" % (i + 1), 8, 4, 0.07,
                   (x, 0.88, 0.31), 0.6, darkgreen)
    # Ammo crates: a stack at the back-left corner, one at front-right.
    add_box("Marine_Crate_01", (-0.80, 0.75, 0.18), 0.4, (0.09, 0.09, 0.06), gunmetal)
    add_box("Marine_Crate_02", (-0.80, 0.75, 0.30), -0.3, (0.06, 0.06, 0.06), dark)
    add_box("Marine_Crate_03", (0.80, -0.60, 0.165), 0.5, (0.08, 0.08, 0.05), dark)
    # Training post (pole + crossbar) beside the block on the -X side.
    add_cylinder("Marine_TrainPost", 8, 0.025, 0.43, 0.335, gunmetal, x=-0.88, y=0.0)
    add_box("Marine_TrainBar", (-0.88, 0.0, 0.50), 0.0, (0.015, 0.125, 0.015), dark)

    # ---- Corner floodlight pole (back-left) + bollard lights.
    add_cylinder("Marine_CornerLight", 8, 0.02, 1.18, 0.71, gunmetal, x=-0.92, y=0.92)
    add_box("Marine_CornerLightHead", (-0.92, 0.90, 1.31), 0.0, (0.03, 0.025, 0.02), glow)
    for i, (x, y) in enumerate(((-0.92, -0.92), (0.92, -0.92))):
        add_cone("Marine_Bollard_%02d" % (i + 1), 6, 0.03, 0.012, 0.09, 0.165,
                 glow, x=x, y=y)

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
    panel grid (48 px cells, 4 px lines), rivets at the cell corners,
    per-cell brightness variation, and soft grime blotches. Returns the
    Blender image, saved as a PNG next to the script. Turned-up
    settings (same as the Nexus textures after the faint first pass)."""
    S = ATLAS_SIZE
    CELL = 48
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    # Panel grid: lines where the within-cell position is near a border.
    cx = np.mod(xx, CELL)
    cy = np.mod(yy, CELL)
    line = ((cx < 4) | (cy < 4)).astype(np.float32)
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
    of (material name, filepath, bytes). Four textures here, not three:
    the green barracks block is the Foundry's hero surface, so it gets
    its own panel texture instead of staying flat."""
    seeds = {"MarineTitanium": 131, "MarineGunmetal": 137,
             "MarineDarkAlloy": 149, "MarineGreen": 151}
    base_colors = {"MarineTitanium": TITANIUM, "MarineGunmetal": GUNMETAL,
                   "MarineDarkAlloy": DARK_ALLOY, "MarineGreen": MARINE_GREEN}
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
    print("Marine Foundry v2 build:")
    for obj in objects:
        if obj.type != "MESH":
            continue
        mesh = obj.data
        mesh.calc_loop_triangles()
        total_tris += len(mesh.loop_triangles)
        print("  %-22s tris=%4d  materials=%s"
              % (obj.name, len(mesh.loop_triangles),
                 [m.name if m else "?" for m in mesh.materials]))
    print("  objects=%d  total_tris=%d" % (len(objects), total_tris))
    # Bounding box check: footprint must stay 2.0 x 2.0, height <= 1.47.
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
                  if m.name in ("MarineTitanium", "MarineGunmetal",
                                "MarineDarkAlloy", "MarineGreen")]
    make_uv_atlas(built)
    TEXTURE_INFO = apply_metal_textures(metal_mats)
    export(built)
    report(built)
