# BuildMountains.py — Lunar Dominion: mountain range v2
# (detailed Blender mountains to replace the 13 primitive peaks).
#
# Replaces the 13 code-built stacked-cylinder mountains (BaseView.cs
# BuildMountains, full-bleed patch a5351d11) with three detailed,
# textured, low-poly rocky peaks at the v2 standard used for the 8
# buildings: procedural craggy geometry (jittered rings with coherent
# ridge variation, a shoulder peak, a rock skirt, base boulders),
# numpy pixel-generated rock texture (NOT the building panel texture
# — rock needs noise/fracture variation, not panel cells), Lightmap
# Pack UV atlas, pure-Python zlib PNG encoder (Blender's image.save()
# blackens generated pixels — never use it).
#
# NOMINAL SIZE (all three variants, so the wiring can scale them
# uniformly): footprint within +/-3.5, height 0-7.0. The wiring scales
# each instance to its spot as (spotRadius/3.5, spotHeight/7.0,
# spotRadius/3.5). No emission — mountains are unlit rock.
#
# VARIANTS (distinct silhouettes, not the same shape 3 times):
#   MountainA "Broad Twin"  — wide base, two peaks (main + shoulder).
#   MountainB "Sharp Spire" — narrower, steeper, apex leans toward
#                             the camera (-Y) for a dramatic spire.
#   MountainC "Craggy Dome" — fuller profile, flat-topped summit
#                             plateau, heaviest boulder field.
#
# CAMERA NOTE: the game camera sits at Unity (10,16,10) looking at
# the origin, so detail faces Blender -Y / Unity +Z. Ridge and
# shoulder features are biased toward the -Y side.
#
# NORMAL SAFETY (Helium-3 dish lesson: Cycles renders backfaces,
# Unity's URP culls them, so a flipped-normal surface looks fine in
# a Cycles preview and vanishes in the game). Every mesh built here
# gets its face normals recalculated outward with bmesh
# (recalc_face_normals) and flat shading, and the preview script
# renders a red-backface check for each variant.
#
# FBX EXPORT: same convention as the building scripts (axis_forward
# "-Z", axis_up "Y", bake_space_transform False) — the -90 deg X
# axis conversion stays in the model node, exactly like the building
# FBXs. Import the FBX with the same settings as the buildings
# (notably "Bake Axis Conversion" ON, as for the terrain fix), and
# the wiring must NOT force the terrain rotation to identity on
# these — set only position, Y rotation and scale (see README).
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildMountains.py
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
NOMINAL_RADIUS = 3.5
NOMINAL_HEIGHT = 7.0

# Rock base tones (display values, one per variant so the range does
# not read as three clones of one grey).
ROCK_A = (0.42, 0.43, 0.46)   # mid grey
ROCK_B = (0.35, 0.37, 0.42)   # darker blue-grey
ROCK_C = (0.45, 0.42, 0.38)   # warmer brown-grey


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
# ---------------------------------------------------------------------
def build_cone_mesh(name, segments, ring_ts, height, base_radius,
                    top_frac, seed, ridge_phases, apex_xy=(0.0, 0.0),
                    profile_exp=1.2):
    """A craggy cone: rings of jittered vertices with a ridge function
    coherent up the peak (same ridge angles on every ring, so ridges
    read as vertical features, not per-vertex noise), a flat-shaded
    faceted surface, and a closed bottom cap at z 0.
    ring_ts: t values 0..1 for the side rings. radius(t) =
    base_radius * ((1-t)**profile_exp * (1-top_frac) + top_frac).
    top_frac 0 -> sharp apex vertex; > 0 -> flat-topped plateau ring
    capped with a fan."""
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

# ---------------------------------------------------------------------
# Variant builder: main peak + shoulder + rock skirt + boulders.
# ---------------------------------------------------------------------
def build_variant(prefix, params, material):
    """Build one mountain variant in the scene; returns its objects.
    params: dict with segments, profile_exp, ridge_phases, apex_xy,
    top_frac, shoulder (dict or None), skirt_radius, boulders (list
    of (angle_deg, radius, size, seed)). All within +/-3.5, z 0-7."""
    objs = []
    H = NOMINAL_HEIGHT
    # Main peak.
    peak = build_cone_mesh(
        prefix + "_Peak", params["segments"],
        [0.0, 0.18, 0.36, 0.54, 0.72, 0.88], H,
        params["peak_radius"], params["top_frac"], params["seed"],
        params["ridge_phases"], apex_xy=params["apex_xy"],
        profile_exp=params["profile_exp"])
    peak.data.materials.append(material)
    objs.append(peak)
    # Shoulder peak (second, smaller cone, offset from centre).
    sh = params.get("shoulder")
    if sh is not None:
        a = math.radians(sh["angle_deg"])
        ox, oy = sh["offset"] * math.cos(a), sh["offset"] * math.sin(a)
        shoulder = build_cone_mesh(
            prefix + "_Shoulder", sh["segments"],
            [0.0, 0.25, 0.55, 0.85], sh["height"], sh["radius"], 0.0,
            params["seed"] + 11,
            (params["ridge_phases"][0] + 1.7,
             params["ridge_phases"][1] + 2.3),
            apex_xy=(0.0, 0.0), profile_exp=1.25)
        shoulder.location = (ox, oy, 0.0)
        shoulder.data.materials.append(material)
        objs.append(shoulder)
    # Rock skirt: low wide apron so the base merges into the terrain.
    skirt = build_cone_mesh(
        prefix + "_Skirt", 10, [0.0, 1.0], 1.0,
        params["skirt_radius"], 0.55, params["seed"] + 23,
        (params["ridge_phases"][0] + 0.9,
         params["ridge_phases"][1] + 3.1),
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
    """Generate one 1024 rock texture with numpy. The Lightmap Pack
    UV atlas gives each face a small island, so the detail must read
    at a FINE scale (like the building panel grid, whose 48 px lines
    show inside one face's island) — large soft blotches alone wash
    out to a flat tint. So: strong fine speckle and medium mottling,
    dark fracture lines at a fine scale, plus a large tonal variation
    that gives each face a slightly different base tone.
    Returns the Blender image; the PNG is written next to the script."""
    S = ATLAS_SIZE
    n_large = value_noise(S, 256, seed + 1)
    n_med = value_noise(S, 64, seed + 2)
    n_fine = value_noise(S, 24, seed + 3)
    n_grain = value_noise(S, 8, seed + 4)
    # Fracture: dark lines where the medium (64 px) noise is steepest
    # (its cell boundaries). Threshold from the data itself so the
    # crack fraction is stable across seeds; lower percentile than
    # the first pass so cracks are frequent and visible.
    gy, gx = np.gradient(n_med)
    gmag = np.sqrt(gx * gx + gy * gy)
    lo = np.percentile(gmag, 70)
    hi = np.percentile(gmag, 97)
    crack = np.clip((gmag - lo) / (hi - lo + 1e-6), 0.0, 1.0)
    crack = crack * crack
    # Dust: light soft patches where the large noise is brightest.
    dust = np.clip((n_large - 0.62) / 0.38, 0.0, 1.0)
    base = np.array(rgb, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= (0.80 + 0.40 * n_large)[..., None]
    arr *= (0.80 + 0.40 * n_med)[..., None]
    arr *= (0.85 + 0.30 * n_fine)[..., None]
    arr *= (0.88 + 0.24 * n_grain)[..., None]
    arr *= (1.0 - 0.60 * crack[..., None])
    arr *= (1.0 + 0.25 * dust[..., None])
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
# Variants. Boulders are (angle_deg, radius_from_centre, size, seed);
# angles are biased toward -Y (the camera side) so the base reads
# rocky from the game view. All footprints stay within +/-3.5.
# ---------------------------------------------------------------------
VARIANTS = {
    "MountainA": {
        "label": "Broad Twin",
        "rgb": ROCK_A, "seed": 101,
        "texture": "MountainA_Rock.png",
        "segments": 12, "profile_exp": 1.2, "top_frac": 0.0,
        "peak_radius": 3.1, "apex_xy": (0.0, 0.0),
        "ridge_phases": (0.7, 2.1),
        "shoulder": {"angle_deg": 200, "offset": 1.1, "radius": 1.5,
                     "height": 4.8, "segments": 9},
        "skirt_radius": 3.5,
        "boulders": [(200, 2.9, 0.55, 301), (250, 2.6, 0.45, 302),
                     (290, 3.0, 0.60, 303), (160, 2.7, 0.40, 304),
                     (90, 2.8, 0.50, 305)],
    },
    "MountainB": {
        "label": "Sharp Spire",
        "rgb": ROCK_B, "seed": 202,
        "texture": "MountainB_Rock.png",
        "segments": 10, "profile_exp": 1.35, "top_frac": 0.0,
        "peak_radius": 2.9, "apex_xy": (0.4, -0.3),
        "ridge_phases": (2.4, 0.3),
        "shoulder": {"angle_deg": 320, "offset": 1.3, "radius": 1.0,
                     "height": 3.2, "segments": 8},
        "skirt_radius": 3.5,
        "boulders": [(210, 2.8, 0.50, 311), (270, 3.0, 0.55, 312),
                     (330, 2.6, 0.42, 313), (140, 2.9, 0.48, 314)],
    },
    "MountainC": {
        "label": "Craggy Dome",
        "rgb": ROCK_C, "seed": 303,
        "texture": "MountainC_Rock.png",
        "segments": 11, "profile_exp": 1.1, "top_frac": 0.14,
        "peak_radius": 3.2, "apex_xy": (0.0, 0.0),
        "ridge_phases": (4.1, 1.2),
        "shoulder": {"angle_deg": 60, "offset": 1.0, "radius": 1.4,
                     "height": 4.0, "segments": 9},
        "skirt_radius": 3.5,
        "boulders": [(190, 2.7, 0.55, 321), (230, 3.0, 0.60, 322),
                     (270, 2.6, 0.45, 323), (310, 2.9, 0.52, 324),
                     (150, 2.8, 0.44, 325), (100, 2.7, 0.50, 326)],
    },
}


def build_one(name, params):
    """Build, texture and export one variant; returns its report data."""
    clear_scene()
    mat = make_material(name + "Rock", params["rgb"])
    objs = build_variant(name, params, mat)
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
    print("mountains done: %s" % ", ".join(VARIANTS.keys()))
