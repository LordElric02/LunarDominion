# BuildResearchLabV5.py — Lunar Dominion: Research Lab v5
# (graphics upgrade: v2 + surface detail + finer textures).
#
# v5 = v2 (detailed geometry + baked panel textures) + a new section
# of greebles (section "v5 greebles" in build()): 2 more vent grilles
# on the plinth, 2 junction boxes on the tower, a second glowing
# window band lower on the tower, 2 more sample crates, 2 hazard
# bars on the tier, a second hologram emitter, the fourth bollard,
# a third annex vent, a sensor pod on the observation deck, and a
# small antenna on the dome. The v2 geometry is untouched, so the
# drop-in contract is unchanged: footprint 2.2 x 2.2, height ~2.26
# (antenna tip). Textures are finer: 32 px panel cells + a faint
# 16 px sub-grid (was 48 px).
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildResearchLabV5.py
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

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# Draft outputs next to this script. When installed in the repo, rename the
# exported FBX to ResearchLab.fbx and move the PNGs next to it in
# Assets/_Project/Resources/Models/.
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "ResearchLabV5.fbx")
ATLAS_SIZE = 1024
TEXTURE_NAMES = {
    "LabTitanium": "ResearchLabV5_Titanium.png",
    "LabGunmetal": "ResearchLabV5_Gunmetal.png",
    "LabDarkAlloy": "ResearchLabV5_DarkAlloy.png",
    "LabPurple": "ResearchLabV5_Purple.png",
}

# Palette: v1 Lab colours + spec section 4 metals (linear RGB).
TITANIUM = (0.78, 0.82, 0.85)      # #C6D0DA primary armor
GUNMETAL = (0.27, 0.33, 0.39)      # #465363
DARK_ALLOY = (0.09, 0.15, 0.22)    # #172638 recesses
LAB_PURPLE = (0.55, 0.30, 0.85)    # v1 tower / annex purple
LAB_DARK = (0.30, 0.18, 0.45)      # v1 dark purple (dome, door)
LAB_LIGHT = (0.90, 0.88, 0.80)     # v1 pale doorway
CYAN_GLOW = (0.20, 0.80, 1.00)     # saturated cyan windows / tips
PURPLE_GLOW = (0.70, 0.40, 1.00)   # purple glow accent (deck strip, hologram)

# Layout constants (Blender units; 1 unit = 1 Unity unit).
TOWER_R = 0.75
TOWER_Z0 = 0.12
TOWER_Z1 = 1.57
DOME_Z = 1.57
DOME_TOP = 1.87


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

    titanium = make_material("LabTitanium", TITANIUM, metallic=0.8, roughness=0.30)
    gunmetal = make_material("LabGunmetal", GUNMETAL, metallic=0.9, roughness=0.40)
    dark = make_material("LabDarkAlloy", DARK_ALLOY, metallic=0.85, roughness=0.35)
    purple = make_material("LabPurple", LAB_PURPLE, metallic=0.15, roughness=0.55)
    labdark = make_material("LabDark", LAB_DARK, metallic=0.25, roughness=0.55)
    light = make_material("LabLight", LAB_LIGHT, metallic=0.10, roughness=0.45)
    glow = make_material("LabGlow", CYAN_GLOW, metallic=0.10, roughness=0.30,
                         emission=2.5)
    glowpurple = make_material("LabGlowPurple", PURPLE_GLOW, metallic=0.10,
                               roughness=0.30, emission=2.0)

    # ---- Foundation: stepped platform (v1 envelope 2.2 x 2.2).
    # Plinth 2.2 x 2.2 z 0-0.06, tier 2.0 x 2.0 z 0.06-0.12.
    add_box("Lab_Plinth", (0.0, 0.0, 0.03), 0.0, (1.10, 1.10, 0.03), dark)
    add_box("Lab_Tier", (0.0, 0.0, 0.09), 0.0, (1.00, 1.00, 0.03), titanium)
    # Vent grilles on the plinth faces (camera-facing -Y, and +X).
    add_box("Lab_VentGrille_01", (0.30, -1.11, 0.04), 0.0, (0.15, 0.01, 0.04), dark)
    add_box("Lab_VentGrille_02", (1.11, 0.30, 0.04), 0.0, (0.01, 0.15, 0.04), dark)

    # ---- Main tower: 24-sided cylinder r 0.75, z 0.12-1.57 (as v1).
    add_cylinder("Lab_Tower", 24, TOWER_R, 1.45, 0.845, purple)
    # 6 vertical ribs (thin boxes, proud of the tower wall). Skipped at
    # 0 deg (annex) and 270 deg (doorway) so they never cross a feature.
    for i, a_deg in enumerate((45, 90, 135, 180, 225, 315)):
        a = math.radians(a_deg)
        add_box("Lab_Rib_%02d" % (i + 1),
                (0.76 * math.cos(a), 0.76 * math.sin(a), 0.845),
                a, (0.015, 0.05, 0.72), titanium)
    # Horizontal segmentation rings around the tower.
    add_torus("Lab_Ring_01", 0.77, 0.02, 16, 3, 0.45, dark)
    add_torus("Lab_Ring_02", 0.77, 0.02, 16, 3, 1.45, dark)
    # Glowing cyan window band ringing the tower (v1 position, stronger
    # emission): r 0.78, z 1.11-1.39, with dark trim rings above/below.
    add_cylinder("Lab_WindowBand", 24, 0.78, 0.28, 1.25, glow)
    add_torus("Lab_BandTrim_01", 0.785, 0.015, 16, 3, 1.10, dark)
    add_torus("Lab_BandTrim_02", 0.785, 0.015, 16, 3, 1.40, dark)
    # Small cyan window slits on the tower's camera-facing sides.
    for i, a_deg in enumerate((300, 330)):
        a = math.radians(a_deg)
        add_box("Lab_WindowSlit_%02d" % (i + 1),
                (0.765 * math.cos(a), 0.765 * math.sin(a), 0.70),
                a, (0.015, 0.09, 0.05), glow)
    # Vertical pipes on the tower (camera-facing angles).
    for i, a_deg in enumerate((30, 300, 330)):
        a = math.radians(a_deg)
        add_cylinder("Lab_Pipe_%02d" % (i + 1), 8, 0.025, 1.33, 0.785,
                     gunmetal, x=0.77 * math.cos(a), y=0.77 * math.sin(a))
    # One pipe on the far side so the back is not bare.
    add_cylinder("Lab_Pipe_04", 8, 0.025, 1.33, 0.785,
                 gunmetal, x=0.77 * math.cos(math.radians(180)),
                 y=0.77 * math.sin(math.radians(180)))

    # ---- Observation deck: ring around the tower top (r 0.95, z 1.50-1.60),
    # railing torus + 4 posts, purple glow strip under its edge.
    add_cylinder("Lab_Deck", 24, 0.95, 0.10, 1.55, titanium)
    add_torus("Lab_DeckGlow", 0.95, 0.012, 16, 3, 1.50, glowpurple)
    add_torus("Lab_DeckRail", 0.93, 0.015, 16, 3, 1.70, gunmetal)
    for i, a_deg in enumerate((45, 135, 225, 315)):
        a = math.radians(a_deg)
        add_cylinder("Lab_RailPost_%02d" % (i + 1), 6, 0.012, 0.12, 1.64,
                     gunmetal, x=0.93 * math.cos(a), y=0.93 * math.sin(a))

    # ---- Domed roof: squashed sphere r 0.75, z-scale 0.4, centre z 1.57
    # (top 1.87, as v1), dark alloy, with a titanium base ring and 6
    # ribs hugging the dome profile (same trick as the Hydroponics dome:
    # a torus scaled in Y to an ellipse matching the squashed dome,
    # then stood in a vertical plane).
    add_sphere("Lab_Dome", 24, 12, 0.75, (0.0, 0.0, DOME_Z), 0.4, dark)
    add_torus("Lab_DomeRing", 0.75, 0.02, 16, 3, 1.57, titanium)
    for i in range(6):
        bpy.ops.mesh.primitive_torus_add(
            major_radius=0.76, minor_radius=0.02,
            major_segments=20, minor_segments=4,
            location=(0.0, 0.0, 0.0))
        rib = bpy.context.active_object
        rib.name = "Lab_DomeRib_%02d" % (i + 1)
        rib.scale = (1.0, 0.4, 1.0)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        rib.rotation_euler = (math.radians(90), 0.0, i * math.pi / 6.0)
        rib.location = (0.0, 0.0, DOME_Z)
        rib.data.materials.append(titanium)

    # ---- Antenna array: main antenna + glowing tip + radar bar, and two
    # shorter whip antennas on the dome edge.
    add_cylinder("Lab_Antenna", 8, 0.03, 0.30, 2.02, gunmetal)
    add_sphere("Lab_AntennaTip", 8, 6, 0.055, (0.0, 0.0, 2.20), 1.0, glow)
    add_box("Lab_RadarBar", (0.0, 0.0, 2.14), 0.0, (0.08, 0.01, 0.01), gunmetal)
    for i, (x, y) in enumerate(((0.45, 0.35), (-0.45, -0.35))):
        add_cylinder("Lab_Whip_%02d" % (i + 1), 6, 0.018, 0.15, 1.835,
                     gunmetal, x=x, y=y)
        add_sphere("Lab_WhipTip_%02d" % (i + 1), 6, 4, 0.028,
                   (x, y, 1.92), 1.0, glow)

    # ---- Side annex (v1 position, two storeys now): lower box
    # x 0.5-1.1, y -0.3-0.6, z 0.12-0.62; upper box x 0.55-1.05,
    # y -0.25-0.55, z 0.62-1.02; flat roof, cyan window strip on its
    # -Y face, roof vents, vent stack.
    add_box("Lab_Annex", (0.80, 0.15, 0.37), 0.0, (0.30, 0.45, 0.25), purple)
    add_box("Lab_AnnexTop", (0.80, 0.15, 0.82), 0.0, (0.25, 0.40, 0.20), purple)
    add_box("Lab_AnnexRoof", (0.80, 0.15, 1.04), 0.0, (0.27, 0.42, 0.02), dark)
    add_box("Lab_AnnexWindow", (0.80, -0.315, 0.45), 0.0, (0.20, 0.01, 0.05), glow)
    add_box("Lab_AnnexVent_01", (0.65, 0.00, 1.08), 0.0, (0.06, 0.06, 0.04), dark)
    add_box("Lab_AnnexVent_02", (0.95, 0.30, 1.08), 0.0, (0.06, 0.06, 0.04), dark)
    add_cylinder("Lab_VentStack", 8, 0.05, 0.12, 1.12, gunmetal, x=0.80, y=0.35)
    add_cylinder("Lab_VentCap", 8, 0.06, 0.03, 1.19, dark, x=0.80, y=0.35)

    # ---- Satellite dish on the annex roof (v1 layout, more detail):
    # pole, flared dish tilted ~52 deg toward the camera (-Y) side,
    # feed arm + glowing feed horn.
    add_cylinder("Lab_DishPole", 8, 0.035, 0.28, 1.16, gunmetal, x=0.78, y=0.15)
    add_cone("Lab_Dish", 16, 0.06, 0.34, 0.24, 1.40, gunmetal,
             x=0.78, y=0.05, rotation=(0.9, 0.0, 0.0))
    add_cylinder("Lab_FeedArm", 6, 0.012, 0.22, 1.462, gunmetal,
                 x=0.78, y=-0.028, rotation=(0.9, 0.0, 0.0))
    add_sphere("Lab_FeedHorn", 8, 6, 0.035, (0.78, -0.106, 1.524), 1.0, glow)
    # Small second dish on the observation deck edge (-X side).
    add_cylinder("Lab_Dish2Pole", 6, 0.025, 0.12, 1.66, gunmetal, x=-0.80, y=0.20)
    add_cone("Lab_Dish2", 12, 0.04, 0.18, 0.12, 1.76, gunmetal,
             x=-0.80, y=0.16, rotation=(0.9, 0.0, 0.0))

    # ---- Doorway on the camera-facing (-Y) face: pale frame, dark door,
    # step, glowing sign above.
    add_box("Lab_DoorFrame", (0.0, -0.76, 0.47), 0.0, (0.30, 0.02, 0.40), light)
    add_box("Lab_Door", (0.0, -0.775, 0.47), 0.0, (0.22, 0.025, 0.34), labdark)
    add_box("Lab_DoorStep", (0.0, -0.83, 0.145), 0.0, (0.30, 0.08, 0.025), gunmetal)
    add_box("Lab_DoorSign", (0.0, -0.79, 0.95), 0.0, (0.15, 0.015, 0.04), glow)

    # ---- Sample crates + sample container on the platform (-X side,
    # clear of the annex and the hologram).
    add_box("Lab_Crate_01", (-0.70, 0.70, 0.18), 0.4, (0.09, 0.09, 0.06), gunmetal)
    add_box("Lab_Crate_02", (-0.70, 0.70, 0.30), 0.4, (0.06, 0.06, 0.06), dark)
    add_box("Lab_Crate_03", (-0.88, 0.52, 0.16), -0.3, (0.07, 0.07, 0.04), dark)
    add_cylinder("Lab_Sample", 12, 0.09, 0.14, 0.19, purple, x=-0.70, y=-0.70)
    add_cylinder("Lab_SampleCap", 12, 0.095, 0.03, 0.275, glowpurple,
                 x=-0.70, y=-0.70)

    # ---- Holographic emitter on the camera-facing platform corner:
    # pedestal + floating purple glow ring + glowing core sphere.
    add_cylinder("Lab_HoloBase", 8, 0.09, 0.12, 0.18, dark, x=0.55, y=-0.85)
    add_torus("Lab_HoloRing", 0.14, 0.015, 16, 3, 0.42, glowpurple,
              x=0.55, y=-0.85)
    add_sphere("Lab_HoloCore", 8, 6, 0.05, (0.55, -0.85, 0.42), 1.0, glowpurple)

    # ---- Bollard lights on the platform corners (cyan glow).
    for i, (x, y) in enumerate(((0.95, 0.95), (-0.95, 0.95), (-0.95, -0.95))):
        add_cone("Lab_Bollard_%02d" % (i + 1), 6, 0.03, 0.012, 0.09, 0.165,
                 glow, x=x, y=y)

    # ---- v5 greebles (graphics upgrade): surface detail on the v2
    # geometry, all inside the v2 envelope (footprint 2.2 x 2.2,
    # z <= 2.26). Two more vent grilles on the plinth (v2 has -Y and
    # +X; these add +Y and -X).
    add_box("Lab_VentGrille_03", (-0.30, 1.11, 0.04), 0.0, (0.15, 0.01, 0.04), dark)
    add_box("Lab_VentGrille_04", (-1.11, -0.30, 0.04), 0.0, (0.01, 0.15, 0.04), dark)
    # Junction boxes on the tower wall (angled to face outward).
    # The annex covers 0 deg and the doorway 270 deg, so these go at
    # 135 and 225 deg on the back side.
    add_box("Lab_JunctionBox_01", (-0.548, 0.548, 0.60),
            math.radians(135), (0.12, 0.02, 0.10), gunmetal)
    add_box("Lab_JunctionBox_02", (-0.548, -0.548, 1.00),
            math.radians(225), (0.12, 0.02, 0.10), gunmetal)
    # A second, thinner glowing window band lower on the tower
    # (v2's is at z 1.11-1.39).
    add_cylinder("Lab_WindowBand2", 24, 0.78, 0.10, 0.60, glow)
    # Two more sample crates (v2 has 3, on the -X side).
    add_box("Lab_Crate_04", (-0.88, -0.52, 0.16), 0.3, (0.07, 0.07, 0.04), dark)
    add_box("Lab_Crate_05", (-0.50, 0.85, 0.16), -0.2, (0.07, 0.07, 0.04), gunmetal)
    # Hazard bars on the tier front (-Y) face (tier is 2.0 x 2.0, so
    # the face is at y = -1.00; the plinth face at -1.10 would push
    # the bars outside the 2.2 x 2.2 footprint).
    add_box("Lab_HazardBar_01", (-0.55, -1.005, 0.09), 0.0, (0.15, 0.01, 0.02), glowpurple)
    add_box("Lab_HazardBar_02", (0.55, -1.005, 0.09), 0.0, (0.15, 0.01, 0.02), glowpurple)
    # A second hologram emitter (v2's is at (0.55,-0.85); this one
    # mirrors it to the back-right corner).
    add_cylinder("Lab_HoloBase2", 8, 0.09, 0.12, 0.18, dark, x=0.55, y=0.85)
    add_torus("Lab_HoloRing2", 0.14, 0.015, 16, 3, 0.42, glowpurple,
              x=0.55, y=0.85)
    add_sphere("Lab_HoloCore2", 8, 6, 0.05, (0.55, 0.85, 0.42), 1.0, glowpurple)
    # The fourth bollard (v2 has 3 corners; this fills (0.95,-0.95)).
    add_cone("Lab_Bollard_04", 6, 0.03, 0.012, 0.09, 0.165, glow, x=0.95, y=-0.95)
    # A third vent on the annex roof (v2 has 2).
    add_box("Lab_AnnexVent_03", (0.80, -0.15, 1.08), 0.0, (0.06, 0.06, 0.04), dark)
    # A sensor pod on the observation deck with a glowing tip.
    add_cylinder("Lab_DeckSensor", 6, 0.05, 0.10, 1.66, gunmetal, x=0.60, y=-0.60)
    add_sphere("Lab_DeckSensorTip", 6, 4, 0.02, (0.60, -0.60, 1.72), 1.0, glow)
    # A small antenna on the dome (dome surface at (0.45,-0.35) is
    # z ~1.77, so the antenna sits proud at 1.86).
    add_cylinder("Lab_DomeAntenna2", 6, 0.012, 0.20, 1.86, gunmetal, x=0.45, y=-0.35)
    add_sphere("Lab_DomeAntennaTip2", 6, 4, 0.02, (0.45, -0.35, 1.97), 1.0, glow)

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
    the purple tower is the Lab's hero surface, so it gets its own
    panel texture instead of staying flat."""
    seeds = {"LabTitanium": 71, "LabGunmetal": 83, "LabDarkAlloy": 97,
             "LabPurple": 103}
    base_colors = {"LabTitanium": TITANIUM, "LabGunmetal": GUNMETAL,
                   "LabDarkAlloy": DARK_ALLOY, "LabPurple": LAB_PURPLE}
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
    print("Research Lab v5 build:")
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
    # Bounding box check: footprint must stay 2.2 x 2.2, height ~2.26.
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
                  if m.name in ("LabTitanium", "LabGunmetal", "LabDarkAlloy",
                                "LabPurple")]
    make_uv_atlas(built)
    TEXTURE_INFO = apply_metal_textures(metal_mats)
    export(built)
    report(built)
