# BuildCommandNexus.py — Lunar Dominion: low-poly Command Nexus model.
#
# Builds the model from scratch in Blender and exports it as FBX for Unity.
# This script is the source of truth for the model: re-running it regenerates
# Assets/_Project/Resources/Models/CommandNexus.fbx, so the next 7 buildings can follow
# the same pattern (copy this file, change the parts, keep the helpers).
# Note: the FBX lives under Resources/ on purpose — BaseView loads it at
# runtime with Resources.Load, which only works for assets under a Resources
# folder in a built player (Android/iOS).
#
# How to re-run (headless, no Blender UI needed):
#   blender --background --python BuildCommandNexus.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Blender used here: 4.2.3 LTS (any Blender 4.x should work).
#
# Design (all dimensions in Blender units; 1 Blender unit = 1 Unity unit):
#   Origin at ground centre, Z is up in Blender (exported as Y-up for Unity).
#   Footprint diameter ~2 units, total height ~2.9 units, matching the blue
#   cylinder primitive the Nexus replaces in BaseView.cs (scale 2 x 3 x 2 at
#   position (0, 0, 0)). Colours match BaseView's Nexus blue (0.15, 0.35, 0.75).
#   Parts: octagonal base platform, thin glowing ring, octagonal main tower,
#   light-blue window band, upper collar, domed top, antenna + tip.
#   Low-poly on purpose: a few hundred triangles, readable at phone size.
#   Materials are Principled BSDF with base colours only (plus a small
#   emission on the light-blue accents); no textures, no Shader.Find hack —
#   the FBX materials import into Unity URP as-is.
#
# FBX export: forward -Z, up Y, apply unit scale, so the model drops into
# Unity at the same spot and scale as the primitive it replaces.

import math
import os

import bpy

# ---------------------------------------------------------------------------
# Output path: ../../Resources/Models/CommandNexus.fbx relative to this
# script, wherever it runs.
# ---------------------------------------------------------------------------
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "..", "Resources", "Models", "CommandNexus.fbx"))

# ---------------------------------------------------------------------------
# Colours — same values as BaseView.cs uses for the Nexus primitive.
# Blender material colours are linear RGB; these match closely enough that
# the model reads as the same building in-game.
# ---------------------------------------------------------------------------
NEXUS_BLUE = (0.15, 0.35, 0.75)   # main tower body
NEXUS_DARK = (0.22, 0.23, 0.27)   # base platform, collar, antenna
NEXUS_LIGHT = (0.45, 0.72, 1.00)  # dome, window band, ring, antenna tip


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


def add_cylinder(name, vertices, radius, depth, z, material):
    """Add a Z-up cylinder (Blender's primitive_cylinder_add is Z-aligned)."""
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth, location=(0.0, 0.0, z)
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    return obj


def add_sphere(name, segments, rings, radius, z, z_scale, material):
    """Add a UV sphere, optionally squashed vertically (for the dome)."""
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=radius, location=(0.0, 0.0, z)
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (1.0, 1.0, z_scale)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
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

    blue = make_material("NexusBlue", NEXUS_BLUE, metallic=0.15, roughness=0.55)
    dark = make_material("NexusDark", NEXUS_DARK, metallic=0.35, roughness=0.50)
    light = make_material(
        "NexusLight", NEXUS_LIGHT, metallic=0.10, roughness=0.40, emission=0.6
    )

    # Octagonal base platform: radius 1.0, 0.25 tall, sits on the ground.
    add_cylinder("Nexus_Base", 8, 1.00, 0.25, 0.125, dark)
    # Thin glowing ring on top of the base platform.
    add_cylinder("Nexus_Ring", 8, 0.88, 0.06, 0.28, light)
    # Main tower: octagonal prism, radius 0.68, from z=0.31 to z=1.66.
    add_cylinder("Nexus_Tower", 8, 0.68, 1.35, 0.985, blue)
    # Window band: slightly proud of the tower, wraps around it.
    add_cylinder("Nexus_Windows", 8, 0.71, 0.16, 1.30, light)
    # Upper collar: dark ring the dome sits on.
    add_cylinder("Nexus_Collar", 8, 0.78, 0.18, 1.75, dark)
    # Domed top: squashed sphere, base at the collar, top at ~z=2.20.
    add_sphere("Nexus_Dome", 12, 6, 0.55, 1.84, 0.65, light)
    # Antenna spire on top of the dome.
    add_cylinder("Nexus_Antenna", 6, 0.045, 0.55, 2.475, dark)
    # Glowing tip on the antenna; top of the model at ~z=2.89.
    add_sphere("Nexus_Tip", 8, 4, 0.09, 2.80, 1.0, light)

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
    print("Command Nexus build:")
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
