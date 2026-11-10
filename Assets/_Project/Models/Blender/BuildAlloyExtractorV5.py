# BuildAlloyExtractorV5.py — Lunar Dominion: Alloy Extractor v5
# (graphics upgrade: v2 + surface detail + finer textures).
#
# v5 = v2 (detailed geometry + baked panel textures) + a new section
# of greebles (section "v5 greebles" in build()): 2 more vent grilles
# on the foundation tier, 2 junction boxes on the block faces, 2 more
# vertical pipes, a pressure gauge on the main silo, a glowing window
# on the block +X face, a roof antenna + glowing tip, 2 rust hazard
# bars on the plinth, a railing ring around the silo top, a roof vent,
# and 2 more ore lumps. The v2 geometry is untouched, so the drop-in
# contract is unchanged: base platform 1.8 x 1.8, footprint x/y within
# +/-0.90, z 0-1.60 (tallest point the exhaust stack). Textures are
# finer: 32 px panel cells + a faint 16 px sub-grid (was 48 px).
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildAlloyExtractorV5.py
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
# exported FBX to AlloyExtractor.fbx and move the PNGs next to it in
# Assets/_Project/Resources/Models/.
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "AlloyExtractorV5.fbx")
ATLAS_SIZE = 1024
TEXTURE_NAMES = {
    "AlloyTitanium": "AlloyExtractorV5_Titanium.png",
    "AlloyGunmetal": "AlloyExtractorV5_Gunmetal.png",
    "AlloyDarkAlloy": "AlloyExtractorV5_DarkAlloy.png",
    "AlloyGrey": "AlloyExtractorV5_Grey.png",
}

# Palette: v1 Extractor colours + spec section 4 metals (linear RGB).
TITANIUM = (0.78, 0.82, 0.85)      # #C6D0DA primary armor
GUNMETAL = (0.27, 0.33, 0.39)      # #465363
DARK_ALLOY = (0.09, 0.15, 0.22)    # #172638 recesses
ALLOY_GREY = (0.65, 0.68, 0.72)    # v1 block / silo grey
ALLOY_RUST = (0.85, 0.45, 0.15)    # v1 rust-orange accents
ALLOY_ORE = (0.35, 0.22, 0.12)     # dark ore piles

# Layout constants (Blender units; 1 unit = 1 Unity unit).
BASE_HALF = 0.90
MAX_Z = 1.60  # v1 stack top; nothing in v2 goes above this.


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


def add_ore(name, location, radius, material):
    """Low-poly ore lump (icosahedron, squashed for a rock look)."""
    bpy.ops.mesh.primitive_ico_sphere_add(
        subdivisions=0, radius=radius, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (1.0, 0.8, 0.7)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
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

    titanium = make_material("AlloyTitanium", TITANIUM, metallic=0.8, roughness=0.30)
    gunmetal = make_material("AlloyGunmetal", GUNMETAL, metallic=0.9, roughness=0.40)
    dark = make_material("AlloyDarkAlloy", DARK_ALLOY, metallic=0.85, roughness=0.35)
    grey = make_material("AlloyGrey", ALLOY_GREY, metallic=0.55, roughness=0.45)
    rust = make_material("AlloyRust", ALLOY_RUST, metallic=0.30, roughness=0.60,
                         emission=0.4)
    ore = make_material("AlloyOre", ALLOY_ORE, metallic=0.10, roughness=0.90)
    glow = make_material("AlloyGlow", ALLOY_RUST, metallic=0.10, roughness=0.30,
                         emission=2.5)

    # ---- Foundation: dark plinth 1.8 x 1.8 z 0-0.06, titanium tier
    # 1.7 x 1.7 z 0.06-0.18 (v1 envelope: block/silo sit on z 0.18).
    add_box("Alloy_Plinth", (0.0, 0.0, 0.03), 0.0, (0.90, 0.90, 0.03), dark)
    add_box("Alloy_Tier", (0.0, 0.0, 0.12), 0.0, (0.85, 0.85, 0.06), titanium)
    # Recessed seam ring between plinth and tier.
    add_torus("Alloy_Seam", 0.86, 0.012, 16, 3, 0.06, dark)
    # Vent grilles on the tier faces (camera-facing -Y, and +X).
    add_box("Alloy_VentGrille_01", (0.30, -0.855, 0.11), 0.0, (0.15, 0.01, 0.025), dark)
    add_box("Alloy_VentGrille_02", (0.855, 0.30, 0.11), 0.0, (0.01, 0.15, 0.025), dark)

    # ---- Main refinery block: grey, 1.1 x 0.9 x 0.85 at (-0.25, 0,
    # 0.605), top z 1.03 (as v1).
    add_box("Alloy_Block", (-0.25, 0.0, 0.605), 0.0, (0.55, 0.45, 0.425), grey)
    # 4 corner pilasters, proud of the block faces.
    for i, (x, y) in enumerate(((-0.78, -0.43), (-0.78, 0.43),
                                (0.28, -0.43), (0.28, 0.43))):
        add_box("Alloy_Pilaster_%02d" % (i + 1), (x, y, 0.605), 0.0,
                (0.03, 0.03, 0.425), gunmetal)
    # Segmentation band around the block.
    add_box("Alloy_Band", (-0.25, 0.0, 0.85), 0.0, (0.56, 0.46, 0.03), dark)
    # Vent grille on the front (-Y) face (as v1) with a glowing hot slit.
    add_box("Alloy_Vent", (-0.25, -0.46, 0.72), 0.0, (0.17, 0.02, 0.11), rust)
    add_box("Alloy_VentGlow", (-0.25, -0.48, 0.72), 0.0, (0.13, 0.01, 0.03), glow)
    # Two warm glowing windows on the front face, with dark frames.
    for i, x in enumerate((-0.60, 0.10)):
        add_box("Alloy_WindowFrame_%02d" % (i + 1), (x, -0.455, 0.55), 0.0,
                (0.10, 0.008, 0.08), dark)
        add_box("Alloy_Window_%02d" % (i + 1), (x, -0.46, 0.55), 0.0,
                (0.08, 0.01, 0.06), glow)
    # Vertical pipe on the +X face of the block.
    add_cylinder("Alloy_VPipe", 12, 0.06, 0.80, 0.60, gunmetal, x=0.33, y=0.0)
    # Rust vent on the +X face, clear of the vertical pipe.
    add_box("Alloy_SideVent", (0.31, 0.25, 0.85), 0.0, (0.01, 0.10, 0.07), rust)
    # Pyramid roof (as v1): 4-sided cone, square aligned by rotating 45
    # degrees about Z. Base at z 1.03, apex at ~1.31. Gunmetal, not
    # dark alloy: the first preview showed a dark-alloy roof reading
    # as a near-black slab; gunmetal keeps the v1's mid-dark grey roof
    # and gives it panel texture.
    add_cone("Alloy_Roof", 4, 0.78, 0.0, 0.28, 1.17, gunmetal,
             x=-0.25, y=0.0, rotation=(0.0, 0.0, math.radians(45)))
    # Small cap disc on the roof apex.
    add_cylinder("Alloy_RoofCap", 12, 0.08, 0.04, 1.33, gunmetal, x=-0.25, y=0.0)
    # Rust sign plate above the vent, below the roof.
    add_box("Alloy_Sign", (-0.25, -0.46, 0.95), 0.0, (0.15, 0.01, 0.05), rust)

    # ---- Main silo: cylindrical metal tank r 0.32, 1.0 tall at
    # (0.55, 0.15, 0.68), top z 1.18 (as v1), with a dark cap.
    add_cylinder("Alloy_Silo", 16, 0.32, 1.00, 0.68, grey, x=0.55, y=0.15)
    # 8 vertical ribs around the silo (gunmetal, proud of the wall).
    for i in range(8):
        a = math.radians(i * 45.0)
        add_box("Alloy_SiloRib_%02d" % (i + 1),
                (0.55 + 0.33 * math.cos(a), 0.15 + 0.33 * math.sin(a), 0.68),
                a, (0.02, 0.02, 0.45), gunmetal)
    # Two horizontal band rings around the silo.
    add_torus("Alloy_SiloBand_01", 0.33, 0.02, 16, 6, 0.50, gunmetal,
              x=0.55, y=0.15)
    add_torus("Alloy_SiloBand_02", 0.33, 0.02, 16, 6, 0.90, gunmetal,
              x=0.55, y=0.15)
    # Dark cap disc on top of the silo.
    add_cylinder("Alloy_SiloCap", 12, 0.34, 0.06, 1.21, dark, x=0.55, y=0.15)
    # Ladder on the camera-facing (-Y) side of the silo: 2 rails + 5 rungs.
    add_box("Alloy_LadderRail_01", (0.47, -0.19, 0.68), 0.0, (0.01, 0.01, 0.50), gunmetal)
    add_box("Alloy_LadderRail_02", (0.63, -0.19, 0.68), 0.0, (0.01, 0.01, 0.50), gunmetal)
    for i, z in enumerate((0.35, 0.55, 0.75, 0.95, 1.10)):
        add_box("Alloy_LadderRung_%02d" % (i + 1), (0.55, -0.19, z), 0.0,
                (0.09, 0.01, 0.01), gunmetal)

    # ---- Small storage silo at the front-right: r 0.20, z 0.18-0.78.
    add_cylinder("Alloy_Silo2", 12, 0.20, 0.60, 0.48, grey, x=0.62, y=-0.55)
    add_cylinder("Alloy_Silo2Cap", 12, 0.22, 0.05, 0.805, dark, x=0.62, y=-0.55)
    add_torus("Alloy_Silo2Band", 0.21, 0.015, 12, 4, 0.55, gunmetal,
              x=0.62, y=-0.55)

    # ---- Exhaust stacks at the back-left (as v1): tall thin stack at
    # (-0.55, 0.60), top z 1.60 (the tallest point), with a glowing tip
    # ring and a band; a second shorter stack at (-0.70, 0.72) with a
    # rust tip.
    add_cylinder("Alloy_Stack", 8, 0.09, 1.42, 0.89, dark, x=-0.55, y=0.60)
    add_cylinder("Alloy_StackTip", 8, 0.12, 0.08, 1.56, glow, x=-0.55, y=0.60)
    add_torus("Alloy_StackBand", 0.09, 0.015, 8, 4, 1.20, gunmetal,
              x=-0.55, y=0.60)
    add_cylinder("Alloy_Stack2", 8, 0.07, 1.00, 0.68, dark, x=-0.70, y=0.72)
    add_cylinder("Alloy_Stack2Tip", 8, 0.09, 0.06, 1.15, rust, x=-0.70, y=0.72)

    # ---- Smelting furnace at the back-right (0.62, 0.68): gunmetal
    # box with a glowing door slit on the +X face and a chimney with a
    # glowing tip ring.
    add_box("Alloy_Furnace", (0.62, 0.68, 0.43), 0.0, (0.20, 0.15, 0.25), gunmetal)
    add_box("Alloy_FurnaceDoor", (0.825, 0.68, 0.40), 0.0, (0.008, 0.06, 0.10), glow)
    add_cylinder("Alloy_FurnaceChimney", 12, 0.06, 0.27, 0.815, dark,
                 x=0.62, y=0.68)
    add_torus("Alloy_FurnaceTip", 0.06, 0.012, 12, 4, 0.95, glow,
              x=0.62, y=0.68)

    # ---- Crusher at the front-left: gunmetal base box with a square
    # feed hopper (wide at the top) and ore lumps inside.
    add_box("Alloy_CrusherBase", (-0.68, -0.68, 0.305), 0.0, (0.15, 0.15, 0.125), gunmetal)
    add_cone("Alloy_Hopper", 4, 0.12, 0.18, 0.28, 0.45, dark,
             x=-0.68, y=-0.68, rotation=(0.0, 0.0, math.radians(45)))
    add_ore("Alloy_HopperOre_01", (-0.70, -0.66, 0.58), 0.05, ore)
    add_ore("Alloy_HopperOre_02", (-0.64, -0.70, 0.57), 0.04, ore)

    # ---- Conveyor belt along the front, from the crusher toward the
    # block: flat belt with side rails, 2 support legs, ore lumps.
    add_box("Alloy_Belt", (-0.30, -0.62, 0.30), 0.0, (0.20, 0.08, 0.02), dark)
    add_box("Alloy_BeltRail_01", (-0.30, -0.70, 0.32), 0.0, (0.20, 0.01, 0.03), gunmetal)
    add_box("Alloy_BeltRail_02", (-0.30, -0.54, 0.32), 0.0, (0.20, 0.01, 0.03), gunmetal)
    add_box("Alloy_BeltLeg_01", (-0.45, -0.62, 0.24), 0.0, (0.02, 0.02, 0.06), gunmetal)
    add_box("Alloy_BeltLeg_02", (-0.15, -0.62, 0.24), 0.0, (0.02, 0.02, 0.06), gunmetal)
    add_ore("Alloy_BeltOre_01", (-0.40, -0.62, 0.35), 0.04, ore)
    add_ore("Alloy_BeltOre_02", (-0.20, -0.62, 0.35), 0.035, ore)

    # ---- Control module on the platform front-right: titanium box
    # with a glowing screen on the -Y face and an antenna.
    add_box("Alloy_Control", (0.15, -0.68, 0.38), 0.0, (0.15, 0.06, 0.20), titanium)
    add_box("Alloy_ControlScreen", (0.15, -0.745, 0.42), 0.0, (0.10, 0.008, 0.07), glow)
    add_cylinder("Alloy_ControlAntenna", 6, 0.012, 0.25, 0.68, gunmetal,
                 x=0.05, y=-0.68)
    add_sphere("Alloy_ControlTip", 6, 4, 0.02, (0.05, -0.68, 0.81), 1.0, glow)

    # ---- Crane at the back-centre: mast, jib over the furnace, a
    # short counter-jib, a hanging cable and hook. Top z 1.45, inside
    # the v1 1.60 envelope.
    add_box("Alloy_CraneBase", (0.10, 0.75, 0.23), 0.0, (0.06, 0.06, 0.05), dark)
    add_box("Alloy_CraneMast", (0.10, 0.75, 0.815), 0.0, (0.03, 0.03, 0.635), gunmetal)
    add_box("Alloy_CraneJib", (0.375, 0.75, 1.40), 0.0, (0.275, 0.03, 0.03), gunmetal)
    add_box("Alloy_CraneCounterJib", (0.0, 0.75, 1.40), 0.0, (0.075, 0.03, 0.03), gunmetal)
    add_cylinder("Alloy_CraneCable", 6, 0.01, 0.35, 1.22, dark, x=0.62, y=0.75)
    add_box("Alloy_CraneHook", (0.62, 0.75, 1.02), 0.0, (0.025, 0.025, 0.04), dark)

    # ---- Pipes (as v1): orange horizontal pipe (along X) from the
    # block to the silo, and a dark horizontal pipe (along Y) from the
    # block top toward the stack.
    add_cylinder("Alloy_Pipe", 8, 0.08, 0.75, 0.72, rust,
                 x=0.25, y=0.15, rotation=(0.0, math.radians(90), 0.0))
    add_cylinder("Alloy_Pipe2", 8, 0.07, 0.55, 1.05, dark,
                 x=-0.55, y=0.35, rotation=(math.radians(90), 0.0, 0.0))

    # ---- Glowing bollards on the platform corners.
    add_cone("Alloy_Bollard_01", 6, 0.03, 0.012, 0.09, 0.225, glow,
             x=-0.82, y=0.82)
    add_cone("Alloy_Bollard_02", 6, 0.03, 0.012, 0.09, 0.225, glow,
             x=0.82, y=-0.82)

    # ---- Ore pile in front of the block, left of the control module.
    add_ore("Alloy_OrePile_01", (-0.45, -0.78, 0.23), 0.06, ore)
    add_ore("Alloy_OrePile_02", (-0.38, -0.80, 0.225), 0.05, ore)
    add_ore("Alloy_OrePile_03", (-0.42, -0.74, 0.26), 0.04, ore)

    # ---- v5 greebles (graphics upgrade): surface detail on the v2
    # geometry, all inside the v2 envelope (x/y +/-0.90, z <= 1.60).
    # Two more vent grilles on the foundation tier (v2 has the -Y and
    # +X faces; these add +Y and -X).
    add_box("Alloy_VentGrille_03", (-0.30, 0.855, 0.11), 0.0, (0.15, 0.01, 0.025), dark)
    add_box("Alloy_VentGrille_04", (-0.855, -0.30, 0.11), 0.0, (0.01, 0.15, 0.025), dark)
    # Junction boxes on the block faces (v2 has none on the block).
    add_box("Alloy_JunctionBox_01", (0.31, -0.20, 0.50), 0.0, (0.01, 0.12, 0.10), gunmetal)
    add_box("Alloy_JunctionBox_02", (-0.81, 0.20, 0.50), 0.0, (0.01, 0.12, 0.10), gunmetal)
    # Two more vertical pipes: one on the block -X face (v2 has +X),
    # one on the small silo's +X face.
    add_cylinder("Alloy_VPipe2", 12, 0.05, 0.80, 0.60, gunmetal, x=-0.83, y=0.0)
    add_cylinder("Alloy_Silo2Pipe", 12, 0.04, 0.50, 0.50, gunmetal, x=0.83, y=-0.55)
    # Pressure gauge on the main silo's camera-facing (-Y) side: a
    # dark dial (cylinder rotated to face -Y) with a glowing needle.
    add_cylinder("Alloy_SiloGauge", 12, 0.06, 0.02, 0.85, dark,
                 x=0.55, y=-0.19, rotation=(math.radians(90), 0.0, 0.0))
    add_box("Alloy_SiloGaugeNeedle", (0.55, -0.205, 0.87), 0.0,
            (0.008, 0.008, 0.04), glow)
    # A third glowing window on the block +X face (v2 has two on -Y).
    add_box("Alloy_WindowFrame_03", (0.305, 0.20, 0.55), 0.0,
            (0.008, 0.10, 0.08), dark)
    add_box("Alloy_Window_03", (0.31, 0.20, 0.55), 0.0,
            (0.01, 0.08, 0.06), glow)
    # Roof antenna on the cap + glowing tip (tip z 1.57, under the
    # 1.60 stack top).
    add_cylinder("Alloy_RoofAntenna", 6, 0.012, 0.23, 1.455, gunmetal,
                 x=-0.25, y=0.0)
    add_sphere("Alloy_RoofAntennaTip", 6, 4, 0.02, (-0.25, 0.0, 1.57), 1.0, glow)
    # Rust hazard bars on the tier front (-Y) face (same face as the
    # v2 vent grilles; the plinth face would push them past the
    # +/-0.90 footprint).
    add_box("Alloy_HazardBar_01", (-0.50, -0.855, 0.11), 0.0, (0.20, 0.01, 0.02), rust)
    add_box("Alloy_HazardBar_02", (0.50, -0.855, 0.11), 0.0, (0.20, 0.01, 0.02), rust)
    # Railing ring around the main silo top (major 0.33 so it stays
    # inside the +/-0.90 footprint: 0.55 + 0.33 + 0.01 = 0.89).
    add_torus("Alloy_SiloRail", 0.33, 0.01, 16, 4, 1.10, gunmetal,
              x=0.55, y=0.15)
    # Small vent on the pyramid roof's front slope (roof surface there
    # is ~z 1.16, so the box sits on it).
    add_box("Alloy_RoofVent", (-0.25, -0.30, 1.19), 0.0, (0.12, 0.08, 0.04), dark)
    # Two more ore lumps: one on the belt, one on the pile.
    add_ore("Alloy_BeltOre_03", (-0.30, -0.62, 0.35), 0.03, ore)
    add_ore("Alloy_OrePile_04", (-0.40, -0.76, 0.24), 0.045, ore)

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
    of (material name, filepath, bytes). Four textures here, not three:
    the grey block/silos are the Extractor's hero surface, so they get
    their own panel texture instead of staying flat."""
    seeds = {"AlloyTitanium": 181, "AlloyGunmetal": 187,
             "AlloyDarkAlloy": 193, "AlloyGrey": 199}
    base_colors = {"AlloyTitanium": TITANIUM, "AlloyGunmetal": GUNMETAL,
                   "AlloyDarkAlloy": DARK_ALLOY, "AlloyGrey": ALLOY_GREY}
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
    print("Alloy Extractor v5 build:")
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
    # Bounding box check: footprint must stay within +/-0.90 (v1 base
    # platform), height <= 1.60 (v1 stack top).
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
                  if m.name in ("AlloyTitanium", "AlloyGunmetal",
                                "AlloyDarkAlloy", "AlloyGrey")]
    make_uv_atlas(built)
    TEXTURE_INFO = apply_metal_textures(metal_mats)
    export(built)
    report(built)
