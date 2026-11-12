# BuildMiniTroops.py — Lunar Dominion MAP PHASE 2 (Patrick approved
# 2026-11-12: "Model real mini troops in Blender first")
#
# Builds the three mini troop figures that replace the single cone
# march marker: one per unit class, authored at FINAL map size
# (~2.2 units tall; the code instantiates them at 1x) with chunky
# silhouettes and strong class colours so they read from the map
# camera ~95 units away:
#   MiniInfantry.fbx  steel-blue trooper, chest plate, cyan visor
#   MiniRanged.fbx    gunmetal marksman, hood, long rail rifle
#                     with 3 glowing orange coils
#   MiniMech.fbx      green 4-leg walker, twin side cannons,
#                     cyan eye slit
# Heroes are NOT built here — marching heroes reuse the existing
# full hero models (HeroReyes/Vega/Okafor) at map scale.
#
# Coloured materials only (no PNG textures). All models face +X
# (the code yaws a wrapper to the march direction, same pattern as
# the hero/A.R.E.S. models).
#
# Run: open in Blender (Scripting workspace), Run Script.
# Outputs land next to this script. Install: copy the 3 FBXs into
# Assets/_Project/Resources/Models/ (new files, no rename needed).
#
# Blender 5.2 GUI quirk (Patrick's setup, hit twice 2026-11-12):
# context reads (bpy.context.active_object / selected_objects)
# can raise AttributeError in Text Editor runs, and
# bpy.ops.object.transform_apply goes through the same context.
# This script therefore never reads the context: created objects
# are found via bpy.data before/after diffs, scale is baked into
# mesh data directly, and exports use use_selection=False with
# the scene holding only the model being exported.

import bpy
import math
import os
from mathutils import Matrix, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

OBJECTS = []


def mat(name, color, emission=None, strength=0.0, roughness=0.85, metallic=0.0):
    m = bpy.data.materials.new(name=name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength
    return m


# Materials are created by init_materials() AFTER the factory
# reset in main() — creating them at module level leaves stale
# references once read_factory_settings reloads the file.
STEEL = STEEL_LIGHT = DARK = GUNMETAL = None
ORANGE_PAINT = ORANGE_GLOW = CYAN_GLOW = None
MECH_GREEN = MECH_DARK = GREEN_GLOW = FACE_DARK = None


def init_materials():
    global STEEL, STEEL_LIGHT, DARK, GUNMETAL, ORANGE_PAINT
    global ORANGE_GLOW, CYAN_GLOW, MECH_GREEN, MECH_DARK
    global GREEN_GLOW, FACE_DARK
    STEEL = mat("TroopSteel", (0.36, 0.48, 0.66), roughness=0.5, metallic=0.35)
    STEEL_LIGHT = mat("TroopSteelLight", (0.55, 0.65, 0.80), roughness=0.45, metallic=0.35)
    DARK = mat("TroopDark", (0.10, 0.11, 0.13), roughness=0.8)
    GUNMETAL = mat("TroopGunmetal", (0.16, 0.17, 0.20), roughness=0.45, metallic=0.5)
    ORANGE_PAINT = mat("TroopOrange", (0.85, 0.38, 0.08), roughness=0.5)
    ORANGE_GLOW = mat("TroopOrangeGlow", (0.45, 0.18, 0.04), emission=(1.0, 0.45, 0.10), strength=2.0, roughness=0.4)
    CYAN_GLOW = mat("TroopCyanGlow", (0.35, 0.85, 0.95), emission=(0.30, 0.95, 1.0), strength=2.2, roughness=0.3)
    MECH_GREEN = mat("TroopMechGreen", (0.32, 0.42, 0.30), roughness=0.55, metallic=0.3)
    MECH_DARK = mat("TroopMechDark", (0.14, 0.16, 0.14), roughness=0.7, metallic=0.3)
    GREEN_GLOW = mat("TroopGreenGlow", (0.15, 0.45, 0.20), emission=(0.30, 1.0, 0.45), strength=2.0, roughness=0.4)
    FACE_DARK = mat("TroopFaceDark", (0.03, 0.03, 0.04), roughness=1.0)


def _finish(obj, material, flat=False):
    if material is not None:
        obj.data.materials.append(material)
    if flat:
        for p in obj.data.polygons:
            p.use_smooth = False
    OBJECTS.append(obj)
    return obj


def _snapshot():
    return set(bpy.data.objects)


def _created(before):
    # The object a primitive-add just made, found through bpy.data
    # (never the context — see the header note).
    new = [o for o in bpy.data.objects if o not in before]
    return new[0]


def _bake_scale(obj):
    # transform_apply without the operator: bake the object's
    # scale into its mesh data along the local axes.
    sx, sy, sz = obj.scale
    if (sx, sy, sz) != (1.0, 1.0, 1.0):
        obj.data.transform(Matrix(((sx, 0, 0, 0), (0, sy, 0, 0), (0, 0, sz, 0), (0, 0, 0, 1))))
        obj.scale = (1.0, 1.0, 1.0)


def box(name, loc, scale, material, rot=(0.0, 0.0, 0.0)):
    before = _snapshot()
    bpy.ops.mesh.primitive_cube_add(location=loc)
    obj = _created(before)
    obj.name = name
    obj.scale = scale
    _bake_scale(obj)
    obj.rotation_euler = rot
    return _finish(obj, material)


def ico(name, loc, scale, material, subdiv=1):
    before = _snapshot()
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdiv, radius=1.0, location=loc)
    obj = _created(before)
    obj.name = name
    obj.scale = scale
    _bake_scale(obj)
    return _finish(obj, material, flat=True)


def cyl(name, loc, scale, material, rot=(0.0, 0.0, 0.0), vertices=12):
    before = _snapshot()
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=1.0, depth=1.0, location=loc)
    obj = _created(before)
    obj.name = name
    obj.scale = scale
    _bake_scale(obj)
    obj.rotation_euler = rot
    return _finish(obj, material)


def cone(name, loc, scale, material, rot=(0.0, 0.0, 0.0), vertices=12):
    before = _snapshot()
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=1.0, radius2=0.0, depth=1.0, location=loc)
    obj = _created(before)
    obj.name = name
    obj.scale = scale
    _bake_scale(obj)
    obj.rotation_euler = rot
    return _finish(obj, material, flat=True)


D = math.radians


def build_infantry():
    for side, tag in ((1.0, "L"), (-1.0, "R")):
        y = 0.15 * side
        box("Leg" + tag, (0, y, 0.42), (0.13, 0.10, 0.42), DARK)
        box("Shin" + tag, (0.06, y, 0.30), (0.05, 0.11, 0.28), STEEL)
        box("Boot" + tag, (0.04, y, 0.05), (0.17, 0.11, 0.05), DARK)
        ico("Shoulder" + tag, (0, 0.36 * side, 1.62), (0.15, 0.15, 0.15), STEEL)
        box("Arm" + tag, (0.10, 0.38 * side, 1.28), (0.09, 0.08, 0.30), DARK, rot=(0.0, D(-28), 0.0))
    box("Pelvis", (0, 0, 0.95), (0.16, 0.19, 0.11), STEEL)
    box("Torso", (0, 0, 1.36), (0.21, 0.27, 0.34), STEEL)
    box("ChestPlate", (0.20, 0, 1.40), (0.05, 0.21, 0.27), STEEL_LIGHT)
    box("ChestLight", (0.26, 0, 1.48), (0.015, 0.06, 0.03), CYAN_GLOW)
    box("Backpack", (-0.26, 0, 1.38), (0.09, 0.20, 0.30), DARK)
    box("BackCell", (-0.36, 0, 1.45), (0.02, 0.10, 0.16), CYAN_GLOW)
    box("Rifle", (0.34, -0.10, 1.22), (0.52, 0.05, 0.07), GUNMETAL, rot=(0.0, D(-4), 0.0))
    box("RifleTip", (0.84, -0.13, 1.25), (0.06, 0.03, 0.03), CYAN_GLOW)
    ico("Helmet", (0, 0, 1.95), (0.20, 0.20, 0.22), STEEL)
    box("Visor", (0.155, 0, 1.97), (0.05, 0.13, 0.045), CYAN_GLOW)
    box("Crest", (0, 0, 2.16), (0.17, 0.02, 0.06), STEEL_LIGHT)


def build_ranged():
    for side, tag in ((1.0, "L"), (-1.0, "R")):
        y = 0.13 * side
        box("Leg" + tag, (0, y, 0.42), (0.11, 0.09, 0.42), DARK)
        box("Boot" + tag, (0.04, y, 0.05), (0.15, 0.10, 0.05), DARK)
        ico("Shoulder" + tag, (0, 0.30 * side, 1.60), (0.13, 0.13, 0.13), GUNMETAL)
    box("ArmL", (0.12, 0.30, 1.30), (0.08, 0.07, 0.28), DARK, rot=(0.0, D(-32), 0.0))
    box("ArmR", (0.16, -0.30, 1.32), (0.08, 0.07, 0.28), DARK, rot=(0.0, D(-40), 0.0))
    box("Pelvis", (0, 0, 0.95), (0.14, 0.16, 0.11), GUNMETAL)
    box("Torso", (0, 0, 1.36), (0.18, 0.22, 0.34), GUNMETAL)
    box("ChestStripe", (0.175, 0, 1.44), (0.02, 0.16, 0.05), ORANGE_PAINT)
    box("Backpack", (-0.23, 0, 1.38), (0.08, 0.17, 0.28), DARK)
    cyl("Antenna", (-0.28, 0.10, 1.85), (0.012, 0.012, 0.30), DARK, vertices=6)
    ico("AntennaTip", (-0.28, 0.10, 2.16), (0.035, 0.035, 0.035), ORANGE_GLOW)
    box("RailRifle", (0.48, -0.06, 1.34), (0.72, 0.045, 0.06), GUNMETAL)
    for i, cx in enumerate((0.30, 0.55, 0.80)):
        cyl("Coil%d" % i, (cx, -0.06, 1.34), (0.085, 0.085, 0.03), ORANGE_GLOW, rot=(0.0, D(90), 0.0))
    box("Scope", (0.30, -0.06, 1.48), (0.14, 0.04, 0.05), DARK)
    cone("Hood", (0, 0, 1.97), (0.26, 0.26, 0.22), GUNMETAL)
    box("Face", (0.10, 0, 1.88), (0.08, 0.11, 0.09), FACE_DARK)
    box("Eye", (0.185, 0, 1.90), (0.02, 0.05, 0.02), ORANGE_GLOW)


def build_mech():
    box("Hull", (0, 0, 1.30), (0.58, 0.44, 0.32), MECH_GREEN)
    box("HullTop", (-0.05, 0, 1.66), (0.40, 0.32, 0.08), MECH_DARK)
    box("NosePlate", (0.56, 0, 1.22), (0.10, 0.36, 0.26), MECH_GREEN, rot=(0.0, D(-14), 0.0))
    box("EyeSlit", (0.665, 0, 1.38), (0.02, 0.24, 0.045), CYAN_GLOW)
    box("AmmoBox", (-0.62, 0, 1.30), (0.10, 0.30, 0.24), MECH_DARK)
    box("HatchLight", (-0.05, 0, 1.75), (0.10, 0.10, 0.02), GREEN_GLOW)
    for sx, tx in ((1.0, "F"), (-1.0, "B")):
        for sy, ty in ((1.0, "L"), (-1.0, "R")):
            x = 0.38 * sx
            y = 0.40 * sy
            box("UpperLeg%s%s" % (tx, ty), (x, y * 1.15, 0.92), (0.11, 0.10, 0.34), MECH_GREEN, rot=(D(14) * sy, 0.0, 0.0))
            box("LowerLeg%s%s" % (tx, ty), (x, y * 1.32, 0.40), (0.09, 0.08, 0.38), MECH_DARK)
            box("Foot%s%s" % (tx, ty), (x + 0.03, y * 1.38, 0.04), (0.16, 0.12, 0.04), DARK)
    for side, tag in ((1.0, "L"), (-1.0, "R")):
        y = 0.52 * side
        cyl("Cannon" + tag, (0.30, y, 1.42), (0.07, 0.07, 0.38), GUNMETAL, rot=(0.0, D(90), 0.0))
        cyl("Muzzle" + tag, (0.70, y, 1.42), (0.045, 0.045, 0.05), GREEN_GLOW, rot=(0.0, D(90), 0.0))


def export_model(filename):
    path = os.path.join(SCRIPT_DIR, filename)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=False,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        path_mode="STRIP",
    )
    print("Wrote", path)


def wipe():
    global OBJECTS
    for obj in OBJECTS:
        bpy.data.objects.remove(obj, do_unlink=True)
    OBJECTS = []
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)


def verify(tag, want_objects, max_z):
    global OBJECTS
    ok = True

    def check(cond, msg):
        nonlocal ok
        print(("  PASS " if cond else "  FAIL ") + msg)
        if not cond:
            ok = False

    check(len(OBJECTS) == want_objects, "%s object count %d (want %d)" % (tag, len(OBJECTS), want_objects))
    # Bounds from the mesh data with a manual location+rotation
    # transform: obj.matrix_world is stale in script runs until a
    # depsgraph update, and scale is already baked into the mesh,
    # so location + Euler rotation is exact for these objects.
    xs, ys, zs = [], [], []
    for obj in OBJECTS:
        rot = obj.rotation_euler.to_matrix()
        for vtx in obj.data.vertices:
            v = obj.location + (rot @ vtx.co)
            xs.append(v.x)
            ys.append(v.y)
            zs.append(v.z)
    check(min(zs) > -0.01, "%s feet on the ground (min z %.2f)" % (tag, min(zs)))
    check(max(zs) < max_z, "%s height %.2f (under %.1f)" % (tag, max(zs), max_z))
    check(max(xs) - min(xs) > 1.0, "%s has real extent (x %.2f..%.2f)" % (tag, min(xs), max(xs)))
    return ok


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    init_materials()
    all_ok = True
    for build, fname, count, max_z in (
        (build_infantry, "MiniInfantry.fbx", 21, 2.6),
        (build_ranged, "MiniRanged.fbx", 22, 2.6),
        (build_mech, "MiniMech.fbx", 22, 2.4),
    ):
        build()
        all_ok = verify(fname, count, max_z) and all_ok
        export_model(fname)
        wipe()
    print("VERIFY:", "ALL PASS" if all_ok else "FAILURES PRESENT")


main()
