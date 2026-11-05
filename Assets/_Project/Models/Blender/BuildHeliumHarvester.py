# BuildHeliumHarvester.py — Lunar Dominion: low-poly Helium-3 Harvester model.
#
# Builds the model from scratch in Blender and exports it as FBX for Unity.
# This script is the source of truth for the model: re-running it regenerates
# Assets/_Project/Resources/Models/HeliumHarvester.fbx, following the same
# pattern as BuildAlloyExtractor.py / BuildHydroponicsDome.py (copy this file,
# change the parts, keep the helpers).
# Note: the FBX lives under Resources/ on purpose — BaseView loads it at
# runtime with Resources.Load, which only works for assets under a Resources
# folder in a built player (Android/iOS).
#
# How to re-run (headless, no Blender UI needed):
#   blender --background --python BuildHeliumHarvester.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Blender used here: 4.2.3 LTS (any Blender 4.x should work).
#
# Design (all dimensions in Blender units; 1 Blender unit = 1 Unity unit):
#   Origin at ground centre, Z is up in Blender (exported as Y-up for Unity).
#   Footprint diameter ~1.4 units, total height ~2.4 units, matching the cyan
#   cylinder primitive the Harvester replaces in BaseView.cs (scale 1.4 x
#   2.4 x 1.4 at position (0, 0, 5.5)). Colours match BaseView's actual
#   primitive colour (0.35, 0.82, 0.90) — the brief's (0.2, 0.7, 0.9) is
#   the older value; the file in the workspace is authoritative.
#   Parts: dark base platform, tall cyan storage tank with a domed top, a
#   flared collector dish with a rim ring around the tank near the top, two
#   vertical pipes down the tank side, a pale gauge panel on the
#   camera-facing face, and a small glowing cyan orb on a stem at the top.
#   The cyan + pale glow keeps it readable against the blue Nexus tower and
#   the green Dome at phone size.
#   Camera-facing note (learned on the Extractor): the game's isometric
#   camera sits at Unity (9,11,9), i.e. the +X/+Z side. Blender -Y maps to
#   Unity +Z and Blender +X maps to Unity +X on export, so visible details
#   (gauge, pipes) go on the Blender -Y and/or +X faces, NOT on Blender +Y.
#   Low-poly on purpose: a few hundred triangles, readable at phone size.
#   Materials are Principled BSDF with base colours only (plus a small
#   emission on the pale glow parts); no textures, no Shader.Find hack —
#   the FBX materials import into Unity URP as-is.
#
# FBX export: forward -Z, up Y, apply unit scale, so the model drops into
# Unity at the same spot and scale as the primitive it replaces.

import os

import bpy

# ---------------------------------------------------------------------------
# Output path: ../../Resources/Models/HeliumHarvester.fbx relative to this
# script, wherever it runs.
# ---------------------------------------------------------------------------
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "..", "Resources", "Models", "HeliumHarvester.fbx"))

# ---------------------------------------------------------------------------
# Colours — same values as BaseView.cs uses for the Helium primitive.
# Blender material colours are linear RGB; these match closely enough that
# the model reads as the same building in-game.
# ---------------------------------------------------------------------------
HE3_CYAN = (0.35, 0.82, 0.90)   # main tank, dish rim, dome
HE3_DARK = (0.25, 0.28, 0.32)   # base platform, dish, pipes, stem
HE3_GLOW = (0.70, 0.95, 1.00)   # gauge panel, glowing orb on top


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


def add_sphere(name, segments, rings, radius, location, material, scale=(1.0, 1.0, 1.0)):
    """Add a UV sphere, optionally squashed via scale (applied)."""
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=radius, location=location,
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def add_cone(name, vertices, radius_bottom, radius_top, depth, location, material):
    """Add a truncated cone (Z-aligned), bottom radius first."""
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=radius_bottom, radius2=radius_top,
        depth=depth, location=location,
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    return obj


def add_torus(name, major_segments, minor_segments, major_radius, minor_radius, location, material):
    """Add a torus lying in the XY plane (ring around Z)."""
    bpy.ops.mesh.primitive_torus_add(
        major_segments=major_segments, minor_segments=minor_segments,
        major_radius=major_radius, minor_radius=minor_radius,
        location=location,
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

    cyan = make_material("HeliumCyan", HE3_CYAN, metallic=0.45, roughness=0.40)
    dark = make_material("HeliumDark", HE3_DARK, metallic=0.50, roughness=0.50)
    glow = make_material("HeliumGlow", HE3_GLOW, metallic=0.10, roughness=0.30, emission=0.9)

    # Dark base platform: 1.4 x 1.4, 0.16 tall, sits on the ground.
    add_box("He3_Base", (1.40, 1.40, 0.16), (0.0, 0.0, 0.08), dark)
    # Tall cylindrical cyan storage tank. Radius 0.55, 1.60 tall,
    # from z = 0.16 to z = 1.76.
    add_cylinder("He3_Tank", 16, 0.55, 1.60, (0.0, 0.0, 0.96), cyan)
    # Domed top: a squashed sphere, radius 0.55, z-scale 0.45, centred at
    # the tank top (z = 1.76). Top of the dome at z ~ 2.01.
    add_sphere("He3_Dome", 12, 5, 0.55, (0.0, 0.0, 1.76), cyan,
               scale=(1.0, 1.0, 0.45))
    # Flared collector dish around the tank near the top: a truncated cone
    # flaring outward upward, from radius 0.55 at z = 1.31 to 0.85 at
    # z = 1.53. Dark, so the cyan rim ring on top of it stands out.
    add_cone("He3_Dish", 16, 0.55, 0.85, 0.22, (0.0, 0.0, 1.42), dark)
    # Cyan rim ring around the top edge of the dish.
    add_torus("He3_Rim", 12, 6, 0.85, 0.05, (0.0, 0.0, 1.53), cyan)
    # Vertical pipe down the camera-facing (-Y, becomes Unity +Z) side of
    # the tank, from the base up to the dish. Tank radius is 0.55, so the
    # pipe centre at y = -0.58 sits just proud of the tank wall.
    add_cylinder("He3_PipeFront", 8, 0.06, 1.30, (0.25, -0.58, 0.85), dark)
    # Second vertical pipe on the +X side (also camera-facing in Unity).
    add_cylinder("He3_PipeSide", 8, 0.06, 1.30, (0.58, 0.0, 0.85), dark)
    # Pale gauge panel on the camera-facing (-Y) face of the tank, so the
    # player can see it from the isometric camera. Sits just proud of the
    # tank wall (y = -0.57 vs tank radius 0.55).
    add_box("He3_Gauge", (0.30, 0.05, 0.40), (0.0, -0.57, 1.05), glow)
    # Short dark stem on top of the dome, holding the glowing orb.
    add_cylinder("He3_Stem", 8, 0.05, 0.18, (0.0, 0.0, 2.10), dark)
    # Small glowing cyan orb on top — the tallest point, top at z ~ 2.42,
    # matching the old primitive's 2.4 height.
    add_sphere("He3_Orb", 10, 5, 0.14, (0.0, 0.0, 2.28), glow)

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
    print("Helium-3 Harvester build:")
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
