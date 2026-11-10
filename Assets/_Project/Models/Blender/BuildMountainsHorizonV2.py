# BuildMountainsHorizonV2.py — Lunar Dominion: low horizon mountains v2
# (graphics upgrade: more faceting + snow-capped summits + richer rock).
#
# v2 = the v1 HorizonA/B/C low ridges (BuildMountainsHorizon.py) with
# more detail. The HEIGHT ENVELOPE IS UNCHANGED (2.0-4.0, low horizon)
# — Patrick chose the low horizon deliberately on 2026-11-08 (the tall
# MountainA/B/C peaks read as a ridgeline blocking the sky, and the
# planet disc clears the low horizon; spots 1 and 3 are lowered in the
# wiring so they don't split the planet). The upgrade is detail and
# texture, not height.
#
# WHAT CHANGES IN v2:
#   * More faceting: each sub-peak now uses 4 rings (was 3) and 12
#     segments (was 10-11), so the peaks read more craggy and less
#     smooth from the game camera.
#   * One extra sub-peak per variant (A 4->5, B 3->4, C 3->4), so the
#     ridge silhouette is more complex and less regular.
#   * Snow-capped summits (NEW): a pale cap cone on the tallest
#     sub-peak of each variant, using a second pale-grey material.
#     It reads as a sunlit / frosted summit and gives the ridge a
#     bright high point that catches the eye. (Lunar realism aside,
#     it is a stylised highlight, like the pale rock caps in the
#     reference image.)
#   * Richer rock texture: stronger fracture contrast and a new
#     "frost" term — pale patches where the large-scale noise is
#     brightest — so the rock has more tonal variation.
#   * The skirt and boulder field are unchanged.
#
# OUTPUTS (next to this script): HorizonA2.fbx / HorizonB2.fbx /
# HorizonC2.fbx + HorizonA2_Rock.png / HorizonB2_Rock.png /
# HorizonC2_Rock.png. Same install pattern as the buildings: rename
# ONLY the FBX in Unity's Project window (HorizonA2.fbx ->
# HorizonA.fbx, etc.), do NOT rename the PNGs (the FBX references
# them by their V2 name). Delete the old v1 PNG (HorizonA_Rock.png)
# when the v2 is in, or leave it — it is orphaned but harmless.
# The FBX carries two materials (rock + snow cap); both PNGs are
# referenced by bare filename (path_mode COPY, as in v1).
#
# PIPELINE (same v2 standard as the buildings and the v1 horizon):
# procedural geometry (jittered rings with coherent ridge variation),
# numpy pixel-generated rock texture, Lightmap Pack UV atlas,
# pure-Python zlib PNG encoder (Blender's image.save() blackens
# generated pixels — never use it).
#
# CAMERA NOTE: the game camera sits at Unity (10,16,10) looking at
# the origin, so detail faces Blender -Y / Unity +Z. The ridge long
# axis runs along X, so the wide silhouette faces the camera; ridge
# and boulder features are biased toward the -Y side.
#
# NORMAL SAFETY (Helium-3 dish lesson: Cycles renders backfaces,
# Unity's URP culls them, so a flipped-normal surface looks fine in
# a Cycles preview and vanishes in the game). Every mesh built here
# gets its face normals recalculated outward with bmesh
# (recalc_face_normals) and flat shading, and the preview script
# renders a red-backface check for each variant.
#
# FBX EXPORT: same convention as the v1 horizon script
# (axis_forward "-Z", axis_up "Y", bake_space_transform False) — the
# -90 deg X axis conversion stays in the model node. Import the FBX
# with the same settings as the buildings (notably "Bake Axis
# Conversion" ON, as for the terrain fix), and the wiring must NOT
# force the rotation to identity — set only position, Y rotation
# and scale (see README in hidden_files/mountains-horizon/).
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildMountainsHorizonV2.py
# From inside Blender: open the Scripting workspace, open this file,
# Run Script. Written/tested against Blender 4.2.3 LTS here; uses no
# deprecated APIs, so it should also run on Blender 5.2 LTS.
# NOTE: the script exports the FBX + PNGs next to wherever the script
# file was opened from (SCRIPT_DIR). If you open it from F:\ or
# Downloads, the files land there — check the "exported:" lines.

import math
import os
import random
import struct
import zlib

import bmesh
import bpy
import numpy as np

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ATLAS_SIZE = 1024

# Nominal envelope every variant is built to (wiring scales from this).
# The ridge mass itself stays within +/-2.5 in X/Y and 0-4.0 in Z;
# a few boulder tips may reach ~+/-2.9 (reported per variant).
NOMINAL_RADIUS = 2.5
NOMINAL_HEIGHT = 4.0

# Rock base tones (display values, one per variant so the range does
# not read as three clones of one grey). Same tones as the v1 horizon,
# so the new ridges match the scene.
ROCK_A = (0.42, 0.43, 0.46)   # mid grey
ROCK_B = (0.35, 0.37, 0.42)   # darker blue-grey
ROCK_C = (0.45, 0.42, 0.38)   # warmer brown-grey
# Snow-cap tone: pale grey-blue, bright enough to read as a frosted
# summit against the rock, but not pure white (which would blow out
# under the scene sun).
SNOW = (0.82, 0.84, 0.88)


def make_material(name, rgb):
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = 0.95
    return mat


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in list(bpy.data.meshes):
        bpy.data.meshes.remove(block)
    for block in list(bpy.data.materials):
        bpy.data.materials.remove(block)


def flat_shade(obj):
    for poly in obj.data.polygons:
        poly.use_smooth = False


def recalc_outward(obj):
    """Force all face normals outward (closed solid) — the dish fix."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()


# ---------------------------------------------------------------------
# Procedural peak mesh (rings + apex/plateau + bottom cap).
# Reused from the tall-mountain script: a craggy cone with jittered
# rings and a ridge function coherent up the peak (same ridge angles
# on every ring, so ridges read as vertical features, not per-vertex
# noise), flat-shaded, closed bottom cap at z 0.
# ring_ts: t values 0..1 for the side rings. radius(t) =
# base_radius * ((1-t)**profile_exp * (1-top_frac) + top_frac).
# top_frac 0 -> sharp apex vertex; > 0 -> flat-topped plateau ring
# capped with a fan. Low sub-peaks use 3 rings [0.0, 0.5, 1.0]
# (2 bands) — enough vertical faceting for a 2-4 unit height, and
# it keeps the tri budget for a 4-sub-peak ridge in range.
# ---------------------------------------------------------------------
def build_cone_mesh(name, segments, ring_ts, height, base_radius,
                    top_frac, seed, ridge_phases, apex_xy=(0.0, 0.0),
                    profile_exp=1.2):
    rng = random.Random(seed)
    S = segments
    # Per-ring radius wobble (a ring slightly wider/narrower overall).
    ring_wobble = [1.0 + rng.uniform(-0.06, 0.06) for _ in ring_ts]
    p1, p2 = ridge_phases
    verts = []
    # Side rings.
    for i, t in enumerate(ring_ts):
        z = height * (t ** 1.1)
        prof = (1.0 - t) ** profile_exp * (1.0 - top_frac) + top_frac
        r_base = base_radius * prof * ring_wobble[i]
        for j in range(S):
            theta = 2.0 * math.pi * j / S
            ridge = 1.0 + 0.10 * math.sin(3.0 * theta + p1) \
                        + 0.06 * math.sin(7.0 * theta + p2)
            r = r_base * ridge * (1.0 + rng.uniform(-0.04, 0.04))
            verts.append((r * math.cos(theta), r * math.sin(theta), z))
    n_rings = len(ring_ts)
    faces = []
    for i in range(n_rings - 1):
        for j in range(S):
            j2 = (j + 1) % S
            faces.append((i * S + j, i * S + j2,
                          (i + 1) * S + j2, (i + 1) * S + j))
    top_ring = (n_rings - 1) * S
    if top_frac == 0.0:
        # Sharp apex vertex.
        apex_idx = len(verts)
        verts.append((apex_xy[0], apex_xy[1], height))
        for j in range(S):
            j2 = (j + 1) % S
            faces.append((top_ring + j, top_ring + j2, apex_idx))
    else:
        # Flat-topped plateau: fan to a centre vertex at the top.
        centre_idx = len(verts)
        verts.append((apex_xy[0], apex_xy[1], height))
        for j in range(S):
            j2 = (j + 1) % S
            faces.append((centre_idx, top_ring + j, top_ring + j2))
    # Bottom cap (centre vertex at z 0, normal down).
    bot_idx = len(verts)
    verts.append((0.0, 0.0, 0.0))
    for j in range(S):
        j2 = (j + 1) % S
        faces.append((bot_idx, j2, j))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    flat_shade(obj)
    recalc_outward(obj)
    return obj


def add_boulder(name, location, radius, material, seed):
    """Low-poly rock (icosahedron, squashed, random tilt) at the base."""
    rng = random.Random(seed)
    bpy.ops.mesh.primitive_ico_sphere_add(
        subdivisions=0, radius=radius, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (rng.uniform(0.85, 1.25), rng.uniform(0.8, 1.15),
                 rng.uniform(0.5, 0.75))
    obj.rotation_euler = (rng.uniform(0.0, 0.6),
                          rng.uniform(0.0, 0.6),
                          rng.uniform(0.0, 6.283))
    bpy.ops.object.transform_apply(location=False, rotation=False,
                                   scale=True)
    flat_shade(obj)
    obj.data.materials.append(material)
    return obj


def add_snow_cap(name, center_xy, peak_height, peak_radius, material,
                 seed):
    """Pale snow cap on a sub-peak's summit: a low, slightly squashed
    cone whose base sits just below the peak's apex and whose tip
    pokes slightly above it, so it reads as a frosted summit rather
    than a separate peak. The cone is jittered like the peaks so its
    edge is irregular, not a clean geometric cone."""
    rng = random.Random(seed)
    # Base radius ~0.45 of the sub-peak's radius, height ~0.5, base
    # at (peak_height - 0.25) so it overlaps the summit.
    cap_r = peak_radius * 0.45
    cap_h = 0.55
    base_z = peak_height - 0.25
    bpy.ops.mesh.primitive_cone_add(
        vertices=10, radius1=cap_r, radius2=0.02, depth=cap_h,
        location=(center_xy[0], center_xy[1], base_z + cap_h / 2.0))
    obj = bpy.context.active_object
    obj.name = name
    # Jitter the cone's vertices radially so the cap edge is irregular.
    for v in obj.data.vertices:
        f = 1.0 + rng.uniform(-0.08, 0.08)
        v.co.x *= f
        v.co.y *= f
    flat_shade(obj)
    recalc_outward(obj)
    obj.data.materials.append(material)
    return obj

# ---------------------------------------------------------------------
# Ridge builder: several low sub-peaks along X + wide low apron +
# boulders. The sub-peaks overlap into one continuous jagged ridge
# mass with an uneven top line.
# ---------------------------------------------------------------------
def build_ridge_variant(prefix, params, material, snow_material):
    """Build one horizon-ridge variant in the scene; returns its objects.
    params: dict with sub_peaks (list of dicts: x, y, radius, height,
    top_frac, profile_exp, apex_xy, segments, phase_off), skirt
    (dict: radius, height), boulders (list of (angle_deg,
    radius_from_centre, size, seed)), snow_cap (dict: peak_index —
    which sub-peak gets the cap). All within +/-2.5 (ridge mass),
    z 0-4.0. v2: 4 rings per sub-peak (was 3) for more vertical
    faceting."""
    objs = []
    base_phases = params["ridge_phases"]
    # Sub-peaks along the X axis (the ridge long axis).
    for i, sp in enumerate(params["sub_peaks"]):
        peak = build_cone_mesh(
            "%s_Peak_%02d" % (prefix, i + 1),
            sp["segments"], [0.0, 0.33, 0.66, 1.0], sp["height"],
            sp["radius"], sp["top_frac"], params["seed"] + i * 7,
            (base_phases[0] + sp["phase_off"],
             base_phases[1] + sp["phase_off"] * 1.3),
            apex_xy=sp.get("apex_xy", (0.0, 0.0)),
            profile_exp=sp["profile_exp"])
        peak.location = (sp["x"], sp["y"], 0.0)
        peak.data.materials.append(material)
        objs.append(peak)
    # Snow cap on the tallest sub-peak (params["snow_cap"]["peak_index"]).
    cap = params.get("snow_cap")
    if cap is not None:
        sp = params["sub_peaks"][cap["peak_index"]]
        cap_obj = add_snow_cap(
            prefix + "_SnowCap", (sp["x"], sp["y"]), sp["height"],
            sp["radius"], snow_material, params["seed"] + 97)
        objs.append(cap_obj)
    # Rock apron: low wide skirt so the base merges into the terrain.
    sk = params["skirt"]
    skirt = build_cone_mesh(
        prefix + "_Skirt", 10, [0.0, 1.0], sk["height"],
        sk["radius"], 0.5, params["seed"] + 23,
        (base_phases[0] + 0.9, base_phases[1] + 3.1),
        apex_xy=(0.0, 0.0), profile_exp=1.0)
    skirt.data.materials.append(material)
    objs.append(skirt)
    # Boulders around the base (biased toward the camera side, -Y).
    for i, (ang_deg, rad, size, sd) in enumerate(params["boulders"]):
        a = math.radians(ang_deg)
        x, y = rad * math.cos(a), rad * math.sin(a)
        b = add_boulder("%s_Boulder_%02d" % (prefix, i + 1),
                        (x, y, size * 0.35), size, material, sd)
        objs.append(b)
    return objs


# ---------------------------------------------------------------------
# Rock texture (numpy): value noise + fracture lines + dust.
# Same as the tall-mountain script: the Lightmap Pack UV atlas gives
# each face a small island, so the detail must read at a FINE scale
# — large soft blotches alone wash out to a flat tint.
# ---------------------------------------------------------------------
def value_noise(S, cell, seed):
    """(S, S) float 0-1 value noise, bilinear-interpolated random grid."""
    xs = np.arange(S, dtype=np.float32) / float(cell)
    ys = np.arange(S, dtype=np.float32) / float(cell)
    ix = np.floor(xs).astype(np.int32)
    iy = np.floor(ys).astype(np.int32)
    fx = xs - ix
    fy = ys - iy
    sx = fx * fx * (3.0 - 2.0 * fx)
    sy = fy * fy * (3.0 - 2.0 * fy)

    def h(ax, ay):
        v = np.sin(ax * 127.1 + ay * 311.7 + seed * 74.7) * 43758.5453
        return v - np.floor(v)

    IX, IY = np.meshgrid(ix, iy)
    h00 = h(IX, IY)
    h10 = h(IX + 1, IY)
    h01 = h(IX, IY + 1)
    h11 = h(IX + 1, IY + 1)
    SX, SY = np.meshgrid(sx, sy)
    top = h00 + (h10 - h00) * SX
    bot = h01 + (h11 - h01) * SX
    return top + (bot - top) * SY


def write_png(path, rgb_float):
    """Write an (H, W, 3) float 0-1 array as an 8-bit RGB PNG. Pure
    Python (zlib) — Blender's image.save() blackens a generated
    image's pixels on save, so the PNG is written directly instead.
    Rows are flipped so the file matches Blender's bottom-to-top
    pixel order."""
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


def make_rock_texture(rgb, seed, filename):
    """Generate one 1024 rock texture with numpy. Strong fine speckle
    and medium mottling, dark fracture lines at a fine scale, plus a
    large tonal variation that gives each face a slightly different
    base tone. Returns the Blender image; the PNG is written next to
    the script."""
    S = ATLAS_SIZE
    n_large = value_noise(S, 256, seed + 1)
    n_med = value_noise(S, 64, seed + 2)
    n_fine = value_noise(S, 24, seed + 3)
    n_grain = value_noise(S, 8, seed + 4)
    # Fracture: dark lines where the medium (64 px) noise is steepest
    # (its cell boundaries). Threshold from the data itself so the
    # crack fraction is stable across seeds. v2: stronger (0.75, was
    # 0.60) so the fractures read more clearly.
    gy, gx = np.gradient(n_med)
    gmag = np.sqrt(gx * gx + gy * gy)
    lo = np.percentile(gmag, 70)
    hi = np.percentile(gmag, 97)
    crack = np.clip((gmag - lo) / (hi - lo + 1e-6), 0.0, 1.0)
    crack = crack * crack
    # Dust: light soft patches where the large noise is brightest.
    dust = np.clip((n_large - 0.62) / 0.38, 0.0, 1.0)
    # Frost (NEW in v2): pale patches where the large noise is at its
    # very brightest AND the fine noise is high — reads as frost
    # settled in the high, rough areas. Subtle: it lightens, it does
    # not paint the rock white.
    frost = np.clip((n_large - 0.78) / 0.22, 0.0, 1.0) \
        * np.clip((n_fine - 0.55) / 0.45, 0.0, 1.0)
    base = np.array(rgb, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= (0.80 + 0.40 * n_large)[..., None]
    arr *= (0.80 + 0.40 * n_med)[..., None]
    arr *= (0.85 + 0.30 * n_fine)[..., None]
    arr *= (0.88 + 0.24 * n_grain)[..., None]
    arr *= (1.0 - 0.75 * crack[..., None])
    arr *= (1.0 + 0.25 * dust[..., None])
    arr *= (1.0 + 0.35 * frost[..., None])
    arr = np.clip(arr, 0.0, 1.0)
    rgba = np.ones((S, S, 4), dtype=np.float32)
    rgba[..., :3] = arr
    img = bpy.data.images.new(filename, S, S, alpha=False)
    img.pixels.foreach_set(rgba.ravel())
    path = os.path.join(SCRIPT_DIR, filename)
    write_png(path, arr)
    img.filepath = path
    img.file_format = "PNG"
    return img


def apply_rock_texture(material, rgb, seed, filename):
    """Generate the variant's rock texture, plug it into the
    material's Base Color, and set the base colour to white so
    Unity's (map x colour) multiply returns exactly the texture."""
    img = make_rock_texture(rgb, seed, filename)
    nt = material.node_tree
    node = nt.nodes.new("ShaderNodeTexImage")
    node.name = "BakedTexture"
    node.image = img
    bsdf = nt.nodes["Principled BSDF"]
    nt.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Base Color"].default_value = (1.0, 1.0, 1.0, 1.0)
    path = os.path.join(SCRIPT_DIR, filename)
    return (material.name, path, os.path.getsize(path))


def make_uv_atlas(objects):
    """Pack every mesh object's UV islands into one 0-1 atlas
    (Lightmap Pack)."""
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


def report(name, objects, fbx_path, texture_info):
    total_tris = 0
    print("%s build:" % name)
    for obj in objects:
        if obj.type != "MESH":
            continue
        mesh = obj.data
        mesh.calc_loop_triangles()
        total_tris += len(mesh.loop_triangles)
        print("  %-22s tris=%4d" % (obj.name, len(mesh.loop_triangles)))
    print("  objects=%d  total_tris=%d" % (len(objects), total_tris))
    pts = [o.matrix_world @ v.co for o in objects if o.type == "MESH"
           for v in o.data.vertices]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    print("  bounds x=[%.2f, %.2f] y=[%.2f, %.2f] z=[%.2f, %.2f]"
          % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    print("  exported: %s (%d bytes)" % (fbx_path, os.path.getsize(fbx_path)))
    print("  texture %s: %s (%d bytes)" % texture_info)


# ---------------------------------------------------------------------
# Variants. Sub-peaks are dicts: x, y (offset from centre), radius,
# height, top_frac, profile_exp, segments, phase_off, apex_xy.
# Boulders are (angle_deg, radius_from_centre, size, seed); angles
# are biased toward -Y (the camera side) so the base reads rocky
# from the game view. Ridge long axis is X.
# ---------------------------------------------------------------------
VARIANTS = {
    "HorizonA2": {
        "label": "Broad Ridge v2",
        "rgb": ROCK_A, "seed": 401,
        "texture": "HorizonA2_Rock.png",
        "ridge_phases": (0.7, 2.1),
        "sub_peaks": [
            {"x": -0.85, "y": 0.15, "radius": 1.55, "height": 2.2,
             "top_frac": 0.10, "profile_exp": 1.05, "segments": 12,
             "phase_off": 0.0},
            {"x": -0.30, "y": -0.20, "radius": 1.65, "height": 2.8,
             "top_frac": 0.12, "profile_exp": 1.05, "segments": 12,
             "phase_off": 1.3},
            {"x": 0.30, "y": 0.25, "radius": 1.60, "height": 2.5,
             "top_frac": 0.10, "profile_exp": 1.05, "segments": 12,
             "phase_off": 2.6},
            {"x": 0.85, "y": -0.10, "radius": 1.45, "height": 2.0,
             "top_frac": 0.10, "profile_exp": 1.05, "segments": 12,
             "phase_off": 3.9},
            {"x": 1.35, "y": 0.20, "radius": 1.20, "height": 1.8,
             "top_frac": 0.10, "profile_exp": 1.05, "segments": 12,
             "phase_off": 5.2},
        ],
        "snow_cap": {"peak_index": 1},
        "skirt": {"radius": 2.3, "height": 0.8},
        "boulders": [(200, 2.2, 0.50, 401), (260, 2.0, 0.45, 402),
                     (320, 2.3, 0.55, 403)],
    },
    "HorizonB2": {
        "label": "Sharp Crags v2",
        "rgb": ROCK_B, "seed": 502,
        "texture": "HorizonB2_Rock.png",
        "ridge_phases": (2.4, 0.3),
        "sub_peaks": [
            {"x": -0.90, "y": 0.10, "radius": 1.45, "height": 2.8,
             "top_frac": 0.0, "profile_exp": 1.2, "segments": 12,
             "phase_off": 0.0},
            {"x": 0.00, "y": -0.25, "radius": 1.55, "height": 3.6,
             "top_frac": 0.08, "profile_exp": 1.2, "segments": 12,
             "phase_off": 1.7},
            {"x": 0.90, "y": 0.15, "radius": 1.40, "height": 3.0,
             "top_frac": 0.0, "profile_exp": 1.2, "segments": 12,
             "phase_off": 3.4},
            {"x": 1.40, "y": -0.20, "radius": 1.10, "height": 2.2,
             "top_frac": 0.0, "profile_exp": 1.2, "segments": 12,
             "phase_off": 5.1},
        ],
        "snow_cap": {"peak_index": 1},
        "skirt": {"radius": 2.3, "height": 0.7},
        "boulders": [(210, 2.1, 0.50, 501), (250, 2.3, 0.45, 502),
                     (290, 2.0, 0.50, 503), (330, 2.2, 0.42, 504)],
    },
    "HorizonC2": {
        "label": "Craggy Bluff v2",
        "rgb": ROCK_C, "seed": 603,
        "texture": "HorizonC2_Rock.png",
        "ridge_phases": (4.1, 1.2),
        "sub_peaks": [
            {"x": -0.85, "y": -0.15, "radius": 1.70, "height": 2.2,
             "top_frac": 0.16, "profile_exp": 1.05, "segments": 12,
             "phase_off": 0.0},
            {"x": 0.10, "y": 0.20, "radius": 1.75, "height": 2.8,
             "top_frac": 0.20, "profile_exp": 1.05, "segments": 12,
             "phase_off": 1.9},
            {"x": 0.95, "y": -0.05, "radius": 1.60, "height": 2.4,
             "top_frac": 0.16, "profile_exp": 1.05, "segments": 12,
             "phase_off": 3.8},
            {"x": -1.35, "y": 0.15, "radius": 1.25, "height": 1.9,
             "top_frac": 0.16, "profile_exp": 1.05, "segments": 12,
             "phase_off": 5.7},
        ],
        "snow_cap": {"peak_index": 1},
        "skirt": {"radius": 2.4, "height": 0.9},
        "boulders": [(190, 2.2, 0.55, 601), (230, 2.4, 0.50, 602),
                     (270, 2.1, 0.60, 603), (310, 2.3, 0.48, 604),
                     (150, 2.2, 0.45, 605)],
    },
}


def build_one(name, params):
    """Build, texture and export one variant; returns its report data."""
    clear_scene()
    mat = make_material(name + "Rock", params["rgb"])
    snow_mat = make_material(name + "Snow", SNOW)
    objs = build_ridge_variant(name, params, mat, snow_mat)
    make_uv_atlas(objs)
    tex_info = apply_rock_texture(mat, params["rgb"], params["seed"],
                                  params["texture"])
    fbx_path = os.path.join(SCRIPT_DIR, name + ".fbx")
    export_fbx(objs, fbx_path)
    report("%s (%s)" % (name, params["label"]), objs, fbx_path, tex_info)
    return objs, fbx_path, tex_info


if __name__ == "__main__":
    for _name, _params in VARIANTS.items():
        build_one(_name, _params)
    print("horizon mountains done: %s" % ", ".join(VARIANTS.keys()))
