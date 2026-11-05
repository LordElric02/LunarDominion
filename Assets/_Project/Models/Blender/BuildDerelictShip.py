# BuildDerelictShip.py — Lunar Dominion: low-poly derelict spaceship model.
#
# Builds the model from scratch in Blender and exports it as FBX for Unity.
# This script is the source of truth for the model: re-running it regenerates
# Assets/_Project/Resources/Models/DerelictShip.fbx, following the same
# pattern as BuildResearchLab.py (copy this file, change the parts, keep
# the helpers).
# Note: the FBX lives under Resources/ on purpose — BaseView loads it at
# runtime with Resources.Load, which only works for assets under a Resources
# folder in a built player (Android/iOS).
#
# How to re-run (headless, no Blender UI needed):
#   blender --background --python BuildDerelictShip.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Blender used here: 4.2.3 LTS (any Blender 4.x should work).
#
# Design (all dimensions in Blender units; 1 Blender unit = 1 Unity unit):
#   Origin at ground centre under the hull, Z is up in Blender (exported
#   as Y-up for Unity). The ship is built along the X axis, nose at +X,
#   ~5.3 units long, ~2.2 units tall — it is scenery in the distance, so
#   it is bigger than a building on purpose. It is placed by BaseView at
#   Unity (-13, 0, -11) with a -45 degree yaw so its nose points toward
#   the camera side.
#   Parts: tapered main hull (wide tail, narrow nose), dark cockpit dome
#   at the front, engine block + nozzle at the back, one intact swept
#   wing (+Y side) and one broken wing stub (-Y side), a tail fin, two
#   dark scorch patches, a pale accent panel and a rust panel on the
#   camera-facing (-Y, becomes Unity +Z) side, and a regolith mound
#   piled under the tail where the crash dug in.
#   The crash pose — nose down, tail up — is baked INTO the geometry:
#   after building the ship level, every part is rotated 12 degrees
#   about the Blender Y axis through the world origin (nose +X drops
#   toward the ground, tail rises). The Unity placement adds only the
#   -45 degree yaw, never a second pitch. With the hull centre at
#   z = 0.9 the nose tip ends up ~0.1 above the ground and the tail
#   ~2.2 up, so the wreck rests on its nose and belly.
#   Camera-facing note (learned on the Extractor/Harvester): the game's
#   isometric camera sits at Unity (9,11,9), i.e. the +X/+Z side. Blender
#   -Y maps to Unity +Z and Blender +X maps to Unity +X on export, so
#   visible details (cockpit, accent panels, intact wing) go on the
#   Blender -Y and/or +X faces, NOT on Blender +Y.
#   Low-poly on purpose: a few hundred triangles, readable at phone size
#   from ~30 units away. Materials are Principled BSDF with base colours
#   only; no textures, no Shader.Find hack — the FBX materials import
#   into Unity URP as-is.
#
# FBX export: forward -Z, up Y, apply unit scale, so the model drops into
# Unity at the same spot and scale as the primitive fallback in BaseView.

import math
import os

import bpy
from mathutils import Matrix

# ---------------------------------------------------------------------------
# Output path: ../../Resources/Models/DerelictShip.fbx relative to this
# script, wherever it runs.
# ---------------------------------------------------------------------------
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "..", "Resources", "Models", "DerelictShip.fbx"))

# ---------------------------------------------------------------------------
# Colours — a dead ship: dark greys, rust where it broke, one pale panel
# left. Blender material colours are linear RGB.
# ---------------------------------------------------------------------------
SHIP_HULL = (0.32, 0.33, 0.36)    # main hull (dark grey)
SHIP_DARK = (0.15, 0.15, 0.18)    # engine block, nozzle, fin, scorch
SHIP_RUST = (0.75, 0.38, 0.12)    # broken stub, nozzle ring, rust panel
SHIP_PALE = (0.85, 0.85, 0.80)    # accent panel (pale)
SHIP_GLASS = (0.08, 0.10, 0.16)   # cockpit dome (near-black blue)
SHIP_DIRT = (0.28, 0.25, 0.22)    # regolith mound under the tail

# Nose-down tilt baked into the geometry (degrees about Blender Y).
CRASH_TILT_DEG = 12.0


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

    hull = make_material("ShipHull", SHIP_HULL, metallic=0.45, roughness=0.50)
    dark = make_material("ShipDark", SHIP_DARK, metallic=0.55, roughness=0.45)
    rust = make_material("ShipRust", SHIP_RUST, metallic=0.30, roughness=0.65)
    pale = make_material("ShipPale", SHIP_PALE, metallic=0.20, roughness=0.45)
    glass = make_material("ShipGlass", SHIP_GLASS, metallic=0.70, roughness=0.25)
    dirt = make_material("ShipDirt", SHIP_DIRT, metallic=0.05, roughness=0.90)

    # Main hull: tapered cone, 8-sided, wide tail (radius 0.78) to narrow
    # nose (radius 0.34), 4.6 long, centre at z = 0.9. Rotation about Y
    # by +90 deg lays the cone along X with the small end (+Z, radius2)
    # pointing at +X, the nose.
    add_cone("Ship_Hull", 8, 0.78, 0.34, 4.6, (0.0, 0.0, 0.9), hull,
             rotation=(0.0, math.pi / 2.0, 0.0))
    # Cockpit dome: squashed dark sphere on top of the nose, at (1.55, 0,
    # 1.42), radius 0.42, z-scale 0.55 — reads as the dead cockpit.
    add_sphere("Ship_Cockpit", 8, 6, 0.42, (1.55, 0.0, 1.42), glass,
               scale=(1.2, 1.0, 0.55))
    # Engine block at the tail: 0.9 x 1.3 x 1.1, centred (-2.35, 0, 1.0).
    add_box("Ship_EngineBlock", (0.90, 1.30, 1.10), (-2.35, 0.0, 1.0), dark)
    # Engine nozzle: 8-sided cylinder, radius 0.52, 0.45 deep, laid along
    # X at (-2.95, 0, 1.0), plus a thin rust ring at its back end.
    add_cylinder("Ship_Nozzle", 8, 0.52, 0.45, (-2.95, 0.0, 1.0), dark,
                 rotation=(0.0, math.pi / 2.0, 0.0))
    add_cylinder("Ship_NozzleRing", 8, 0.54, 0.08, (-3.15, 0.0, 1.0), rust,
                 rotation=(0.0, math.pi / 2.0, 0.0))
    # Intact swept wing, +Y side: 1.7 x 2.3 x 0.12 at (0.1, 1.45, 0.85),
    # rotated +25 deg about Z so the tip sweeps back toward the tail.
    add_box("Ship_Wing", (1.70, 2.30, 0.12), (0.10, 1.45, 0.85), hull,
            rotation=(0.0, 0.0, 0.45))
    # Broken wing stub, -Y side: short 0.8 x 0.9 x 0.12 at (-0.2, -0.85,
    # 0.9), rust — the other wing snapped off in the crash.
    add_box("Ship_WingStub", (0.80, 0.90, 0.12), (-0.20, -0.85, 0.90), rust,
            rotation=(0.0, 0.0, -0.25))
    # Tail fin: thin vertical box 1.0 x 0.12 x 0.9 at (-1.9, 0, 1.7).
    add_box("Ship_TailFin", (1.00, 0.12, 0.90), (-1.90, 0.0, 1.70), dark)
    # Scorch patches: thin dark slabs half-sunk into the hull — one on
    # the camera-facing (-Y) side, one across the top ridge.
    add_box("Ship_ScorchSide", (1.40, 0.05, 0.40), (0.30, -0.52, 1.15), dark)
    add_box("Ship_ScorchTop", (1.20, 0.50, 0.06), (-0.80, 0.0, 1.60), dark)
    # Pale accent panel on the camera-facing (-Y) side of the hull.
    add_box("Ship_Panel", (2.20, 0.05, 0.28), (0.10, -0.55, 1.05), pale)
    # Rust panel on the +Y side of the hull.
    add_box("Ship_RustPanel", (1.00, 0.05, 0.30), (-1.20, 0.55, 1.10), rust)
    # Regolith mound piled under the tail where the crash dug in:
    # squashed sphere, radius 0.8, at (-1.8, 0, 0.15).
    add_sphere("Ship_CrashMound", 8, 6, 0.80, (-1.80, 0.0, 0.15), dirt,
               scale=(1.4, 1.2, 0.35))

    objects = list(bpy.context.scene.objects)

    # Bake the crash pose into the geometry: rotate every part 12 degrees
    # about the Blender Y axis through the world origin. R_y(+12deg)
    # sends +X (the nose) toward -Z (down), so the nose drops and the
    # tail rises. The rotation is applied to each object's matrix and
    # then baked into its mesh, so the exported FBX has no root rotation
    # — Unity only adds the -45 degree yaw at placement.
    tilt = Matrix.Rotation(math.radians(CRASH_TILT_DEG), 4, "Y")
    for obj in objects:
        if obj.type != "MESH":
            continue
        obj.matrix_world = tilt @ obj.matrix_world
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    bpy.ops.object.select_all(action="DESELECT")

    return objects


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
    print("Derelict Ship build:")
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
