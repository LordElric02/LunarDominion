# BuildAlloyExtractor.py — Lunar Dominion: low-poly Alloy Extractor model.
#
# Builds the model from scratch in Blender and exports it as FBX for Unity.
# This script is the source of truth for the model: re-running it regenerates
# Assets/_Project/Resources/Models/AlloyExtractor.fbx, following the same
# pattern as BuildCommandNexus.py / BuildHydroponicsDome.py (copy this file,
# change the parts, keep the helpers).
# Note: the FBX lives under Resources/ on purpose — BaseView loads it at
# runtime with Resources.Load, which only works for assets under a Resources
# folder in a built player (Android/iOS).
#
# How to re-run (headless, no Blender UI needed):
#   blender --background --python BuildAlloyExtractor.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Blender used here: 4.2.3 LTS (any Blender 4.x should work).
#
# Design (all dimensions in Blender units; 1 Blender unit = 1 Unity unit):
#   Origin at ground centre, Z is up in Blender (exported as Y-up for Unity).
#   Footprint ~1.8 x 1.8 units, total height ~1.6 units, matching the grey
#   cube primitive the Extractor replaces in BaseView.cs (scale 1.8 x 1.6 x
#   1.8 at position (4, 0, 3.5)). Colours match BaseView's Alloy grey
#   (0.65, 0.68, 0.72).
#   Parts: dark base platform, main grey refinery block with a pyramid
#   (slanted) roof, a cylindrical metal silo/tank beside it, a tall thin
#   exhaust stack with an orange tip, and two horizontal pipes plus an
#   orange vent grille. The rust-orange accents keep it readable against
#   the blue Nexus and green Dome at phone size.
#   Low-poly on purpose: a few hundred triangles, readable at phone size.
#   Materials are Principled BSDF with base colours only (plus a small
#   emission on the orange accents); no textures, no Shader.Find hack —
#   the FBX materials import into Unity URP as-is.
#
# FBX export: forward -Z, up Y, apply unit scale, so the model drops into
# Unity at the same spot and scale as the primitive it replaces.

import math
import os

import bpy

# ---------------------------------------------------------------------------
# Output path: ../../Resources/Models/AlloyExtractor.fbx relative to this
# script, wherever it runs.
# ---------------------------------------------------------------------------
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "..", "Resources", "Models", "AlloyExtractor.fbx"))

# ---------------------------------------------------------------------------
# Colours — same values as BaseView.cs uses for the Alloy primitive.
# Blender material colours are linear RGB; these match closely enough that
# the model reads as the same building in-game.
# ---------------------------------------------------------------------------
ALLOY_GREY = (0.65, 0.68, 0.72)  # main refinery block, silo tank
ALLOY_DARK = (0.30, 0.32, 0.35)  # base platform, roof, stack, caps
ALLOY_RUST = (0.85, 0.45, 0.15)  # pipes, vent grille, stack tip accent


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


def add_cone(name, vertices, radius, depth, location, material, rotation=(0.0, 0.0, 0.0)):
    """Add a cone (pyramid when vertices=4), Z-aligned before rotation."""
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=radius, radius2=0.0, depth=depth,
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

    grey = make_material("AlloyGrey", ALLOY_GREY, metallic=0.55, roughness=0.45)
    dark = make_material("AlloyDark", ALLOY_DARK, metallic=0.50, roughness=0.50)
    rust = make_material(
        "AlloyRust", ALLOY_RUST, metallic=0.30, roughness=0.60, emission=0.4
    )

    # Dark base platform: 1.8 x 1.8, 0.18 tall, sits on the ground.
    add_box("Alloy_Base", (1.80, 1.80, 0.18), (0.0, 0.0, 0.09), dark)
    # Main refinery block: grey, 1.1 x 0.9 x 0.85, on the base, left side.
    # Top of the block at z = 0.18 + 0.85 = 1.03.
    add_box("Alloy_Block", (1.10, 0.90, 0.85), (-0.25, 0.0, 0.605), grey)
    # Slanted (pyramid) roof on the block: 4-sided cone, square aligned
    # by rotating 45 degrees about Z. Base at z = 1.03, top at ~1.31.
    add_cone("Alloy_Roof", 4, 0.78, 0.28, (-0.25, 0.0, 1.17), dark,
             rotation=(0.0, 0.0, math.radians(45)))
    # Cylindrical metal silo/tank beside the block, right side.
    # Radius 0.32, 1.0 tall, top at z = 1.18.
    add_cylinder("Alloy_Silo", 12, 0.32, 1.00, (0.55, 0.15, 0.68), grey)
    # Dark cap disc on top of the silo.
    add_cylinder("Alloy_SiloCap", 12, 0.34, 0.06, (0.55, 0.15, 1.21), dark)
    # Tall thin exhaust stack at the back-left corner. Top at z = 1.60,
    # the tallest point of the model.
    add_cylinder("Alloy_Stack", 8, 0.09, 1.42, (-0.55, 0.60, 0.89), dark)
    # Orange tip ring on the stack.
    add_cylinder("Alloy_StackTip", 8, 0.12, 0.08, (-0.55, 0.60, 1.56), rust)
    # Orange horizontal pipe (along X) from the block to the silo.
    add_cylinder("Alloy_Pipe", 8, 0.08, 0.75, (0.25, 0.15, 0.72), rust,
                 rotation=(0.0, math.radians(90), 0.0))
    # Dark horizontal pipe (along Y) from the block top toward the stack.
    add_cylinder("Alloy_Pipe2", 8, 0.07, 0.55, (-0.55, 0.35, 1.05), dark,
                 rotation=(math.radians(90), 0.0, 0.0))
    # Orange vent grille on the front face of the block. It sits on the
    # Blender -Y face, which becomes the Unity +Z face on export — the
    # side the game's isometric camera (at Unity (9,11,9)) looks at.
    add_box("Alloy_Vent", (0.34, 0.06, 0.22), (-0.25, -0.47, 0.72), rust)

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
    print("Alloy Extractor build:")
    for obj in objects:
        if obj.type != "MESH":
            continue
        mesh = obj.data
        mesh.calc_loop_triangles()
        total_tris += len(mesh.loop_triangles)
        print("  %-16s tris=%3d  materials=%s"
              % (obj.name, len(mesh.loop_triangles),
                 [m.name if m else "?" for m in mesh.materials]))
    print("  objects=%d  total_tris=%d" % (len(objects), total_tris))
    print("  exported: %s" % OUTPUT_FBX)


if __name__ == "__main__":
    built = build()
    export(built)
    report(built)
