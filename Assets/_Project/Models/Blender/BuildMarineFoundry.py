# BuildMarineFoundry.py — Lunar Dominion: low-poly Marine Foundry model.
#
# Builds the model from scratch in Blender and exports it as FBX for Unity.
# This script is the source of truth for the model: re-running it regenerates
# Assets/_Project/Resources/Models/MarineFoundry.fbx, following the same
# pattern as BuildHeliumHarvester.py / BuildAlloyExtractor.py (copy this file,
# change the parts, keep the helpers).
# Note: the FBX lives under Resources/ on purpose — BaseView loads it at
# runtime with Resources.Load, which only works for assets under a Resources
# folder in a built player (Android/iOS).
#
# How to re-run (headless, no Blender UI needed):
#   blender --background --python BuildMarineFoundry.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Blender used here: 4.2.3 LTS (any Blender 4.x should work).
#
# Design (all dimensions in Blender units; 1 Blender unit = 1 Unity unit):
#   Origin at ground centre, Z is up in Blender (exported as Y-up for Unity).
#   Footprint ~2 x 2 units, total height ~1.47 units, matching the green-grey
#   cube primitive the Foundry replaces in BaseView.cs (scale 2 x 1.4 x 2
#   at position (-4, 0, -3.5)). Colours match BaseView's actual primitive
#   colour (0.35, 0.65, 0.25) for the main block.
#   Parts: dark base platform, main green-grey barracks block with a flat
#   dark roof slab, a smaller annex block on the +X side, a thin antenna on
#   the roof, a flag pole with a small orange flag, a pale doorway and two
#   pale windows on the camera-facing face, and a dark vent on the +X face.
#   The green-grey + pale doorway keeps it readable against the green
#   Hydroponics Dome and the other buildings at phone size.
#   Camera-facing note (learned on the Extractor/Harvester): the game's
#   isometric camera sits at Unity (9,11,9), i.e. the +X/+Z side. Blender
#   -Y maps to Unity +Z and Blender +X maps to Unity +X on export, so
#   visible details (doorway, windows, vent, annex, flag) go on the
#   Blender -Y and/or +X faces, NOT on Blender +Y. This matters most for
#   the Foundry: it sits at (-4,-3.5), on the far side of the base from
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
# Output path: ../../Resources/Models/MarineFoundry.fbx relative to this
# script, wherever it runs.
# ---------------------------------------------------------------------------
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "..", "Resources", "Models", "MarineFoundry.fbx"))

# ---------------------------------------------------------------------------
# Colours — same values as BaseView.cs uses for the Marine primitive.
# Blender material colours are linear RGB; these match closely enough that
# the model reads as the same building in-game.
# ---------------------------------------------------------------------------
MARINE_GREEN = (0.35, 0.65, 0.25)   # main barracks block, annex
MARINE_DARK = (0.20, 0.35, 0.18)    # base platform, roof slab, vent
MARINE_LIGHT = (0.85, 0.88, 0.80)   # doorway, windows (pale off-white)
MARINE_ORANGE = (0.85, 0.45, 0.15)  # small flag


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

    green = make_material("MarineGreen", MARINE_GREEN, metallic=0.25, roughness=0.55)
    dark = make_material("MarineDark", MARINE_DARK, metallic=0.40, roughness=0.55)
    light = make_material("MarineLight", MARINE_LIGHT, metallic=0.10, roughness=0.45)
    orange = make_material("MarineOrange", MARINE_ORANGE, metallic=0.10, roughness=0.50)

    # Dark base platform: 2.0 x 2.0, 0.12 tall, sits on the ground.
    add_box("Marine_Base", (2.00, 2.00, 0.12), (0.0, 0.0, 0.06), dark)
    # Main barracks block: 1.5 x 1.2 x 0.9, from z = 0.12 to z = 1.02.
    add_box("Marine_Block", (1.50, 1.20, 0.90), (0.0, 0.0, 0.57), green)
    # Flat dark roof slab, slightly wider than the block: 1.6 x 1.3 x 0.08,
    # top at z = 1.10.
    add_box("Marine_Roof", (1.60, 1.30, 0.08), (0.0, 0.0, 1.06), dark)
    # Smaller annex block on the +X (camera-facing) side: 0.6 x 0.8 x 0.55,
    # from z = 0.12 to z = 0.67, sticking out to x = 0.95.
    add_box("Marine_Annex", (0.60, 0.80, 0.55), (0.65, 0.10, 0.395), green)
    # Thin antenna on the roof, back-left corner: radius 0.03, 0.35 tall,
    # from the roof (z = 1.10) up to z = 1.45.
    add_cylinder("Marine_Antenna", 8, 0.03, 0.35, (-0.55, -0.35, 1.275), dark)
    # Flag pole on the roof, +X side: radius 0.02, 0.35 tall, top at 1.45.
    add_cylinder("Marine_FlagPole", 8, 0.02, 0.35, (0.55, 0.35, 1.275), dark)
    # Small orange flag on top of the pole, flying toward +X: 0.22 x 0.02
    # x 0.14, centred at z = 1.40 (top edge at 1.47, the model's high point).
    add_box("Marine_Flag", (0.22, 0.02, 0.14), (0.67, 0.35, 1.40), orange)
    # Pale doorway on the camera-facing (-Y, becomes Unity +Z) face of the
    # main block: 0.4 wide, 0.6 tall, just proud of the wall (y = -0.62
    # vs block face at y = -0.60), from z = 0.12 to z = 0.72.
    add_box("Marine_Door", (0.40, 0.06, 0.60), (0.10, -0.62, 0.42), light)
    # Two pale windows on the same camera-facing face, either side of the
    # door, at z = 0.75.
    add_box("Marine_WindowLeft", (0.20, 0.05, 0.20), (-0.45, -0.62, 0.75), light)
    add_box("Marine_WindowRight", (0.20, 0.05, 0.20), (0.55, -0.62, 0.75), light)
    # Dark vent grille on the +X (camera-facing) face of the main block,
    # above the annex: 0.05 thick, 0.30 wide, 0.20 tall, at z = 0.80.
    add_box("Marine_Vent", (0.05, 0.30, 0.20), (0.77, 0.10, 0.80), dark)

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
    print("Marine Foundry build:")
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
