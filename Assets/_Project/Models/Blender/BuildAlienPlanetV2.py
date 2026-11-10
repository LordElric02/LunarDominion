# BuildAlienPlanetV2.py — Lunar Dominion: alien planet texture v2
# (graphics upgrade: richer bands + a storm spot + more turbulence).
#
# TEXTURE ONLY — the FBX (AlienPlanet.fbx, a UV sphere radius 26) is
# unchanged. This script generates ONLY the texture:
#   AlienPlanetV2.png — 1024x512 equirectangular banded texture.
# The wiring (BaseView.cs BuildAlienPlanet) loads the texture at
# runtime by name ("Models/AlienPlanet") and sets it as the planet
# material's _BaseMap, so the FBX's own texture reference does not
# matter. Install: copy AlienPlanetV2.png to
# Assets/_Project/Resources/Models/ and rename it to AlienPlanet.png
# (replacing the v1 texture). No FBX change, no code change.
# Placement is unchanged: centre (-170.6, -70.0, 7.9), scale 0.70 —
# only the top arc (~21.5%) shows above the mountains, a broad
# banded arc in the upper right. Do NOT move or rescale the planet.
#
# WHAT CHANGES IN v2:
#   * More bands: 13 (was 9), with a richer palette — more colour
#     variation, including a couple of thin bright/dark bands, so
#     the disc reads less like flat stripes.
#   * A storm spot (NEW): a large rust-red oval in the northern
#     hemisphere (v ~0.82, inside the visible arc), like Jupiter's
#     Great Red Spot — a signature feature that makes the planet
#     read as a real gas giant rather than a striped ball. It sits
#     in a pale cream band so it contrasts, with a slightly darker
#     rim and a lighter core.
#   * More turbulence: the band boundaries get stronger, higher-
#     frequency waviness, and small turbulent eddies near the
#     boundaries, so the bands look churned rather than painted.
#   * More cloud streaks (18 thin bright streaks, was 14) and
#     slightly stronger fine grain.
#   * The brighter north pole / darker far south shading is kept.
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
# RING: deliberately NOT built (same as v1). A Saturn-style ring
# around a planet of which only the top arc is visible would read
# as a thin stray line above the mountains and clutter the
# horizon; the bare banded disc reads cleaner at this framing.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildAlienPlanetV2.py
# From inside Blender: open the Scripting workspace, open this file,
# Run Script. Written/tested against Blender 4.2.3 LTS here; uses no
# deprecated APIs, so it should also run on Blender 5.2 LTS.
# NOTE: the script writes the PNG next to wherever the script file
# was opened from (SCRIPT_DIR). If you open it from F:\ or
# Downloads, the file lands there — check the "texture" line.

import math
import os
import struct
import zlib

import bpy
import numpy as np

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
TEXTURE_FILE = "AlienPlanetV2.png"
TEXTURE_PATH = os.path.join(SCRIPT_DIR, TEXTURE_FILE)

TEX_W = 1024
TEX_H = 512

# --- palette (display values, see colour note above) ---
# South pole (v 0) -> north pole (v 1). 13 bands (was 9).
# Band 10 is the brightest pale cream — the storm spot sits in it.
BAND_COLORS = [
    (0.30, 0.23, 0.42),   # 0 deep purple (south polar cap)
    (0.70, 0.44, 0.40),   # 1 dusty rose
    (0.76, 0.56, 0.30),   # 2 amber
    (0.34, 0.53, 0.53),   # 3 muted teal
    (0.82, 0.75, 0.62),   # 4 pale cream (equatorial bright band)
    (0.66, 0.42, 0.44),   # 5 dusty rose
    (0.36, 0.28, 0.48),   # 6 deep purple
    (0.72, 0.52, 0.32),   # 7 amber
    (0.38, 0.55, 0.55),   # 8 muted teal (deeper than band 3)
    (0.78, 0.70, 0.58),   # 9 pale cream (warm)
    (0.84, 0.78, 0.66),   # 10 pale cream (brightest — storm band)
    (0.68, 0.44, 0.42),   # 11 dusty rose
    (0.70, 0.62, 0.72),   # 12 pale lilac (north polar cap)
]
# Band boundaries in v (13 colours -> 12 boundaries).
BAND_EDGES = [0.06, 0.14, 0.22, 0.30, 0.38, 0.46, 0.54, 0.62, 0.70,
              0.78, 0.86, 0.93]
# Per-boundary waviness: (amplitude in v, waves around, phase).
# v2: stronger amplitudes and more waves than v1, so the boundaries
# look churned rather than painted.
BAND_WAVES = [
    (0.016, 3, 0.0), (0.022, 4, 1.1), (0.018, 3, 2.4),
    (0.024, 4, 0.6), (0.020, 3, 3.5), (0.022, 5, 1.8),
    (0.018, 4, 2.9), (0.016, 3, 4.2), (0.020, 4, 0.9),
    (0.022, 5, 2.2), (0.018, 3, 3.1), (0.014, 3, 1.5),
]
# Storm spot (Great Red Spot analogue): centre (u, v), semi-axes
# (in u and v fractions), colours. v 0.82 sits in band 10 (pale
# cream) and inside the visible arc (the top ~21.5% of the disc
# is v ~0.7-1.0). The oval is wider than tall, like a real gas
# giant storm.
STORM_U = 0.45
STORM_V = 0.82
STORM_AU = 0.09
STORM_AV = 0.045
STORM_COLOR = (0.62, 0.28, 0.20)   # deep rust red
STORM_CORE = (0.72, 0.38, 0.28)    # lighter core
STORM_RIM = (0.48, 0.20, 0.14)     # darker rim


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

    # Storm spot (NEW in v2): a large rust-red oval in band 10
    # (v ~0.82, inside the visible arc). Elliptical falloff with a
    # darker rim and a lighter core, so it reads as a rotating storm
    # rather than a flat blob. The u distance wraps (the texture
    # tiles in u), and the oval is wider than tall.
    du = np.abs(u - STORM_U)
    du = np.minimum(du, 1.0 - du)
    r2 = (du / STORM_AU) ** 2 + ((v - STORM_V) / STORM_AV) ** 2
    # Smooth falloff: 1 at the centre, 0 at the edge (r2 = 1).
    storm = np.clip(1.0 - r2, 0.0, 1.0)
    storm = storm * storm * (3.0 - 2.0 * storm)  # smoothstep
    # Rim: a darker ring where r2 is near 0.7-1.0.
    rim = np.clip((r2 - 0.55) / 0.45, 0.0, 1.0) \
        * np.clip((1.0 - r2) / 0.15, 0.0, 1.0)
    # Core: lighter where r2 < 0.35.
    core = np.clip((0.35 - r2) / 0.35, 0.0, 1.0)
    storm_col = np.array(STORM_COLOR, dtype=np.float32)
    rim_col = np.array(STORM_RIM, dtype=np.float32)
    core_col = np.array(STORM_CORE, dtype=np.float32)
    arr = arr * (1.0 - storm[..., None]) + storm_col * storm[..., None]
    arr = arr * (1.0 - 0.5 * rim[..., None]) + rim_col * (0.5 * rim[..., None])
    arr = arr * (1.0 - 0.5 * core[..., None]) + core_col * (0.5 * core[..., None])

    # Cloud streaks: elongated sine wisps, strongest in the teal
    # and cream bands (indices 3, 4, 8, 9, 10), lighter elsewhere. The
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
    in_cloud_bands = ((idx == 3) | (idx == 4) | (idx == 8)
                      | (idx == 9) | (idx == 10)).astype(np.float32) * 1.0 \
        + ((idx == 1) | (idx == 5) | (idx == 11)).astype(np.float32) * 0.45
    lighten = 1.0 + 0.12 * wisp * in_cloud_bands
    arr = arr * lighten[..., None]

    # A few discrete thin bright streaks (seeded, elongated in u).
    # v2: 18 (was 14).
    for _ in range(18):
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
    # Texture only — the FBX (AlienPlanet.fbx, UV sphere radius 26)
    # is unchanged. The wiring loads the texture at runtime by name,
    # so only the PNG is replaced.
    print("Alien planet texture v2 build:")
    make_planet_texture()
    print("  install: copy %s to Assets/_Project/Resources/Models/ "
          "and rename it to AlienPlanet.png (replacing the v1 texture)"
          % TEXTURE_FILE)
