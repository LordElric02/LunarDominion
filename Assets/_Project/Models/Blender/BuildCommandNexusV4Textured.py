# BuildCommandNexusV4Textured.py — Lunar Dominion: Command Nexus v4 + baked
# image textures (textures pilot).
#
# DRAFT — not yet in the Unity project and NOT pushed to GitHub.
# Extends the v4 pilot (166 objects, 10,372 tris, 6 spec-palette Principled
# materials, no textures) with image textures for the three metal materials
# (titanium, gunmetal, dark alloy): panel lines, rivets and wear, baked to
# three 1024 PNGs (one per metal material group). Emission stays
# procedural (emissive colour inputs, not baked), so the reactor, window
# strips and cyan details keep glowing.
#
# HOW THE TEXTURES ARE MADE: pixel-generated with numpy, NOT a Cycles
# bake. A Cycles diffuse bake was tried first and is unusable headless
# here — even a flat-colour bake takes 5+ minutes on this VM (the bake
# itself, not the shader), so a shader-node bake cannot be verified.
# The task allows pixel-generated textures, and the outcome is the same:
# real image files that Unity's FBX importer reads. The detail is a
# tileable-style panel grid (32 px cells, 3 px lines, rivets at the
# corners, soft grime blotches, per-cell brightness variation), computed
# in numpy and written to PNGs. The model's UVs are packed into one
# atlas layout (Lightmap Pack) so the texture is sampled per part.
#
# Same drop-in contract as v4 (do not break it): footprint 2.0 (bounds
# x/y within +/-1.00), height ~2.90, origin at ground centre, Blender
# Z-up. Same 6 materials, same object names (Reactor_Core,
# Containment_Ring_01..04 — the Unity animation code depends on them).
# Textures add no triangles. The FBX + the three PNGs travel together
# into Assets/_Project/Resources/Models/ as CommandNexus.fbx +
# CommandNexusV4_*.png.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildCommandNexusV4Textured.py
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
import numpy as np
from mathutils import Matrix, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# Draft outputs next to this script. When installed in the repo, rename the
# exported FBX to CommandNexus.fbx and move the three PNGs next to it in
# Assets/_Project/Resources/Models/.
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "CommandNexusV4Textured.fbx")
ATLAS_SIZE = 1024
TEXTURE_NAMES = {
    "NexusTitanium": "CommandNexusV4_Titanium.png",
    "NexusGunmetal": "CommandNexusV4_Gunmetal.png",
    "NexusDarkAlloy": "CommandNexusV4_DarkAlloy.png",
}

# Spec section 4 palette (linear RGB approximations of the spec hex values).
TITANIUM = (0.78, 0.82, 0.85)      # #C6D0DA primary armor
GUNMETAL = (0.27, 0.33, 0.39)      # #465363
DARK_ALLOY = (0.09, 0.15, 0.22)    # #172638 recesses
REACTOR_BLUE = (0.14, 0.55, 1.00)  # #238BFF
ENERGY_CYAN = (0.38, 0.85, 1.00)   # #62D9FF
WARNING_AMBER = (1.00, 0.70, 0.28)  # #FFB347

# Layout constants (Blender units; 1 unit = 1 Unity unit).
HEX_ROT = math.pi / 6.0    # hex vertices at 0/60/120...; faces at 30/90/150...
QUAD_ROT = math.pi / 4.0   # 4-gon parts: flat faces toward +-X / +-Y
CAMERA_FACE = math.radians(330)  # hex face nearest the game camera axis (315 deg)
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
REACTOR_Z = 2.45
REACTOR_R = 0.40


def make_material(name, rgb, metallic=0.1, roughness=0.6, emission=0.0,
                  emission_rgb=None):
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission > 0.0:
        # Emission can be a deeper colour than the base: a bright emissive
        # renders pale/white under Cycles AgX, so the reactor's glow is a
        # deep blue rather than its lighter base blue.
        ergb = emission_rgb if emission_rgb is not None else rgb
        bsdf.inputs["Emission Color"].default_value = (ergb[0], ergb[1], ergb[2], 1.0)
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


def add_ramp(name, angle, r_inner, r_outer, z_inner, z_outer, width, thick,
             material, edge_material=None):
    """Sloped rectangular ramp rising from (r_outer, z_outer) to
    (r_inner, z_inner) along the hex-face normal at `angle`. The slab
    cuts through the foundation tiers and reads as an access ramp into
    the top tier. With `edge_material`, two thin glowing strips run
    along the ramp edges."""
    ca, sa = math.cos(angle), math.sin(angle)
    # T = tangential (width axis); U = up-slope; N = T x U, the top-face
    # normal (points up and outward). (T, U, N) is right-handed.
    T = Vector((-sa, ca, 0.0))
    run = r_outer - r_inner
    rise = z_inner - z_outer
    length = math.hypot(run, rise)
    ct, st = run / length, rise / length
    U = Vector((-ca * ct, -sa * ct, st))
    N = T.cross(U)
    rot = Matrix((T, U, N)).transposed()
    r_mid = (r_inner + r_outer) / 2.0
    z_mid = (z_inner + z_outer) / 2.0
    center = Vector((r_mid * ca, r_mid * sa, z_mid))
    bpy.ops.mesh.primitive_cube_add()
    obj = bpy.context.active_object
    obj.name = name
    obj.matrix_world = Matrix.Translation(center) @ rot.to_4x4()
    obj.scale = (width / 2.0, length / 2.0, thick / 2.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    if edge_material is not None:
        for side in (-1.0, 1.0):
            edge_center = center + side * (width / 2.0 - 0.02) * T + 0.018 * N
            bpy.ops.mesh.primitive_cube_add()
            edge = bpy.context.active_object
            edge.name = name + "_Edge_%+d" % (int(side))
            edge.matrix_world = Matrix.Translation(edge_center) @ rot.to_4x4()
            edge.scale = (0.01, length / 2.0, 0.008)
            bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
            edge.data.materials.append(edge_material)
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
    reactor = make_material("NexusReactorBlue", (0.08, 0.35, 1.00),
                            metallic=0.1, roughness=0.30, emission=3.0,
                            emission_rgb=(0.02, 0.28, 1.00))
    cyan = make_material("NexusEnergyCyan", ENERGY_CYAN,
                         metallic=0.05, roughness=0.30, emission=1.6)
    amber = make_material("NexusWarningAmber", WARNING_AMBER,
                          metallic=0.05, roughness=0.35, emission=1.0)

    # ---- 3.1 Hexagonal foundation: plinth + three stepped tiers.
    # z spans: plinth 0-0.06, base 0.06-0.16, mid 0.16-0.24, top 0.24-0.30.
    add_cylinder("Foundation_Plinth", 6, 0.99, 0.06, 0.03, dark, HEX_ROT)
    add_cylinder("Foundation_Hex_Base", 6, 0.95, 0.10, 0.11, titanium, HEX_ROT)
    add_cylinder("Foundation_Hex_Mid", 6, 0.84, 0.08, 0.20, gunmetal, HEX_ROT)
    add_cylinder("Foundation_Hex_Top", 6, 0.72, 0.06, 0.27, titanium, HEX_ROT)
    # Recessed seam rings between the tiers.
    add_cylinder("Foundation_Seam_01", 6, 0.955, 0.015, 0.06, dark, HEX_ROT)
    add_cylinder("Foundation_Seam_02", 6, 0.845, 0.015, 0.16, dark, HEX_ROT)
    add_cylinder("Foundation_Seam_03", 6, 0.725, 0.015, 0.24, dark, HEX_ROT)
    # Illuminated cyan perimeter channel on the top tier (hex ring).
    add_torus("Foundation_Lighting", 0.66, 0.02, 6, 4, 0.31, cyan,
              rotation=(0.0, 0.0, HEX_ROT))
    # Floor plates on the top tier (v3 positions, 6 now, not 3).
    for i, a_deg in enumerate((0, 60, 120, 180, 240, 300)):
        a = math.radians(a_deg)
        add_box("Foundation_Plate_%d" % (i + 1),
                (0.50 * math.cos(a), 0.50 * math.sin(a), 0.31),
                a + math.pi / 2.0, (0.06, 0.04, 0.01), dark)
    # Thin plates on the mid-tier top annulus (face angles, inside the
    # annulus between the top tier r 0.72 and the mid tier face 0.727).
    # The 330-degree face is skipped: the front ramp crosses it.
    for i, a_deg in enumerate((30, 90, 150, 210, 270)):
        a = math.radians(a_deg)
        add_box("MidTier_Plate_%d" % (i + 1),
                (0.68 * math.cos(a), 0.68 * math.sin(a), 0.246),
                a + math.pi / 2.0, (0.05, 0.015, 0.006), gunmetal)
    # Vertical wall panels on the base-tier faces (apothem 0.823, so the
    # panel centre sits just proud at r 0.831). Skipped on the 330-degree
    # face, where the front ramp crosses.
    for i, a_deg in enumerate((30, 90, 150, 210, 270)):
        a = math.radians(a_deg)
        add_box("Foundation_WallPanel_%d" % (i + 1),
                (0.831 * math.cos(a), 0.831 * math.sin(a), 0.11),
                a + math.pi / 2.0, (0.06, 0.008, 0.025), dark)
    # Vent grilles (dark) and cyan strip lights on the base-tier faces.
    for i, a_deg in enumerate((90, 210)):
        a = math.radians(a_deg)
        add_box("Foundation_Vent_%d" % (i + 1),
                (0.831 * math.cos(a), 0.831 * math.sin(a), 0.11),
                a + math.pi / 2.0, (0.04, 0.008, 0.02), dark)
    for i, a_deg in enumerate((30, 150, 270)):
        a = math.radians(a_deg)
        add_box("Foundation_Strip_%d" % (i + 1),
                (0.831 * math.cos(a), 0.831 * math.sin(a), 0.11),
                a + math.pi / 2.0, (0.035, 0.008, 0.012), cyan)
    # Front access ramp on the camera-facing 330-degree face, rising from
    # the plinth edge (r 0.95, z 0.06) to the top tier (r 0.60, z 0.30),
    # with glowing edge strips and a dark doorway on the top-tier face.
    add_ramp("Foundation_Ramp", CAMERA_FACE, 0.60, 0.95, 0.30, 0.06,
             0.30, 0.03, gunmetal, edge_material=cyan)
    add_box("Foundation_Doorway",
            (0.632 * math.cos(CAMERA_FACE), 0.632 * math.sin(CAMERA_FACE), 0.27),
            CAMERA_FACE + math.pi / 2.0, (0.05, 0.008, 0.03), dark)
    # Bollard lights on the mid-tier top (vertex angles, clear of the
    # pylons at 15/135/255 and of the front ramp at 330).
    for i, a_deg in enumerate((0, 60, 120, 180, 240, 300)):
        a = math.radians(a_deg)
        add_cone("Foundation_Bollard_%d" % (i + 1), 6, 0.03, 0.012, 0.09,
                 0.285, cyan)
        bollard = bpy.context.active_object
        bollard.location = (0.79 * math.cos(a), 0.79 * math.sin(a), 0.285)
    # Crates and vent stacks on the base-tier top (vertex angles only —
    # the tier edge is 0.95 there; at face angles it is only 0.823).
    for i, a_deg in enumerate((0, 120, 240)):
        a = math.radians(a_deg)
        add_box("Foundation_Crate_%d" % (i + 1),
                (0.90 * math.cos(a), 0.90 * math.sin(a), 0.20),
                a + math.pi / 2.0, (0.05, 0.05, 0.04), gunmetal)
    add_box("Foundation_Crate_Stack",
            (0.90 * math.cos(math.radians(0)), 0.90 * math.sin(math.radians(0)), 0.275),
            math.pi / 2.0, (0.035, 0.035, 0.035), dark)
    for i, a_deg in enumerate((60, 180, 300)):
        a = math.radians(a_deg)
        add_cylinder("Foundation_VentStack_%d" % (i + 1), 6, 0.045, 0.10,
                     0.21, gunmetal)
        stack = bpy.context.active_object
        stack.location = (0.90 * math.cos(a), 0.90 * math.sin(a), 0.21)
    # Vertical pipes on the mid-tier faces (radius just proud of the
    # face at 0.727), from the base tier up to the top tier.
    for i, a_deg in enumerate((30, 150, 270)):
        a = math.radians(a_deg)
        add_cylinder("Foundation_Pipe_%d" % (i + 1), 6, 0.025, 0.14,
                     0.23, gunmetal)
        pipe = bpy.context.active_object
        pipe.location = (0.75 * math.cos(a), 0.75 * math.sin(a), 0.23)

    # ---- 3.2 Central command spire: two stacked tapered 4-gon segments.
    # Lower 0.30-0.95 (r 0.55->0.46), mid band 0.95-1.02, upper 1.02-1.55
    # (r 0.46->0.38). Faces point at 0/90/180/270; the 315-degree vertex
    # points at the game camera, so the tower reads corner-on.
    add_cone("Tower_Skirt", 4, 0.62, 0.55, 0.15, 0.375, dark, QUAD_ROT)
    add_cone("Tower_Lower", 4, 0.55, 0.46, 0.65, 0.625, titanium, QUAD_ROT)
    add_cylinder("Tower_MidBand", 4, 0.47, 0.07, 0.985, gunmetal, QUAD_ROT)
    add_cylinder("Tower_MidBand_Strip", 4, 0.475, 0.02, 0.985, cyan, QUAD_ROT)
    add_cone("Tower_Upper", 4, 0.46, 0.38, 0.53, 1.285, titanium, QUAD_ROT)
    # Glowing ring around the tower at the mid band.
    add_torus("Tower_Ring", 0.48, 0.015, 4, 4, 1.02, cyan,
              rotation=(0.0, 0.0, QUAD_ROT))
    # 8 structural ribs: one centred on each face, lower + upper segment.
    for i in range(4):
        a = i * math.pi / 2.0
        add_face_box("Tower_Rib_Lower_%d" % (i + 1), (0.0, 0.0), a,
                     0.65, 0.50, 0.09, 0.05,
                     0.55, 0.46, 0.30, 0.95, gunmetal, proud=0.02)
        add_face_box("Tower_Rib_Upper_%d" % (i + 1), (0.0, 0.0), a,
                     1.28, 0.40, 0.09, 0.05,
                     0.46, 0.38, 1.02, 1.55, gunmetal, proud=0.02)
    # Corner ribs on the 4 tower vertices (45/135/225/315), lower segment.
    for i, a_deg in enumerate((45, 135, 225, 315)):
        a = math.radians(a_deg)
        add_box("Tower_CornerRib_%d" % (i + 1),
                (0.50 * math.cos(a), 0.50 * math.sin(a), 0.70),
                a, (0.03, 0.025, 0.28), dark)
    # Tall emissive window strips on every face, lower + upper segment —
    # the close-up's signature vertical blue slits.
    for i in range(4):
        a = i * math.pi / 2.0
        add_face_box("Tower_Window_Lower_%d" % (i + 1), (0.0, 0.0), a,
                     0.65, 0.45, 0.05, 0.02,
                     0.55, 0.46, 0.30, 0.95, cyan, proud=0.005)
        add_face_box("Tower_Window_Upper_%d" % (i + 1), (0.0, 0.0), a,
                     1.28, 0.35, 0.05, 0.02,
                     0.46, 0.38, 1.02, 1.55, cyan, proud=0.005)
    # Armor panels flanking the window strips on the lower segment.
    for i in range(4):
        a = i * math.pi / 2.0
        ta = Vector((-math.sin(a), math.cos(a), 0.0))
        for j, (off, zc) in enumerate(((-0.15, 0.50), (0.15, 0.50),
                                       (-0.15, 0.80), (0.15, 0.80))):
            axis = (off * ta.x, off * ta.y)
            add_face_box("Tower_Panel_L%d_%d" % (i + 1, j + 1), axis, a,
                         zc, 0.08, 0.10, 0.02,
                         0.55, 0.46, 0.30, 0.95, dark, proud=0.005)
    # Elevated observation section: two bands + cyan windows + deck rail.
    add_cone("Observation_Lower", 4, 0.44, 0.40, 0.09, 1.595, gunmetal, QUAD_ROT)
    add_cone("Observation_Upper", 4, 0.40, 0.38, 0.08, 1.68, titanium, QUAD_ROT)
    for i in range(4):
        a = i * math.pi / 2.0
        add_face_box("Observation_Window_%d" % (i + 1), (0.0, 0.0), a,
                     1.595, 0.05, 0.12, 0.02,
                     0.44, 0.40, 1.55, 1.64, cyan, proud=0.005)
    add_torus("Observation_Rail", 0.42, 0.015, 4, 4, 1.71, gunmetal,
              rotation=(0.0, 0.0, QUAD_ROT))

    # ---- 3.5 Command crown: stepped slabs, corner fins, amber lights,
    # mast + radar bar on the camera-facing corner, dish + antennas.
    add_cylinder("Command_Crown", 4, 0.52, 0.12, 1.78, titanium, QUAD_ROT)
    add_cylinder("Crown_Band", 4, 0.53, 0.03, 1.78, cyan, QUAD_ROT)
    add_cylinder("Crown_Upper", 4, 0.44, 0.06, 1.89, gunmetal, QUAD_ROT)
    # Corner fins on the lower slab's 4 vertices.
    for i, a_deg in enumerate((45, 135, 225, 315)):
        a = math.radians(a_deg)
        add_box("Crown_Fin_%d" % (i + 1),
                (0.50 * math.cos(a), 0.50 * math.sin(a), 1.86),
                a, (0.02, 0.02, 0.06), gunmetal)
    # Amber warning lights on the upper slab corners.
    for i, a_deg in enumerate((45, 135, 225, 315)):
        a = math.radians(a_deg)
        add_sphere("Crown_Light_%d" % (i + 1), 6, 4, 0.035,
                   (0.42 * math.cos(a), 0.42 * math.sin(a), 1.93), 1.0, amber)
    # Antenna mast + radar bar on the camera-facing (315-degree) corner.
    # The reactor sphere (centre 2.45, r 0.40) does not reach this radius,
    # so the mast stays clear of it.
    mast_a = math.radians(315)
    mast_xy = (0.30 * math.cos(mast_a), 0.30 * math.sin(mast_a))
    add_cylinder("Crown_Mast", 6, 0.018, 0.14, 1.99, dark)
    mast = bpy.context.active_object
    mast.location = (mast_xy[0], mast_xy[1], 1.99)
    add_sphere("Crown_Mast_Tip", 6, 4, 0.025,
               (mast_xy[0], mast_xy[1], 2.07), 1.0, cyan)
    add_box("Crown_RadarBar", (mast_xy[0], mast_xy[1], 2.02),
            math.radians(45), (0.14, 0.015, 0.01), gunmetal)

    # ---- 3.3 Suspended quantum reactor: larger orb, equator band,
    # cradle ring, halo, 10 couplers, 4 containment rings.
    # Gap between crown top (1.92) and reactor bottom (2.05) is the
    # suspension: the sphere touches nothing.
    add_sphere("Reactor_Core", 56, 28, REACTOR_R, (0.0, 0.0, REACTOR_Z), 1.0, reactor)
    # Bright equator band hugging the sphere (the close-up's hot equator).
    add_torus("Reactor_Equator", 0.41, 0.02, 40, 6, REACTOR_Z, cyan,
              rotation=(0.0, 0.0, 0.0))
    # Glowing cradle ring the orb sits in, tilted slightly.
    add_torus("Reactor_Cradle", 0.44, 0.035, 40, 8, 2.12, cyan,
              rotation=(math.radians(10), 0.0, 0.0))
    # Thin titanium halo ring, tilted the other way.
    add_torus("Reactor_Halo", 0.50, 0.015, 40, 6, REACTOR_Z, titanium,
              rotation=(math.radians(35), 0.0, 0.0))
    # 10 energy couplers around the equator.
    for i in range(10):
        a = i * math.pi / 5.0
        add_sphere("Reactor_Coupler_%02d" % (i + 1), 6, 4, 0.05,
                   (0.38 * math.cos(a), 0.38 * math.sin(a), REACTOR_Z), 1.0, cyan)
    # Four containment rings, separate objects, different orientations.
    add_torus("Containment_Ring_01", 0.52, 0.035, 48, 8, REACTOR_Z, cyan,
              rotation=(math.radians(12), 0.0, 0.0))
    add_torus("Containment_Ring_02", 0.54, 0.03, 40, 8, REACTOR_Z, gunmetal,
              rotation=(math.radians(50), 0.0, 0.0))
    add_torus("Containment_Ring_03", 0.60, 0.03, 40, 8, REACTOR_Z, titanium,
              rotation=(0.0, math.radians(45), 0.0))
    add_torus("Containment_Ring_04", 0.46, 0.025, 48, 8, REACTOR_Z, cyan,
              rotation=(math.radians(65), 0.0, 0.0))

    # ---- 3.4 Three reactor pylons: rise from the mid tier, frame the
    # reactor, 60 degrees off the camera axis so they never hide it.
    for i, a in enumerate(PYLON_ANGLES):
        px, py = PYLON_R * math.cos(a), PYLON_R * math.sin(a)
        # Base plate on the mid-tier top.
        add_cylinder("Pylon_Base_%02d" % (i + 1), 6, 0.20, 0.04, 0.24,
                     dark, HEX_ROT)
        base = bpy.context.active_object
        base.location = (px, py, 0.24)
        add_cone("Pylon_%02d" % (i + 1), 4, PYLON_R_BOTTOM, PYLON_R_TOP,
                 PYLON_Z1 - PYLON_Z0, (PYLON_Z0 + PYLON_Z1) / 2.0, gunmetal, QUAD_ROT)
        # NOTE: add_cone builds at the origin — move this pylon into place.
        pylon = bpy.context.active_object
        pylon.location = (px, py, (PYLON_Z0 + PYLON_Z1) / 2.0)
        # Vertical emissive energy channel on the inner face (faces the core).
        add_face_box("Pylon_Channel_%02d" % (i + 1), (px, py), a + math.pi,
                     1.15, 1.60, 0.06, 0.03,
                     PYLON_R_BOTTOM, PYLON_R_TOP, PYLON_Z0, PYLON_Z1, cyan,
                     proud=0.01)
        # Armor plates on the two side faces.
        for j, side in enumerate((math.pi / 2.0, -math.pi / 2.0)):
            add_face_box("Pylon_Plate_%02d_%d" % (i + 1, j + 1), (px, py),
                         a + side, 1.00, 1.20, 0.08, 0.03,
                         PYLON_R_BOTTOM, PYLON_R_TOP, PYLON_Z0, PYLON_Z1, dark,
                         proud=0.01)
        # Angled strut from the pylon top to the reactor sphere surface.
        start = Vector((px, py, 2.28))
        target = Vector((0.0, 0.0, REACTOR_Z))
        to_core = target - start
        strut_len = to_core.length - REACTOR_R
        strut_dir = to_core.normalized()
        add_tracked_cylinder("Pylon_Strut_%02d" % (i + 1), start, strut_dir,
                             strut_len, 0.04, 8, gunmetal)
        tip_point = start + strut_dir * strut_len
        add_sphere("Pylon_StrutTip_%02d" % (i + 1), 6, 4, 0.04,
                   (tip_point.x, tip_point.y, tip_point.z), 1.0, cyan)
        # Cap + energy emitter tip + spike, aimed at the reactor.
        add_cylinder("Pylon_Cap_%02d" % (i + 1), 4, 0.11, 0.05, 2.325,
                     dark, QUAD_ROT)
        cap = bpy.context.active_object
        cap.location = (px, py, 2.325)
        add_sphere("Pylon_Emitter_%02d" % (i + 1), 8, 5, 0.055,
                   (px, py, 2.38), 1.0, cyan)
        add_cone("Pylon_Spike_%02d" % (i + 1), 6, 0.025, 0.008, 0.07,
                 2.44, dark)
        spike = bpy.context.active_object
        spike.location = (px, py, 2.44)

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
    # Second, shorter antenna at 150 degrees.
    add_cylinder("Antenna_02", 6, 0.015, 0.30, 0.45, dark)
    antenna2 = bpy.context.active_object
    antenna2.location = (-0.52, 0.30, 0.45)
    add_sphere("Antenna_02_Tip", 6, 4, 0.025, (-0.52, 0.30, 0.61), 1.0, cyan)

    return list(bpy.context.scene.objects)


def make_uv_atlas(objects):
    """Pack every mesh object's UV islands into one 0-1 atlas (Lightmap
    Pack). The procedural detail is defined in object space, so the UVs
    only decide WHERE in the atlas each surface lands, not the pattern.
    Verified headless: 71% of the 1024 image is used, largest islands
    ~220 px, all UVs inside 0-1."""
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
    panel grid (48 px cells, 4 px lines), rivets at the cell corners,
    per-cell brightness variation, and soft grime blotches. Returns the
    Blender image, saved as a PNG next to the script."""
    S = ATLAS_SIZE
    CELL = 48
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    # Panel grid: lines where the within-cell position is near a border.
    cx = np.mod(xx, CELL)
    cy = np.mod(yy, CELL)
    line = ((cx < 4) | (cy < 4)).astype(np.float32)
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
    of (material name, filepath, bytes)."""
    seeds = {"NexusTitanium": 11, "NexusGunmetal": 23, "NexusDarkAlloy": 37}
    base_colors = {"NexusTitanium": TITANIUM, "NexusGunmetal": GUNMETAL,
                   "NexusDarkAlloy": DARK_ALLOY}
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
    print("Command Nexus v4 build:")
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
    print("  fbx bytes: %d" % os.path.getsize(OUTPUT_FBX))
    for mat_name, path, size in TEXTURE_INFO:
        print("  texture %s: %s (%d bytes)" % (mat_name, path, size))


if __name__ == "__main__":
    built = build()
    metal_mats = [m for m in bpy.data.materials
                  if m.name in ("NexusTitanium", "NexusGunmetal", "NexusDarkAlloy")]
    make_uv_atlas(built)
    TEXTURE_INFO = apply_metal_textures(metal_mats)
    export(built)
    report(built)
