# BuildAlienPlanet.py — Lunar Dominion: alien planet on the horizon.
#
# ASSETS ONLY — no Unity code. The BaseView.cs wiring to place the
# planet is a separate step done later.
#
# WHAT IT GENERATES (next to this script):
#   AlienPlanet.png — 1024x512 equirectangular banded texture:
#     dusty rose / amber / muted teal / deep purple bands with
#     wavy edges, subtle cloud streaks, fine noise, and a slightly
#     brighter north pole (the upper limb as seen from the game
#     camera). Alien bands — NOT Earth-like (no blue oceans or
#     green continents).
#   AlienPlanet.fbx — one UV sphere, radius PLANET_RADIUS,
#     centred at its own origin, so the wiring step can place it
#     by position alone (Resources.Load "Models/AlienPlanet").
#     No collider, untappable — scenery only.
#
# PLACEMENT (verified from a game-camera render — see
# render_preview.py and the README): the planet sits far out on
# the -X/-Z horizon, LOW, so only the top arc shows above the
# mountains. The game camera looks down steeply (48.5 deg), so
# the frame top is at elevation -18.5 deg from the camera and
# the mountain tops sit at NDC 1.01-1.19 (cut off by the frame).
# The verified centre is Unity (-47, -20, -47), radius 26: the
# disc is ~246 px across in the 768x432 game-camera render and
# roughly its top third shows above the mountains (a broad
# ~230 px wide, ~75 px tall arc at the top centre, bands
# clearly readable). The centre is below ground level (y -20)
# — that is expected: the planet is a huge distant world, most
# of it below the horizon line, only its top rising into the
# frame. Do NOT "fix" the negative y and do NOT raise it into
# the sky.
# The first guess (-50, 5, -50) r12 was checked and is wrong:
# its disc sits at NDC 1.13-2.01, completely out of frame. A
# radius-15 disc at the same centre showed only a thin wedge
# (~170 px wide, ~30 px tall) — too small to read as a looming
# world. Radius 26 is the verified size.
#
# HOW THE TEXTURE IS MADE: pixel-generated with numpy, NOT a
# Cycles bake (a bake is unusable headless). Bands are defined
# in v (latitude) with wavy boundaries (sums of sines in u),
# cloud streaks are elongated sine wisps, then fine noise.
# Written with the pure-Python zlib PNG encoder — Blender's
# image.save()/save_render() blacken generated pixels.
#
# COLOUR NOTE (from the ground texture): the regolith PNG was
# authored at sRGB (0.56, 0.58, 0.61) and renders in Play at
# ~sRGB 0.53 — i.e. roughly as-authored, not decoded to linear.
# So this texture is authored at the DISPLAY values it should
# read as. Do not "correct" these to linear values.
#
# BACKFACE NOTE (Harvester dish lesson): the planet is a UV
# sphere (outward normals by construction) — no cones or open
# dishes anywhere. The verification script checks the exported
# FBX face normals point outward.
#
# RING: deliberately NOT built. A Saturn-style ring around a
# planet of which only the top arc is visible would read as a
# thin stray line above the mountains and clutter the horizon;
# the bare banded disc reads cleaner at this framing.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildAlienPlanet.py
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
TEXTURE_FILE = "AlienPlanet.png"
TEXTURE_PATH = os.path.join(SCRIPT_DIR, TEXTURE_FILE)
FBX_FILE = "AlienPlanet.fbx"

# Verified placement (Unity coords) — see header + README.
PLANET_CENTER_UNITY = (-47.0, -20.0, -47.0)
PLANET_RADIUS = 26.0

TEX_W = 1024
TEX_H = 512

# --- palette (display values, see colour note above) ---
# South pole (v 0) -> north pole (v 1).
BAND_COLORS = [
    (0.30, 0.23, 0.42),   # 0 deep purple (south polar cap)
    (0.70, 0.44, 0.40),   # 1 dusty rose
    (0.76, 0.56, 0.30),   # 2 amber
    (0.34, 0.53, 0.53),   # 3 muted teal
    (0.82, 0.75, 0.62),   # 4 pale cream (equatorial bright band)
    (0.66, 0.42, 0.44),   # 5 dusty rose
    (0.36, 0.28, 0.48),   # 6 deep purple
    (0.72, 0.52, 0.32),   # 7 amber
    (0.70, 0.62, 0.72),   # 8 pale lilac (north polar cap)
]
# Band boundaries in v (9 colours -> 8 boundaries).
BAND_EDGES = [0.08, 0.22, 0.35, 0.48, 0.62, 0.74, 0.86, 0.94]
# Per-boundary waviness: (amplitude in v, waves around, phase).
BAND_WAVES = [
    (0.012, 2, 0.0), (0.018, 3, 1.1), (0.015, 2, 2.4),
    (0.020, 3, 0.6), (0.016, 2, 3.5), (0.018, 4, 1.8),
    (0.014, 3, 2.9), (0.012, 2, 4.2),
]


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


def make_planet_texture():
    """Generate the 1024x512 equirectangular banded texture.
    Array rows are v (0 = south pole at row 0, 1 = north pole at
    the last row); columns are u (longitude, wraps around)."""
    rng = np.random.default_rng(7)
    u = np.linspace(0.0, 1.0, TEX_W, endpoint=False)[None, :]
    v = np.linspace(0.0, 1.0, TEX_H)[:, None]

    # Band index: count how many wavy boundaries sit below v.
    # Each boundary wiggles in u, so the bands have soft wavy edges.
    idx = np.zeros((TEX_H, TEX_W), dtype=np.int32)
    for (edge, (amp, waves, phase)) in zip(BAND_EDGES, BAND_WAVES):
        wiggle = amp * np.sin(2.0 * np.pi * (waves * u + phase)
                              / 1.0 + 0.0)
        wiggle += 0.4 * amp * np.sin(2.0 * np.pi * (waves * 2 + 1) * u
                                     + phase * 2.0)
        idx += (v > (edge + wiggle)).astype(np.int32)
    idx = np.clip(idx, 0, len(BAND_COLORS) - 1)

    palette = np.array(BAND_COLORS, dtype=np.float32)
    arr = palette[idx]

    # Cloud streaks: elongated sine wisps, strongest in the teal
    # and cream bands (indices 3 and 4), lighter elsewhere. The
    # wisps run mostly along u (horizontal), as real banded-planet
    # clouds do.
    streak = np.sin(2.0 * np.pi * (1.5 * u
                                   + 0.8 * np.sin(2.0 * np.pi * 2.0 * v
                                                  + 1.3)
                                   + 0.5 * np.sin(2.0 * np.pi * 6.0 * u
                                                  + 2.0)))
    streak2 = np.sin(2.0 * np.pi * (3.0 * u - 1.2
                                    + 0.6 * np.sin(2.0 * np.pi * 4.0 * v
                                                   + 0.4)))
    wisp = np.clip((streak - 0.55) / 0.35, 0.0, 1.0) * 0.6 \
        + np.clip((streak2 - 0.65) / 0.30, 0.0, 1.0) * 0.4
    wisp = np.clip(wisp, 0.0, 1.0)
    in_cloud_bands = ((idx == 3) | (idx == 4)).astype(np.float32) * 1.0 \
        + ((idx == 1) | (idx == 5)).astype(np.float32) * 0.45
    lighten = 1.0 + 0.12 * wisp * in_cloud_bands
    arr = arr * lighten[..., None]

    # A few discrete thin bright streaks (seeded, elongated in u).
    for _ in range(14):
        cu = rng.uniform(0.0, 1.0)
        cv = rng.uniform(0.15, 0.85)
        # Wrap-aware distance in u.
        du = np.abs(u - cu)
        du = np.minimum(du, 1.0 - du)
        g = np.exp(-((du / 0.07) ** 2 + ((v - cv) / 0.008) ** 2))
        arr = arr * (1.0 + 0.10 * g[..., None])

    # Slightly brighter north pole (the upper limb as seen from
    # the game camera, which looks slightly down at the planet).
    bright = 1.0 + 0.07 * np.clip((v - 0.75) / 0.25, 0.0, 1.0)
    arr = arr * bright[..., None]
    # Slightly darker far south (the limb curving away).
    dark = 1.0 - 0.05 * np.clip((0.06 - v) / 0.06, 0.0, 1.0)
    arr = arr * dark[..., None]

    # Fine grain.
    arr = arr * (1.0 + rng.normal(0.0, 0.025, (TEX_H, TEX_W, 1))
                 .astype(np.float32))
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


def make_planet_material():
    """Planet material: AlienPlanet.png plugged into Base Color,
    base colour white (the texture carries the colour). Matte
    rock/gas — no emission, no metalness. The FBX export with
    path_mode=COPY keeps the reference to AlienPlanet.png next
    to the FBX, so Unity resolves it in Resources/Models/."""
    mat = bpy.data.materials.new(name="AlienPlanetMat")
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


def check_uv_orientation(obj):
    """The UV sphere's v must run 0 at the south pole (local -Z)
    to 1 at the north pole (local +Z), so the texture's north
    (last row) lands at the top of the planet in Unity. Prints
    the v of the topmost and bottommost vertices."""
    mesh = obj.data
    uv_layer = mesh.uv_layers.active.data
    top_v, bot_v = [], []
    for poly in mesh.polygons:
        for loop_idx in poly.loop_indices:
            loop = mesh.loops[loop_idx]
            vert = mesh.vertices[loop.vertex_index]
            if vert.co.z > PLANET_RADIUS * 0.99:
                top_v.append(uv_layer[loop_idx].uv[1])
            elif vert.co.z < -PLANET_RADIUS * 0.99:
                bot_v.append(uv_layer[loop_idx].uv[1])
    print("  UV check: north-pole v %.3f (want ~1), south-pole v %.3f "
          "(want ~0)" % (sum(top_v) / max(1, len(top_v)),
                         sum(bot_v) / max(1, len(bot_v))))
    return top_v, bot_v


def check_normals_outward(obj):
    """Every face normal must point away from the sphere centre
    (dot with the radial direction ~ +1). The Harvester dish
    lesson: a flipped normal is culled by Unity's URP."""
    mesh = obj.data
    mesh.calc_loop_triangles()
    dots = []
    for tri in mesh.loop_triangles:
        c = sum((mesh.vertices[i].co for i in tri.vertices),
                __import__("mathutils").Vector((0.0, 0.0, 0.0))) / 3.0
        dots.append(tri.normal.dot(c.normalized()))
    worst = min(dots)
    print("  normals check: min dot(normal, radial) %.3f "
          "(want > 0.9, FAIL if negative)" % worst)
    return worst


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

    print("Alien planet build:")
    make_planet_texture()
    planet_mat = make_planet_material()

    # UV sphere, radius PLANET_RADIUS, centred at its own origin.
    # 48 segments x 24 rings: smooth limb at this distance, ~2.2k
    # tris (well under the 5k budget). The wiring places it by
    # position alone, so the origin MUST stay the sphere centre.
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=48, ring_count=24, radius=PLANET_RADIUS,
        location=(0.0, 0.0, 0.0))
    planet = bpy.context.active_object
    planet.name = "AlienPlanet"
    planet.data.materials.append(planet_mat)

    check_uv_orientation(planet)
    check_normals_outward(planet)

    path = os.path.join(SCRIPT_DIR, FBX_FILE)
    export_fbx([planet], path)
    report_model("AlienPlanet", [planet], path)
    print("  verified placement (Unity): centre %s radius %.1f"
          % (PLANET_CENTER_UNITY, PLANET_RADIUS))
    print("  FBX references texture by name: %s (PNG sits next to it)"
          % TEXTURE_FILE)
