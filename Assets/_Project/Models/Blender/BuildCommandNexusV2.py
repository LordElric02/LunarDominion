# BuildCommandNexusV2.py — Lunar Dominion: Command Nexus v2 (graphics-quality pilot).
#
# DRAFT — not yet in the Unity project and NOT pushed to GitHub.
# Patrick's plan (2026-11-05): finish levelling the Tier 3 army first, then
# branch release/0.1 from main, and only then start the graphics work.
# This v2 stays a draft until he says army testing is done and the branch
# is cut. To install it then: copy this file into
#   Assets/_Project/Models/Blender/BuildCommandNexusV2.py
# run it in Blender, and rename the exported CommandNexusV2.fbx to
# CommandNexus.fbx in Assets/_Project/Resources/Models/ (overwriting v1).
#
# v1 (BuildCommandNexus.py): 328 tris, 8 objects, 8-gon tower/base.
# v2 keeps the exact same contract so it is a drop-in replacement:
#   footprint diameter 2.0, total height ~2.89, origin at ground centre,
#   same Nexus blue / dark / light palette, same material approach
#   (Principled BSDF, no textures, no Shader.Find hack).
# What changes: 16-gon tower/base/collar, 24-segment dome, 8 vertical
# panel ribs, 4 base fins, 8 individual emissive window panes, 4 base
# vents, a satellite dish, and 2 emissive running lights — roughly
# 3-4x the geometry, still low-poly and phone-friendly.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildCommandNexusV2.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Written/tested against Blender 4.2.3 LTS here; the v1 scripts were written
# for 4.2, and 4.5 LTS is the closer match if 5.2 throws an error.

import math
import os

import bpy

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# Draft output next to this script. When installed in the repo, change this
# to the v1 path (../../Resources/Models/CommandNexus.fbx relative to the
# Blender folder) or simply rename the exported file to CommandNexus.fbx.
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "CommandNexusV2.fbx")

NEXUS_BLUE = (0.15, 0.35, 0.75)   # main tower body
NEXUS_DARK = (0.22, 0.23, 0.27)   # base platform, collar, antenna
NEXUS_LIGHT = (0.45, 0.72, 1.00)  # dome, window band, ring, antenna tip
NEXUS_PANEL = (0.10, 0.22, 0.48)  # panel ribs / fins — darker painted metal


def make_material(name, rgb, metallic=0.1, roughness=0.6, emission=0.0):
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
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth, location=(0.0, 0.0, z)
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
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=(0.0, 0.0, rotation_z))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
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

    blue = make_material("NexusBlue", NEXUS_BLUE, metallic=0.15, roughness=0.55)
    dark = make_material("NexusDark", NEXUS_DARK, metallic=0.35, roughness=0.50)
    light = make_material(
        "NexusLight", NEXUS_LIGHT, metallic=0.10, roughness=0.40, emission=0.6
    )
    glow = make_material(
        "NexusGlow", NEXUS_LIGHT, metallic=0.05, roughness=0.30, emission=1.4
    )
    panel = make_material("NexusPanel", NEXUS_PANEL, metallic=0.45, roughness=0.45)

    # Core silhouette — same sizes as v1, smoother (16-gon / 24-seg).
    add_cylinder("Nexus_Base", 16, 1.00, 0.25, 0.125, dark)
    add_cylinder("Nexus_Ring", 16, 0.88, 0.06, 0.28, light)
    add_cylinder("Nexus_Tower", 16, 0.68, 1.35, 0.985, blue)
    add_cylinder("Nexus_Windows", 16, 0.71, 0.16, 1.30, light)
    add_cylinder("Nexus_Collar", 16, 0.78, 0.18, 1.75, dark)
    add_sphere("Nexus_Dome", 24, 12, 0.55, (0.0, 0.0, 1.84), 0.65, light)
    add_cylinder("Nexus_Antenna", 6, 0.045, 0.55, 2.475, dark)
    add_sphere("Nexus_Tip", 8, 4, 0.09, (0.0, 0.0, 2.80), 1.0, glow)

    # 8 vertical panel ribs on the tower faces (radial thickness 0.05,
    # tangential width 0.10, height 1.10, centred z=0.99).
    for i in range(8):
        a = i * math.pi / 4.0
        x, y = 0.685 * math.cos(a), 0.685 * math.sin(a)
        add_box("Nexus_Rib_%d" % (i + 1), (x, y, 0.99), a,
                (0.025, 0.05, 0.55), panel)

    # 4 base fins at the diagonals, standing on the base platform.
    for i in range(4):
        a = math.pi / 4.0 + i * math.pi / 2.0
        x, y = 0.72 * math.cos(a), 0.72 * math.sin(a)
        add_box("Nexus_Fin_%d" % (i + 1), (x, y, 0.75), a,
                (0.06, 0.09, 0.45), panel)

    # 8 individual emissive window panes on the window band.
    for i in range(8):
        a = i * math.pi / 4.0
        x, y = 0.705 * math.cos(a), 0.705 * math.sin(a)
        add_box("Nexus_Pane_%d" % (i + 1), (x, y, 1.30), a,
                (0.015, 0.12, 0.06), glow)

    # 4 vents on the base platform, between the fins.
    for i in range(4):
        a = i * math.pi / 2.0
        x, y = 0.55 * math.cos(a), 0.55 * math.sin(a)
        add_box("Nexus_Vent_%d" % (i + 1), (x, y, 0.30), a,
                (0.09, 0.05, 0.03), dark)

    # Satellite dish on the +X side: short arm + tilted cone dish.
    # Arm: thin horizontal cylinder, rotated to lie along X.
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=8, radius=0.03, depth=0.18,
        location=(0.74, 0.0, 1.55), rotation=(0.0, math.pi / 2.0, 0.0)
    )
    arm = bpy.context.active_object
    arm.name = "Nexus_DishArm"
    arm.data.materials.append(dark)
    # Dish: cone opening outward/up, max radius 0.20, tip at the arm.
    bpy.ops.mesh.primitive_cone_add(
        vertices=16, radius1=0.18, radius2=0.02, depth=0.12,
        location=(0.86, 0.0, 1.60), rotation=(0.0, -math.pi / 3.0, 0.0)
    )
    dish = bpy.context.active_object
    dish.name = "Nexus_Dish"
    dish.data.materials.append(panel)

    # 2 emissive running lights on the collar sides.
    add_sphere("Nexus_Light_L", 8, 4, 0.05, (0.79, 0.0, 1.75), 1.0, glow)
    add_sphere("Nexus_Light_R", 8, 4, 0.05, (-0.79, 0.0, 1.75), 1.0, glow)

    return list(bpy.context.scene.objects)


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
        path_mode="AUTO",
        embed_textures=False,
    )


def report(objects):
    total_tris = 0
    print("Command Nexus v2 build:")
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
    # Bounding box check: footprint must stay within radius 1.0, height ~2.89.
    # Bounding box check in world space: footprint must stay within
    # radius 1.0, height ~2.89, base on the ground (z >= 0).
    pts = [o.matrix_world @ v.co for o in objects if o.type == "MESH"
           for v in o.data.vertices]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    print("  bounds x=[%.2f, %.2f] y=[%.2f, %.2f] z=[%.2f, %.2f]"
          % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    print("  exported: %s" % OUTPUT_FBX)


if __name__ == "__main__":
    built = build()
    export(built)
    report(built)
