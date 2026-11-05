# BuildNovaArsenal.py — Lunar Dominion: low-poly Nova Arsenal model.
#
# Builds the model from scratch in Blender and exports it as FBX for Unity.
# This script is the source of truth for the model: re-running it regenerates
# Assets/_Project/Resources/Models/NovaArsenal.fbx, following the same
# pattern as BuildMarineFoundry.py / BuildHeliumHarvester.py (copy this file,
# change the parts, keep the helpers).
# Note: the FBX lives under Resources/ on purpose — BaseView loads it at
# runtime with Resources.Load, which only works for assets under a Resources
# folder in a built player (Android/iOS).
#
# How to re-run (headless, no Blender UI needed):
#   blender --background --python BuildNovaArsenal.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Blender used here: 4.2.3 LTS (any Blender 4.x should work).
#
# Design (all dimensions in Blender units; 1 Blender unit = 1 Unity unit):
#   Origin at ground centre, Z is up in Blender (exported as Y-up for Unity).
#   Footprint ~2 x 2 units, total height ~1.84 units, matching the red cube
#   primitive the Arsenal replaces in BaseView.cs (scale 2 x 1.4 x 2 at
#   position (4, 0, -3.5)). Colours match BaseView's actual primitive colour
#   (0.85, 0.35, 0.20) for the main block. The extra height over the 1.4
#   primitive is the targeting spire and the angled cannon barrel on the
#   roof — the block itself stays at the primitive's height.
#   Parts: dark base platform, main red arsenal block with a sloped pyramid
#   roof, a tall targeting spire with a glowing tip on the front corner, an
#   angled cannon/railgun barrel on the roof, a pale doorway and a glowing
#   sight strip on the camera-facing face, and an ammo crate plus a vent on
#   the +X face. Deliberately distinct from the Marine Foundry next door:
#   the Foundry is a flat-roofed green barracks with windows and a flag;
#   the Arsenal is a red weapons building with a pyramid roof, a spire
#   and a cannon barrel.
#   Camera-facing note (learned on the Extractor/Harvester): the game's
#   isometric camera sits at Unity (9,11,9), i.e. the +X/+Z side. Blender
#   -Y maps to Unity +Z and Blender +X maps to Unity +X on export, so
#   visible details (doorway, sight strip, spire, barrel, crate, vent) go
#   on the Blender -Y and/or +X faces, NOT on Blender +Y. This matters most
#   for the Arsenal: it sits at (4,-3.5), on the far side of the base from
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
# Output path: ../../Resources/Models/NovaArsenal.fbx relative to this
# script, wherever it runs.
# ---------------------------------------------------------------------------
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "..", "Resources", "Models", "NovaArsenal.fbx"))

# ---------------------------------------------------------------------------
# Colours — same values as BaseView.cs uses for the Nova primitive.
# Blender material colours are linear RGB; these match closely enough that
# the model reads as the same building in-game.
# ---------------------------------------------------------------------------
NOVA_RED = (0.85, 0.35, 0.20)    # main arsenal block
NOVA_DARK = (0.35, 0.15, 0.12)   # base platform, roof, mount, vent
NOVA_GUN = (0.25, 0.25, 0.28)    # spire, barrel, ammo crate (dark grey)
NOVA_LIGHT = (0.90, 0.85, 0.70)  # doorway (pale)
NOVA_GLOW = (1.00, 0.55, 0.15)   # spire tip, sight strip (orange, glowing)


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
    """Add a cone (radius2 = 0, Z-aligned before rotation)."""
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

    red = make_material("NovaRed", NOVA_RED, metallic=0.25, roughness=0.55)
    dark = make_material("NovaDark", NOVA_DARK, metallic=0.40, roughness=0.55)
    gun = make_material("NovaGun", NOVA_GUN, metallic=0.60, roughness=0.40)
    light = make_material("NovaLight", NOVA_LIGHT, metallic=0.10, roughness=0.45)
    glow = make_material("NovaGlow", NOVA_GLOW, metallic=0.10, roughness=0.50, emission=0.9)

    # Dark base platform: 2.0 x 2.0, 0.12 tall, sits on the ground.
    add_box("Nova_Base", (2.00, 2.00, 0.12), (0.0, 0.0, 0.06), dark)
    # Main arsenal block: 1.5 x 1.3 x 0.9, from z = 0.12 to z = 1.02.
    add_box("Nova_Block", (1.50, 1.30, 0.90), (0.0, 0.0, 0.57), red)
    # Sloped pyramid roof (4-sided cone, rotated 45 deg so its square base
    # lines up with the block): base ~1.49 x 1.49, 0.35 tall, apex at
    # z = 1.37. The rotation is needed: the default 4-vertex cone has its
    # vertices on the axes, i.e. a diamond that overhangs the block's faces
    # (caught in a preview render without the rotation). With the rotation
    # the square's edges run parallel to the block's, true extent +/-0.74.
    add_cone("Nova_Roof", 4, 1.05, 0.35, (0.0, 0.0, 1.195), dark,
             rotation=(0.0, 0.0, 0.7853981633974483))
    # Tall targeting spire on the front-right (+X, -Y) corner of the roof:
    # radius 0.10, 0.70 tall, from z = 1.02 to z = 1.72.
    add_cylinder("Nova_Spire", 8, 0.10, 0.70, (0.55, -0.45, 1.37), gun)
    # Glowing orange tip cone on the spire: 0.07 radius, 0.12 tall, apex
    # at z = 1.84, the model's high point.
    add_cone("Nova_Tip", 4, 0.07, 0.12, (0.55, -0.45, 1.78), glow)
    # Cannon/railgun barrel on the roof, angled ~34 deg up toward the
    # camera-facing (-Y) side: radius 0.10, 1.1 long, centre at z = 1.35,
    # so its muzzle clears the roof and reads at phone size. Positive X
    # rotation tilts the top of the Z-aligned cylinder toward -Y.
    add_cylinder("Nova_Barrel", 12, 0.10, 1.10, (0.0, -0.10, 1.35), gun,
                 rotation=(0.6, 0.0, 0.0))
    # Dark mount block under the barrel's base end, on the roof.
    add_box("Nova_Mount", (0.30, 0.30, 0.20), (0.0, 0.15, 1.10), dark)
    # Pale doorway on the camera-facing (-Y, becomes Unity +Z) face of the
    # main block: 0.4 wide, 0.6 tall, just proud of the wall (y = -0.67
    # vs block face at y = -0.65), from z = 0.12 to z = 0.72.
    add_box("Nova_Door", (0.40, 0.06, 0.60), (0.10, -0.67, 0.42), light)
    # Glowing orange sight strip on the same camera-facing face, above and
    # to the left of the door, at z = 0.85.
    add_box("Nova_Sight", (0.30, 0.05, 0.08), (-0.35, -0.67, 0.85), glow)
    # Ammo crate on the +X (camera-facing) face of the main block: 0.28
    # wide, sticking out to x = 0.99, inside the 2.0 base platform.
    add_box("Nova_Crate", (0.28, 0.30, 0.30), (0.85, 0.25, 0.27), gun)
    # Dark vent grille on the +X face of the main block, above the crate:
    # 0.05 thick, 0.30 wide, 0.20 tall, at z = 0.80.
    add_box("Nova_Vent", (0.05, 0.30, 0.20), (0.77, -0.25, 0.80), dark)

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
    print("Nova Arsenal build:")
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
