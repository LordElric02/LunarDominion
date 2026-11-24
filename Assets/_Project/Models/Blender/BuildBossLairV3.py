# BuildBossLairV3.py — Lunar Dominion: boss lair for the exterior
# Map view, V3 (2026-11-24, Patrick's full lair pass, part 2).
#
# WHAT IT IS: the V2 cave mouth rebuilt at the modern map's scale
# and re-themed to the lava tube the fight actually happens in.
# V2 (BuildBossLair.py, 2026-11-09) predates the x3 terrain
# transplant: authored 3.0 across, it renders ~6 units wide on
# today's map — a pebble next to the 46-78-unit camp fortresses
# and the 12-unit mech now standing beside it. V3 keeps V2's
# read (rock mound, portal frame, near-black opening, glow
# inside, spikes, rubble, ruined-bunker metal) and changes:
#   - SIZE: footprint +/-13.5, height ~9.0 — at the map's flat 2x
#     legacy scale that is ~54 wide and ~18 tall, fortress-class.
#   - FACING: the doorway faces -Y in Blender = +Z (north) in
#     Unity, toward the boss figure standing 36 units north of
#     the structure. V2's door faced +X (east), away from it.
#   - GLOW: saturated LAVA ORANGE (the LavaTube interior's lava),
#     not V2's red robot-eye glow: a lit panel inside the opening,
#     a lava pool on the threshold, runnels spilling forward,
#     ember stones, and a lava vent crater on the mound top.
#
# DESIGN (Blender coords: x = Unity x, y = -Unity z, z = up;
# front of the lair is -Y): a cluster of three squashed rock
# spheres forms the mound (main + rear + side boulder); the
# portal frame (2 pillars + lintel) stands proud of the front;
# the dark opening is recessed between them with the glow panel
# just proud of its face. Textures: 4 PNGs at 1024 via the v5
# numpy pipeline (pure-Python zlib encoder, never image.save()),
# Lightmap Pack UVs. Glow parts are solid saturated materials
# with emission (no texture needed).
#
# House rules honoured (Patrick's Blender 5.2 GUI): new objects
# are found by a bpy.data.objects before/after diff — the script
# never reads bpy.context.active_object / selected_objects;
# scale is baked into the mesh with mesh.transform(Matrix),
# never transform_apply; the export uses use_selection=False on
# a scene containing only the build.
#
# How to run: open in Blender's Text Editor, Run Script.
# NOTE: outputs land next to wherever __file__ resolves — in
# Patrick's Text Editor workflow that is the F:\ root. Install:
# copy BossLair.fbx + the 4 PNGs from there into
# Assets/_Project/Resources/Models/ (overwrites the V2 model;
# same file names, no code change needed). Ignore the .fbm
# sidecar; never rename the PNGs — the FBX references them.

import math
import os
import struct
import zlib

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "BossLair.fbx")
ATLAS_SIZE = 1024
TEXTURE_NAMES = {
    "LairRock": "BossLair_Rock.png",
    "LairRockAlt": "BossLair_RockAlt.png",
    "LairMetal": "BossLair_Metal.png",
    "LairHazard": "BossLair_Hazard.png",
}

# Palette (linear RGB). Saturated on purpose: pale tints read
# washed-out white in Unity (the Hydroponics lesson).
ROCK_DARK = (0.16, 0.15, 0.17)   # basalt mound / pillars
ROCK_ALT = (0.21, 0.18, 0.16)    # warmer rear mound / rubble
LAIR_METAL = (0.20, 0.22, 0.26)  # dark metal plates / strips
DARK_OPENING = (0.02, 0.02, 0.03)  # near-black cave mouth
GLOW_LAVA = (1.0, 0.36, 0.04)    # lava orange (LavaTube palette)
HAZARD_YELLOW = (0.95, 0.75, 0.08)  # hazard stripe yellow
HAZARD_BLACK = (0.04, 0.04, 0.04)   # hazard stripe black
GROUND_DARK = (0.09, 0.09, 0.10)  # ground disc

# Envelope: footprint +/-14.6, height 0-9.2 (authored units;
# the map renders the lair at 2x).
MAX_HEIGHT = 9.2


def make_material(name, rgb, metallic=0.0, roughness=0.8, emission=0.0,
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


def _spawn(op_call):
    """Run a primitive-add op and return the object it created,
    found by diffing bpy.data.objects (never context reads)."""
    before = set(bpy.data.objects)
    op_call()
    new = [o for o in bpy.data.objects if o not in before]
    if len(new) != 1:
        raise RuntimeError("primitive add created %d objects" % len(new))
    return new[0]


def _bake_scale(obj, sx, sy, sz):
    """Bake a scale into the mesh itself (house rule: never
    transform_apply). Local-space diagonal — equivalent to the
    old apply for these unrotated/rotated primitives alike."""
    obj.data.transform(Matrix.Diagonal(Vector((sx, sy, sz, 1.0))))


def add_cylinder(name, vertices, radius, depth, z, material, x=0.0, y=0.0,
                 rotation=(0.0, 0.0, 0.0)):
    obj = _spawn(lambda: bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth,
        location=(x, y, z), rotation=rotation,
    ))
    obj.name = name
    obj.data.materials.append(material)
    return obj


def add_sphere(name, segments, rings, radius, location, material,
               scale=(1.0, 1.0, 1.0)):
    obj = _spawn(lambda: bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=radius, location=location
    ))
    obj.name = name
    _bake_scale(obj, scale[0], scale[1], scale[2])
    obj.data.materials.append(material)
    return obj


def add_box(name, location, rotation_z, scale, material):
    """Box with `scale` as half-extents (the cube primitive is 2
    units), scale baked into the mesh. Kept axis-aligned
    (rotation_z 0.0) for wide boxes: a rotated wide box's corners
    reach half-extent x (cos + sin) and can silently exceed the
    envelope (the Nova v5 lesson)."""
    obj = _spawn(lambda: bpy.ops.mesh.primitive_cube_add(
        location=location, rotation=(0.0, 0.0, rotation_z)))
    obj.name = name
    _bake_scale(obj, scale[0], scale[1], scale[2])
    obj.data.materials.append(material)
    return obj


def add_box_rot(name, location, rotation, scale, material):
    """Box with a full Euler rotation (kept on the object; only
    the scale is baked). Only for small pieces (the broken metal
    plates): the half-extents are small, so the rotated corners
    stay well inside the envelope."""
    obj = _spawn(lambda: bpy.ops.mesh.primitive_cube_add(
        location=location, rotation=rotation))
    obj.name = name
    _bake_scale(obj, scale[0], scale[1], scale[2])
    obj.data.materials.append(material)
    return obj


def add_cone_tilted(name, vertices, radius_bottom, radius_top, depth,
                    location, material, tilt_deg=0.0, tilt_dir=(1.0, 0.0)):
    """Cone tilted outward by tilt_deg toward tilt_dir (x, y). The
    tilt axis is horizontal and perpendicular to the tilt
    direction, so the cone's +Z axis leans toward (dx, dy) by
    exactly tilt_deg."""
    obj = _spawn(lambda: bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=radius_bottom, radius2=radius_top,
        depth=depth, location=location,
    ))
    obj.name = name
    if tilt_deg != 0.0:
        dx, dy = tilt_dir
        length = math.hypot(dx, dy)
        dx, dy = dx / length, dy / length
        axis = Vector((-dy, dx, 0.0))
        obj.rotation_mode = 'QUATERNION'
        obj.rotation_quaternion = Quaternion(axis, math.radians(tilt_deg))
    obj.data.materials.append(material)
    return obj


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in list(bpy.data.meshes):
        bpy.data.meshes.remove(block)
    for block in list(bpy.data.materials):
        bpy.data.materials.remove(block)


def recalc_normals(objects):
    """Safety net (the Helium-3 dish lesson): force every face
    normal outward so Unity never culls a flipped surface."""
    for obj in objects:
        if obj.type != "MESH":
            continue
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.normals_make_consistent(inside=False)
        bpy.ops.object.mode_set(mode="OBJECT")
        obj.select_set(False)


def build():
    clear_scene()

    rock = make_material("LairRock", ROCK_DARK, metallic=0.05, roughness=0.95)
    rock_alt = make_material("LairRockAlt", ROCK_ALT, metallic=0.05,
                             roughness=0.95)
    metal = make_material("LairMetal", LAIR_METAL, metallic=0.85,
                          roughness=0.45)
    dark = make_material("LairDark", DARK_OPENING, roughness=0.95)
    glow = make_material("LairGlow", GLOW_LAVA, metallic=0.10,
                         roughness=0.30, emission=3.0)
    hazard = make_material("LairHazard", HAZARD_YELLOW, metallic=0.2,
                           roughness=0.60)
    ground = make_material("LairGround", GROUND_DARK, roughness=0.90)

    objects = []

    # ---- Ground disc: flat base, top at z 0.5; grounded pieces
    # sit at z 0 and embed into it (the V2 pattern). Radius 14
    # matches the lair's painted ring (rOut 28 at 2x). ----
    objects.append(add_cylinder("Lair_Ground", 24, 14.0, 0.5, 0.25, ground))

    # ---- The mound: three overlapping squashed spheres. Main
    # dome centre (0, 1.5): r 12.5 x scale (1.05, 0.95, 0.34) —
    # half-height 4.25, centre z 4.25 so the bottom is exactly
    # z 0, top at 8.5. Rear dome and a side boulder layer the
    # silhouette. ----
    objects.append(add_sphere("Lair_Mound", 18, 9, 12.5,
                              (0.0, 1.5, 4.25), rock, (1.05, 0.95, 0.34)))
    objects.append(add_sphere("Lair_MoundRear", 14, 7, 8.0,
                              (0.0, 6.5, 3.36), rock_alt, (1.0, 1.0, 0.42)))
    objects.append(add_sphere("Lair_Boulder", 12, 6, 5.5,
                              (-9.0, 2.0, 2.2), rock_alt, (1.0, 1.0, 0.40)))

    # ---- Portal frame on the front (-Y, toward the boss
    # figure): two rock pillars + a lintel, standing proud of
    # the mound bulge (front reach -10.4 at mid-height).
    # Pillars: half-extents (1.5, 1.3, 3.4) at x +/-4.6, fronts
    # at y -12.3, tops at 6.8. Lintel bridges them at z 7.3. ----
    for i, px in enumerate((4.6, -4.6)):
        objects.append(add_box("Lair_Pillar_%02d" % (i + 1),
                               (px, -11.0, 3.4), 0.0, (1.5, 1.3, 3.4),
                               rock))
    objects.append(add_box("Lair_Lintel", (0.0, -11.0, 7.3), 0.0,
                           (5.9, 1.3, 0.9), rock))

    # ---- Dark cave mouth: near-black box between the pillars,
    # front face at y -11.7, proud of the mound surface. ----
    objects.append(add_box("Lair_Opening", (0.0, -10.9, 3.1), 0.0,
                           (3.0, 0.8, 3.1), dark))
    # ---- Lava glow just inside the opening, its face proud of
    # the dark panel (-11.95 vs -11.7): the doorway reads as a
    # dark cave mouth with lava light inside, not a glowing
    # door. ----
    objects.append(add_box("Lair_Glow", (0.0, -11.75, 2.6), 0.0,
                           (1.3, 0.2, 1.5), glow))
    # ---- Lava pool on the threshold, flat on the disc in front
    # of the opening, + two runnels spilling toward the
    # approach. ----
    objects.append(add_box("Lair_LavaPool", (0.0, -13.2, 0.56), 0.0,
                           (2.6, 1.6, 0.12), glow))
    for i, rx in enumerate((1.2, -1.2)):
        objects.append(add_box("Lair_LavaRun_%02d" % (i + 1),
                               (rx, -15.4, 0.54), 0.0, (0.35, 1.6, 0.10),
                               glow))
    # ---- Two ember stones flanking the doorway, grounded. ----
    for i, ex in enumerate((5.6, -5.6)):
        objects.append(add_sphere("Lair_Ember_%02d" % (i + 1), 8, 4, 0.5,
                                  (ex, -12.6, 0.5), glow))

    # ---- Hazard stripes on the threshold, flanking the pool. ----
    for i, hx in enumerate((3.6, -3.6)):
        objects.append(add_box("Lair_HazardBar_%02d" % (i + 1),
                               (hx, -12.9, 0.56), 0.0, (0.9, 0.8, 0.10),
                               hazard))
    # ---- Thin metal strips on the pillar fronts (ruined
    # bunker remnants, as V2). ----
    for i, mx in enumerate((4.6, -4.6)):
        objects.append(add_box("Lair_MetalStrip_%02d" % (i + 1),
                               (mx, -12.35, 3.4), 0.0, (0.85, 0.15, 2.8),
                               metal))

    # ---- Lava vent on the mound top: a short rock crater
    # cylinder with a glowing disc inside (top z ~9.0, the
    # model's tallest point). ----
    objects.append(add_cylinder("Lair_Vent", 10, 1.6, 1.3, 8.35, rock,
                                x=0.0, y=1.5))
    objects.append(add_cylinder("Lair_VentGlow", 10, 1.15, 0.3, 8.85, glow,
                                x=0.0, y=1.5))

    # ---- Seven rock spikes ringed around the mound (sides and
    # back only — the front sector is the portal), tilted
    # outward 25 deg. ----
    spike_spots = ((10.5, -3.5, 5.2), (11.5, 3.0, 5.6), (7.5, 9.0, 4.6),
                   (0.5, 11.5, 5.4), (-7.0, 9.5, 4.8), (-11.0, 3.5, 5.6),
                   (-10.5, -3.5, 5.0))
    for i, (sx, sy, sh) in enumerate(spike_spots):
        objects.append(add_cone_tilted("Lair_Spike_%02d" % (i + 1),
                                       7, 1.5, 0.0, sh,
                                       (sx, sy, sh * 0.42), rock,
                                       tilt_deg=25.0, tilt_dir=(sx, sy)))

    # ---- Debris / rubble around the base: squashed spheres,
    # bottoms exactly at z 0 (centre z = radius x z-scale). ----
    for i, (dx, dy, dr) in enumerate(((12.0, -6.0, 1.5), (-12.2, -4.5, 1.7),
                                      (-9.0, 10.5, 1.4), (8.5, 10.8, 1.6),
                                      (13.0, 5.5, 1.2), (-13.2, 6.5, 1.3))):
        objects.append(add_sphere("Lair_Debris_%02d" % (i + 1), 8, 4, dr,
                                  (dx, dy, dr * 0.6),
                                  rock if i % 2 == 0 else rock_alt,
                                  (1.0, 1.0, 0.6)))

    # ---- Two broken metal plates half-buried at the base. ----
    objects.append(add_box_rot("Lair_BrokenPlate", (-6.5, -8.5, 0.7),
                               (0.3, 0.0, 0.5), (1.6, 0.15, 0.9), metal))
    objects.append(add_box_rot("Lair_BrokenPlate2", (8.0, 7.5, 0.6),
                               (-0.2, 0.15, -0.4), (1.3, 0.15, 0.75), metal))

    recalc_normals(objects)
    return objects


def make_uv_atlas(objects):
    """Pack every mesh object's UV islands into one 0-1 atlas
    (Lightmap Pack). The panel detail is defined in object
    space, so the UVs only decide WHERE in the atlas each
    surface lands, not the pattern."""
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
    """Write an (H, W, 3) float 0-1 array as an 8-bit RGB PNG.
    Pure Python (zlib) — Blender's image.save()/save_render()
    blacken a generated image's pixels on save, so the PNG is
    written directly instead. Rows are flipped so the file
    matches Blender's bottom-to-top pixel order."""
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
    """Generate one 1024 metal texture with numpy: flat spec
    colour, panel grid (32 px cells + a faint 16 px sub-grid,
    4 px lines), rivets at the cell corners, per-cell brightness
    variation, and soft grime blotches. Returns the Blender
    image, saved as a PNG next to the script."""
    S = ATLAS_SIZE
    CELL = 32
    SUB = 16
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    cx = np.mod(xx, CELL)
    cy = np.mod(yy, CELL)
    line = ((cx < 4) | (cy < 4)).astype(np.float32)
    sx = np.mod(xx, SUB)
    sy = np.mod(yy, SUB)
    subline = ((sx < 2) | (sy < 2)).astype(np.float32)
    cell_x = (xx // CELL).astype(np.int32)
    cell_y = (yy // CELL).astype(np.int32)
    h = np.sin(cell_x * 127.1 + cell_y * 311.7) * 43758.5453
    h = h - np.floor(h)
    cell_bright = 0.90 + 0.20 * h
    dx = np.minimum(cx, CELL - cx)
    dy = np.minimum(cy, CELL - cy)
    dist_corner = np.sqrt(dx * dx + dy * dy)
    rivet = (dist_corner < 3.5).astype(np.float32)
    grime = np.zeros((S, S), dtype=np.float32)
    for _ in range(48):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(40, 140)
        strength = rng.uniform(0.08, 0.25)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        grime += strength * np.exp(-d2 / (2.0 * (r / 2.5) ** 2))
    grime = np.clip(grime, 0.0, 0.45)
    base = np.array(rgb, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= cell_bright[..., None]
    arr *= (1.0 - line[..., None] * 0.75)
    arr *= (1.0 - subline[..., None] * 0.25)
    arr *= (1.0 + rivet[..., None] * 0.25)
    arr *= (1.0 - grime[..., None])
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


def make_rock_texture(rgb, seed, filename):
    """Generate one 1024 dark rock texture with numpy: flat spec
    colour, large soft mottled blotches, fine speckle, and a few
    dark pits. No panel grid — rock is not plated."""
    S = ATLAS_SIZE
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    # Large soft blotches (random circles, radial falloff).
    blotch = np.zeros((S, S), dtype=np.float32)
    for _ in range(60):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(30, 120)
        strength = rng.uniform(-0.15, 0.15)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        blotch += strength * np.exp(-d2 / (2.0 * (r / 2.5) ** 2))
    # Fine speckle.
    speckle = rng.uniform(0.88, 1.12, (S, S)).astype(np.float32)
    # Dark pits (small, deep).
    pits = np.zeros((S, S), dtype=np.float32)
    for _ in range(20):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(5, 15)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        pits += 0.30 * np.exp(-d2 / (2.0 * (r / 2.5) ** 2))
    pits = np.clip(pits, 0.0, 0.35)
    base = np.array(rgb, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= (1.0 + blotch[..., None])
    arr *= speckle[..., None]
    arr *= (1.0 - pits[..., None])
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


def make_hazard_texture(filename):
    """Diagonal yellow/black hazard stripes (64 px bands) with
    slight noise and grime, for the entrance threshold bars."""
    S = ATLAS_SIZE
    rng = np.random.default_rng(301)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    band = (((xx + yy) // 64) % 2).astype(np.float32)  # 0 yellow, 1 black
    yellow = np.array(HAZARD_YELLOW, dtype=np.float32)
    black = np.array(HAZARD_BLACK, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * yellow
    arr = np.where(band[..., None] > 0.5, black, arr)
    noise = rng.uniform(0.90, 1.05, (S, S)).astype(np.float32)
    arr *= noise[..., None]
    grime = np.zeros((S, S), dtype=np.float32)
    for _ in range(24):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(40, 120)
        strength = rng.uniform(0.05, 0.18)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        grime += strength * np.exp(-d2 / (2.0 * (r / 2.5) ** 2))
    grime = np.clip(grime, 0.0, 0.35)
    arr *= (1.0 - grime[..., None])
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


def apply_textures(textured_materials):
    """Generate one texture per textured material, plug it into
    the material's Base Color, and set the base colour to white
    so Unity's (map x colour) multiply returns exactly the
    texture. Returns a list of (material name, filepath,
    bytes)."""
    seeds = {"LairRock": 417, "LairRockAlt": 419, "LairMetal": 421}
    base_colors = {"LairRock": ROCK_DARK, "LairRockAlt": ROCK_ALT,
                   "LairMetal": LAIR_METAL}
    info = []
    for mat in textured_materials:
        filename = TEXTURE_NAMES[mat.name]
        if mat.name == "LairHazard":
            img = make_hazard_texture(filename)
        elif mat.name in ("LairRock", "LairRockAlt"):
            img = make_rock_texture(base_colors[mat.name],
                                    seeds[mat.name], filename)
        else:
            img = make_metal_texture(base_colors[mat.name],
                                     seeds[mat.name], filename)
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
    # House rule: the scene contains ONLY the build (clear_scene
    # ran first), so export everything with use_selection=False —
    # no context selection reads.
    bpy.ops.export_scene.fbx(
        filepath=OUTPUT_FBX,
        use_selection=False,
        object_types={"MESH"},
        use_mesh_modifiers=True,
        apply_scale_options="FBX_SCALE_ALL",
        apply_unit_scale=True,
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,
        add_leaf_bones=False,
        path_mode="STRIP",
        embed_textures=False,
    )


def report(objects):
    total_tris = 0
    print("Boss Lair V3 build:")
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
    pts = [o.matrix_world @ v.co for o in objects if o.type == "MESH"
           for v in o.data.vertices]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    print("  bounds x=[%.2f, %.2f] y=[%.2f, %.2f] z=[%.2f, %.2f]"
          % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    print("  exported: %s" % OUTPUT_FBX)
    print("  fbx bytes: %d" % os.path.getsize(OUTPUT_FBX))
    for mat_name, path, size in TEXTURE_INFO:
        print("  texture %s: %s (%d bytes)" % (mat_name, path, size))


def verify(objects):
    """Checks on the built mesh (Blender coords). Includes
    clearance-style hard checks, not just placement rules: the
    glow face must be PROUD of the dark panel's front face
    (measured from world vertices, not locations), and the
    pillars must actually intersect the mound (no floating
    frame)."""
    ok = True

    def check(cond, msg):
        nonlocal ok
        print("  [%s] %s" % ("PASS" if cond else "FAIL", msg))
        if not cond:
            ok = False

    def world_pts(obj):
        return [obj.matrix_world @ v.co for v in obj.data.vertices]

    def by_name(name):
        found = [o for o in objects if o.name == name]
        return found[0] if found else None

    total_tris = 0
    for obj in objects:
        if obj.type != "MESH":
            continue
        obj.data.calc_loop_triangles()
        total_tris += len(obj.data.loop_triangles)
    check(800 <= total_tris <= 8000,
          "total tris %d (spec 800-8000)" % total_tris)
    pts = [p for o in objects if o.type == "MESH" for p in world_pts(o)]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    check(abs(min(xs)) <= 14.6 and abs(max(xs)) <= 14.6,
          "footprint x in +/-14.6 (got [%.2f, %.2f])" % (min(xs), max(xs)))
    check(abs(min(ys)) <= 17.2 and abs(max(ys)) <= 14.6,
          "footprint y in [-17.2, 14.6] (got [%.2f, %.2f])"
          % (min(ys), max(ys)))
    # The ground disc's bottom is the model's base plane. Spike
    # bases deliberately bury below it into the mound: a tilted
    # cone's lowest rim point is centre_z - (depth/2)cos(tilt)
    # - radius*sin(tilt); with centre_z = 0.42*depth that is
    # -(0.08*depth + 1.5*sin25) — up to -1.08 for the tallest
    # spike. So the burial check is per-class: spikes may reach
    # -1.15 (that computed bound + margin); EVERYTHING ELSE must
    # stay at the base plane — that is what catches a genuinely
    # sunken part. (First run, 2026-11-24: min z -0.812, owned
    # by a spike rim — the flat -0.6 bound here missed the tilt
    # term and false-failed.)
    ground_obj = by_name("Lair_Ground")
    if ground_obj is not None:
        gz = min(p.z for p in world_pts(ground_obj))
        check(abs(gz) < 1e-4, "ground disc flat-based at z=0 (min z %.3f)" % gz)
    else:
        check(False, "no ground disc")
    lowest_name, lowest_z = None, None
    burial_ok = True
    for o in objects:
        if o.type != "MESH":
            continue
        oz = min(p.z for p in world_pts(o))
        if lowest_z is None or oz < lowest_z:
            lowest_name, lowest_z = o.name, oz
        limit = -1.15 if o.name.startswith("Lair_Spike_") else -0.05
        if oz < limit:
            burial_ok = False
    check(burial_ok,
          "burial within per-class bounds (lowest: %s at %.3f)"
          % (lowest_name, lowest_z if lowest_z is not None else 0.0))
    check(max(zs) <= MAX_HEIGHT + 0.01,
          "height %.2f (spec <= %.1f)" % (max(zs), MAX_HEIGHT))
    check(max(zs) >= 7.5, "height %.2f (spec >= 7.5, reads from far)"
          % max(zs))
    # The dark opening must face -Y (north in Unity, toward the
    # boss figure).
    opening = by_name("Lair_Opening")
    check(opening is not None and opening.location.y < -9.0,
          "opening on the -Y face (y %.2f)"
          % opening.location.y if opening else "no opening")
    # The lava glow face must be proud of the opening's front
    # face — measured on world vertices, not centre locations.
    glow_obj = by_name("Lair_Glow")
    if opening is not None and glow_obj is not None:
        open_front = min(p.y for p in world_pts(opening))
        glow_front = min(p.y for p in world_pts(glow_obj))
        check(glow_front < open_front - 0.05,
              "glow proud of the opening (glow front %.2f < panel %.2f)"
              % (glow_front, open_front))
    else:
        check(False, "opening/glow missing")
    # The portal frame must have two pillars + a lintel, and the
    # pillars must reach INTO the mound (no floating frame):
    # each pillar's back face must pass the mound's front
    # surface line at the pillar's height band.
    pillars = [o for o in objects if o.name.startswith("Lair_Pillar_")]
    lintel = by_name("Lair_Lintel")
    check(len(pillars) == 2 and lintel is not None,
          "portal frame: 2 pillars + lintel (got %d + %s)"
          % (len(pillars), "1" if lintel is not None else "0"))
    if len(pillars) == 2:
        backs = [max(p.y for p in world_pts(pl)) for pl in pillars]
        check(all(b > -10.4 for b in backs),
              "pillar backs inside the mound (backs %s)"
              % ", ".join("%.2f" % b for b in backs))
    # Lava pool + vent glow present.
    check(by_name("Lair_LavaPool") is not None
          and by_name("Lair_VentGlow") is not None,
          "lava pool + vent glow present")
    print("  verification: %s" % ("ALL PASS" if ok else "FAILURES ABOVE"))
    return ok


if __name__ == "__main__":
    built = build()
    textured_mats = [m for m in bpy.data.materials
                     if m.name in ("LairRock", "LairRockAlt",
                                   "LairMetal", "LairHazard")]
    make_uv_atlas(built)
    TEXTURE_INFO = apply_textures(textured_mats)
    export(built)
    report(built)
    print("Verification:")
    verify(built)
