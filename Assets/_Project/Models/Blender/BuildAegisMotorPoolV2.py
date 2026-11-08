# BuildAegisMotorPoolV2.py — Lunar Dominion: Aegis Motor Pool v2
# (detailed geometry + baked panel textures).
#
# DRAFT — not yet in the Unity project and NOT pushed to GitHub.
# Rebuilds the Aegis Motor Pool at the Nexus v4 standard: same
# footprint, height and position as the v1 Motor Pool
# (BuildAegisMotorPool.py, 304 tris — the mechanized-vehicle
# barracks, home of Scout Rovers, Assault Mechs and Titan Walkers),
# but with real detail — a grey vehicle garage with pilasters, a
# parapet roof, a gantry crane, a roller door with slats and a
# glowing strip, warm windows, a detailed orange rover half-out
# in front (headlights, windshield, hub caps, antenna, roof rack),
# fuel drums and a service pipe on the +X face, a control module,
# a tool cabinet, spare-parts crates, tire tracks on the apron and
# glowing bollards. Textures use the proven Nexus pipeline:
# pixel-generated with numpy (turned-up settings: 48 px cells,
# 4 px lines, 0.75 line darkening, +/-10% per-cell variation,
# stronger grime), Lightmap Pack UV atlas. No Cycles bake — it
# proved unusable headless (a flat-colour bake takes 5+ minutes
# on this VM).
#
# DROP-IN CONTRACT (from v1 — do not break it): base platform
# 2.4 x 2.0 (x +/-1.20, y +/-1.00), main grey garage block
# 2.0 x 1.7 x 1.0 at (0, 0, 0.62) with a flat roof slab
# 2.1 x 1.8 x 0.10 at z 1.12-1.22, a wide garage doorway (pale
# frame, dark door) with a glowing strip above it on the
# camera-facing (-Y) face, an orange rover parked half-out in
# front of the doorway at (0.55, -1.10), a roof vent and an
# antenna at (0.75, -0.55) with the top at z 1.67 (the high
# point), and a fuel drum plus a crate on the +X face. v2 keeps
# the same outer envelope: x within +/-1.21 (the v1 drum's outer
# edge), y -1.55..1.00 (tire tracks on the apron in front, as
# ground markings), z 0-1.67. Grey identity kept: grey block,
# dark base/roof, gunmetal trim, orange rover/drum accent and
# orange glow; the frame is spec-palette titanium/gunmetal/dark
# alloy.
#
# CAMERA NOTE (from v1): the game camera sits at Unity +X/+Z, so
# Blender -Y and +X faces are the visible ones. Details (doorway,
# roller-door slats, strip light, windows, rover, tire tracks on
# -Y; drums, service pipe, control module, crates, tool cabinet
# on +X) go on -Y / +X faces. The Motor Pool sits on the far side
# of the base from the camera, so nothing important goes on the
# +Y (back) face.
#
# EMISSION (lesson from the Hydroponics dome): pale tints read
# washed-out white in Unity. The glowing parts here use the v1's
# saturated orange (1.00, 0.55, 0.15) at 2.5 for the strip light,
# windows, rover headlights, antenna tips, stack tip ring,
# control screen and bollards — not pale tints. There is no
# glass on the Motor Pool, so no transparency import caveat.
#
# The grey block is the hero surface and gets its own panel
# texture; the orange rover body and fuel drums get their own
# orange panel texture too (the fifth PNG) — the rover is the
# building's signature detail, and an untextured orange would
# read flat next to the panelled grey.
#
# Emission stays procedural (emissive colour inputs, not baked),
# so the glowing parts keep glowing.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildAegisMotorPoolV2.py
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
# exported FBX to AegisMotorPool.fbx and move the PNGs next to it in
# Assets/_Project/Resources/Models/.
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "AegisMotorPoolV2.fbx")
ATLAS_SIZE = 1024
TEXTURE_NAMES = {
    "AegisTitanium": "AegisMotorPoolV2_Titanium.png",
    "AegisGunmetal": "AegisMotorPoolV2_Gunmetal.png",
    "AegisDarkAlloy": "AegisMotorPoolV2_DarkAlloy.png",
    "AegisGrey": "AegisMotorPoolV2_Grey.png",
    "AegisOrange": "AegisMotorPoolV2_Orange.png",
}

# Palette: v1 Motor Pool colours + spec section 4 metals (linear RGB).
TITANIUM = (0.78, 0.82, 0.85)      # #C6D0DA primary armor
GUNMETAL = (0.27, 0.33, 0.39)      # #465363
DARK_ALLOY = (0.09, 0.15, 0.22)    # #172638 recesses
AEGIS_GREY = (0.55, 0.55, 0.60)    # v1 main garage block grey
AEGIS_ORANGE = (0.85, 0.45, 0.15)  # v1 rover body / fuel drum orange
AEGIS_LIGHT = (0.90, 0.88, 0.80)   # v1 pale door frame
AEGIS_GLOW = (1.00, 0.55, 0.15)    # v1 orange glow (saturated)

# Layout constants (Blender units; 1 unit = 1 Unity unit).
MAX_Z = 1.67  # v1 antenna tip; nothing in v2 goes above this.


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


def add_sphere(name, segments, rings, radius, location, z_scale, material,
               rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=radius, location=location
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (1.0, 1.0, z_scale)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.rotation_euler = rotation
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

    titanium = make_material("AegisTitanium", TITANIUM, metallic=0.8, roughness=0.30)
    gunmetal = make_material("AegisGunmetal", GUNMETAL, metallic=0.9, roughness=0.40)
    dark = make_material("AegisDarkAlloy", DARK_ALLOY, metallic=0.85, roughness=0.35)
    grey = make_material("AegisGrey", AEGIS_GREY, metallic=0.55, roughness=0.45)
    orange = make_material("AegisOrange", AEGIS_ORANGE, metallic=0.20, roughness=0.50)
    light = make_material("AegisLight", AEGIS_LIGHT, metallic=0.10, roughness=0.45)
    glow = make_material("AegisGlow", AEGIS_GLOW, metallic=0.10, roughness=0.30,
                         emission=2.5)

    # ---- Foundation: dark plinth 2.4 x 2.0 z 0-0.06, titanium tier
    # 2.3 x 1.9 z 0.06-0.12 (v1 base platform is 2.4 x 2.0 x 0.12).
    add_box("Aegis_Plinth", (0.0, 0.0, 0.03), 0.0, (1.20, 1.00, 0.03), dark)
    add_box("Aegis_Tier", (0.0, 0.0, 0.09), 0.0, (1.15, 0.95, 0.03), titanium)
    # Rectangular seam frame between plinth and tier, proud of the
    # tier faces by 0.02.
    add_box("Aegis_Seam_01", (1.16, 0.0, 0.06), 0.0, (0.01, 0.97, 0.01), dark)
    add_box("Aegis_Seam_02", (-1.16, 0.0, 0.06), 0.0, (0.01, 0.97, 0.01), dark)
    add_box("Aegis_Seam_03", (0.0, 0.96, 0.06), 0.0, (1.17, 0.01, 0.01), dark)
    add_box("Aegis_Seam_04", (0.0, -0.96, 0.06), 0.0, (1.17, 0.01, 0.01), dark)
    # Vent grilles on the tier faces (camera-facing -Y, and +X).
    add_box("Aegis_VentGrille_01", (0.30, -0.955, 0.09), 0.0, (0.15, 0.01, 0.02), dark)
    add_box("Aegis_VentGrille_02", (1.155, 0.30, 0.09), 0.0, (0.01, 0.15, 0.02), dark)
    # Tire tracks on the apron in front of the doorway, under the
    # rover's wheels (thin dark strips on the bare ground, z 0-0.012).
    add_box("Aegis_Track_01", (0.28, -1.20, 0.006), 0.0, (0.07, 0.30, 0.006), dark)
    add_box("Aegis_Track_02", (0.82, -1.20, 0.006), 0.0, (0.07, 0.30, 0.006), dark)

    # ---- Main garage block: grey, 2.0 x 1.7 x 1.0 at (0, 0, 0.62),
    # top z 1.12 (as v1).
    add_box("Aegis_Block", (0.0, 0.0, 0.62), 0.0, (1.00, 0.85, 0.50), grey)
    # 4 corner pilasters, proud of the block faces.
    for i, (x, y) in enumerate(((-0.97, -0.82), (-0.97, 0.82),
                                (0.97, -0.82), (0.97, 0.82))):
        add_box("Aegis_Pilaster_%02d" % (i + 1), (x, y, 0.62), 0.0,
                (0.04, 0.04, 0.50), gunmetal)
    # Segmentation band around the block.
    add_box("Aegis_Band", (0.0, 0.0, 0.85), 0.0, (1.01, 0.86, 0.03), dark)
    # Vertical service pipes on the +X face, with band rings.
    for i, y in enumerate((-0.40, 0.40)):
        add_cylinder("Aegis_VPipe_%02d" % (i + 1), 12, 0.05, 1.00, 0.62,
                     gunmetal, x=1.02, y=y)
        add_torus("Aegis_VPipeBand_%02d" % (i + 1), 0.05, 0.012, 12, 4, 0.62,
                  dark, x=1.02, y=y)

    # ---- Flat roof slab (as v1): dark, 2.1 x 1.8 x 0.10 at
    # z 1.12-1.22, plus a gunmetal parapet rim on top.
    add_box("Aegis_Roof", (0.0, 0.0, 1.17), 0.0, (1.05, 0.90, 0.05), dark)
    add_box("Aegis_Parapet_01", (0.0, -0.88, 1.245), 0.0, (1.06, 0.03, 0.025), gunmetal)
    add_box("Aegis_Parapet_02", (0.0, 0.88, 1.245), 0.0, (1.06, 0.03, 0.025), gunmetal)
    add_box("Aegis_Parapet_03", (-1.04, 0.0, 1.245), 0.0, (0.03, 0.88, 0.025), gunmetal)
    add_box("Aegis_Parapet_04", (1.04, 0.0, 1.245), 0.0, (0.03, 0.88, 0.025), gunmetal)
    # Roof vent (as v1, back-left) plus a second vent (front-left).
    add_box("Aegis_Vent", (-0.70, 0.40, 1.31), 0.0, (0.15, 0.15, 0.09), dark)
    add_box("Aegis_Vent2", (-0.75, -0.45, 1.31), 0.0, (0.12, 0.12, 0.09), dark)
    # Exhaust stack at the back of the roof, with a glowing tip ring.
    add_cylinder("Aegis_Stack", 12, 0.09, 0.28, 1.36, gunmetal, x=-0.35, y=0.55)
    add_torus("Aegis_StackTip", 0.09, 0.015, 12, 4, 1.48, glow, x=-0.35, y=0.55)
    # Antenna (as v1): thin cylinder at (0.75, -0.55), z 1.22-1.64,
    # with a glowing tip cone whose apex reaches z 1.67 (the v1
    # high point — nothing goes above it).
    add_cylinder("Aegis_Antenna", 8, 0.04, 0.42, 1.43, dark, x=0.75, y=-0.55)
    add_cone("Aegis_AntennaTip", 4, 0.04, 0.0, 0.06, 1.64, glow,
             x=0.75, y=-0.55)

    # ---- Gantry crane over the back of the roof: two legs, a
    # crossbeam, a hoist block, a cable and a hook. Top of the beam
    # is z 1.56, below the antenna tip.
    add_box("Aegis_CraneLeg_01", (-0.95, 0.70, 1.36), 0.0, (0.04, 0.04, 0.14), gunmetal)
    add_box("Aegis_CraneLeg_02", (0.95, 0.70, 1.36), 0.0, (0.04, 0.04, 0.14), gunmetal)
    add_box("Aegis_CraneBeam", (0.0, 0.70, 1.52), 0.0, (0.99, 0.04, 0.04), gunmetal)
    add_box("Aegis_CraneHoist", (0.35, 0.70, 1.44), 0.0, (0.06, 0.06, 0.04), dark)
    add_cylinder("Aegis_CraneCable", 6, 0.01, 0.14, 1.37, dark, x=0.35, y=0.70)
    add_torus("Aegis_CraneHook", 0.04, 0.012, 8, 4, 1.28, dark, x=0.35, y=0.70)

    # ---- Camera-facing (-Y) face: pale door frame and dark roller
    # door (as v1), 5 horizontal roller slats, the glowing strip
    # above the door (as v1), and two warm windows with dark frames
    # flanking the door.
    add_box("Aegis_DoorFrame", (0.0, -0.865, 0.555), 0.0, (0.58, 0.025, 0.435), light)
    add_box("Aegis_Door", (0.0, -0.88, 0.495), 0.0, (0.50, 0.02, 0.375), dark)
    for i, z in enumerate((0.20, 0.32, 0.44, 0.56, 0.68)):
        add_box("Aegis_DoorSlat_%02d" % (i + 1), (0.0, -0.905, z), 0.0,
                (0.50, 0.01, 0.015), gunmetal)
    add_box("Aegis_LightStrip", (0.0, -0.87, 1.02), 0.0, (0.25, 0.025, 0.04), glow)
    for i, x in enumerate((-0.85, 0.85)):
        add_box("Aegis_WindowFrame_%02d" % (i + 1), (x, -0.86, 0.60), 0.0,
                (0.09, 0.01, 0.13), dark)
        add_box("Aegis_Window_%02d" % (i + 1), (x, -0.87, 0.60), 0.0,
                (0.07, 0.01, 0.11), glow)

    # ---- +X face: two orange fuel drums with band rings (one as
    # v1), a horizontal service pipe along the face, a control
    # module with a glowing screen, and spare-parts crates.
    add_cylinder("Aegis_Drum", 16, 0.16, 0.45, 0.345, orange, x=1.05, y=0.30)
    add_torus("Aegis_DrumBand", 0.16, 0.012, 16, 4, 0.345, dark, x=1.05, y=0.30)
    add_cylinder("Aegis_Drum2", 16, 0.16, 0.45, 0.345, orange, x=1.05, y=0.65)
    add_torus("Aegis_Drum2Band", 0.16, 0.012, 16, 4, 0.345, dark, x=1.05, y=0.65)
    # Horizontal service pipe along the +X face (axis Y), with bands.
    add_cylinder("Aegis_Pipe", 12, 0.04, 1.20, 0.95, gunmetal, x=1.02, y=0.0,
                 rotation=(math.radians(90), 0.0, 0.0))
    add_torus("Aegis_PipeBand_01", 0.04, 0.01, 12, 4, 0.95, dark,
              rotation=(math.radians(90), 0.0, 0.0), x=1.02, y=-0.30)
    add_torus("Aegis_PipeBand_02", 0.04, 0.01, 12, 4, 0.95, dark,
              rotation=(math.radians(90), 0.0, 0.0), x=1.02, y=0.30)
    # Control module with a glowing screen, between the crate and
    # the drums.
    add_box("Aegis_Control", (1.05, -0.15, 0.45), 0.0, (0.05, 0.15, 0.25), titanium)
    add_box("Aegis_ControlScreen", (1.11, -0.15, 0.50), 0.0, (0.008, 0.10, 0.08), glow)
    # Crate (as v1) plus a second crate and one stacked on top.
    add_box("Aegis_Crate", (1.02, -0.45, 0.27), 0.0, (0.15, 0.15, 0.15), gunmetal)
    add_box("Aegis_Crate2", (1.05, -0.78, 0.24), 0.0, (0.12, 0.12, 0.12), dark)
    add_box("Aegis_Crate3", (1.05, -0.78, 0.45), 0.0, (0.09, 0.09, 0.09), gunmetal)
    # Tool cabinet on the platform front-left corner, with drawer lines.
    add_box("Aegis_ToolCabinet", (-1.00, -0.85, 0.30), 0.0, (0.15, 0.12, 0.18), gunmetal)
    add_box("Aegis_ToolDrawer_01", (-1.00, -0.975, 0.24), 0.0, (0.12, 0.008, 0.01), dark)
    add_box("Aegis_ToolDrawer_02", (-1.00, -0.975, 0.38), 0.0, (0.12, 0.008, 0.01), dark)

    # ---- Orange rover parked half-out in front of the doorway
    # (as v1): body at (0.55, -1.10), cabin, windshield, 4 wheels
    # with hub caps, headlights, antenna with a glowing tip, and a
    # roof rack with a crate.
    add_box("Aegis_RoverBody", (0.55, -1.10, 0.26), 0.0, (0.35, 0.25, 0.14), orange)
    add_box("Aegis_RoverCabin", (0.45, -1.10, 0.49), 0.0, (0.18, 0.17, 0.09), gunmetal)
    add_box("Aegis_RoverWindshield", (0.45, -1.275, 0.50), 0.0, (0.15, 0.01, 0.06), dark)
    for i, (wx, wy) in enumerate(
            [(0.28, -0.92), (0.82, -0.92), (0.28, -1.28), (0.82, -1.28)]):
        add_cylinder("Aegis_Wheel%02d" % (i + 1), 8, 0.11, 0.08, 0.11, gunmetal,
                     x=wx, y=wy, rotation=(0.0, math.radians(90), 0.0))
        add_cylinder("Aegis_Hub%02d" % (i + 1), 8, 0.05, 0.10, 0.11, titanium,
                     x=wx, y=wy, rotation=(0.0, math.radians(90), 0.0))
    add_box("Aegis_Headlight_01", (0.30, -1.36, 0.30), 0.0, (0.04, 0.01, 0.03), glow)
    add_box("Aegis_Headlight_02", (0.80, -1.36, 0.30), 0.0, (0.04, 0.01, 0.03), glow)
    add_cylinder("Aegis_RoverAntenna", 6, 0.015, 0.22, 0.69, dark, x=0.30, y=-1.10)
    add_sphere("Aegis_RoverAntennaTip", 6, 4, 0.025, (0.30, -1.10, 0.81), 1.0, glow)
    add_box("Aegis_RoverRack", (0.45, -1.10, 0.60), 0.0, (0.15, 0.14, 0.02), dark)
    add_box("Aegis_RoverRackCrate", (0.45, -1.10, 0.68), 0.0, (0.08, 0.08, 0.06), gunmetal)

    # ---- Glowing bollards on the platform back corners.
    add_cone("Aegis_Bollard_01", 6, 0.03, 0.012, 0.09, 0.225, glow,
             x=-1.05, y=0.92)
    add_cone("Aegis_Bollard_02", 6, 0.03, 0.012, 0.09, 0.225, glow,
             x=1.05, y=0.92)

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
    of (material name, filepath, bytes). Five textures here, not three:
    the grey block is the Motor Pool's hero surface, so it gets its own
    panel texture instead of staying flat, and the orange rover body
    and fuel drums get their own orange panel texture too — the rover
    is the building's signature detail."""
    seeds = {"AegisTitanium": 241, "AegisGunmetal": 247,
             "AegisDarkAlloy": 251, "AegisGrey": 257, "AegisOrange": 263}
    base_colors = {"AegisTitanium": TITANIUM, "AegisGunmetal": GUNMETAL,
                   "AegisDarkAlloy": DARK_ALLOY, "AegisGrey": AEGIS_GREY,
                   "AegisOrange": AEGIS_ORANGE}
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
    print("Aegis Motor Pool v2 build:")
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
    # Bounding box check: footprint must stay within the v1 envelope
    # (x +/-1.21 at the drum's outer edge, y -1.55..1.00 with the
    # tire tracks on the apron, height <= 1.67 at the antenna tip).
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
                  if m.name in ("AegisTitanium", "AegisGunmetal",
                                "AegisDarkAlloy", "AegisGrey", "AegisOrange")]
    make_uv_atlas(built)
    TEXTURE_INFO = apply_metal_textures(metal_mats)
    export(built)
    report(built)
