# BuildGroundTextureDebris.py — Lunar Dominion: ground regolith texture
# + debris models (first ground-work piece, after all 8 buildings
# reached v4 standard).
#
# ASSETS ONLY — no Unity code. The BaseView.cs wiring to apply the
# texture and place the debris is a separate step done later.
#
# WHAT IT GENERATES (next to this script):
#   GroundRegolith.png — 1024x1024 SEAMLESSLY TILING regolith texture
#     (dust, pebbles, faint patches, tiny pocks). Base colour is the
#     sRGB encoding of the current ground's linear (0.27, 0.29, 0.33),
#     i.e. (0.56, 0.58, 0.61), so the look stays consistent in Unity.
#     Goes in Assets/_Project/Resources/Textures/ (like the splash PNG).
#   DebrisRubble.fbx / DebrisRocks.fbx / DebrisShards.fbx — three small
#     low-poly debris variants (a few hundred tris each), flat base at
#     z 0, grey regolith palette. Go in
#     Assets/_Project/Resources/Models/ (like the buildings). They are
#     NOT tappable: the code must strip their colliders like the
#     existing decor (no BoxCollider is added here).
#
# HOW THE TEXTURE IS MADE: pixel-generated with numpy, NOT a Cycles
# bake (a bake is unusable headless — even a flat-colour bake takes
# 5+ minutes on this VM). Every feature is seamless by construction:
# integer-frequency sinusoids for the tonal/grain variation, per-pixel
# noise (independent pixels cannot seam), and wrapped stamps for the
# patches/pebbles/pocks (each stamp is split toroidally at the edges).
# Written with the pure-Python zlib PNG encoder — Blender's
# image.save()/save_render() blacken generated pixels.
#
# DEBRIS: angular broken rocks built from jittered icospheres with
# flat shading (reads as broken stone, not smooth blobs), plus flat
# shards (thin boxes). Plain grey materials — no texture on the rocks:
# the regolith PNG lives in Resources/Textures/, so an FBX referencing
# it would not resolve next to the model in Resources/Models/.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildGroundTextureDebris.py
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
TEXTURE_FILE = "GroundRegolith.png"
TEXTURE_PATH = os.path.join(SCRIPT_DIR, TEXTURE_FILE)
FBX_FILES = {
    "rubble": "DebrisRubble.fbx",
    "rocks": "DebrisRocks.fbx",
    "shards": "DebrisShards.fbx",
}

# Ground base colour — as sRGB DISPLAY values, not the linear values.
# BaseView.cs sets the ground material to linear (0.27, 0.29, 0.33);
# a texture PNG stores sRGB, and Unity decodes sRGB -> linear on
# sample, so the PNG must hold the sRGB encoding of that linear
# colour: (0.56, 0.58, 0.61). Authoring it at (0.27, 0.29, 0.33)
# would decode to linear ~0.06 and the ground would render nearly
# black (caught by the Cycles tiling check, which renders the
# texture at its linear value). Calibrated against a Unity
# screenshot: the ground displays at ~sRGB (0.37, 0.41, 0.50) under
# the scene lighting, consistent with linear albedo 0.27-0.33.
REGOLITH = (0.56, 0.58, 0.61)
# Debris palette (matches the existing rock colours, 0.30-0.44).
ROCK_DARK = (0.22, 0.24, 0.28)
ROCK_MID = (0.30, 0.32, 0.36)
ROCK_LIGHT = (0.38, 0.40, 0.44)

TEXTURE_SIZE = 1024


def make_material(name, rgb, metallic=0.0, roughness=0.9):
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    return mat


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


def stamp(arr, cx, cy, kernel):
    """Add `kernel` (2D float array) to `arr` (H, W) centred at (cx, cy),
    wrapping toroidally at the edges so the result tiles seamlessly.
    The kernel is split into up to 4 rectangles at the wrap boundary."""
    H, W = arr.shape
    kh, kw = kernel.shape
    x0 = int(round(cx - kw / 2.0))
    y0 = int(round(cy - kh / 2.0))

    def intervals(start, length, size):
        s = start % size
        if s + length <= size:
            return [(s, 0, length)]
        first = size - s
        return [(s, 0, first), (0, first, length - first)]

    for (ax, kx, nx) in intervals(x0, kw, W):
        for (ay, ky, ny) in intervals(y0, kh, H):
            arr[ay:ay + ny, ax:ax + nx] += kernel[ky:ky + ny, kx:kx + nx]


def gauss_kernel(radius, sigma, amp):
    """2D Gaussian disc kernel, half-size `radius` px, peak `amp`."""
    r = int(math.ceil(radius))
    yy, xx = np.mgrid[-r:r + 1, -r:r + 1].astype(np.float32)
    return amp * np.exp(-(xx * xx + yy * yy) / (2.0 * sigma * sigma))


def make_regolith_texture():
    """Generate the 1024 seamlessly tiling regolith texture with numpy.
    Base colour is the sRGB encoding of the current ground's linear
    (0.27, 0.29, 0.33) — (0.56, 0.58, 0.61) — so Unity displays it
    the same as the plain ground. Dust variation, scattered pebbles
    (10-28 px), faint darker patches (50-200 px), tiny pocks. Returns
    the float array; the PNG is written next to the script."""
    S = TEXTURE_SIZE
    rng = np.random.default_rng(7)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    base = np.array(REGOLITH, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    # Large tonal variation + fine grain: integer-frequency sinusoids,
    # so the pattern repeats exactly at the tile edge.
    var = np.zeros((S, S), dtype=np.float32)
    for (fx, fy, amp, ph) in ((2, 1, 0.030, 0.0), (3, -2, 0.020, 1.7),
                              (5, 3, 0.015, 4.2), (-4, 5, 0.012, 2.3),
                              (37, 21, 0.015, 0.5), (53, -31, 0.012, 2.0),
                              (71, 47, 0.010, 3.1)):
        var += amp * np.sin(2.0 * np.pi * (fx * xx + fy * yy) / S + ph)
    arr *= (1.0 + var[..., None])
    # Per-pixel noise (independent pixels cannot seam).
    arr *= (1.0 + rng.normal(0.0, 0.012, (S, S, 1)).astype(np.float32))
    # Soft darker patches (14) and a few lighter ones (6), wrapped.
    patch = np.zeros((S, S), dtype=np.float32)
    for _ in range(14):
        px, py = rng.uniform(0, S, 2)
        pr = rng.uniform(50, 200)
        stamp(patch, px, py,
              gauss_kernel(pr, pr / 2.5, -rng.uniform(0.05, 0.16)))
    for _ in range(6):
        px, py = rng.uniform(0, S, 2)
        pr = rng.uniform(60, 180)
        stamp(patch, px, py,
              gauss_kernel(pr, pr / 2.5, rng.uniform(0.04, 0.08)))
    arr *= (1.0 + patch[..., None])
    # Pebbles: ~700 small discs (5-14 px radius), each a light bump
    # with a darker shadow offset by (2, 2) px, wrapped.
    peb = np.zeros((S, S), dtype=np.float32)
    for _ in range(700):
        px, py = rng.uniform(0, S, 2)
        pr = rng.uniform(5, 14)
        stamp(peb, px, py,
              gauss_kernel(2 * pr, pr / 2.0, rng.uniform(0.08, 0.16)))
        stamp(peb, px + 2, py + 2,
              gauss_kernel(2 * pr, pr / 2.0, -rng.uniform(0.06, 0.12)))
    arr *= (1.0 + peb[..., None])
    # Tiny pocks: ~220 small dark dots (2-4 px) with a light rim, wrapped.
    pock = np.zeros((S, S), dtype=np.float32)
    for _ in range(220):
        px, py = rng.uniform(0, S, 2)
        pr = rng.uniform(2, 4)
        stamp(pock, px, py,
              gauss_kernel(2 * pr, pr / 2.0, -rng.uniform(0.08, 0.15)))
        stamp(pock, px - 1, py - 1,
              gauss_kernel(2 * pr, pr / 2.0, rng.uniform(0.03, 0.06)))
    arr *= (1.0 + pock[..., None])
    arr = np.clip(arr, 0.0, 1.0)
    # Restore the base mean (the features roughly balance, but correct
    # any drift so the ground keeps its current colour).
    mean = arr.reshape(-1, 3).mean(axis=0)
    arr = np.clip(arr + (base - mean)[None, None, :], 0.0, 1.0)
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


def add_rock(name, center, radius, z_scale, material, seed,
             subdivisions=2, jitter=0.18):
    """Angular broken rock: jittered icosphere, flat shading, squashed
    in Z, resting on `center[2]` (its lowest vertex sits at that z).
    NOTE: in Blender 4.2 the icosphere operator counts subdivisions
    from 1 = base icosahedron (20 faces), so subdivisions=2 gives the
    80-face rock and subdivisions=1 gives 20 faces (pebbles)."""
    rng = np.random.default_rng(seed)
    bpy.ops.mesh.primitive_ico_sphere_add(
        subdivisions=subdivisions, radius=radius, location=center)
    obj = bpy.context.active_object
    obj.name = name
    mesh = obj.data
    for v in mesh.vertices:
        f = 1.0 + rng.uniform(-jitter, jitter)
        v.co *= f
    for p in mesh.polygons:
        p.use_smooth = False
    obj.scale = (1.0, 1.0, z_scale)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    minz = min(v.co.z for v in mesh.vertices)
    obj.location = (center[0], center[1], center[2] - minz)
    obj.data.materials.append(material)
    return obj


def add_shard(name, center, length, width, thick, rot_z, material):
    """Flat angular shard: thin box lying on the ground, random yaw."""
    bpy.ops.mesh.primitive_cube_add(
        location=center, rotation=(0.0, 0.0, rot_z))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (length / 2.0, width / 2.0, thick / 2.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def build_rubble(mats):
    """DebrisRubble: a rubble pile — 6 angular broken rocks piled up
    (3 base, 2 middle, 1 top) plus 3 tiny pebbles at the foot.
    ~0.7 units wide, ~0.32 tall, base at z 0."""
    objs = []
    # Base ring: three rocks in a triangle.
    objs.append(add_rock("Rubble_Base_01", (0.14, 0.0, 0.0), 0.20, 0.60,
                         mats["mid"], 101))
    objs.append(add_rock("Rubble_Base_02", (-0.10, 0.13, 0.0), 0.18, 0.65,
                         mats["dark"], 102))
    objs.append(add_rock("Rubble_Base_03", (-0.06, -0.15, 0.0), 0.17, 0.60,
                         mats["light"], 103))
    # Middle layer, resting on the base rocks.
    objs.append(add_rock("Rubble_Mid_01", (0.05, 0.02, 0.12), 0.14, 0.60,
                         mats["light"], 104))
    objs.append(add_rock("Rubble_Mid_02", (-0.08, -0.05, 0.10), 0.12, 0.65,
                         mats["mid"], 105))
    # Top rock.
    objs.append(add_rock("Rubble_Top_01", (0.0, 0.0, 0.20), 0.10, 0.60,
                         mats["dark"], 106))
    # Tiny pebbles at the foot of the pile.
    for i, (x, y, r) in enumerate(((0.32, 0.18, 0.05), (-0.30, 0.22, 0.04),
                                   (0.05, -0.32, 0.06))):
        objs.append(add_rock("Rubble_Pebble_%02d" % (i + 1), (x, y, 0.0), r,
                             0.55, mats["mid"], 110 + i, subdivisions=0))
    return objs


def build_rocks(mats):
    """DebrisRocks: a cluster of 4 broken rocks of different sizes,
    spread over ~0.9 units, plus 2 tiny pebbles. Base at z 0."""
    objs = []
    objs.append(add_rock("Rocks_Main_01", (-0.30, 0.10, 0.0), 0.18, 0.60,
                         mats["mid"], 201))
    objs.append(add_rock("Rocks_Main_02", (0.25, -0.15, 0.0), 0.14, 0.65,
                         mats["dark"], 202))
    objs.append(add_rock("Rocks_Main_03", (0.10, 0.30, 0.0), 0.11, 0.60,
                         mats["light"], 203))
    objs.append(add_rock("Rocks_Main_04", (0.35, 0.20, 0.0), 0.08, 0.60,
                         mats["mid"], 204))
    for i, (x, y, r) in enumerate(((-0.05, -0.30, 0.05), (0.42, -0.05, 0.04))):
        objs.append(add_rock("Rocks_Pebble_%02d" % (i + 1), (x, y, 0.0), r,
                             0.55, mats["dark"], 210 + i, subdivisions=0))
    return objs


def build_shards(mats):
    """DebrisShards: 8 scattered flat shards/pebbles spread over
    ~1 unit, plus 6 tiny pebbles. All flat on the ground (z 0)."""
    objs = []
    rng = np.random.default_rng(301)
    shard_defs = [
        (0.30, 0.16, 0.04, (-0.30, 0.15)),
        (0.24, 0.12, 0.03, (0.25, -0.20)),
        (0.20, 0.14, 0.04, (0.05, 0.32)),
        (0.26, 0.10, 0.03, (-0.10, -0.32)),
        (0.18, 0.12, 0.03, (0.38, 0.10)),
        (0.22, 0.10, 0.03, (-0.38, -0.10)),
        (0.16, 0.10, 0.03, (0.15, 0.05)),
        (0.20, 0.12, 0.04, (-0.20, 0.30)),
    ]
    for i, (length, width, thick, (x, y)) in enumerate(shard_defs):
        rot = float(rng.uniform(0.0, math.pi))
        mat = mats["mid"] if i % 2 == 0 else mats["dark"]
        objs.append(add_shard("Shards_Shard_%02d" % (i + 1),
                              (x, y, thick / 2.0), length, width, thick,
                              rot, mat))
    for i, (x, y, r) in enumerate(((0.30, 0.30, 0.04), (-0.32, 0.28, 0.03),
                                   (0.05, -0.12, 0.05), (-0.05, 0.42, 0.03),
                                   (0.42, -0.28, 0.04), (-0.28, -0.28, 0.03))):
        objs.append(add_rock("Shards_Pebble_%02d" % (i + 1), (x, y, 0.0), r,
                             0.55, mats["light"], 310 + i, subdivisions=0))
    return objs


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


if __name__ == "__main__":
    clear_scene()

    mats = {
        "dark": make_material("DebrisDark", ROCK_DARK, roughness=0.95),
        "mid": make_material("DebrisMid", ROCK_MID, roughness=0.95),
        "light": make_material("DebrisLight", ROCK_LIGHT, roughness=0.95),
    }

    print("Ground texture + debris build:")
    make_regolith_texture()

    rubble = build_rubble(mats)
    rocks = build_rocks(mats)
    shards = build_shards(mats)

    for label, objects in (("DebrisRubble", rubble),
                           ("DebrisRocks", rocks),
                           ("DebrisShards", shards)):
        key = label.replace("Debris", "").lower()
        path = os.path.join(SCRIPT_DIR, FBX_FILES[key])
        export_fbx(objects, path)
        report_model(label, objects, path)
