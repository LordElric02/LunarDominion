# BuildNovaArsenalV2.py — Lunar Dominion: Nova Arsenal v2
# (detailed geometry + baked panel textures).
#
# DRAFT — not yet in the Unity project and NOT pushed to GitHub.
# Rebuilds the Nova Arsenal at the Nexus v4 standard: same
# footprint, height and position as the v1 Arsenal
# (BuildNovaArsenal.py, 168 tris — the ranged-weapons barracks, home
# of Pulse Troopers, Railgunners and Nova Snipers), but with real
# detail — a red weapons hall with pilasters, a pyramid roof with
# eave trim, a targeting spire with sensor bar and glowing tip, a
# railgun barrel plus a secondary barrel, a radar dish, a weapons
# rack, ammo crates, a firing-range target lane, a control panel and
# glowing orange targeting lights. Textures use the proven Nexus
# pipeline: pixel-generated with numpy (turned-up settings: 48 px
# cells, 4 px lines, 0.75 line darkening, +/-10% per-cell variation,
# stronger grime), Lightmap Pack UV atlas. No Cycles bake — it proved
# unusable headless (a flat-colour bake takes 5+ minutes on this VM).
#
# DROP-IN CONTRACT (from v1 — do not break it): base platform
# 2.0 x 2.0, main red arsenal block 1.5 x 1.3 x 0.9 at (0, 0, 0.57)
# with a sloped pyramid roof (apex z ~1.37), a tall targeting spire
# at (0.55, -0.45) with a glowing tip cone (apex z 1.84, the high
# point), an angled cannon/railgun barrel on the roof (centre
# z 1.35, tilted toward -Y), a pale doorway and glowing sight strip
# on the camera-facing (-Y) face, and an ammo crate plus a vent on
# the +X face. v2 keeps the same outer envelope: x/y within +/-1.00,
# z 0-1.84. Red identity kept: red block, dark base/roof, gunmetal
# spire/barrel, orange glow; the frame is spec-palette
# titanium/gunmetal/dark alloy.
#
# CAMERA NOTE (from v1): the game camera sits at Unity +X/+Z, so
# Blender -Y and +X faces are the visible ones. Details (doorway,
# sight strip, windows, control panel, target lane on -Y; crates,
# weapon rack, vent, pipe on +X) go on -Y / +X faces. The Arsenal
# sits on the far side of the base from the camera, so nothing
# important goes on the +Y (back) face.
#
# EMISSION (lesson from the Hydroponics dome): pale tints read
# washed-out white in Unity. The glowing parts here use the v1's
# saturated orange (1.00, 0.55, 0.15) at 2.5 for the spire tip,
# sight strip, windows, sensor-bar tips, muzzle rings, control
# screen, target rings and bollards — not pale tints. There is no
# glass on the Arsenal, so no transparency import caveat.
#
# The pyramid roof keeps the v1's dark red-brown and gets its own
# panel texture (a fifth texture, not four): the roof is the largest
# surface the isometric game camera sees, and the first preview
# showed an untextured roof reading as a flat brown slab. The base
# colour is brightened slightly from the v1's (0.38, 0.18, 0.13) so
# the panel detail reads — the Extractor's dark-alloy roof first read
# as a near-black slab for the same reason.
#
# Emission stays procedural (emissive colour inputs, not baked), so
# the glowing parts keep glowing.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildNovaArsenalV2.py
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
# exported FBX to NovaArsenal.fbx and move the PNGs next to it in
# Assets/_Project/Resources/Models/.
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "NovaArsenalV2.fbx")
ATLAS_SIZE = 1024
TEXTURE_NAMES = {
    "NovaTitanium": "NovaArsenalV2_Titanium.png",
    "NovaGunmetal": "NovaArsenalV2_Gunmetal.png",
    "NovaDarkAlloy": "NovaArsenalV2_DarkAlloy.png",
    "NovaRed": "NovaArsenalV2_Red.png",
    "NovaRoof": "NovaArsenalV2_Roof.png",
}

# Palette: v1 Arsenal colours + spec section 4 metals (linear RGB).
TITANIUM = (0.78, 0.82, 0.85)      # #C6D0DA primary armor
GUNMETAL = (0.27, 0.33, 0.39)      # #465363
DARK_ALLOY = (0.09, 0.15, 0.22)    # #172638 recesses
NOVA_RED = (0.85, 0.35, 0.20)      # v1 main block red
NOVA_ROOF = (0.42, 0.20, 0.14)     # v1 dark red-brown roof, slightly
                                   # brightened so the panel texture reads
NOVA_LIGHT = (0.90, 0.85, 0.70)    # v1 pale doorway
NOVA_GLOW = (1.00, 0.55, 0.15)     # v1 orange glow (saturated)

# Layout constants (Blender units; 1 unit = 1 Unity unit).
BASE_HALF = 1.00
MAX_Z = 1.84  # v1 spire tip apex; nothing in v2 goes above this.


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


def add_sphere(name, segments, rings, radius, location, z_scale, material,
               rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=radius, location=location
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (1.0, 1.0, z_scale)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.rotation_euler = rotation
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

    titanium = make_material("NovaTitanium", TITANIUM, metallic=0.8, roughness=0.30)
    gunmetal = make_material("NovaGunmetal", GUNMETAL, metallic=0.9, roughness=0.40)
    dark = make_material("NovaDarkAlloy", DARK_ALLOY, metallic=0.85, roughness=0.35)
    red = make_material("NovaRed", NOVA_RED, metallic=0.55, roughness=0.45)
    roof = make_material("NovaRoof", NOVA_ROOF, metallic=0.45, roughness=0.50)
    light = make_material("NovaLight", NOVA_LIGHT, metallic=0.10, roughness=0.45)
    glow = make_material("NovaGlow", NOVA_GLOW, metallic=0.10, roughness=0.30,
                         emission=2.5)

    # ---- Foundation: dark plinth 2.0 x 2.0 z 0-0.06, titanium tier
    # 1.9 x 1.9 z 0.06-0.12 (v1 base platform is 2.0 x 2.0 x 0.12).
    add_box("Nova_Plinth", (0.0, 0.0, 0.03), 0.0, (1.00, 1.00, 0.03), dark)
    add_box("Nova_Tier", (0.0, 0.0, 0.09), 0.0, (0.95, 0.95, 0.03), titanium)
    # Recessed seam ring between plinth and tier.
    add_torus("Nova_Seam", 0.96, 0.012, 12, 3, 0.06, dark)
    # Vent grilles on the tier faces (camera-facing -Y, and +X).
    add_box("Nova_VentGrille_01", (0.30, -0.955, 0.09), 0.0, (0.15, 0.01, 0.02), dark)
    add_box("Nova_VentGrille_02", (0.955, 0.30, 0.09), 0.0, (0.01, 0.15, 0.02), dark)

    # ---- Main arsenal block: red, 1.5 x 1.3 x 0.9 at (0, 0, 0.57),
    # top z 1.02 (as v1).
    add_box("Nova_Block", (0.0, 0.0, 0.57), 0.0, (0.75, 0.65, 0.45), red)
    # 4 corner pilasters, proud of the block faces.
    for i, (x, y) in enumerate(((-0.72, -0.62), (-0.72, 0.62),
                                (0.72, -0.62), (0.72, 0.62))):
        add_box("Nova_Pilaster_%02d" % (i + 1), (x, y, 0.57), 0.0,
                (0.03, 0.03, 0.45), gunmetal)
    # Segmentation band around the block.
    add_box("Nova_Band", (0.0, 0.0, 0.80), 0.0, (0.76, 0.66, 0.03), dark)

    # ---- Camera-facing (-Y) face: pale doorway (as v1), glowing
    # sight strip (as v1), two glowing windows with dark frames, and
    # a control panel with a glowing screen.
    add_box("Nova_Door", (0.10, -0.67, 0.42), 0.0, (0.20, 0.03, 0.30), light)
    add_box("Nova_Sight", (-0.35, -0.67, 0.85), 0.0, (0.15, 0.025, 0.04), glow)
    for i, (x, z) in enumerate(((-0.60, 0.55), (0.55, 0.72))):
        add_box("Nova_WindowFrame_%02d" % (i + 1), (x, -0.655, z), 0.0,
                (0.10, 0.008, 0.08), dark)
        add_box("Nova_Window_%02d" % (i + 1), (x, -0.66, z), 0.0,
                (0.08, 0.01, 0.06), glow)
    add_box("Nova_Control", (0.58, -0.67, 0.30), 0.0, (0.10, 0.03, 0.15), titanium)
    add_box("Nova_ControlScreen", (0.58, -0.70, 0.34), 0.0, (0.07, 0.008, 0.05), glow)

    # ---- +X face: ammo crate (as v1), a second crate, a vent grille
    # with slats (as v1), a weapon rack with three rifle barrels, and
    # a vertical pipe.
    add_box("Nova_Crate", (0.85, 0.25, 0.27), 0.0, (0.14, 0.15, 0.15), gunmetal)
    add_box("Nova_Crate2", (0.85, -0.35, 0.24), 0.0, (0.12, 0.12, 0.12), dark)
    add_box("Nova_Vent", (0.77, -0.25, 0.80), 0.0, (0.025, 0.15, 0.10), dark)
    for i, z in enumerate((0.75, 0.80, 0.85)):
        add_box("Nova_VentSlat_%02d" % (i + 1), (0.80, -0.25, z), 0.0,
                (0.01, 0.12, 0.015), gunmetal)
    # Weapon rack: dark frame on the +X face near the back corner,
    # three vertical rifle barrels with glowing tips.
    add_box("Nova_Rack", (0.77, 0.55, 0.45), 0.0, (0.02, 0.15, 0.25), dark)
    for i, y in enumerate((0.45, 0.55, 0.65)):
        add_cylinder("Nova_Rifle_%02d" % (i + 1), 6, 0.015, 0.42, 0.50,
                     gunmetal, x=0.80, y=y)
        add_cylinder("Nova_RifleTip_%02d" % (i + 1), 6, 0.02, 0.05, 0.73,
                     glow, x=0.80, y=y)
    # Vertical pipe on the +X face near the front corner.
    add_cylinder("Nova_VPipe", 12, 0.05, 0.80, 0.60, gunmetal, x=0.78, y=-0.55)
    add_torus("Nova_VPipeBand", 0.05, 0.012, 12, 4, 0.60, dark, x=0.78, y=-0.55)

    # ---- Pyramid roof (as v1): 4-sided cone, square aligned by
    # rotating 45 degrees about Z. Base at z 1.02, apex at ~1.37.
    # Dark red-brown (v1 roof colour), untextured — see header note.
    add_cone("Nova_Roof", 4, 1.05, 0.0, 0.35, 1.195, roof,
             x=0.0, y=0.0, rotation=(0.0, 0.0, math.radians(45)))
    # Gunmetal eave trim: a square frame just above the block top,
    # proud of the roof base edge.
    add_box("Nova_Eave_01", (0.0, -0.76, 1.035), 0.0, (0.78, 0.03, 0.025), gunmetal)
    add_box("Nova_Eave_02", (0.0, 0.76, 1.035), 0.0, (0.78, 0.03, 0.025), gunmetal)
    add_box("Nova_Eave_03", (-0.76, 0.0, 1.035), 0.0, (0.03, 0.78, 0.025), gunmetal)
    add_box("Nova_Eave_04", (0.76, 0.0, 1.035), 0.0, (0.03, 0.78, 0.025), gunmetal)
    # Cap disc on the roof apex.
    add_cylinder("Nova_RoofCap", 12, 0.10, 0.05, 1.39, gunmetal, x=0.0, y=0.0)

    # ---- Targeting spire (as v1): gunmetal cylinder r 0.10, 0.70
    # tall at (0.55, -0.45), z 1.02-1.72, with a glowing orange tip
    # cone (apex z 1.84, the high point). Two band rings and a sensor
    # bar with glowing tips added.
    add_cylinder("Nova_Spire", 8, 0.10, 0.70, 1.37, gunmetal, x=0.55, y=-0.45)
    add_torus("Nova_SpireBand_01", 0.11, 0.015, 12, 4, 1.25, dark, x=0.55, y=-0.45)
    add_torus("Nova_SpireBand_02", 0.11, 0.015, 12, 4, 1.55, dark, x=0.55, y=-0.45)
    add_cone("Nova_Tip", 4, 0.07, 0.0, 0.12, 1.78, glow,
             x=0.55, y=-0.45)
    add_box("Nova_SensorBar", (0.55, -0.45, 1.60), 0.0, (0.17, 0.025, 0.025), gunmetal)
    add_box("Nova_SensorTip_01", (0.36, -0.45, 1.60), 0.0, (0.015, 0.03, 0.03), glow)
    add_box("Nova_SensorTip_02", (0.74, -0.45, 1.60), 0.0, (0.015, 0.03, 0.03), glow)

    # ---- Cannon/railgun barrel (as v1): gunmetal cylinder r 0.10,
    # 1.1 long, centre (0, -0.10, 1.35), angled ~34 deg up toward the
    # camera-facing (-Y) side. Dark mount block under the base end
    # (as v1), plus a glowing muzzle ring at the top end.
    add_cylinder("Nova_Barrel", 12, 0.10, 1.10, 1.35, gunmetal,
                 x=0.0, y=-0.10, rotation=(0.6, 0.0, 0.0))
    # (centred 0.05 down the barrel axis from the muzzle end: the
    # tilted ring's upper rim would otherwise reach z 1.87, above
    # the v1 1.84 envelope.)
    add_torus("Nova_Muzzle", 0.11, 0.015, 12, 4, 1.76, glow,
              rotation=(0.6, 0.0, 0.0), x=0.0, y=-0.38)
    add_box("Nova_Mount", (0.0, 0.15, 1.10), 0.0, (0.15, 0.15, 0.10), dark)
    # Secondary, shorter railgun barrel parallel to the main one,
    # offset +X, with its own glowing muzzle ring and mount.
    add_cylinder("Nova_Barrel2", 12, 0.06, 0.80, 1.30, gunmetal,
                 x=0.25, y=-0.05, rotation=(0.6, 0.0, 0.0))
    add_torus("Nova_Muzzle2", 0.065, 0.012, 12, 4, 1.63, glow,
              rotation=(0.6, 0.0, 0.0), x=0.25, y=-0.276)
    add_box("Nova_Mount2", (0.25, 0.15, 1.06), 0.0, (0.08, 0.08, 0.08), dark)

    # ---- Radar dish on the roof front-left (camera-facing): pole,
    # tilted squashed sphere dish, glowing feed horn. Roof surface
    # there is ~z 1.11. (First placed on the back-left; it was hidden
    # behind the spire/barrel from the game camera, so it moved to
    # the front-left to balance the spire on the front-right.)
    add_cylinder("Nova_DishPole", 6, 0.02, 0.25, 1.175, gunmetal,
                 x=-0.55, y=-0.45)
    add_sphere("Nova_Dish", 16, 8, 0.14, (-0.55, -0.45, 1.33), 0.35,
               gunmetal, rotation=(-0.5, 0.0, 0.0))
    add_sphere("Nova_DishFeed", 6, 4, 0.03, (-0.55, -0.58, 1.40), 1.0, glow)

    # ---- Firing-range target lane on the platform front-left: two
    # target posts with glowing rings, clear of the doorway.
    for i, x in enumerate((-0.55, -0.80)):
        add_box("Nova_Target_%02d" % (i + 1), (x, -0.85, 0.285), 0.0,
                (0.04, 0.02, 0.165), dark)
        add_torus("Nova_TargetRing_%02d" % (i + 1), 0.07, 0.015, 12, 4, 0.42,
                  glow, rotation=(math.radians(90), 0.0, 0.0), x=x, y=-0.88)

    # ---- Ammo crate on the platform front-right, with a glow strip.
    add_box("Nova_PlatformCrate", (0.85, -0.85, 0.30), 0.0, (0.13, 0.13, 0.12), dark)
    add_box("Nova_PlatformCrateStrip", (0.85, -0.85, 0.36), 0.0,
            (0.14, 0.02, 0.02), glow)

    # ---- Glowing bollards on the platform back corners.
    add_cone("Nova_Bollard_01", 6, 0.03, 0.012, 0.09, 0.225, glow,
             x=-0.92, y=0.92)
    add_cone("Nova_Bollard_02", 6, 0.03, 0.012, 0.09, 0.225, glow,
             x=0.92, y=0.92)

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
    panel grid (48 px cells, 4 px lines), rivets at the cell corners,
    per-cell brightness variation, and soft grime blotches. Returns the
    Blender image, saved as a PNG next to the script. Turned-up
    settings (same as the Nexus textures after the faint first pass)."""
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
    of (material name, filepath, bytes). Five textures here, not three:
    the red block is the Arsenal's hero surface, so it gets its own
    panel texture instead of staying flat, and the roof gets its own
    dark red-brown panel texture too — it is the largest surface the
    isometric camera sees, and the first preview showed an untextured
    roof reading as a flat brown slab."""
    seeds = {"NovaTitanium": 211, "NovaGunmetal": 217,
             "NovaDarkAlloy": 223, "NovaRed": 229, "NovaRoof": 233}
    base_colors = {"NovaTitanium": TITANIUM, "NovaGunmetal": GUNMETAL,
                   "NovaDarkAlloy": DARK_ALLOY, "NovaRed": NOVA_RED,
                   "NovaRoof": NOVA_ROOF}
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
    print("Nova Arsenal v2 build:")
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
    # Bounding box check: footprint must stay within +/-1.00 (v1 base
    # platform), height <= 1.84 (v1 spire tip apex).
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
                  if m.name in ("NovaTitanium", "NovaGunmetal",
                                "NovaDarkAlloy", "NovaRed", "NovaRoof")]
    make_uv_atlas(built)
    TEXTURE_INFO = apply_metal_textures(metal_mats)
    export(built)
    report(built)
