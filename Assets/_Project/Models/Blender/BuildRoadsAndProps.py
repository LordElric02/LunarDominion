# BuildRoadsAndProps.py — Lunar Dominion: roads + props (second
# ground-work piece, after the ground texture + debris, which are
# done and Play-verified).
#
# ASSETS ONLY — no Unity code. The BaseView.cs wiring to place the
# roads and props is a separate step done later.
#
# WHAT IT GENERATES (next to this script):
#   RoadTexture.png — 1024x1024 atlas: straight-road region, 4-way
#     junction region, T-junction region, spare asphalt. The three
#     road FBX materials reference this ONE file, so it goes in
#     Assets/_Project/Resources/Models/ WITH the FBX files (same
#     folder, like the building PNGs — never rename it).
#   RoadStraight.fbx — flat straight segment, 4 x 1.2, top face
#     UV-mapped to the straight region of the atlas.
#   RoadJunction.fbx — flat 4-way crossroads, 2.4 x 2.4, top face
#     UV-mapped to the junction region.
#   RoadTee.fbx — flat T-junction, 2.4 x 2.4, top face UV-mapped to
#     the tee region. The missing arm is on local -Y.
#   PropSolarPanel.fbx / PropCrates.fbx / PropStorageTank.fbx /
#   PropRover.fbx / PropGate.fbx — five small low-poly props,
#   flat base at z 0, plain materials in the established palette.
#
# ROADS sit flat on the ground (base z 0, top z 0.02). The code
# step must place them at y 0.015 so the base clears the ground
# plane (no z-fight) — the model itself is built at z 0 like
# everything else. The road pieces are NOT tappable: the code
# must strip their colliders like the existing decor.
#
# HOW THE TEXTURE IS MADE: pixel-generated with numpy, NOT a
# Cycles bake (a bake is unusable headless). One 1024 atlas with
# three regions, each piece's top face UV-mapped to its region,
# so the markings start/end cleanly at the piece edges. Written
# with the pure-Python zlib PNG encoder — Blender's
# image.save()/save_render() blacken generated pixels.
#
# COLOUR NOTE (from the ground texture): the regolith PNG was
# authored at sRGB (0.56, 0.58, 0.61) and renders in Play at
# ~sRGB 0.53 — i.e. roughly as-authored, not decoded to linear.
# So this texture is authored at the DISPLAY values it should
# read as: asphalt ~0.30 (darker than the ground), markings
# ~0.70-0.85. Do not "correct" these to linear values.
#
# BACKFACE NOTE (Harvester dish lesson): the tank dome is a UV
# sphere (outward normals by construction) and the gate doors
# are thin boxes (outward normals by construction) — no cones
# or open dishes anywhere. The verification script checks the
# exported FBX face normals point outward.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildRoadsAndProps.py
# From inside Blender: open the Scripting workspace, open this file, Run Script.
# Written/tested against Blender 4.2.3 LTS here; uses no deprecated APIs,
# so it should also run on Blender 5.2 LTS.
# NOTE: the script exports next to wherever the script file was opened
# from (SCRIPT_DIR). If you open it from F:\ or Downloads, the files
# land there — check the "exported:" lines in the console / report.

import math
import os
import struct
import zlib

import bpy
import numpy as np

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
TEXTURE_FILE = "RoadTexture.png"
TEXTURE_PATH = os.path.join(SCRIPT_DIR, TEXTURE_FILE)

# --- palette (display values, see colour note above) ---
ASPHALT = (0.30, 0.31, 0.34)       # road surface, darker than the ground
ASPHALT_LIGHT = (0.38, 0.39, 0.42)  # junction centre disc
DASH_YELLOW = (0.85, 0.72, 0.38)   # centre dashes (warm, matches amber accents)
EDGE_LINE = (0.68, 0.69, 0.72)     # solid edge lines
GUNMETAL = (0.27, 0.33, 0.39)      # spec gunmetal
DARK_ALLOY = (0.09, 0.15, 0.22)    # spec dark alloy
TITANIUM = (0.78, 0.82, 0.85)      # spec titanium
SOLAR_BLUE = (0.12, 0.18, 0.32)    # solar panel face
SOLAR_FRAME = (0.45, 0.47, 0.50)   # solar panel frame / stand
CRATE_GREY = (0.42, 0.43, 0.46)    # supply crates
CRATE_AMBER = (0.62, 0.42, 0.16)   # amber crates
TANK_GREY = (0.55, 0.56, 0.60)     # storage tank body
ROVER_ORANGE = (0.85, 0.45, 0.15)  # utility rover body (v1 orange)
ROVER_GREY = (0.35, 0.36, 0.40)    # rover frame / chassis
ROVER_DARK = (0.15, 0.16, 0.20)    # rover wheels / seat
CYAN_GLOW = (0.38, 0.85, 1.00)     # gate strip light (saturated cyan)

TEXTURE_SIZE = 1024

# Atlas regions in UV space (u, v0, u1, v1) — v=0 is the bottom of
# the image. Straight: top strip (aspect 1024x308 ~ 4 x 1.2).
# Junction / Tee: 512x512 squares. Spare: bottom strip, plain
# asphalt for the road sides and undersides.
REGION_STRAIGHT = (0.0, 0.70, 1.0, 1.0)
REGION_JUNCTION = (0.0, 0.20, 0.5, 0.70)
REGION_TEE = (0.5, 0.20, 1.0, 0.70)
REGION_SPARE = (0.45, 0.05, 0.55, 0.15)


def make_material(name, rgb, metallic=0.0, roughness=0.9, emission=0.0,
                  emission_rgb=None):
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
    return mat


def write_png(path, rgb_float):
    """Write an (H, W, 3) float 0-1 array as an 8-bit RGB PNG. Pure
    Python (zlib) — Blender's image.save()/save_render() blacken a
    generated image's pixels on save. The array is in UV space
    (row 0 = v 0 = image bottom); rows are flipped so the file
    matches PNG top-to-bottom order."""
    h, w, _ = rgb_float.shape
    rgb = (np.clip(rgb_float, 0.0, 1.0) * 255.0).astype(np.uint8)
    rgb = rgb[::-1]
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


def _world_to_px(region, wx, wy):
    """World (x, y) inside a region -> (col, row) in the 1024 array.
    region = (u0, v0, u1, v1, wx0, wy0, wx1, wy1); array row 0 is
    v 0 (bottom), so row = v * 1024."""
    u0, v0, u1, v1, wx0, wy0, wx1, wy1 = region
    u = u0 + (wx - wx0) / (wx1 - wx0) * (u1 - u0)
    v = v0 + (wy - wy0) / (wy1 - wy0) * (v1 - v0)
    return u * TEXTURE_SIZE, v * TEXTURE_SIZE


def fill_rect(arr, region, wx0, wy0, wx1, wy1, color):
    """Fill a world-space rectangle inside a region with `color`."""
    x0, y0 = _world_to_px(region, wx0, wy0)
    x1, y1 = _world_to_px(region, wx1, wy1)
    cx0, cx1 = int(round(min(x0, x1))), int(round(max(x0, x1)))
    cy0, cy1 = int(round(min(y0, y1))), int(round(max(y0, y1)))
    arr[cy0:cy1, cx0:cx1] = color


def fill_disc(arr, region, cx, cy, r, color):
    """Fill a world-space disc inside a region with `color`."""
    x0, y0 = _world_to_px(region, cx - r, cy - r)
    x1, y1 = _world_to_px(region, cx + r, cy + r)
    px0, px1 = int(math.floor(min(x0, x1))), int(math.ceil(max(x0, x1)))
    py0, py1 = int(math.floor(min(y0, y1))), int(math.ceil(max(y0, y1)))
    px0, py0 = max(px0, 0), max(py0, 0)
    px1, py1 = min(px1, TEXTURE_SIZE), min(py1, TEXTURE_SIZE)
    if px1 <= px0 or py1 <= py0:
        return
    yy, xx = np.mgrid[py0:py1, px0:px1].astype(np.float32)
    # Convert pixel centres back to world coordinates.
    u0, v0, u1, v1, wx0, wy0, wx1, wy1 = region
    wxs = wx0 + ((xx + 0.5) / TEXTURE_SIZE - u0) / (u1 - u0) * (wx1 - wx0)
    wys = wy0 + ((yy + 0.5) / TEXTURE_SIZE - v0) / (v1 - v0) * (wy1 - wy0)
    mask = (wxs - cx) ** 2 + (wys - cy) ** 2 <= r * r
    arr[py0:py1, px0:px1][mask] = color


def make_road_texture():
    """Generate the 1024 road atlas with numpy. Asphalt base with
    subtle noise and faint patches, then bold markings drawn in
    world coordinates per region (the roads are ~1.2 units wide on
    a 56-unit ground, so the markings are bold, not fine)."""
    S = TEXTURE_SIZE
    rng = np.random.default_rng(11)
    base = np.array(ASPHALT, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    # Fine grain + a few soft darker patches (no wrapping needed —
    # the road texture does not tile).
    arr *= (1.0 + rng.normal(0.0, 0.010, (S, S, 1)).astype(np.float32))
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    var = np.zeros((S, S), dtype=np.float32)
    for (fx, fy, amp, ph) in ((3, 2, 0.020, 0.7), (7, -4, 0.012, 2.1),
                              (13, 9, 0.008, 4.0)):
        var += amp * np.sin(2.0 * np.pi * (fx * xx + fy * yy) / S + ph)
    arr *= (1.0 + var[..., None])
    arr = np.clip(arr, 0.0, 1.0)

    # Regions: (u0, v0, u1, v1, world x0, world y0, world x1, world y1).
    straight = REGION_STRAIGHT + (-2.0, -0.6, 2.0, 0.6)
    junction = REGION_JUNCTION + (-1.2, -1.2, 1.2, 1.2)
    tee = REGION_TEE + (-1.2, -1.2, 1.2, 1.2)

    # --- Straight (4 x 1.2): 4 centre dashes + 2 edge lines. ---
    for (dx0, dx1) in ((-1.75, -1.25), (-0.75, -0.25),
                       (0.25, 0.75), (1.25, 1.75)):
        fill_rect(arr, straight, dx0, -0.06, dx1, 0.06, DASH_YELLOW)
    fill_rect(arr, straight, -2.0, 0.48, 2.0, 0.56, EDGE_LINE)
    fill_rect(arr, straight, -2.0, -0.56, 2.0, -0.48, EDGE_LINE)

    # --- Junction (2.4 x 2.4): dashes + edge lines on all 4 arms,
    # meeting a lighter centre disc. ---
    fill_disc(arr, junction, 0.0, 0.0, 0.35, ASPHALT_LIGHT)
    for (ax0, ay0, ax1, ay1) in ((0.70, -0.06, 1.20, 0.06),
                                 (-1.20, -0.06, -0.70, 0.06),
                                 (-0.06, 0.70, 0.06, 1.20),
                                 (-0.06, -1.20, 0.06, -0.70)):
        fill_rect(arr, junction, ax0, ay0, ax1, ay1, DASH_YELLOW)
    for (ex0, ey0, ex1, ey1) in (
            (0.60, 0.48, 1.20, 0.56), (0.60, -0.56, 1.20, -0.48),
            (-1.20, 0.48, -0.60, 0.56), (-1.20, -0.56, -0.60, -0.48),
            (0.48, 0.60, 0.56, 1.20), (-0.56, 0.60, -0.48, 1.20),
            (0.48, -1.20, 0.56, -0.60), (-0.56, -1.20, -0.48, -0.60)):
        fill_rect(arr, junction, ex0, ey0, ex1, ey1, EDGE_LINE)

    # --- Tee (2.4 x 2.4): same as the junction but only 3 arms
    # (+X, -X, +Y); the -Y side is a solid edge line (road ends).
    fill_disc(arr, tee, 0.0, 0.0, 0.35, ASPHALT_LIGHT)
    for (ax0, ay0, ax1, ay1) in ((0.70, -0.06, 1.20, 0.06),
                                 (-1.20, -0.06, -0.70, 0.06),
                                 (-0.06, 0.70, 0.06, 1.20)):
        fill_rect(arr, tee, ax0, ay0, ax1, ay1, DASH_YELLOW)
    for (ex0, ey0, ex1, ey1) in (
            (0.60, 0.48, 1.20, 0.56), (0.60, -0.56, 1.20, -0.48),
            (-1.20, 0.48, -0.60, 0.56), (-1.20, -0.56, -0.60, -0.48),
            (0.48, 0.60, 0.56, 1.20), (-0.56, 0.60, -0.48, 1.20)):
        fill_rect(arr, tee, ex0, ey0, ex1, ey1, EDGE_LINE)
    fill_rect(arr, tee, -1.20, -1.20, 1.20, -1.12, EDGE_LINE)

    arr = np.clip(arr, 0.0, 1.0)
    write_png(TEXTURE_PATH, arr)
    print("  texture %s: %s (%d bytes) mean RGB (%.3f, %.3f, %.3f)"
          % (TEXTURE_FILE, TEXTURE_PATH, os.path.getsize(TEXTURE_PATH),
             arr[..., 0].mean(), arr[..., 1].mean(), arr[..., 2].mean()))
    return arr


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in list(bpy.data.meshes):
        bpy.data.meshes.remove(block)
    for block in list(bpy.data.materials):
        bpy.data.materials.remove(block)


def make_road_material():
    """Road material: RoadTexture.png plugged into Base Color, base
    colour white (the texture carries the colour). The FBX export
    with path_mode=COPY keeps the reference to RoadTexture.png
    next to the FBX, so Unity resolves it in Resources/Models/."""
    mat = bpy.data.materials.new(name="RoadTextureMat")
    mat.use_nodes = True
    img = bpy.data.images.load(TEXTURE_PATH)
    nt = mat.node_tree
    node = nt.nodes.new("ShaderNodeTexImage")
    node.image = img
    bsdf = nt.nodes["Principled BSDF"]
    nt.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Base Color"].default_value = (1.0, 1.0, 1.0, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.9
    bsdf.inputs["Metallic"].default_value = 0.0
    return mat


def set_road_uvs(obj, region):
    """Map the +Z face of a thin-box road piece to the UV rect
    `region` (u0, v0, u1, v1), and every other face to the spare
    asphalt rect. Local +X maps to u, local +Y maps to v."""
    mesh = obj.data
    if not mesh.uv_layers:
        mesh.uv_layers.new(name="UVMap")
    uv_layer = mesh.uv_layers.active.data
    u0, v0, u1, v1 = region
    su0, sv0, su1, sv1 = REGION_SPARE
    xs = [v.co.x for v in mesh.vertices]
    ys = [v.co.y for v in mesh.vertices]
    xmin, xmax = min(xs), max(xs)
    ymin, ymax = min(ys), max(ys)
    for poly in mesh.polygons:
        for loop_idx in poly.loop_indices:
            loop = mesh.loops[loop_idx]
            v = mesh.vertices[loop.vertex_index]
            if poly.normal.z > 0.9:
                u = u0 + (v.co.x - xmin) / (xmax - xmin) * (u1 - u0)
                vv = v0 + (v.co.y - ymin) / (ymax - ymin) * (v1 - v0)
                uv_layer[loop_idx].uv = (u, vv)
            else:
                uv_layer[loop_idx].uv = (
                    su0 + 0.5 * (su1 - su0), sv0 + 0.5 * (sv1 - sv0))


def add_road_box(name, width, depth, region, material):
    """Flat road slab: `width` (X) by `depth` (Y), 0.02 thick,
    base at z 0, top face UV-mapped to `region`."""
    bpy.ops.mesh.primitive_cube_add(location=(0.0, 0.0, 0.01))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (width / 2.0, depth / 2.0, 0.01)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    set_road_uvs(obj, region)
    obj.data.materials.append(material)
    return obj


def build_road_straight(road_mat):
    """RoadStraight: 4 x 1.2 straight segment, centre dashes + edge
    lines, base z 0, top z 0.02."""
    return [add_road_box("RoadStraight", 4.0, 1.2, REGION_STRAIGHT, road_mat)]


def build_road_junction(road_mat):
    """RoadJunction: 2.4 x 2.4 four-way crossroads, markings
    meeting in the middle, base z 0, top z 0.02."""
    return [add_road_box("RoadJunction", 2.4, 2.4, REGION_JUNCTION, road_mat)]


def build_road_tee(road_mat):
    """RoadTee: 2.4 x 2.4 T-junction. The missing arm is on local
    -Y (a solid edge line there); rotate in the code step so the
    missing arm faces the right way. Base z 0, top z 0.02."""
    return [add_road_box("RoadTee", 2.4, 2.4, REGION_TEE, road_mat)]


def export_fbx(objects, filepath):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        if obj.type == "MESH":
            obj.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=filepath,
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


def report_model(label, objects, filepath):
    total_tris = 0
    for obj in objects:
        if obj.type != "MESH":
            continue
        mesh = obj.data
        mesh.calc_loop_triangles()
        total_tris += len(mesh.loop_triangles)
    pts = [o.matrix_world @ v.co for o in objects if o.type == "MESH"
           for v in o.data.vertices]
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    zs = [p.z for p in pts]
    print("  %s: objects=%d tris=%d bounds x=[%.2f, %.2f] "
          "y=[%.2f, %.2f] z=[%.2f, %.2f]"
          % (label, len(objects), total_tris,
             min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    print("  exported: %s (%d bytes)" % (filepath, os.path.getsize(filepath)))
    return total_tris


def add_box(name, center, size, rot_z, material, rot_x=0.0, rot_y=0.0):
    bpy.ops.mesh.primitive_cube_add(
        location=center, rotation=(rot_x, rot_y, rot_z))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (size[0] / 2.0, size[1] / 2.0, size[2] / 2.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def add_cyl(name, center, radius, depth, material, rot_x=0.0, rot_y=0.0,
            rot_z=0.0, segments=16):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=segments, radius=radius, depth=depth, location=center,
        rotation=(rot_x, rot_y, rot_z))
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    return obj


def build_solar_panel(mats):
    """PropSolarPanel: a solar array on a stand — post + crossbar
    + 4 dark blue-grey panels (2x2) tilted 30 deg toward -Y
    (the camera side in Unity). ~1.5 wide, ~0.9 tall, base z 0."""
    objs = []
    objs.append(add_box("Solar_Post", (0.0, 0.0, 0.25),
                        (0.12, 0.12, 0.50), 0.0, mats["frame"]))
    objs.append(add_box("Solar_Crossbar", (0.0, 0.0, 0.55),
                        (1.50, 0.10, 0.08), 0.0, mats["frame"]))
    # Panels: 2x2 in a plane tilted 30 deg about X (normal tips
    # toward -Y). Local panel offsets (px, py) map to world
    # (px, py*cos30, 0.72 + py*sin30).
    tilt = math.radians(30.0)
    for i, (px, py) in enumerate(((-0.36, 0.295), (0.36, 0.295),
                                  (-0.36, -0.295), (0.36, -0.295))):
        wx = px
        wy = py * math.cos(tilt)
        wz = 0.72 + py * math.sin(tilt)
        bpy.ops.mesh.primitive_cube_add(
            location=(wx, wy, wz), rotation=(tilt, 0.0, 0.0))
        obj = bpy.context.active_object
        obj.name = "Solar_Panel_%02d" % (i + 1)
        obj.scale = (0.34, 0.275, 0.015)
        bpy.ops.object.transform_apply(location=False, rotation=False,
                                       scale=True)
        obj.data.materials.append(mats["panel"])
        objs.append(obj)
    return objs


def build_crates(mats):
    """PropCrates: a stack of supply crates — 3 on the bottom, 2
    in the middle, 1 on top, grey and amber mixed. ~0.8 wide,
    ~0.85 tall, base z 0."""
    objs = []
    defs = [
        ("Crates_Bot_01", (-0.20, 0.00, 0.175), (0.35, 0.35, 0.35), 0.0, "grey"),
        ("Crates_Bot_02", (0.20, 0.05, 0.175), (0.35, 0.35, 0.35), 0.18, "amber"),
        ("Crates_Bot_03", (0.05, -0.25, 0.150), (0.30, 0.30, 0.30), -0.26, "grey"),
        ("Crates_Mid_01", (-0.10, 0.05, 0.500), (0.30, 0.30, 0.30), 0.44, "amber"),
        ("Crates_Mid_02", (0.15, -0.10, 0.480), (0.28, 0.28, 0.28), -0.17, "grey"),
        ("Crates_Top_01", (0.00, 0.00, 0.720), (0.22, 0.22, 0.22), 0.70, "amber"),
    ]
    for name, center, size, rot, key in defs:
        objs.append(add_box(name, center, size, rot, mats[key]))
    return objs


def build_storage_tank(mats):
    """PropStorageTank: a cylindrical tank with a domed top, a
    base ring, a side pipe and a top vent. Light grey, ~1.0 wide,
    ~1.26 tall, base z 0. The dome is a UV sphere (outward
    normals by construction — no flipped-cone risk)."""
    objs = []
    objs.append(add_cyl("Tank_BaseRing", (0.0, 0.0, 0.04), 0.48, 0.08,
                        mats["gunmetal"], segments=16))
    objs.append(add_cyl("Tank_Body", (0.0, 0.0, 0.475), 0.42, 0.95,
                        mats["tank"], segments=16))
    # Dome: UV sphere squashed in Z, centred at the body top; the
    # lower half sits inside the body, the top half is the dome.
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=16, ring_count=8, radius=0.42, location=(0.0, 0.0, 0.95))
    dome = bpy.context.active_object
    dome.name = "Tank_Dome"
    dome.scale = (1.0, 1.0, 0.45)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    dome.data.materials.append(mats["tank"])
    objs.append(dome)
    # Side pipe, horizontal along +X.
    objs.append(add_cyl("Tank_Pipe", (0.45, 0.0, 0.25), 0.08, 0.50,
                        mats["gunmetal"], rot_y=math.radians(90.0),
                        segments=12))
    # Top vent.
    objs.append(add_cyl("Tank_Vent", (0.0, 0.0, 1.20), 0.06, 0.12,
                        mats["gunmetal"], segments=12))
    return objs


def build_rover(mats):
    """PropRover: a small parked 6-wheel open-frame utility rover
    (distinct from the Aegis rover — this one is a bare frame with
    a seat, a roll cage and a beacon mast). ~1.1 long, ~0.75 tall,
    base z 0. Wheels are cylinders (outward normals by
    construction)."""
    objs = []
    objs.append(add_box("Rover_Chassis", (0.0, 0.0, 0.32),
                        (1.10, 0.60, 0.18), 0.0, mats["orange"]))
    for i, wx in enumerate((-0.40, 0.00, 0.40)):
        for j, wy in enumerate((-0.32, 0.32)):
            objs.append(add_cyl("Rover_Wheel_%d%d" % (i + 1, j + 1),
                                (wx, wy, 0.14), 0.14, 0.08,
                                mats["dark"], rot_x=math.radians(90.0),
                                segments=12))
    # Roll cage: two side bars + two cross bars.
    objs.append(add_box("Rover_Cage_L", (0.0, -0.25, 0.55),
                        (1.00, 0.05, 0.05), 0.0, mats["grey"]))
    objs.append(add_box("Rover_Cage_R", (0.0, 0.25, 0.55),
                        (1.00, 0.05, 0.05), 0.0, mats["grey"]))
    objs.append(add_box("Rover_Cage_F", (-0.40, 0.0, 0.55),
                        (0.05, 0.55, 0.05), 0.0, mats["grey"]))
    objs.append(add_box("Rover_Cage_B", (0.40, 0.0, 0.55),
                        (0.05, 0.55, 0.05), 0.0, mats["grey"]))
    objs.append(add_box("Rover_Seat", (-0.20, 0.0, 0.50),
                        (0.30, 0.40, 0.25), 0.0, mats["dark"]))
    objs.append(add_cyl("Rover_Mast", (0.45, 0.0, 0.60), 0.03, 0.25,
                        mats["grey"], segments=8))
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=8, ring_count=4, radius=0.05, location=(0.45, 0.0, 0.75))
    beacon = bpy.context.active_object
    beacon.name = "Rover_Beacon"
    beacon.data.materials.append(mats["beacon"])
    objs.append(beacon)
    return objs


def build_gate(mats):
    """PropGate: two posts, a crossbeam with a cyan strip, and two
    doors swung open ~70 deg into the compound (+Y). 2.65 wide
    (posts at +/-1.2, fitting a 2.4-wide wall gap), 1.6 tall,
    base z 0. Doors are thin boxes (outward normals by
    construction — no flipped-face risk)."""
    objs = []
    objs.append(add_box("Gate_Post_L", (-1.20, 0.0, 0.80),
                        (0.25, 0.25, 1.60), 0.0, mats["gunmetal"]))
    objs.append(add_box("Gate_Post_R", (1.20, 0.0, 0.80),
                        (0.25, 0.25, 1.60), 0.0, mats["gunmetal"]))
    objs.append(add_box("Gate_Beam", (0.0, 0.0, 1.50),
                        (2.65, 0.25, 0.22), 0.0, mats["gunmetal"]))
    objs.append(add_box("Gate_Strip", (0.0, -0.14, 1.50),
                        (2.65, 0.04, 0.08), 0.0, mats["strip"]))
    # Doors: hinged at the posts, open 70 deg toward +Y.
    # Left door: hinge (-1.2, 0), long axis (cos70, sin70).
    dl = 1.05 / 2.0
    ang = math.radians(70.0)
    lx = -1.20 + dl * math.cos(ang)
    ly = dl * math.sin(ang)
    objs.append(add_box("Gate_Door_L", (lx, ly, 0.675),
                        (1.05, 0.06, 1.35), ang, mats["dark"]))
    rx = 1.20 - dl * math.cos(ang)
    objs.append(add_box("Gate_Door_R", (rx, ly, 0.675),
                        (1.05, 0.06, 1.35), -ang, mats["dark"]))
    return objs


# --- Proposed road layout + prop placements (world space) ---
# Checked against the rotated building/wall/decor positions.
# The layout will be finalized in the code step; these are
# checked and sensible. The ground is NOT rotated, so these
# positions are world coordinates.
#
# Buildings (world, radius ~1.0; Lab 1.1): Nexus (0,0),
# Hydroponics (-1.77,5.44), Alloy (5.44,-1.77), Helium-3
# (2.90,2.90), Marine (-5.44,1.77), Nova (1.77,-5.44),
# Aegis (-2.90,-2.90), Lab (-5.87,5.87).
# The near wall edge is the line x+z = 9.19; the gate is at
# (4.6, 4.6) on it. The tonal patch discs and the crater discs
# are GONE (removed 2026-11-07) — only the 10 rocks remain as
# code-built decor, plus the 8 debris placements.
#
# Roads (1.2 wide; Y rotation in Unity terms):
#   E1 straight, centre (5.45,3.75), rot +45: along the near
#     wall edge, through the gate gap (the gap is cut in the
#     code step). Straddles the wall.
#   S1 straight, centre (4.46,1.07), rot -45: main spine, NE
#     end butts E1's inner side, SW end meets J1.
#   J1 junction, centre (2.20,-1.19), rot -45: crossroads on
#     the spine; SE arm is a driveway toward Alloy.
#   S2 straight, centre (-0.06,-3.45), rot -45: spine toward
#     Nova/Aegis, ends inside the compound.
#   N1 straight, centre (-1.42,2.58), rot -45: NW street past
#     Hydroponics. N2 straight, centre (-4.24,-0.24), rot -45:
#     NW street past Marine Foundry.
# The spine (E1/S1/J1/S2) and the NW street (N1/N2) are two
# SEPARATE systems — there is no cross link between them. The
# direct link from the spine to the NW street would run along
# the line x+z = 1.01 and pass 0.71 from the Nexus centre
# (measured clearance -0.89: it runs through the Nexus
# footprint). The only safe line is x+z in [2.26, 3.54]
# (through the Nexus-Helium-3 gap), which needs a 5.2-unit
# link while the straight piece is 4 units — so the NW street
# is a standalone pair for now, and a future shorter link
# piece (or a dogleg) could connect the systems.
# The Research Lab has no direct road in this layout: no line
# between Marine and the Lab clears both footprints (the only
# gap is a single line with zero slack), so the Lab stays set
# apart in the NW corner — a future spur could link it.
#
# Props (world space, Y rotation in Unity terms):
#   SolarPanel (1.5,6.5) rot 30, (-5.0,8.0) rot -20,
#     (8.5,-2.0) rot -30.
#   Crates (-6.8,3.8) rot 15, (3.5,-4.0) rot 70.
#   StorageTank (-3.5,6.5) rot 0, (3.5,-6.5) rot 0.
#   Rover (-1.5,-1.5) rot -45 (on the apron by the Aegis).
#   Gate (4.6,4.6) rot +45 (crossbeam along the wall edge).
# Roads are placed at y 0.015 in the code step so the base
# clears the ground plane (no z-fight).

BUILDINGS = [
    ("Nexus", 0.0, 0.0, 1.0),
    ("Hydroponics", -1.77, 5.44, 1.0),
    ("Alloy", 5.44, -1.77, 1.0),
    ("Helium-3", 2.90, 2.90, 1.0),
    ("Marine", -5.44, 1.77, 1.0),
    ("Nova", 1.77, -5.44, 1.0),
    ("Aegis", -2.90, -2.90, 1.0),
    ("Lab", -5.87, 5.87, 1.1),
]
# Rocks (world, from the local positions in BaseView.cs, radius ~0.25).
ROCKS = [
    (2.47, 5.30, 0.25), (-2.12, -5.66, 0.25), (0.0, 22.63, 0.25),
    (22.63, -11.31, 0.25), (19.80, 2.83, 0.25), (18.38, -15.56, 0.25),
    (18.38, 7.07, 0.25), (21.21, -1.41, 0.25), (24.04, -7.07, 0.25),
    (25.46, -2.83, 0.25),
]
# Debris placements (world, radius ~0.5).
DEBRIS = [
    (0.0, -7.5, 0.5), (-7.5, 0.0, 0.5), (7.5, 0.0, 0.5),
    (-0.5, 7.5, 0.5), (14.0, -10.0, 0.5), (-14.0, 18.0, 0.5),
    (20.0, 8.0, 0.5), (16.0, -12.0, 0.5),
]
# Road centrelines: (label, x0, z0, x1, z1), half-width 0.6.
ROAD_SEGS = [
    ("E1", 4.04, 5.16, 6.86, 2.34),
    ("S1", 5.87, 2.48, 3.05, -0.34),
    ("S2", 1.35, -2.04, -1.48, -4.87),
    ("N1", -2.83, 1.17, 0.0, 4.0),
    ("N2", -5.66, -1.65, -2.83, 1.17),
]
# Junction J1 as a circle (arm length 1.2) + its SE arm tip.
JUNCTION = ("J1", 2.20, -1.19, 1.2)
J1_SE_TIP = (3.05, -2.04)
# Props: (label, x, z, radius).
PROPS = [
    ("SolarPanel", 1.5, 6.5, 0.75), ("SolarPanel", -5.0, 8.0, 0.75),
    ("SolarPanel", 8.5, -2.0, 0.75),
    ("Crates", -6.8, 3.8, 0.5), ("Crates", 3.5, -4.0, 0.5),
    ("StorageTank", -3.5, 6.5, 0.5), ("StorageTank", 3.5, -6.5, 0.5),
    ("Rover", -1.5, -1.5, 0.6),
    ("Gate", 4.6, 4.6, 1.35),
]


def _dist_pt_seg(px, pz, x0, z0, x1, z1):
    dx, dz = x1 - x0, z1 - z0
    L2 = dx * dx + dz * dz
    if L2 == 0:
        return math.hypot(px - x0, pz - z0)
    t = max(0.0, min(1.0, ((px - x0) * dx + (pz - z0) * dz) / L2))
    return math.hypot(px - (x0 + t * dx), pz - (z0 + t * dz))


def check_layout():
    """Verify the proposed layout: every road clear of every
    building/rock/debris, every prop clear of everything and
    inside the wall, and the butt joints meeting. Prints a
    report; returns True if every check passes."""
    ok = True

    def check(cond, msg):
        nonlocal ok
        print("  [%s] %s" % ("PASS" if cond else "FAIL", msg))
        if not cond:
            ok = False

    obstacles = ([(n, x, z, r) for (n, x, z, r) in BUILDINGS]
                 + [("Rock", x, z, r) for (x, z, r) in ROCKS]
                 + [("Debris", x, z, r) for (x, z, r) in DEBRIS])
    # Roads vs obstacles.
    for (label, x0, z0, x1, z1) in ROAD_SEGS:
        for (name, ox, oz, orad) in obstacles:
            d = _dist_pt_seg(ox, oz, x0, z0, x1, z1)
            check(d >= orad + 0.6,
                  "road %s vs %s (%.2f, %.2f): clearance %.2f"
                  % (label, name, ox, oz, d - orad - 0.6))
    # Junction (circle) vs obstacles.
    _, jx, jz, jr = JUNCTION
    for (name, ox, oz, orad) in obstacles:
        d = math.hypot(ox - jx, oz - jz)
        check(d >= orad + jr,
              "junction J1 vs %s (%.2f, %.2f): clearance %.2f"
              % (name, ox, oz, d - orad - jr))
    check(math.hypot(5.44 - J1_SE_TIP[0], -1.77 - J1_SE_TIP[1])
          >= 1.0 + 0.6,
          "J1 SE arm tip vs Alloy: clearance %.2f"
          % (math.hypot(5.44 - J1_SE_TIP[0], -1.77 - J1_SE_TIP[1]) - 1.6))
    # Butt joints.
    check(abs((5.87 + 2.48) - 8.34) < 0.05,
          "S1 NE end butts E1 inner side (x+z=%.2f, want 8.34)"
          % (5.87 + 2.48))
    # Props vs obstacles, roads, each other, and the wall.
    for (label, px, pz, pr) in PROPS:
        if label == "Gate":
            check(abs((px + pz) - 9.19) < 0.05,
                  "Gate sits on the near wall edge (x+z=%.2f)" % (px + pz))
            continue
        check(abs(px + pz) <= 9.19 - pr * 0.7
              and abs(px - pz) <= 14.85 - pr * 0.7,
              "prop %s (%.1f, %.1f) inside the wall" % (label, px, pz))
        for (name, ox, oz, orad) in obstacles:
            d = math.hypot(px - ox, pz - oz)
            check(d >= orad + pr,
                  "prop %s (%.1f, %.1f) vs %s: clearance %.2f"
                  % (label, px, pz, name, d - orad - pr))
        for (rlabel, x0, z0, x1, z1) in ROAD_SEGS:
            d = _dist_pt_seg(px, pz, x0, z0, x1, z1)
            check(d >= pr + 0.6,
                  "prop %s (%.1f, %.1f) vs road %s: clearance %.2f"
                  % (label, px, pz, rlabel, d - pr - 0.6))
        d = math.hypot(px - jx, pz - jz)
        check(d >= pr + jr,
              "prop %s (%.1f, %.1f) vs junction J1: clearance %.2f"
              % (label, px, pz, d - pr - jr))
    for i, (l1, x1, z1, r1) in enumerate(PROPS):
        for (l2, x2, z2, r2) in PROPS[i + 1:]:
            d = math.hypot(x1 - x2, z1 - z2)
            check(d >= r1 + r2,
                  "prop %s (%.1f, %.1f) vs prop %s: clearance %.2f"
                  % (l1, x1, z1, l2, d - r1 - r2))
    print("  layout check: %s" % ("ALL PASS" if ok else "FAILURES ABOVE"))
    return ok


if __name__ == "__main__":
    clear_scene()

    print("Roads + props build:")
    make_road_texture()
    road_mat = make_road_material()

    for label, builder in (("RoadStraight", build_road_straight),
                           ("RoadJunction", build_road_junction),
                           ("RoadTee", build_road_tee)):
        objs = builder(road_mat)
        path = os.path.join(SCRIPT_DIR, label + ".fbx")
        export_fbx(objs, path)
        report_model(label, objs, path)
        clear_scene()
        road_mat = make_road_material()

    # Prop materials are created fresh for EACH prop inside the
    # loop: clear_scene() removes every material, so materials
    # made before the loop (or for a previous prop) are gone by
    # the time the next prop builds.
    def make_prop_mats():
        prop_mats = {
            "panel": make_material("SolarPanel", SOLAR_BLUE, metallic=0.3,
                                   roughness=0.4),
            "frame": make_material("SolarFrame", SOLAR_FRAME, metallic=0.2,
                                   roughness=0.6),
            "grey": make_material("RoverGrey", ROVER_GREY, metallic=0.2,
                                  roughness=0.6),
            "tank": make_material("TankGrey", TANK_GREY, metallic=0.1,
                                  roughness=0.5),
            "gunmetal": make_material("Gunmetal", GUNMETAL, metallic=0.2,
                                      roughness=0.6),
            "orange": make_material("RoverOrange", ROVER_ORANGE,
                                    metallic=0.1, roughness=0.6),
            "dark": make_material("RoverDark", ROVER_DARK, roughness=0.9),
            "beacon": make_material("RoverBeacon", (1.0, 0.55, 0.15),
                                    emission=2.5,
                                    emission_rgb=(1.0, 0.55, 0.15)),
            "strip": make_material("GateStrip", CYAN_GLOW, emission=2.5,
                                   emission_rgb=CYAN_GLOW),
        }
        crate_mats = {"grey": make_material("CrateGrey", CRATE_GREY,
                                            roughness=0.9),
                      "amber": make_material("CrateAmber", CRATE_AMBER,
                                             roughness=0.9)}
        return prop_mats, crate_mats

    # The crates builder uses "grey"/"amber" keys; the rover uses
    # "orange"/"grey"/"dark"/"beacon"; the gate uses
    # "gunmetal"/"strip"/"dark"; the tank uses "tank"/"gunmetal".
    for label, builder, use_mats in (
            ("PropSolarPanel", build_solar_panel, "prop"),
            ("PropCrates", build_crates, "crate"),
            ("PropStorageTank", build_storage_tank, "prop"),
            ("PropRover", build_rover, "prop"),
            ("PropGate", build_gate, "prop")):
        prop_mats, crate_mats = make_prop_mats()
        mats = prop_mats if use_mats == "prop" else crate_mats
        objs = builder(mats)
        path = os.path.join(SCRIPT_DIR, label + ".fbx")
        export_fbx(objs, path)
        report_model(label, objs, path)
        clear_scene()

    print("Layout check (roads + props vs buildings/rocks/debris/wall):")
    check_layout()
