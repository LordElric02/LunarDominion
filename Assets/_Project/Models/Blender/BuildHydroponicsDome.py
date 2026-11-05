# BuildHydroponicsDome.py — Lunar Dominion: low-poly Hydroponics Dome model.
#
# Builds the model from scratch in Blender and exports it as FBX for Unity.
# This script is the source of truth for the model: re-running it regenerates
# Assets/_Project/Resources/Models/HydroponicsDome.fbx, following the same
# pattern as BuildCommandNexus.py (copy this file, change the parts, keep
# the helpers).
# Note: the FBX lives under Resources/ on purpose — BaseView loads it at
# runtime with Resources.Load, which only works for assets under a Resources
# folder in a built player (Android/iOS).
#
# How to re-run (headless, no Blender UI needed):
#   blender --background --python BuildHydroponicsDome.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Blender used here: 4.2.3 LTS (any Blender 4.x should work).
#
# Design (all dimensions in Blender units; 1 Blender unit = 1 Unity unit):
#   Origin at ground centre, Z is up in Blender (exported as Y-up for Unity).
#   Footprint diameter ~2 units, total height ~1.24 units, matching the green
#   sphere primitive the Dome replaces in BaseView.cs (scale 2 x 1.2 x 2 at
#   position (-4, 0, 3.5)). Colours match BaseView's Hydro green
#   (0.25, 0.75, 0.35).
#   Parts: dark circular base platform, thin pale rim ring, squashed-sphere
#   greenhouse dome, dark frame ring around the dome, small vent on top,
#   and three low plant mounds on the base rim outside the dome.
#   Low-poly on purpose: a few hundred triangles, readable at phone size.
#   Materials are Principled BSDF with base colours only (plus a small
#   emission on the pale-green accents); no textures, no Shader.Find hack —
#   the FBX materials import into Unity URP as-is.
#
# FBX export: forward -Z, up Y, apply unit scale, so the model drops into
# Unity at the same spot and scale as the primitive it replaces.

import math
import os

import bpy

# ---------------------------------------------------------------------------
# Output path: ../../Resources/Models/HydroponicsDome.fbx relative to this
# script, wherever it runs.
# ---------------------------------------------------------------------------
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "..", "Resources", "Models", "HydroponicsDome.fbx"))

# ---------------------------------------------------------------------------
# Colours — same values as BaseView.cs uses for the Hydro primitive.
# Blender material colours are linear RGB; these match closely enough that
# the model reads as the same building in-game.
# ---------------------------------------------------------------------------
HYDRO_GREEN = (0.25, 0.75, 0.35)  # greenhouse dome body
HYDRO_DARK = (0.20, 0.28, 0.22)   # base platform, frame ring, vent
HYDRO_LIGHT = (0.55, 0.90, 0.60)  # rim ring accent
HYDRO_PLANT = (0.18, 0.60, 0.28)  # plant mounds on the base rim


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


def add_sphere(name, segments, rings, radius, x, y, z, z_scale, material):
    """Add a UV sphere, optionally squashed vertically (for the dome)."""
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=radius, location=(x, y, z)
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (1.0, 1.0, z_scale)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def add_torus(name, major_radius, minor_radius, major_segments, minor_segments, z, material):
    """Add a torus lying in the XY plane (ring around the Z axis)."""
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius, minor_radius=minor_radius,
        major_segments=major_segments, minor_segments=minor_segments,
        location=(0.0, 0.0, z),
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

    green = make_material("HydroGreen", HYDRO_GREEN, metallic=0.10, roughness=0.45)
    dark = make_material("HydroDark", HYDRO_DARK, metallic=0.35, roughness=0.50)
    light = make_material(
        "HydroLight", HYDRO_LIGHT, metallic=0.10, roughness=0.40, emission=0.5
    )
    plant = make_material("HydroPlant", HYDRO_PLANT, metallic=0.05, roughness=0.70)

    # Dark circular base platform: radius 1.0, 0.22 tall, sits on the ground.
    add_cylinder("Hydro_Base", 10, 1.00, 0.22, 0.11, dark)
    # Thin pale rim ring on top of the base platform.
    add_cylinder("Hydro_Ring", 10, 0.94, 0.06, 0.25, light)
    # Greenhouse dome: squashed sphere, base inside the rim ring, top ~z=1.15.
    add_sphere("Hydro_Dome", 12, 6, 0.86, 0.0, 0.0, 0.68, 0.55, green)
    # Dark frame ring around the dome, half-embedded in the dome surface
    # so it reads as a band (major radius chosen to poke just outside it).
    add_torus("Hydro_FrameRing", 0.84, 0.045, 12, 4, 0.62, dark)
    # Small vent on top of the dome; top of the model at ~z=1.24.
    add_cylinder("Hydro_Vent", 6, 0.11, 0.10, 1.19, dark)
    # Three low plant mounds on the base rim, outside the dome glass.
    for i, angle_deg in enumerate((40, 160, 280)):
        angle = math.radians(angle_deg)
        x = 0.88 * math.cos(angle)
        y = 0.88 * math.sin(angle)
        add_sphere("Hydro_Plant%d" % (i + 1), 8, 4, 0.13, x, y, 0.30, 0.6, plant)

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
    print("Hydroponics Dome build:")
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
