# BuildAegisMotorPool.py — Lunar Dominion: low-poly Aegis Motor Pool model.
#
# Builds the model from scratch in Blender and exports it as FBX for Unity.
# This script is the source of truth for the model: re-running it regenerates
# Assets/_Project/Resources/Models/AegisMotorPool.fbx, following the same
# pattern as BuildNovaArsenal.py / BuildMarineFoundry.py (copy this file,
# change the parts, keep the helpers).
# Note: the FBX lives under Resources/ on purpose — BaseView loads it at
# runtime with Resources.Load, which only works for assets under a Resources
# folder in a built player (Android/iOS).
#
# How to re-run (headless, no Blender UI needed):
#   blender --background --python BuildAegisMotorPool.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Blender used here: 4.2.3 LTS (any Blender 4.x should work).
#
# Design (all dimensions in Blender units; 1 Blender unit = 1 Unity unit):
#   Origin at ground centre, Z is up in Blender (exported as Y-up for Unity).
#   Footprint ~2.4 x 2.0 units, total height ~1.67 units, matching the grey
#   cube primitive the Motor Pool replaces in BaseView.cs (scale 2.4 x 1.2 x 2
#   at position (0, 0, -5.5)). Colours match BaseView's actual primitive
#   colour (0.55, 0.55, 0.60) for the main block. The extra height over the
#   1.2 primitive is the roof antenna — the garage block itself stays at the
#   primitive's height.
#   Parts: dark base platform, main grey garage block with a flat roof slab,
#   a wide garage doorway (dark opening with a pale frame) on the
#   camera-facing face, a small orange rover parked half-out in front of the
#   doorway, a glowing strip light above the door, a roof vent and antenna,
#   and a fuel drum plus a crate on the +X face. Deliberately distinct from
#   the other two barracks: the Foundry is a green barracks with windows and
#   a flag, the Arsenal is a red weapons building with a pyramid roof, a
#   spire and a cannon barrel; the Motor Pool is a grey vehicle garage with
#   a big roller door and a rover out front.
#   Camera-facing note (learned on the Extractor/Harvester): the game's
#   isometric camera sits at Unity (9,11,9), i.e. the +X/+Z side. Blender
#   -Y maps to Unity +Z and Blender +X maps to Unity +X on export, so
#   visible details (doorway, rover, light strip, drum, crate) go on the
#   Blender -Y and/or +X faces, NOT on Blender +Y. This matters most for
#   the Motor Pool: it sits at (0,-5.5), on the far side of the base from
#   the camera, so a detail hidden on its far side would be invisible
#   in-game.
#   Low-poly on purpose: a few hundred triangles, readable at phone size.
#   Materials are Principled BSDF with base colours only; no textures,
#   no Shader.Find hack — the FBX materials import into Unity URP as-is.
#
# FBX export: forward -Z, up Y, apply unit scale, so the model drops into
# Unity at the same spot and scale as the primitive it replaces.

import os

import bpy

# ---------------------------------------------------------------------------
# Output path: ../../Resources/Models/AegisMotorPool.fbx relative to this
# script, wherever it runs.
# ---------------------------------------------------------------------------
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "..", "Resources", "Models", "AegisMotorPool.fbx"))

# ---------------------------------------------------------------------------
# Colours — same values as BaseView.cs uses for the Aegis primitive.
# Blender material colours are linear RGB; these match closely enough that
# the model reads as the same building in-game.
# ---------------------------------------------------------------------------
AEGIS_GREY = (0.55, 0.55, 0.60)   # main garage block
AEGIS_DARK = (0.30, 0.30, 0.34)   # base platform, roof slab, door opening, vent
AEGIS_GUN = (0.25, 0.25, 0.28)    # rover cabin, wheels, crate (dark grey)
AEGIS_ORANGE = (0.85, 0.45, 0.15) # rover body, fuel drum (warm accent)
AEGIS_LIGHT = (0.90, 0.88, 0.80)  # door frame (pale)
AEGIS_GLOW = (1.00, 0.55, 0.15)   # strip light above the door (glowing)


def make_material(name, rgb, metallic=0.1, roughness=0.6, emission=0.0):
    """Create a Principled BSDF material with the given base colour."""
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission > 0.0:
        bsdf.inputs["Emission Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission
    return mat


def add_box(name, size, location, material, rotation=(0.0, 0.0, 0.0)):
    """Add a box of the given (x, y, z) size, centred at location."""
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (size[0] / 2.0, size[1] / 2.0, size[2] / 2.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def add_cylinder(name, vertices, radius, depth, location, material, rotation=(0.0, 0.0, 0.0)):
    """Add a cylinder (Z-aligned before rotation)."""
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth,
        location=location, rotation=rotation,
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    return obj


def clear_scene():
    """Delete everything, so re-running the script starts from scratch."""
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in list(bpy.data.meshes):
        bpy.data.meshes.remove(block)
    for block in list(bpy.data.materials):
        bpy.data.materials.remove(block)


def build():
    clear_scene()

    grey = make_material("AegisGrey", AEGIS_GREY, metallic=0.25, roughness=0.55)
    dark = make_material("AegisDark", AEGIS_DARK, metallic=0.40, roughness=0.55)
    gun = make_material("AegisGun", AEGIS_GUN, metallic=0.60, roughness=0.40)
    orange = make_material("AegisOrange", AEGIS_ORANGE, metallic=0.20, roughness=0.50)
    light = make_material("AegisLight", AEGIS_LIGHT, metallic=0.10, roughness=0.45)
    glow = make_material("AegisGlow", AEGIS_GLOW, metallic=0.10, roughness=0.50, emission=0.9)

    # Dark base platform: 2.4 x 2.0, 0.12 tall, sits on the ground.
    add_box("Aegis_Base", (2.40, 2.00, 0.12), (0.0, 0.0, 0.06), dark)
    # Main garage block: 2.0 x 1.7 x 1.0, from z = 0.12 to z = 1.12.
    add_box("Aegis_Block", (2.00, 1.70, 1.00), (0.0, 0.0, 0.62), grey)
    # Flat roof slab: 2.1 x 1.8 x 0.10, from z = 1.12 to z = 1.22.
    add_box("Aegis_Roof", (2.10, 1.80, 0.10), (0.0, 0.0, 1.17), dark)
    # Pale door frame on the camera-facing (-Y, becomes Unity +Z) face of
    # the main block: 1.16 wide, 0.87 tall, just proud of the wall
    # (y = -0.865 vs block face at y = -0.85), from z = 0.12 to z = 0.99.
    add_box("Aegis_DoorFrame", (1.16, 0.05, 0.87), (0.0, -0.865, 0.555), light)
    # Dark garage doorway inside the frame: 1.0 wide, 0.75 tall, a touch
    # prouder still (y = -0.88), from z = 0.12 to z = 0.87.
    add_box("Aegis_Door", (1.00, 0.04, 0.75), (0.0, -0.88, 0.495), dark)
    # Glowing orange strip light above the door, on the same face.
    add_box("Aegis_LightStrip", (0.50, 0.05, 0.08), (0.0, -0.87, 1.02), glow)
    # Small orange rover parked half-out in front of the doorway, on the
    # camera-facing side: body 0.70 x 0.50 x 0.28, from z = 0.12 to
    # z = 0.40, centred at y = -1.10 (in front of the block face).
    add_box("Aegis_RoverBody", (0.70, 0.50, 0.28), (0.55, -1.10, 0.26), orange)
    # Rover cabin on top of the body: 0.36 x 0.34 x 0.18, z = 0.40 to 0.58.
    add_box("Aegis_RoverCabin", (0.36, 0.34, 0.18), (0.45, -1.10, 0.49), gun)
    # Four rover wheels: 8-sided cylinders, radius 0.11, axis along X
    # (rotated 90 deg about Y), at the body corners, z = 0.11.
    for i, (wx, wy) in enumerate(
            [(0.28, -0.92), (0.82, -0.92), (0.28, -1.28), (0.82, -1.28)]):
        add_cylinder("Aegis_Wheel%d" % (i + 1), 8, 0.11, 0.08,
                     (wx, wy, 0.11), gun, rotation=(0.0, 1.5707963267948966, 0.0))
    # Dark roof vent on the roof, back-left corner.
    add_box("Aegis_Vent", (0.30, 0.30, 0.18), (-0.70, 0.40, 1.31), dark)
    # Thin antenna on the roof, front-right corner: radius 0.04, 0.45 tall,
    # from z = 1.22 to z = 1.67, the model's high point.
    add_cylinder("Aegis_Antenna", 8, 0.04, 0.45, (0.75, -0.55, 1.445), dark)
    # Orange fuel drum on the +X (camera-facing) face of the main block:
    # radius 0.16, 0.45 tall, from z = 0.12 to z = 0.57, sticking out to
    # x = 1.16, inside the 2.4 base platform.
    add_cylinder("Aegis_Drum", 12, 0.16, 0.45, (1.05, 0.30, 0.345), orange)
    # Dark crate on the +X face, in front of the drum: 0.30 cube.
    add_box("Aegis_Crate", (0.30, 0.30, 0.30), (1.02, -0.45, 0.27), gun)

    return list(bpy.context.scene.objects)


def export(objects):
    """Export all mesh objects to FBX, 1 Blender unit = 1 Unity unit."""
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
        path_mode="AUTO",
        embed_textures=False,
    )


def report(objects):
    total_tris = 0
    print("Aegis Motor Pool build:")
    for obj in objects:
        if obj.type != "MESH":
            continue
        mesh = obj.data
        mesh.calc_loop_triangles()
        total_tris += len(mesh.loop_triangles)
        print("  %-18s tris=%3d  materials=%s"
              % (obj.name, len(mesh.loop_triangles),
                 [m.name if m else "?" for m in mesh.materials]))
    print("  objects=%d  total_tris=%d" % (len(objects), total_tris))
    print("  exported: %s" % OUTPUT_FBX)


if __name__ == "__main__":
    built = build()
    export(built)
    report(built)
