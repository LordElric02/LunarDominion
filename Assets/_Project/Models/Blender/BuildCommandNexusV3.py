# BuildCommandNexusV3.py — Lunar Dominion: Command Nexus v3 (Quantum Command Spire).
#
# DRAFT — not yet in the Unity project and NOT pushed to GitHub.
# Follows the "Quantum Command Nexus" design spec (v1.0): a broad stepped
# hexagonal foundation, tall angular command tower, suspended luminous blue
# reactor sphere framed by three containment rings and three armored reactor
# pylons, command crown with communications, restrained premium sci-fi
# palette (spec section 4). This is the FULL signature design — not the
# stripped Stage I; the four progression stages come later from this script.
#
# Drop-in contract (same as v1/v2, do not break it):
#   footprint diameter 2.0 (world bounds x/y within +/-1.00), total height
#   ~2.89, origin at ground centre, Blender Z-up. Same Nexus blue identity,
#   same material approach (Principled BSDF, no textures).
# Adapted from the spec (its 14x14 footprint / 18 height / 20-35k tris /
# textures / LODs do not fit this project's 2.0 plot and low-poly pipeline):
#   low-poly first pass, ~3.5k tris, no textures, no UV work, no LODs.
# The spec assumes Unity prefabs; this project is runtime-built via
# BaseView.cs + Resources.Load, so there is no prefab work — the FBX drops
# into Assets/_Project/Resources/Models/ like v1/v2 did.
#
# Camera: Unity camera sits at (9,11,9), looking from +X/+Z, which is
# Blender +X / -Y. Readable detail (reactor, windows, crown band) faces
# the camera; the dish and antenna sit on the back side (-X/+Y). The three
# pylons sit 60 degrees off the camera axis so they frame the reactor
# without hiding it.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildCommandNexusV3.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Written/tested against Blender 4.2.3 LTS here; uses no deprecated APIs,
# so it should also run on Blender 5.2 LTS.
# NOTE: the script exports the FBX next to wherever the script file was
# opened from (SCRIPT_DIR). If you open it from F:\ or Downloads, the FBX
# lands there — check the "exported:" line in the console / report.

import math
import os

import bpy
from mathutils import Matrix, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# Draft output next to this script. When installed in the repo, rename the
# exported file to CommandNexus.fbx in Assets/_Project/Resources/Models/.
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "CommandNexusV3.fbx")

# Spec section 4 palette (linear RGB approximations of the spec hex values).
TITANIUM = (0.78, 0.82, 0.85)      # #C6D0DA primary armor
GUNMETAL = (0.27, 0.33, 0.39)      # #465363
DARK_ALLOY = (0.09, 0.15, 0.22)    # #172638 recesses
REACTOR_BLUE = (0.14, 0.55, 1.00)  # #238BFF
ENERGY_CYAN = (0.38, 0.85, 1.00)   # #62D9FF
WARNING_AMBER = (1.00, 0.70, 0.28)  # #FFB347

# Layout constants (Blender units; 1 unit = 1 Unity unit).
TOWER_R_BOTTOM = 0.55
TOWER_R_TOP = 0.40
TOWER_Z0 = 0.30
TOWER_Z1 = 1.55
PYLON_ANGLES = (math.radians(15), math.radians(135), math.radians(255))
PYLON_R = 0.72
PYLON_Z0 = 0.22
PYLON_Z1 = 2.30
PYLON_R_BOTTOM = 0.16
PYLON_R_TOP = 0.10
REACTOR_Z = 2.43
REACTOR_R = 0.35


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


def add_cylinder(name, vertices, radius, depth, z, material, rotation_z=0.0):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth,
        location=(0.0, 0.0, z), rotation=(0.0, 0.0, rotation_z),
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    return obj


def add_cone(name, vertices, radius_bottom, radius_top, depth, z, material,
             rotation_z=0.0):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=radius_bottom, radius2=radius_top,
        depth=depth, location=(0.0, 0.0, z), rotation=(0.0, 0.0, rotation_z),
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


def add_torus(name, major_radius, minor_radius, major_segments, minor_segments,
              z, material, rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius, minor_radius=minor_radius,
        major_segments=major_segments, minor_segments=minor_segments,
        location=(0.0, 0.0, z), rotation=rotation,
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    return obj


def add_face_box(name, axis_xy, angle, z_center, height, width, thick,
                 r_bottom, r_top, z_bottom, z_top, material, proud=0.02):
    """Box hugging a tapered 4-gon face whose outward normal points at
    `angle` (radians, from +X). The prism axis sits at `axis_xy`; its
    circumradius is r_bottom at z_bottom and r_top at z_top. The box's
    outer face sits `proud` beyond the face plane at z_center. The box is
    tilted to match the face slope, so it reads as an armor rib / panel /
    window rather than a floating slab."""
    theta = math.atan2(r_bottom - r_top, z_top - z_bottom)
    r_mid = r_bottom + (r_top - r_bottom) * (z_center - z_bottom) / (z_top - z_bottom)
    face_dist = r_mid * math.cos(math.pi / 4.0)
    c = face_dist + proud - thick / 2.0
    ca, sa = math.cos(angle), math.sin(angle)
    ct, st = math.cos(theta), math.sin(theta)
    # Columns: face normal, tangential, up-the-slope. (N, T, S) is a
    # right-handed rotation: N x T == S.
    N = Vector((ct * ca, ct * sa, st))
    T = Vector((-sa, ca, 0.0))
    S = Vector((-st * ca, -st * sa, ct))
    rot = Matrix((N, T, S)).transposed()
    bpy.ops.mesh.primitive_cube_add()
    obj = bpy.context.active_object
    obj.name = name
    obj.matrix_world = Matrix.Translation(
        (axis_xy[0] + c * ca, axis_xy[1] + c * sa, z_center)) @ rot.to_4x4()
    obj.scale = (thick / 2.0, width / 2.0, height / 2.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def add_tracked_cylinder(name, base_point, direction, length, radius, vertices,
                         material):
    """Cylinder whose local +Z runs from base_point along `direction`."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=length)
    obj = bpy.context.active_object
    obj.name = name
    direction = Vector(direction).normalized()
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = direction.to_track_quat("Z", "Y")
    obj.location = Vector(base_point) + direction * (length / 2.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def add_tracked_cone(name, center, axis_z, radius_bottom, radius_top, depth,
                     vertices, material):
    """Cone whose local +Z points along `axis_z` (wide opening faces -Z)."""
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=radius_bottom, radius2=radius_top, depth=depth)
    obj = bpy.context.active_object
    obj.name = name
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = Vector(axis_z).normalized().to_track_quat("Z", "Y")
    obj.location = Vector(center)
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

    titanium = make_material("NexusTitanium", TITANIUM, metallic=0.8, roughness=0.30)
    gunmetal = make_material("NexusGunmetal", GUNMETAL, metallic=0.9, roughness=0.40)
    dark = make_material("NexusDarkAlloy", DARK_ALLOY, metallic=0.85, roughness=0.35)
    reactor = make_material("NexusReactorBlue", REACTOR_BLUE,
                            metallic=0.1, roughness=0.30, emission=2.0)
    cyan = make_material("NexusEnergyCyan", ENERGY_CYAN,
                         metallic=0.05, roughness=0.30, emission=1.4)
    amber = make_material("NexusWarningAmber", WARNING_AMBER,
                          metallic=0.05, roughness=0.35, emission=1.0)

    # Blender's cylinder primitive starts its vertices at -30 degrees, so
    # a +30-degree Z rotation puts hex vertices at 0/60/120... and flat
    # faces at 30/90/150... — the 330-degree face sits 15 degrees off the
    # game-camera axis (315 deg), nearly facing the camera.
    hex_rot = math.pi / 6.0
    quad_rot = math.pi / 4.0  # 4-gon parts: flat faces toward +-X / +-Y

    # ---- 3.1 Hexagonal foundation: three stepped tiers, flush with z=0.
    add_cylinder("Foundation_Hex_Base", 6, 0.98, 0.12, 0.06, titanium, hex_rot)
    add_cylinder("Foundation_Hex_Mid", 6, 0.85, 0.10, 0.17, gunmetal, hex_rot)
    add_cylinder("Foundation_Hex_Top", 6, 0.72, 0.08, 0.26, titanium, hex_rot)
    # Recessed seam rings between the tiers.
    add_cylinder("Foundation_Seam_01", 6, 0.86, 0.02, 0.12, dark, hex_rot)
    add_cylinder("Foundation_Seam_02", 6, 0.73, 0.02, 0.22, dark, hex_rot)
    # Illuminated cyan perimeter channel on the top tier (hex ring).
    add_torus("Foundation_Lighting", 0.66, 0.02, 6, 4, 0.31, cyan,
              rotation=(0.0, 0.0, hex_rot))
    # Access panels on the top tier (camera-facing + two rear faces).
    for i, a_deg in enumerate((0, 120, 240)):
        a = math.radians(a_deg)
        add_box("Foundation_Panel_%d" % (i + 1),
                (0.50 * math.cos(a), 0.50 * math.sin(a), 0.31),
                a + math.pi / 2.0, (0.06, 0.04, 0.01), dark)
    # Vents and cyan strip lights alternate around the base-tier rim
    # (vertex angles only — the hex annulus is widest there).
    for i, a_deg in enumerate((90, 210, 330)):
        a = math.radians(a_deg)
        add_box("Foundation_Vent_%d" % (i + 1),
                (0.90 * math.cos(a), 0.90 * math.sin(a), 0.13),
                a + math.pi / 2.0, (0.05, 0.03, 0.01), dark)
    for i, a_deg in enumerate((30, 150, 270)):
        a = math.radians(a_deg)
        add_box("Foundation_Strip_%d" % (i + 1),
                (0.90 * math.cos(a), 0.90 * math.sin(a), 0.13),
                a + math.pi / 2.0, (0.045, 0.02, 0.01), cyan)

    # ---- 3.2 Central command spire: angular tapered 4-face tower.
    # Flared footing, then the main shaft.
    add_cone("Tower_Skirt", 4, 0.62, 0.55, 0.15, 0.375, dark, quad_rot)
    add_cone("Main_Tower", 4, TOWER_R_BOTTOM, TOWER_R_TOP,
             TOWER_Z1 - TOWER_Z0, (TOWER_Z0 + TOWER_Z1) / 2.0, titanium, quad_rot)
    # 4 structural ribs, one centred on each tower face.
    for i in range(4):
        a = i * math.pi / 2.0
        add_face_box("Tower_Rib_%d" % (i + 1), (0.0, 0.0), a,
                     0.925, 1.05, 0.09, 0.05,
                     TOWER_R_BOTTOM, TOWER_R_TOP, TOWER_Z0, TOWER_Z1, gunmetal,
                     proud=0.02)
    # Elevated observation section + narrow illuminated windows.
    add_cone("Observation_Section", 4, 0.44, 0.38, 0.17, 1.635, gunmetal, quad_rot)
    for i in range(4):
        a = i * math.pi / 2.0
        add_face_box("Observation_Window_%d" % (i + 1), (0.0, 0.0), a,
                     1.635, 0.10, 0.10, 0.02,
                     0.44, 0.38, 1.55, 1.72, cyan, proud=0.005)
    # Square deck-edge ring around the observation section.
    add_torus("Observation_Rail", 0.45, 0.015, 4, 4, 1.70, gunmetal,
              rotation=(0.0, 0.0, quad_rot))

    # ---- 3.5 Command crown: wider flat slab + emissive window band.
    add_cylinder("Command_Crown", 4, 0.52, 0.12, 1.78, titanium, quad_rot)
    add_cylinder("Crown_Band", 4, 0.53, 0.04, 1.78, cyan, quad_rot)
    # Amber warning lights on three crown corners.
    for i, a_deg in enumerate((45, 135, 315)):
        a = math.radians(a_deg)
        add_sphere("Crown_Light_%d" % (i + 1), 8, 5, 0.035,
                   (0.52 * math.cos(a), 0.52 * math.sin(a), 1.86), 1.0, amber)

    # ---- 3.3 Suspended quantum reactor: sphere + 3 rings + couplers.
    # Gap between crown top (1.84) and reactor bottom (2.08) is the
    # suspension: the sphere touches nothing.
    add_sphere("Reactor_Core", 28, 14, REACTOR_R, (0.0, 0.0, REACTOR_Z), 1.0, reactor)
    for i in range(4):
        a = math.pi / 4.0 + i * math.pi / 2.0
        add_sphere("Reactor_Coupler_%d" % (i + 1), 8, 4, 0.06,
                   (0.33 * math.cos(a), 0.33 * math.sin(a), REACTOR_Z), 1.0, cyan)
    # Three containment rings, separate objects, different orientations.
    add_torus("Containment_Ring_01", 0.48, 0.035, 32, 8, REACTOR_Z, cyan,
              rotation=(math.radians(12), 0.0, 0.0))
    add_torus("Containment_Ring_02", 0.50, 0.03, 32, 8, REACTOR_Z, gunmetal,
              rotation=(math.radians(50), 0.0, 0.0))
    add_torus("Containment_Ring_03", 0.56, 0.03, 32, 8, REACTOR_Z, titanium,
              rotation=(0.0, math.radians(45), 0.0))

    # ---- 3.4 Three reactor pylons: rise from the mid tier, frame the
    # reactor, 60 degrees off the camera axis so they never hide it.
    for i, a in enumerate(PYLON_ANGLES):
        px, py = PYLON_R * math.cos(a), PYLON_R * math.sin(a)
        add_cone("Pylon_%02d" % (i + 1), 4, PYLON_R_BOTTOM, PYLON_R_TOP,
                 PYLON_Z1 - PYLON_Z0, (PYLON_Z0 + PYLON_Z1) / 2.0, gunmetal, quad_rot)
        # NOTE: add_cone builds at the origin — move this pylon into place.
        pylon = bpy.context.active_object
        pylon.location = (px, py, (PYLON_Z0 + PYLON_Z1) / 2.0)
        # Vertical emissive energy channel on the inner face (faces the core).
        add_face_box("Pylon_Channel_%02d" % (i + 1), (px, py), a + math.pi,
                     1.15, 1.60, 0.06, 0.03,
                     PYLON_R_BOTTOM, PYLON_R_TOP, PYLON_Z0, PYLON_Z1, cyan,
                     proud=0.01)
        # Cap + energy emitter tip, aimed at the reactor.
        add_cylinder("Pylon_Cap_%02d" % (i + 1), 4, 0.11, 0.05, 2.325,
                     dark, quad_rot)
        cap = bpy.context.active_object
        cap.location = (px, py, 2.325)
        add_sphere("Pylon_Emitter_%02d" % (i + 1), 10, 5, 0.05,
                   (px, py, 2.38), 1.0, cyan)

    # ---- 3.5 Communications, back side (-X/+Y), away from the camera.
    # Satellite dish on the foundation rim at 195 degrees.
    dish_a = math.radians(195)
    dish_base = (-0.589, 0.34, 0.30)
    mast_dir = (math.cos(dish_a) * math.sin(math.radians(25)),
                math.sin(dish_a) * math.sin(math.radians(25)),
                math.cos(math.radians(25)))
    add_tracked_cylinder("Dish_Mast", dish_base, mast_dir, 0.20, 0.025, 6, gunmetal)
    out_dir = (math.cos(dish_a) * math.sin(math.radians(50)),
               math.sin(dish_a) * math.sin(math.radians(50)),
               math.cos(math.radians(50)))
    dish_center = (dish_base[0] + mast_dir[0] * 0.20 + out_dir[0] * 0.03,
                   dish_base[1] + mast_dir[1] * 0.20 + out_dir[1] * 0.03,
                   dish_base[2] + mast_dir[2] * 0.20 + out_dir[2] * 0.03)
    # Cone +Z points down-inward, so the wide opening faces up-outward.
    add_tracked_cone("Dish", dish_center,
                     (-out_dir[0], -out_dir[1], -out_dir[2]),
                     0.16, 0.02, 0.10, 12, titanium)
    # Thin antenna on the top tier at 90 degrees (back, +Y).
    add_cylinder("Antenna", 6, 0.02, 0.55, 0.575, dark)
    antenna = bpy.context.active_object
    antenna.location = (0.0, 0.60, 0.575)
    add_sphere("Antenna_Tip", 8, 4, 0.03, (0.0, 0.60, 0.86), 1.0, cyan)

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
    print("Command Nexus v3 build:")
    for obj in objects:
        if obj.type != "MESH":
            continue
        mesh = obj.data
        mesh.calc_loop_triangles()
        total_tris += len(mesh.loop_triangles)
        print("  %-20s tris=%4d  materials=%s"
              % (obj.name, len(mesh.loop_triangles),
                 [m.name if m else "?" for m in mesh.materials]))
    print("  objects=%d  total_tris=%d" % (len(objects), total_tris))
    # Bounding box check: footprint must stay within radius 1.0, height ~2.89.
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
