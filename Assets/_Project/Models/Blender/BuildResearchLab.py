# BuildResearchLab.py — Lunar Dominion: low-poly Research Lab model.
#
# Builds the model from scratch in Blender and exports it as FBX for Unity.
# This script is the source of truth for the model: re-running it regenerates
# Assets/_Project/Resources/Models/ResearchLab.fbx, following the same
# pattern as BuildAegisMotorPool.py / BuildNovaArsenal.py (copy this file,
# change the parts, keep the helpers).
# Note: the FBX lives under Resources/ on purpose — BaseView loads it at
# runtime with Resources.Load, which only works for assets under a Resources
# folder in a built player (Android/iOS).
#
# How to re-run (headless, no Blender UI needed):
#   blender --background --python BuildResearchLab.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Blender used here: 4.2.3 LTS (any Blender 4.x should work).
#
# Design (all dimensions in Blender units; 1 Blender unit = 1 Unity unit):
#   Origin at ground centre, Z is up in Blender (exported as Y-up for Unity).
#   Footprint ~2.2 x 2.2 units (base platform), total height ~2.24 units,
#   matching the purple cylinder primitive the Lab replaces in BaseView.cs
#   (scale 1.8 x 2.2 x 1.8 at position (-6.5, 0, 0)). Colours match
#   BaseView's actual primitive colour (0.55, 0.30, 0.85) for the main tower.
#   The base platform is a touch wider than the 1.8 primitive (as with the
#   Harvester's dish rim and the Motor Pool's rover), but the Lab sits clear
#   of every other building, so nothing overlaps.
#   Parts: dark base platform, main purple cylindrical tower with a glowing
#   pale-cyan window band ringing it, a dark domed roof, a side annex on the
#   +X face with a tilted satellite dish on its roof, a pale doorway on the
#   camera-facing face, a dark vent on the +X face, and a thin antenna with
#   a glowing tip on the dome. Deliberately distinct from the Command Nexus
#   (blue tower, centre): the Lab is purple, shorter and wider, with a dome,
#   a glowing window ring and a dish instead of a spire.
#   Camera-facing note (learned on the Extractor/Harvester): the game's
#   isometric camera sits at Unity (9,11,9), i.e. the +X/+Z side. Blender
#   -Y maps to Unity +Z and Blender +X maps to Unity +X on export, so
#   visible details (doorway, window band, dish, vent) go on the
#   Blender -Y and/or +X faces, NOT on Blender +Y. This matters most for
#   the Lab: it sits at (-6.5,0), on the far west side of the base from
#   the camera, so a detail hidden on its far side would be invisible
#   in-game. The window band rings the whole tower, so it reads from
#   every angle.
#   Low-poly on purpose: a few hundred triangles, readable at phone size.
#   Materials are Principled BSDF with base colours only; no textures,
#   no Shader.Find hack — the FBX materials import into Unity URP as-is.
#
# FBX export: forward -Z, up Y, apply unit scale, so the model drops into
# Unity at the same spot and scale as the primitive it replaces.

import os

import bpy

# ---------------------------------------------------------------------------
# Output path: ../../Resources/Models/ResearchLab.fbx relative to this
# script, wherever it runs.
# ---------------------------------------------------------------------------
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "..", "Resources", "Models", "ResearchLab.fbx"))

# ---------------------------------------------------------------------------
# Colours — same values as BaseView.cs uses for the Research Lab primitive.
# Blender material colours are linear RGB; these match closely enough that
# the model reads as the same building in-game.
# ---------------------------------------------------------------------------
LAB_PURPLE = (0.55, 0.30, 0.85)   # main tower, annex
LAB_DARK = (0.30, 0.18, 0.45)     # base platform, dome, vent (dark purple)
LAB_GUN = (0.25, 0.25, 0.28)      # dish, antenna (dark grey)
LAB_LIGHT = (0.90, 0.88, 0.80)    # doorway (pale)
LAB_GLOW = (0.70, 0.95, 1.00)     # window band, antenna tip (glowing pale cyan)


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
    """Add a UV sphere, optionally squashed along an axis."""
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=radius, location=location,
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def add_cone(name, vertices, radius1, radius2, depth, location, material, rotation=(0.0, 0.0, 0.0)):
    """Add a cone (Z-aligned before rotation); radius1 is the bottom end."""
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=radius1, radius2=radius2, depth=depth,
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

    purple = make_material("LabPurple", LAB_PURPLE, metallic=0.15, roughness=0.55)
    dark = make_material("LabDark", LAB_DARK, metallic=0.25, roughness=0.55)
    gun = make_material("LabGun", LAB_GUN, metallic=0.60, roughness=0.40)
    light = make_material("LabLight", LAB_LIGHT, metallic=0.10, roughness=0.45)
    glow = make_material("LabGlow", LAB_GLOW, metallic=0.10, roughness=0.50, emission=0.9)

    # Dark base platform: 2.2 x 2.2, 0.12 tall, sits on the ground.
    add_box("Lab_Base", (2.20, 2.20, 0.12), (0.0, 0.0, 0.06), dark)
    # Main cylindrical tower: 12-sided, radius 0.75, 1.45 tall,
    # from z = 0.12 to z = 1.57.
    add_cylinder("Lab_Tower", 12, 0.75, 1.45, (0.0, 0.0, 0.845), purple)
    # Glowing pale-cyan window band ringing the tower: radius 0.78 (proud
    # of the tower by 0.03 all round, so it reads from every angle),
    # 0.28 tall, centred at z = 1.25.
    add_cylinder("Lab_WindowBand", 12, 0.78, 0.28, (0.0, 0.0, 1.25), glow)
    # Dark domed roof: squashed sphere, radius 0.75, z-scale 0.4, centred
    # at the tower top (z = 1.57), so the dome runs from z = 1.57 to
    # z = 1.87 (its lower half is hidden inside the tower).
    add_sphere("Lab_Dome", 12, 8, 0.75, (0.0, 0.0, 1.57), dark, scale=(1.0, 1.0, 0.4))
    # Side annex on the +X face: 0.6 x 0.9 x 0.9, from z = 0.12 to
    # z = 1.02, flush with the base platform edge at x = 1.1.
    add_box("Lab_Annex", (0.60, 0.90, 0.90), (0.80, 0.15, 0.57), purple)
    # Pale doorway on the camera-facing (-Y, becomes Unity +Z) face of the
    # tower: 0.5 wide, 0.7 tall, just proud of the wall (y = -0.76 vs the
    # tower surface at y = -0.75 at the centre line), z = 0.12 to 0.82.
    add_box("Lab_Door", (0.50, 0.05, 0.70), (0.0, -0.76, 0.47), light)
    # Dark vent on the +X face of the tower: 0.06 thick in x, 0.3 x 0.3,
    # centred at (0.76, 0.30, 1.45), proud of the tower surface there.
    add_box("Lab_Vent", (0.06, 0.30, 0.30), (0.76, 0.30, 1.45), dark)
    # Satellite dish on the annex roof: a short pole, then a flared cone
    # (narrow end down, wide end up) tilted ~52 deg toward the
    # camera-facing (-Y) side. Pole from z = 1.02 to z = 1.27.
    add_cylinder("Lab_DishPole", 8, 0.04, 0.25, (0.80, 0.15, 1.145), gun)
    # Dish: radius 0.06 at the bottom, 0.32 at the top, 0.22 deep, centred
    # at (0.80, 0.05, 1.35); rotation about X by +0.9 rad tilts the
    # opening toward -Y (the camera side).
    add_cone("Lab_Dish", 12, 0.06, 0.32, 0.22, (0.80, 0.05, 1.35), gun,
             rotation=(0.9, 0.0, 0.0))
    # Thin antenna on the dome, off-centre: radius 0.035, 0.28 tall, from
    # z = 1.87 to z = 2.15.
    add_cylinder("Lab_Antenna", 8, 0.035, 0.28, (-0.30, 0.20, 2.01), gun)
    # Glowing tip on the antenna: small sphere, radius 0.06, at z = 2.18,
    # the model's high point (~2.24).
    add_sphere("Lab_Tip", 8, 6, 0.06, (-0.30, 0.20, 2.18), glow)

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
    print("Research Lab build:")
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
