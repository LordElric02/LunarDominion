# BuildHeliumHarvesterV5.py — Lunar Dominion: Helium-3 Harvester v5
# (graphics upgrade: v2 + surface detail + finer textures).
#
# v5 = v2 (detailed geometry + baked panel textures) + a new section
# of greebles (section "v5 greebles" in build()): 2 more vent grilles
# on the plinth, 2 junction boxes on the tank, a third vertical pipe
# on the tank -X face, 2 more condensation coils, a second gauge
# panel on the tank +X face, a vent on the cooling tower, 2 hazard
# bars on the plinth, an extra canister on the transport pad, 2 more
# glowing bollards, and a pipe from the pump station to the tank.
# The v2 geometry is untouched (including the dish face-flip fix —
# do not touch the dish), so the drop-in contract is unchanged:
# base platform 1.4 x 1.4, footprint x/y within +/-0.85 (dish rim),
# z 0-2.42 (orb top). Textures are finer: 32 px panel cells + a
# faint 16 px sub-grid (was 48 px).
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildHeliumHarvesterV5.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Written/tested against Blender 4.2.3 LTS here; uses no deprecated APIs,
# so it should also run on Blender 5.2 LTS.
# NOTE: the script exports the FBX + PNGs next to wherever the script file
# was opened from (SCRIPT_DIR). If you open it from F:\ or Downloads, the
# files land there — check the "exported:" lines in the console / report.

import math
import os
import struct
import zlib

import bpy
import bmesh
import numpy as np

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# Draft outputs next to this script. When installed in the repo, rename the
# exported FBX to HeliumHarvester.fbx and move the PNGs next to it in
# Assets/_Project/Resources/Models/.
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "HeliumHarvesterV5.fbx")
ATLAS_SIZE = 1024
TEXTURE_NAMES = {
    "HeliumTitanium": "HeliumHarvesterV5_Titanium.png",
    "HeliumGunmetal": "HeliumHarvesterV5_Gunmetal.png",
    "HeliumDarkAlloy": "HeliumHarvesterV5_DarkAlloy.png",
    "HeliumCyan": "HeliumHarvesterV5_Cyan.png",
}

# Palette: v1 Harvester colours + spec section 4 metals (linear RGB).
TITANIUM = (0.78, 0.82, 0.85)      # #C6D0DA primary armor
GUNMETAL = (0.27, 0.33, 0.39)      # #465363
DARK_ALLOY = (0.09, 0.15, 0.22)    # #172638 recesses
HE3_CYAN = (0.35, 0.82, 0.90)      # v1 tank / dome / rim cyan
HE3_DARK = (0.25, 0.28, 0.32)      # v1 dark (base, dish, pipes, stem)
HE3_PALE = (0.85, 0.92, 0.92)      # gauge face
CYAN_GLOW = (0.25, 0.75, 1.00)    # saturated cyan glow (dome lesson)

# Layout constants (Blender units; 1 unit = 1 Unity unit).
BASE_HALF = 0.70
TANK_R = 0.55
DISH_R = 0.85
MAX_Z = 2.42  # v1 orb top; nothing in v2 goes above this.


def make_material(name, rgb, metallic=0.1, roughness=0.6, emission=0.0,
                  emission_rgb=None, alpha=1.0):
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission > 0.0:
        ergb = emission_rgb if emission_rgb is not None else rgb
        bsdf.inputs["Emission Color"].default_value = (ergb[0], ergb[1], ergb[2], 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission
    if alpha < 1.0:
        bsdf.inputs["Alpha"].default_value = alpha
        mat.blend_method = "BLEND"
    return mat


def add_cylinder(name, vertices, radius, depth, z, material, x=0.0, y=0.0,
                 rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth,
        location=(x, y, z), rotation=rotation,
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    return obj


def add_cone(name, vertices, radius_bottom, radius_top, depth, z, material,
             x=0.0, y=0.0, rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=radius_bottom, radius2=radius_top,
        depth=depth, location=(x, y, z), rotation=rotation,
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    return obj


def flip_lateral_faces(obj):
    """Flip the normals of a cone's lateral (side) faces, leaving the
    end caps alone. Blender's cone primitive gives a cone that flares
    outward going up lateral normals pointing outward-DOWN, so the
    funnel surface is a backface from a camera above and Unity's URP
    (Cull Back) culls it — the dish looked fine in the Cycles preview
    (Cycles renders backfaces) and in Unity's Inspector preview, but
    the ground showed through it in Play. After the flip the lateral
    faces point inward-UP, i.e. the funnel's visible side, and the dish
    renders from the game camera. Faces are picked by normal: lateral
    faces have |nz| < 0.9, caps have |nz| = 1."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    for f in bm.faces:
        if abs(f.normal.z) < 0.9:
            f.normal_flip()
    bm.to_mesh(me)
    bm.free()
    me.update()
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
    """Box with `scale` as half-extents (the cube primitive is 2 units)."""
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=(0.0, 0.0, rotation_z))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def add_torus(name, major_radius, minor_radius, major_segments, minor_segments,
              z, material, rotation=(0.0, 0.0, 0.0), x=0.0, y=0.0):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius, minor_radius=minor_radius,
        major_segments=major_segments, minor_segments=minor_segments,
        location=(x, y, z), rotation=rotation,
    )
    obj = bpy.context.active_object
    obj.name = name
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

    titanium = make_material("HeliumTitanium", TITANIUM, metallic=0.8, roughness=0.30)
    gunmetal = make_material("HeliumGunmetal", GUNMETAL, metallic=0.9, roughness=0.40)
    dark = make_material("HeliumDarkAlloy", DARK_ALLOY, metallic=0.85, roughness=0.35)
    cyan = make_material("HeliumCyan", HE3_CYAN, metallic=0.45, roughness=0.40)
    he3dark = make_material("HeliumDark", HE3_DARK, metallic=0.50, roughness=0.50)
    pale = make_material("HeliumPale", HE3_PALE, metallic=0.10, roughness=0.45)
    glow = make_material("HeliumGlow", CYAN_GLOW, metallic=0.10, roughness=0.30,
                         emission=2.5)

    # ---- Foundation: dark plinth 1.4 x 1.4 z 0-0.06, titanium tier
    # 1.3 x 1.3 z 0.06-0.16 (v1 envelope).
    add_box("He3_Plinth", (0.0, 0.0, 0.03), 0.0, (0.70, 0.70, 0.03), dark)
    add_box("He3_Tier", (0.0, 0.0, 0.11), 0.0, (0.65, 0.65, 0.05), titanium)
    # Recessed seam ring between plinth and tier.
    add_torus("He3_Seam", 0.68, 0.012, 16, 3, 0.06, dark)
    # Vent grilles on the plinth faces (camera-facing -Y, and +X).
    add_box("He3_VentGrille_01", (0.30, -0.705, 0.035), 0.0, (0.15, 0.01, 0.035), dark)
    add_box("He3_VentGrille_02", (0.705, 0.30, 0.035), 0.0, (0.01, 0.15, 0.035), dark)

    # ---- Main storage tank: 32-seg cylinder r 0.55, z 0.16-1.76 (as v1).
    add_cylinder("He3_Tank", 32, TANK_R, 1.60, 0.96, cyan)
    # 8 vertical ribs around the tank (gunmetal, proud of the wall).
    for i in range(8):
        a = math.radians(i * 45.0)
        add_box("He3_Rib_%02d" % (i + 1),
                (0.56 * math.cos(a), 0.56 * math.sin(a), 0.74),
                a, (0.02, 0.02, 0.54), gunmetal)
    # Two horizontal band rings around the tank.
    add_torus("He3_Band_01", 0.56, 0.02, 16, 6, 0.45, gunmetal)
    add_torus("He3_Band_02", 0.56, 0.02, 16, 6, 1.15, gunmetal)
    # Condensation coils: 3 thin titanium rings around the tank, with
    # two vertical connector pipes on the +X side.
    for i, z in enumerate((0.62, 0.78, 0.94)):
        add_torus("He3_Coil_%02d" % (i + 1), 0.565, 0.015, 16, 4, z, titanium)
    add_cylinder("He3_CoilPipe_01", 6, 0.015, 0.32, 0.78, gunmetal, x=0.575, y=0.0)
    add_cylinder("He3_CoilPipe_02", 6, 0.015, 0.32, 0.78, gunmetal, x=0.0, y=0.575)

    # ---- Domed top: squashed sphere r 0.55, z-scale 0.45, centred at
    # z 1.76 (as v1, top z ~2.01), with two latitude rings and a cap.
    add_sphere("He3_Dome", 20, 10, 0.55, (0.0, 0.0, 1.76), 0.45, cyan)
    add_torus("He3_DomeRing_01", 0.45, 0.015, 16, 4, 1.90, titanium)
    add_torus("He3_DomeRing_02", 0.32, 0.015, 12, 4, 1.96, titanium)
    add_cylinder("He3_DomeCap", 12, 0.10, 0.04, 2.01, gunmetal)

    # ---- Flared collector dish around the tank near the top: cone
    # r 0.55-0.85, z 1.31-1.53 (as v1), cyan rim ring, inner detail
    # ring, and 8 support struts under the dish.
    # FIX (2026-11-07): the cone's lateral faces point outward-DOWN, so
    # from the game camera above they are backfaces and Unity culls
    # them — the ground showed through the dish in Play (it looked fine
    # in the Cycles preview, which renders backfaces). Flip the lateral
    # faces so they point inward-UP (the funnel's visible side), and add
    # a solid cap disc under the dish annulus (thin 16-seg cylinder
    # r 0.85, z 1.49-1.51, dark) as belt and braces, so the annulus is
    # closed by a front-facing disc even if the cone were culled. The
    # disc sits 0.02 below the cone's own top cap (z 1.53), so there is
    # no z-fighting; its outer edge hides inside the rim torus.
    dish = add_cone("He3_Dish", 32, 0.55, 0.85, 0.22, 1.42, dark)
    flip_lateral_faces(dish)
    add_cylinder("He3_DishCap", 16, 0.85, 0.02, 1.50, dark)
    add_torus("He3_Rim", 0.85, 0.05, 24, 8, 1.53, cyan)
    add_torus("He3_DishRing", 0.62, 0.015, 16, 4, 1.38, titanium)
    for i in range(8):
        a = math.radians(i * 45.0 + 22.5)
        add_cylinder("He3_Strut_%02d" % (i + 1), 6, 0.015, 0.14, 1.35,
                     gunmetal, x=0.70 * math.cos(a), y=0.70 * math.sin(a))

    # ---- Vertical pipes: front (-Y) at x 0.25 and side (+X) at y 0,
    # r 0.07, z 0.20-1.50 (as v1, slightly thicker). Glowing strip on
    # the front pipe, valve wheel on the front pipe, elbow into the
    # tank at the top of the side pipe.
    add_cylinder("He3_PipeFront", 12, 0.07, 1.30, 0.85, gunmetal, x=0.25, y=-0.58)
    add_cylinder("He3_PipeSide", 12, 0.07, 1.30, 0.85, gunmetal, x=0.58, y=0.0)
    add_box("He3_PipeGlow", (0.25, -0.655, 0.85), 0.0, (0.02, 0.008, 0.60), glow)
    add_torus("He3_ValveWheel", 0.09, 0.015, 12, 6, 0.60, gunmetal,
              rotation=(math.pi / 2, 0.0, 0.0), x=0.25, y=-0.66)
    add_cylinder("He3_ValveHub", 8, 0.02, 0.04, 0.60, gunmetal,
                 x=0.25, y=-0.66, rotation=(math.pi / 2, 0.0, 0.0))
    # Horizontal base pipe from the pump station to the tank.
    add_cylinder("He3_BasePipe", 8, 0.05, 0.50, 0.25, gunmetal,
                 x=0.30, y=-0.45, rotation=(0.0, math.pi / 2, 0.0))

    # ---- Gauge panel on the camera-facing (-Y) face: gunmetal frame,
    # pale face, two glowing dials, glowing status strip.
    add_box("He3_GaugeFrame", (0.0, -0.565, 1.05), 0.0, (0.18, 0.02, 0.24), gunmetal)
    add_box("He3_GaugeFace", (0.0, -0.59, 1.05), 0.0, (0.15, 0.01, 0.21), pale)
    for i, x in enumerate((-0.07, 0.07)):
        add_cylinder("He3_Dial_%02d" % (i + 1), 12, 0.06, 0.02, 1.10, glow,
                     x=x, y=-0.605, rotation=(math.pi / 2, 0.0, 0.0))
    add_box("He3_GaugeLight", (0.0, -0.605, 0.90), 0.0, (0.12, 0.008, 0.01), glow)

    # ---- Cooling tower at the back-right (+X/+Y), attached to the
    # tank: hyperboloid-ish stack (flare out, then in), rim ring, vent
    # cap with a glowing ring. Top z 1.31, inside the dish envelope.
    add_cone("He3_CoolTower_Low", 16, 0.16, 0.20, 0.55, 0.435, titanium,
             x=0.42, y=0.48)
    add_cone("He3_CoolTower_High", 16, 0.20, 0.17, 0.54, 0.98, gunmetal,
             x=0.42, y=0.48)
    add_torus("He3_CoolTower_Rim", 0.17, 0.02, 12, 4, 1.25, titanium,
              x=0.42, y=0.48)
    add_cylinder("He3_CoolTower_Vent", 12, 0.10, 0.06, 1.28, he3dark,
                 x=0.42, y=0.48)
    add_torus("He3_CoolTower_Glow", 0.10, 0.012, 12, 4, 1.31, glow,
              x=0.42, y=0.48)

    # ---- Pump station on the platform (front-right, +X/-Y): gunmetal
    # box, dark roof, glowing gauge on the -Y face, roof vent.
    add_box("He3_Pump", (0.45, -0.45, 0.335), 0.0, (0.175, 0.15, 0.175), gunmetal)
    add_box("He3_PumpRoof", (0.45, -0.45, 0.525), 0.0, (0.185, 0.16, 0.015), dark)
    add_box("He3_PumpGauge", (0.45, -0.605, 0.40), 0.0, (0.05, 0.008, 0.06), glow)
    add_box("He3_PumpVent", (0.55, -0.35, 0.555), 0.0, (0.04, 0.04, 0.02), dark)

    # ---- Control module on the platform (front-left, -X/-Y): titanium
    # box, glowing screen on the -Y face, antenna with glowing tip.
    add_box("He3_Control", (-0.45, -0.62, 0.36), 0.0, (0.15, 0.06, 0.20), titanium)
    add_box("He3_ControlScreen", (-0.45, -0.685, 0.42), 0.0, (0.10, 0.008, 0.07), glow)
    add_cylinder("He3_ControlAntenna", 6, 0.012, 0.25, 0.68, gunmetal,
                 x=-0.55, y=-0.62)
    add_sphere("He3_ControlAntennaTip", 8, 6, 0.02, (-0.55, -0.62, 0.81), 1.0, glow)

    # ---- Gas vents on the platform: gunmetal stacks with glowing
    # tip rings. (One at the back-left, one at the front-left.)
    for i, (x, y) in enumerate(((-0.58, 0.28), (-0.58, -0.28))):
        add_cylinder("He3_Vent_%02d" % (i + 1), 8, 0.05, 0.55, 0.435, gunmetal,
                     x=x, y=y)
        add_torus("He3_VentTip_%02d" % (i + 1), 0.05, 0.012, 8, 4, 0.71, glow,
                  x=x, y=y)

    # ---- Transport pad on the platform (front-left): flat octagonal
    # pad with a glowing ring, two gas canisters with glowing caps.
    add_cylinder("He3_Pad", 8, 0.18, 0.02, 0.17, gunmetal, x=-0.12, y=-0.50)
    add_torus("He3_PadRing", 0.18, 0.008, 12, 4, 0.18, glow, x=-0.12, y=-0.50)
    for i, (x, y) in enumerate(((-0.18, -0.50), (-0.06, -0.54))):
        add_cylinder("He3_Canister_%02d" % (i + 1), 12, 0.06, 0.22, 0.29, cyan,
                     x=x, y=y)
        add_cylinder("He3_CanisterCap_%02d" % (i + 1), 12, 0.065, 0.02, 0.41, glow,
                     x=x, y=y)

    # ---- Top assembly: dark stem + glowing orb (v1, tallest point
    # z 2.42), a titanium halo ring around the orb, and 3 antenna
    # whips around the dome with glowing tips.
    add_cylinder("He3_Stem", 8, 0.05, 0.18, 2.10, he3dark)
    add_sphere("He3_Orb", 12, 6, 0.14, (0.0, 0.0, 2.28), 1.0, glow)
    add_torus("He3_OrbRing", 0.20, 0.015, 12, 6, 2.28, titanium)
    for i, a_deg in enumerate((30, 150, 270)):
        a = math.radians(a_deg)
        x, y = 0.30 * math.cos(a), 0.30 * math.sin(a)
        add_cylinder("He3_Whip_%02d" % (i + 1), 6, 0.012, 0.30, 2.10, gunmetal,
                     x=x, y=y)
        add_sphere("He3_WhipTip_%02d" % (i + 1), 8, 6, 0.02, (x, y, 2.25), 1.0, glow)

    # ---- Glowing bollards on the platform corners.
    for i, (x, y) in enumerate(((-0.62, 0.05), (0.62, 0.30))):
        add_cone("He3_Bollard_%02d" % (i + 1), 6, 0.03, 0.012, 0.09, 0.205,
                 glow, x=x, y=y)

    # ---- v5 greebles (graphics upgrade): surface detail on the v2
    # geometry, all inside the v2 envelope (x/y +/-0.85, z <= 2.42).
    # Two more vent grilles on the plinth (v2 has -Y and +X; these
    # add +Y and -X).
    add_box("He3_VentGrille_03", (-0.30, 0.705, 0.035), 0.0, (0.15, 0.01, 0.035), dark)
    add_box("He3_VentGrille_04", (-0.705, -0.30, 0.035), 0.0, (0.01, 0.15, 0.035), dark)
    # Junction boxes on the tank faces (v2 has none on the tank).
    add_box("He3_JunctionBox_01", (0.0, -0.575, 1.30), 0.0, (0.12, 0.02, 0.10), gunmetal)
    add_box("He3_JunctionBox_02", (0.575, 0.0, 0.30), 0.0, (0.02, 0.12, 0.10), gunmetal)
    # A third vertical pipe on the tank -X face (v2 has -Y and +X).
    add_cylinder("He3_PipeBack", 12, 0.06, 1.30, 0.85, gunmetal, x=-0.58, y=0.0)
    # Two more condensation coils (v2 has 3, at 0.62/0.78/0.94).
    add_torus("He3_Coil_04", 0.565, 0.015, 16, 4, 1.10, titanium)
    add_torus("He3_Coil_05", 0.565, 0.015, 16, 4, 0.50, titanium)
    # A second gauge panel on the tank +X face (v2's is on -Y): frame
    # + glowing dial (cylinder rotated to face +X).
    add_box("He3_GaugeFrame2", (0.575, 0.30, 1.05), 0.0, (0.02, 0.12, 0.16), gunmetal)
    add_cylinder("He3_Dial2", 12, 0.04, 0.02, 1.05, glow,
                 x=0.60, y=0.30, rotation=(0.0, math.pi / 2, 0.0))
    # Small vent on the cooling tower cap.
    add_cylinder("He3_CoolTowerVent2", 12, 0.04, 0.08, 1.33, gunmetal,
                 x=0.42, y=0.48)
    # Hazard bars on the plinth front (-Y) face.
    add_box("He3_HazardBar_01", (-0.35, -0.705, 0.035), 0.0, (0.15, 0.01, 0.02), cyan)
    add_box("He3_HazardBar_02", (0.35, -0.705, 0.035), 0.0, (0.15, 0.01, 0.02), cyan)
    # An extra canister on the transport pad (v2 has 2).
    add_cylinder("He3_Canister_03", 12, 0.06, 0.22, 0.29, cyan, x=-0.12, y=-0.38)
    add_cylinder("He3_CanisterCap_03", 12, 0.065, 0.02, 0.41, glow, x=-0.12, y=-0.38)
    # Two more glowing bollards on the platform (v2 has 2).
    add_cone("He3_Bollard_03", 6, 0.03, 0.012, 0.09, 0.205, glow, x=0.62, y=-0.30)
    add_cone("He3_Bollard_04", 6, 0.03, 0.012, 0.09, 0.205, glow, x=-0.62, y=0.55)
    # Horizontal pipe from the pump station toward the tank.
    add_cylinder("He3_PumpPipe", 8, 0.04, 0.40, 0.35, gunmetal,
                 x=0.20, y=-0.45, rotation=(0.0, math.pi / 2, 0.0))

    return list(bpy.context.scene.objects)


def make_uv_atlas(objects):
    """Pack every mesh object's UV islands into one 0-1 atlas (Lightmap
    Pack). The panel detail is defined in object space, so the UVs only
    decide WHERE in the atlas each surface lands, not the pattern."""
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        if obj.type == "MESH":
            obj.select_set(True)
    bpy.context.view_layer.objects.active = next(
        o for o in objects if o.type == "MESH")
    bpy.ops.uv.lightmap_pack(
        PREF_CONTEXT="ALL_FACES", PREF_PACK_IN_ONE=True,
        PREF_BOX_DIV=12, PREF_MARGIN_DIV=0.1)
    bpy.ops.object.select_all(action="DESELECT")


def write_png(path, rgb_float):
    """Write an (H, W, 3) float 0-1 array as an 8-bit RGB PNG. Pure
    Python (zlib) — Blender's image.save()/save_render() blacken a
    generated image's pixels on save, so the PNG is written directly
    instead. Rows are flipped so the file matches Blender's
    bottom-to-top pixel order."""
    h, w, _ = rgb_float.shape
    rgb = (np.clip(rgb_float, 0.0, 1.0) * 255.0).astype(np.uint8)
    rgb = rgb[::-1]  # Blender row 0 is the bottom; PNG row 0 is the top.
    raw = b"".join(b"\x00" + rgb[y].tobytes() for y in range(h))

    def chunk(typ, data):
        c = struct.pack(">I", len(data)) + typ + data
        return c + struct.pack(">I", zlib.crc32(typ + data) & 0xffffffff)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 6))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def make_metal_texture(rgb, seed, filename):
    """Generate one 1024 metal texture with numpy: flat spec colour,
    panel grid (32 px cells + a faint 16 px sub-grid, 4 px lines),
    rivets at the cell corners, per-cell brightness variation, and
    soft grime blotches. Returns the Blender image, saved as a PNG
    next to the script. v5: finer cells than v2's 48 px."""
    S = ATLAS_SIZE
    CELL = 32
    SUB = 16
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    # Panel grid: lines where the within-cell position is near a border.
    cx = np.mod(xx, CELL)
    cy = np.mod(yy, CELL)
    line = ((cx < 4) | (cy < 4)).astype(np.float32)
    # Faint sub-grid: a finer 16 px panel read inside each 32 px cell.
    sx = np.mod(xx, SUB)
    sy = np.mod(yy, SUB)
    subline = ((sx < 2) | (sy < 2)).astype(np.float32)
    # Per-cell brightness variation (deterministic hash of cell indices).
    cell_x = (xx // CELL).astype(np.int32)
    cell_y = (yy // CELL).astype(np.int32)
    h = np.sin(cell_x * 127.1 + cell_y * 311.7) * 43758.5453
    h = h - np.floor(h)
    cell_bright = 0.90 + 0.20 * h
    # Rivets: small dots at the cell corners.
    dx = np.minimum(cx, CELL - cx)
    dy = np.minimum(cy, CELL - cy)
    dist_corner = np.sqrt(dx * dx + dy * dy)
    rivet = (dist_corner < 3.5).astype(np.float32)
    # Grime: soft dark blotches (random circles, radial falloff).
    grime = np.zeros((S, S), dtype=np.float32)
    for _ in range(48):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(40, 140)
        strength = rng.uniform(0.08, 0.25)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        grime += strength * np.exp(-d2 / (2.0 * (r / 2.5) ** 2))
    grime = np.clip(grime, 0.0, 0.45)
    # Combine into RGB (float 0-1).
    base = np.array(rgb, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= cell_bright[..., None]
    arr *= (1.0 - line[..., None] * 0.75)
    arr *= (1.0 - subline[..., None] * 0.25)
    arr *= (1.0 + rivet[..., None] * 0.25)
    arr *= (1.0 - grime[..., None])
    arr = np.clip(arr, 0.0, 1.0)
    # Blender images are RGBA; add an opaque alpha channel.
    rgba = np.ones((S, S, 4), dtype=np.float32)
    rgba[..., :3] = arr
    img = bpy.data.images.new(filename, S, S, alpha=False)
    img.pixels.foreach_set(rgba.ravel())
    path = os.path.join(SCRIPT_DIR, filename)
    # Write the PNG directly (see write_png): image.save() would blacken it.
    write_png(path, arr)
    img.filepath = path
    img.file_format = "PNG"
    return img


def apply_metal_textures(metal_materials):
    """Generate one texture per metal material, plug it into the
    material's Base Color, and set the base colour to white so Unity's
    (map x colour) multiply returns exactly the texture. Returns a list
    of (material name, filepath, bytes). Four textures here, not three:
    the cyan tank is the Harvester's hero surface, so it gets its own
    panel texture instead of staying flat."""
    seeds = {"HeliumTitanium": 161, "HeliumGunmetal": 167,
             "HeliumDarkAlloy": 173, "HeliumCyan": 179}
    base_colors = {"HeliumTitanium": TITANIUM, "HeliumGunmetal": GUNMETAL,
                   "HeliumDarkAlloy": DARK_ALLOY, "HeliumCyan": HE3_CYAN}
    info = []
    for mat in metal_materials:
        filename = TEXTURE_NAMES[mat.name]
        img = make_metal_texture(base_colors[mat.name], seeds[mat.name],
                                 filename)
        nt = mat.node_tree
        node = nt.nodes.new("ShaderNodeTexImage")
        node.name = "BakedTexture"
        node.image = img
        bsdf = nt.nodes["Principled BSDF"]
        nt.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Base Color"].default_value = (1.0, 1.0, 1.0, 1.0)
        path = os.path.join(SCRIPT_DIR, filename)
        info.append((mat.name, path, os.path.getsize(path)))
    return info


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
        path_mode="COPY",
        embed_textures=False,
    )


def report(objects):
    total_tris = 0
    print("Helium-3 Harvester v5 build:")
    for obj in objects:
        if obj.type != "MESH":
            continue
        mesh = obj.data
        mesh.calc_loop_triangles()
        total_tris += len(mesh.loop_triangles)
        print("  %-22s tris=%4d  materials=%s"
              % (obj.name, len(mesh.loop_triangles),
                 [m.name if m else "?" for m in mesh.materials]))
    print("  objects=%d  total_tris=%d" % (len(objects), total_tris))
    # Bounding box check: footprint must stay within +/-0.90 (dish rim
    # ring outer edge, as in v1 — the rim torus is r 0.85 + 0.05 minor),
    # height <= 2.42 (orb top).
    pts = [o.matrix_world @ v.co for o in objects if o.type == "MESH"
           for v in o.data.vertices]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    print("  bounds x=[%.2f, %.2f] y=[%.2f, %.2f] z=[%.2f, %.2f]"
          % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    print("  exported: %s" % OUTPUT_FBX)
    print("  fbx bytes: %d" % os.path.getsize(OUTPUT_FBX))
    for mat_name, path, size in TEXTURE_INFO:
        print("  texture %s: %s (%d bytes)" % (mat_name, path, size))


if __name__ == "__main__":
    built = build()
    metal_mats = [m for m in bpy.data.materials
                  if m.name in ("HeliumTitanium", "HeliumGunmetal",
                                "HeliumDarkAlloy", "HeliumCyan")]
    make_uv_atlas(built)
    TEXTURE_INFO = apply_metal_textures(metal_mats)
    export(built)
    report(built)
