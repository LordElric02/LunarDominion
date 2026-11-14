# BuildLavaTubeV10.py — Lunar Dominion: the sub-lunar lava tube
# cavern arena, V10 (2026-11-13: the shaft panel lines go bold).
#
# WHAT CHANGED FROM V9: THE LIFT-SHAFT GUIDE RAILS BECOME
# MULLIONS. Patrick flagged the vertical lines on the shafts'
# amber panels as jagged in-game. Measured diagnosis: the panel
# and rails are single straight boxes (a Blender render of the
# V9 set shows them dead straight even at his screenshot's
# pixel density) — but the rails are only 0.14 units wide, which
# is 1-3 px at fight distance, and a hairline that slants a few
# pixels across the panel height rasterizes as a stair-step in
# the Editor's URP view (no MSAA on the mobile pipeline). The
# fix is geometric: the two rails per shaft widen from 0.14 to
# 0.40 units (half extents 0.07 -> 0.20, slightly deeper so they
# stand proud of the glow face), reading as bold straight panel
# divisions at any resolution. Same names, same positions,
# same count — everything else frozen exactly as V9, so the
# wall-clearance verify still applies unchanged.
#
# V9 (2026-11-13: the follow-up pass — Patrick:
# the last rocks come off the floor).
#
# WHAT CHANGED FROM V8: THE STALAGMITES GO TOO. V8 cleared the
# cover boulders and fallen columns; the 8 Stalagmite_ rock
# cones around the arena edge (v1/v2 cave dressing) were the
# last rocks standing on the floor, and Patrick called them
# leftover from a previous layout. They are removed, so the
# floor is now 100% facility: hazard ring, walkways, consoles,
# canisters, hauler, crawler, crates. The rock WALLS and
# ceiling stay — the cavern shell is deliberate. Everything
# else is frozen exactly as V8 (geometry, textures, materials);
# removing the stalagmite block shifts the shared rng draw
# sequence for later dressing, which is cosmetic only.
#
# WHAT CHANGED IN V8: THE FLOOR GOES FULL FACILITY. The 6
# CoverBoulder_ rocks and the 3 FallenColumn_ segments come OFF
# the arena floor (the turn-based fight never used the boulders;
# the only code lookup is the dormant real-time sweep cover
# test, which iterates by name and simply finds none). In their
# place: a hazard ring around the boss pad, walkways with amber
# guide strips running to the two lift shafts, canister groups,
# two control consoles with lit screens, and a flatbed hauler
# parked opposite the crawler. The floor plates go brighter,
# finer and cleaner (256 px cells, subtle seams, few pocks),
# the floor material turns harder metallic (0.75 / roughness
# 0.25), and LavaTubeAmbience finishes the sheen code-side
# (metallic 0.9 / smoothness 0.85) plus a generated reflection
# cubemap while in the lair, so the metal has something to
# reflect. Walls, ceiling, crystals, shafts and all V7
# machinery are frozen exactly.
#
# WHAT CHANGED FROM V6: FACILITY DRESSING + THE FLOOR. Two
# illuminated transport lift shafts (LiftShaft_01/02) now stand
# against the wall flanking the boss — truss posts, glowing
# amber interiors with guide rails, a lift cab parked partway
# up each (different heights), base housings at floor level —
# the way a crew travels to and from the lair. Machinery fills
# the heroes' flanks (mock-up B): crate stacks, a parked tracked
# crawler, two tripod floodlight towers with lit heads, and two
# gantry portal frames. The FLOOR goes shiny silver metallic:
# the plate texture lightens to silver-grey and the floor
# material turns metallic (0.55 / roughness 0.38), with the
# matching sheen finished code-side in LavaTubeAmbience (it
# instances the floor material and sets the URP metallic /
# smoothness, plus adds one warm point light per shaft).
# Everything from V6 — walls, ceiling, columns, gameplay
# pieces, cracks — is frozen exactly; all new pieces are
# scenery at the arena edges (shafts at r >= 25.9, flank
# machinery clear of the hero and boss spots).
# WHAT CHANGED IN V6: HEIGHT AND DRESSING, under a frozen
# contract. The ceiling slabs rise from z ~24.3 to ~37.3
# (underside ~34, tops ~40), a THIRD wall tier (Lava_WallHigh_)
# rings the cavern at centre z ~31.5 so the walls meet the new
# ceiling, hexagonal basalt columns stand around the ring with
# 3 fallen segments on the floor edge, stalactites re-hang from
# the new ceiling (tips stay above z ~26), crystal clusters grow
# larger with 2 more high on the walls, 2 extra steam vents
# (Vent_04/05) sit near the boss, and the backdrop shell grows
# to r 46 to stay behind the raised slabs. FROZEN from V5,
# exactly: the floor, wall tiers 1-2, cover boulders, beacons,
# conduits, platforms, pipes, cracks and every gameplay name —
# the wall surface at heights 0-25 is unchanged, so all fight
# placement and the C1 camera math stay valid; columns and the
# new tier live outside the camera cylinder (r >= 25.2) or
# above z ~26. The script also follows the Blender house rules
# now: spawned objects are found by diffing bpy.data.objects
# (never bpy.context.active_object / selected_objects), scale is
# baked with mesh.transform (never transform_apply), and the
# export runs use_selection=False on the pure scene. Textures
# are the V5 pipeline unchanged — but the UV atlas re-packs with
# the new objects, so the install is FULL again (FBX + 4 PNGs).
# WHAT CHANGED IN V5 (kept): GEOMETRY DETAIL ONLY, on the rock
# pieces. add_rock gains a second, COHERENT displacement octave
# (a smooth direction-based sine field layered over the random
# jitter, so facets form carved planes that catch the light
# instead of lumps), and every rock (walls, upper tier, ceiling
# slabs, cover boulders) goes from subdivision 3 to 4 (320 ->
# 1280 faces each; total ~51k tris — a static arena, fine on
# the S24 Ultra). Object names, positions, scales, rotations,
# gameplay pieces and textures are ALL identical to V4; the
# fight space is untouched (boss and heroes keep their exact
# dimensions and clearances — ceiling underside still ~21.7,
# floor top still z=0). Because geometry changes, the install
# is the FULL one again (FBX + PNGs).
# WHAT CHANGED IN V4 (kept): TEXTURES ONLY — every object, name,
# position and triangle is identical to V3 (the whole geometry
# block below is untouched). The basalt + floor textures are
# richer and lighter: the atlas doubles to 2048, the basalt base
# lifts from near-black to a readable blue-grey with strata
# banding and stronger tonal blotches, and the floor gains base
# lift + wider per-plate brightness spread. Because only the
# PNGs change, the install can be PNG-ONLY (skip the FBX).
# WHAT CHANGED IN V3 (kept): the cavern is TALLER. A.R.E.S.-09 now
# stands at 6x in Unity (~17.8 units — Patrick asked for the
# boss to tower), which needs headroom: ceiling slabs rise from
# z 14.8 to 24.3 (underside ~21.7, clearing the boss's head at
# 17.8 and its slam-jump peak at ~20.2), the wall ring gains a
# second staggered tier so the walls meet the raised ceiling,
# stalactites hang from the new ceiling (scatter points keep a
# 4.5-unit exclusion disc over the boss start so nothing hangs
# in the slam arc), and 4 extra crystal clusters glow on the
# upper walls. Everything at floor level — boulders, beacons,
# vents, conduits, platforms, cracks — is IDENTICAL to v2 in
# name and position, and the matching code changes (boss scale,
# cameras, ambience shaft heights) land in the same package.
#
# WHAT CHANGED FROM V1: the v1 set already had the ceiling slabs,
# wall ring and backdrop shell, but in the fight's lighting the
# enclosure reads as black void — the arena looks like it floats
# in space. V2 keeps every v1 piece, name and gameplay position
# IDENTICAL (code finds pieces by name: "CoverBoulder_",
# "Beacon_" domes, "Vent_" grates; the 3 ceiling cracks stay
# open for the code light shafts) and adds SELF-LIT dressing
# that reads in the dark: glowing cyan crystal clusters in the
# walls, emissive floor conduits running toward the combat
# centre, two industrial platforms behind the boss
# with amber edge strips, and amber hazard strips flanking the
# boss start. Wall chunks are also taller, closing the gap
# between the wall tops and the ceiling slabs.
#
# WHAT IT IS: one static FBX set — floor, rock wall ring, ceiling
# slabs with 3 jagged cracks, stalactites, crystal
# clusters, floor conduits, platforms, 6 cover boulders, 4
# emergency beacon poles, 3 steam vent grates, and wall pipes —
# built at REAL scale (the arena is 26 units in radius in Unity;
# the boss model is separate and placed at 3x inside it).
# Code finds gameplay pieces by name prefix: "Beacon_"
# (emissive domes the lighting rig pulses), "Vent_" (steam
# particle anchors). Everything else is scenery. (V8 removed
# the "CoverBoulder_" rocks — the dormant real-time cover test
# looks them up by name and tolerates finding none.)
#
# COORDS (Blender: x = Unity x, y = -Unity z, z = up):
# boss start is Unity (0, +10z) = Blender (0, -10); the heroes
# enter at Unity (0, -20z) = Blender (0, +20). The 3 ceiling
# cracks sit above Blender (-8, -4), (10, 8), (0, -16) — the
# Unity light shafts land there, so the ceiling slabs are placed
# to keep those points open.
#
# Textures: 4 PNGs at 2048 via the v5 numpy pipeline — pure-Python
# zlib PNG encoder (never Blender image.save()), Lightmap Pack UVs:
#   LavaTube_Rock.png (basalt walls/ceiling/boulders),
#   LavaTube_Floor.png (cracked basalt plates + regolith dust),
#   LavaTube_GlowAmber.png (beacon domes, emission material),
#   LavaTube_GlowCyan.png (crystals + conduits, emission material).
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildLavaTubeV10.py
# From inside Blender: open the Scripting workspace, open this
# file, Run Script. Written/tested against Blender 4.2.3 LTS;
# should also run on Blender 5.2 LTS.
# NOTE: outputs land next to the script file (SCRIPT_DIR) — check
# the "exported:" lines. Install (V10): rename LavaTubeV10.fbx to
# LavaTube.fbx (in Explorer, before copying, so the Unity .meta
# GUID survives) and copy it + the 4 PNGs over the old files in
# Assets/_Project/Resources/Models/ (ignore the .fbm sidecar,
# never rename the PNGs — the FBX references them by name).
# (V4's install was PNG-only; V6 changes geometry, so the FBX
# must be replaced too.)
# No C# changes: BossLairView loads "Models/LavaTube" and the
# ambience rig finds the same beacon/vent names.

import math
import os
import struct
import zlib

import bpy
import numpy as np
from mathutils import Matrix, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "LavaTubeV10.fbx")
ATLAS_SIZE = 2048
TEXTURE_NAMES = {
    "LavaRock": "LavaTube_Rock.png",
    "LavaFloor": "LavaTube_Floor.png",
    "LavaGlowAmber": "LavaTube_GlowAmber.png",
    "LavaGlowCyan": "LavaTube_GlowCyan.png",
}

# Palette (linear RGB).
BASALT = (0.195, 0.205, 0.240)     # blue-grey rock (V4: lifted from near-black so the walls read in the dim lair light)
FLOOR_BASE = (0.52, 0.54, 0.57)  # V8: brighter polished steel (V7: silver-grey 0.42/0.44/0.47; V4-V6: dark basalt 0.205/0.210/0.230)
DARK_METAL = (0.10, 0.10, 0.12)     # poles / grates / pipes
GLOW_AMBER = (1.00, 0.62, 0.12)     # emergency beacon domes
GLOW_CYAN = (0.15, 0.85, 1.00)      # crystals + floor conduits (v2)
SCORCH = (0.03, 0.03, 0.03)         # boss-start scorch ring

ARENA_R = 26.0
CRACKS = [(-8.0, -4.0), (10.0, 8.0), (0.0, -16.0)]  # Blender (x, y)


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


def _objects_before():
    return set(bpy.data.objects)


def _spawned(before):
    """The object a primitive op just created, found by diffing
    bpy.data.objects. House rule for Patrick's Blender 5.2 GUI:
    scripts run from the Text Editor can raise on reads of
    bpy.context.active_object / selected_objects, so V6 never
    reads them."""
    new = [o for o in bpy.data.objects if o not in before]
    return new[0] if new else None


def _bake_scale(obj):
    """Bake obj.scale into the mesh via mesh.transform — the
    house-rule replacement for bpy.ops.object.transform_apply
    (banned for the same GUI-context reason)."""
    if tuple(obj.scale) != (1.0, 1.0, 1.0):
        m = Matrix.Diagonal((obj.scale.x, obj.scale.y, obj.scale.z, 1.0))
        obj.data.transform(m)
        obj.scale = (1.0, 1.0, 1.0)


def add_box(name, location, rotation_z, scale, material):
    """Box with `scale` as half-extents (the cube primitive is 2
    units). Wide boxes stay axis-aligned (the Nova v5 lesson)."""
    before = _objects_before()
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=(0.0, 0.0, rotation_z))
    obj = _spawned(before)
    obj.name = name
    obj.scale = scale
    _bake_scale(obj)
    obj.data.materials.append(material)
    return obj


def add_cylinder(name, vertices, radius, depth, z, material, x=0.0, y=0.0,
                 rotation=(0.0, 0.0, 0.0)):
    before = _objects_before()
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth,
        location=(x, y, z), rotation=rotation,
    )
    obj = _spawned(before)
    obj.name = name
    obj.data.materials.append(material)
    return obj


def add_cone(name, vertices, radius_bottom, radius_top, depth, location,
             material, rotation=(0.0, 0.0, 0.0)):
    before = _objects_before()
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=radius_bottom, radius2=radius_top,
        depth=depth, location=location, rotation=rotation,
    )
    obj = _spawned(before)
    obj.name = name
    obj.data.materials.append(material)
    return obj


def add_sphere(name, segments, rings, radius, location, material,
               scale=(1.0, 1.0, 1.0)):
    before = _objects_before()
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=radius, location=location
    )
    obj = _spawned(before)
    obj.name = name
    obj.scale = scale
    _bake_scale(obj)
    obj.data.materials.append(material)
    return obj


def add_rock(name, location, scale, material, seed, subdivisions=4):
    """A displaced icosahedron chunk: per-vertex random radial
    scaling makes jagged basalt; flat shading sells the facets.
    V5 adds a second, coherent octave: a smooth sine field over
    each vertex's direction, phase-shifted by the seed, so the
    facets form carved, light-catching planes (the concept-art
    rock read) instead of pure random lumps. (Blender's operator
    counts subdivisions=4 as 1280 faces.)"""
    before = _objects_before()
    bpy.ops.mesh.primitive_ico_sphere_add(
        subdivisions=subdivisions, radius=1.0, location=location)
    obj = _spawned(before)
    obj.name = name
    obj.scale = scale
    _bake_scale(obj)
    rng = np.random.default_rng(seed)
    mesh = obj.data
    co = np.empty(len(mesh.vertices) * 3, dtype=np.float32)
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    jitter = 1.0 + rng.uniform(-0.22, 0.22, len(co)).astype(np.float32)
    co = co * jitter[:, None]
    # Coherent octave (V5): direction-based, so the displacement
    # amplitude is proportional to each vertex's distance from
    # the chunk centre and independent of the chunk's size.
    norms = np.linalg.norm(co, axis=1, keepdims=True)
    norms[norms == 0.0] = 1.0
    d = co / norms
    ph = float(seed) * 0.617
    field = (np.sin(d[:, 0] * 3.1 + ph) * np.cos(d[:, 1] * 2.7 + ph * 1.3)
             + 0.6 * np.sin(d[:, 2] * 3.7 + ph * 0.7))
    co = co * (1.0 + 0.07 * field)[:, None]
    mesh.vertices.foreach_set("co", co.ravel())
    for poly in mesh.polygons:
        poly.use_smooth = False
    mesh.update()
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

    rock = make_material("LavaRock", BASALT, metallic=0.05, roughness=0.95)
    # V8: the floor material goes harder metallic — polished
    # steel plates (the URP sheen is finished code-side in
    # LavaTubeAmbience, which instances this material and sets
    # metallic/smoothness directly; these values travel with
    # the FBX as authored).
    floor_mat = make_material("LavaFloor", FLOOR_BASE, metallic=0.75,
                              roughness=0.25)
    dark = make_material("LavaDark", DARK_METAL, metallic=0.4, roughness=0.6)
    amber = make_material("LavaGlowAmber", GLOW_AMBER, metallic=0.1,
                          roughness=0.3, emission=2.5)
    cyan = make_material("LavaGlowCyan", GLOW_CYAN, metallic=0.1,
                         roughness=0.25, emission=3.0)
    scorch = make_material("LavaScorch", SCORCH, metallic=0.0, roughness=1.0)

    objects = []
    rng = np.random.default_rng(777)

    # ---- Floor: one big disc, top surface exactly at z=0. ----
    objects.append(add_cylinder("Lava_Floor", 48, ARENA_R + 1.0, 0.5,
                                -0.25, floor_mat))
    # Scorch ring under the boss start (Blender (0, -10)).
    objects.append(add_cylinder("Lava_Scorch", 24, 4.5, 0.04, 0.021,
                                scorch, x=0.0, y=-10.0))

    # ---- Wall ring: 12 displaced rock chunks at r ~29.5, leaning
    # slightly inward, tall enough to close the tube visually.
    # V2: chunks are taller (half-height 7.6, centre z 4.9, tops
    # ~12.5) so they meet the ceiling slabs' underside (~12.4)
    # instead of leaving a dark gap band under the ceiling.
    # V3: base tier grows again (centre z 6.2, half-height 9.8,
    # tops ~16) and a second staggered tier rings the cavern at
    # centre z 19.8 (spans ~13.6-26), meeting the raised ceiling
    # slabs (underside ~21.7). ----
    for i in range(12):
        ang = math.radians(i * 30.0 + 8.0)
        r = 29.5 + rng.uniform(-1.0, 1.0)
        loc = (math.cos(ang) * r, math.sin(ang) * r, 6.2 + rng.uniform(-0.6, 0.6))
        chunk = add_rock("Lava_Wall_%02d" % (i + 1), loc,
                         (8.6, 4.6, 9.8), rock, seed=1000 + i)
        chunk.rotation_euler = (0.0, 0.0, ang + math.pi / 2.0)
        objects.append(chunk)
    for i in range(12):
        ang = math.radians(i * 30.0 + 23.0)
        r = 28.4 + rng.uniform(-0.7, 0.7)
        loc = (math.cos(ang) * r, math.sin(ang) * r, 19.8 + rng.uniform(-0.8, 0.8))
        chunk = add_rock("Lava_WallTop_%02d" % (i + 1), loc,
                         (8.8, 4.6, 6.2), rock, seed=1500 + i)
        chunk.rotation_euler = (0.0, 0.0, ang + math.pi / 2.0)
        objects.append(chunk)
    # V6: a THIRD tier rings the cavern high up (centre z ~31.5,
    # spans roughly z 25-38), overlapping tier 2's top and meeting
    # the raised ceiling. Tiers 1-2 are untouched, so the wall
    # surface at fight heights (0-25) is exactly V5's; this tier
    # sits at r ~28 with its lowest displaced vertices still
    # outside the camera cylinder (checked in verify). Lower
    # subdivision (320 faces) keeps the tri budget in line.
    for i in range(12):
        ang = math.radians(i * 30.0 + 38.0)
        r = 28.0 + rng.uniform(-0.7, 0.7)
        loc = (math.cos(ang) * r, math.sin(ang) * r, 31.5 + rng.uniform(-0.8, 0.8))
        chunk = add_rock("Lava_WallHigh_%02d" % (i + 1), loc,
                         (8.8, 4.4, 6.5), rock, seed=2500 + i, subdivisions=3)
        chunk.rotation_euler = (0.0, 0.0, ang + math.pi / 2.0)
        objects.append(chunk)

    # ---- Ceiling slabs: 7 flattened chunks placed to keep the 3
    # crack points open (checked in verify). V3: raised to
    # z ~24.3 and slightly larger — underside ~21.7 clears the
    # 2x boss (head 17.8, slam-jump peak ~20.2). V6: raised again
    # to z ~37.3 (underside ~34, tops ~40) for the cathedral
    # height; x/y, scale and seeds unchanged, so the cracks stay
    # exactly as open as before. ----
    slab_centres = [(0.0, 2.0), (16.0, -6.0), (-17.0, -8.0), (-14.0, 10.0),
                    (16.0, 16.0), (-4.0, 22.0), (3.0, -23.0)]
    for i, (sx, sy) in enumerate(slab_centres):
        slab = add_rock("Lava_Ceiling_%02d" % (i + 1),
                        (sx, sy, 37.3 + rng.uniform(-0.4, 0.4)),
                        (11.0, 9.0, 2.6), rock, seed=2000 + i)
        slab.rotation_euler = (0.0, 0.0, rng.uniform(0.0, math.pi))
        objects.append(slab)

    # ---- Stalactites: cones hanging apex-down. Clusters ring each
    # crack; the rest scatter over the ceiling. ----
    stal_points = []
    for (cx, cy) in CRACKS:
        for _ in range(5):
            a = rng.uniform(0.0, math.pi * 2.0)
            rr = rng.uniform(1.5, 4.0)
            stal_points.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    for _ in range(20):
        a = rng.uniform(0.0, math.pi * 2.0)
        rr = rng.uniform(5.0, 23.0)
        px = math.cos(a) * rr
        py = math.sin(a) * rr
        # V3: keep a clear disc over the boss start (Blender
        # (0, -10)) — at 2x the boss's slam arc reaches z ~20,
        # so nothing may hang within 4.5 units of that point.
        if math.hypot(px, py + 10.0) < 4.5:
            continue
        stal_points.append((px, py))
    for i, (px, py) in enumerate(stal_points):
        depth = rng.uniform(3.0, 8.0)
        rad = rng.uniform(0.35, 0.85)
        tilt = rng.uniform(-0.10, 0.10)
        objects.append(add_cone(
            "Lava_Stalactite_%02d" % (i + 1), 6, rad, 0.02, depth,
            (px, py, 37.0 - depth / 2.0), rock,
            rotation=(math.pi + tilt, 0.0, rng.uniform(0.0, 0.2))))

    # ---- Cover boulders REMOVED in V8: Patrick's clean-floor
    # direction — the arena floor carries equipment now, not
    # rocks. The turn-based fight never used them; the dormant
    # real-time sweep test looks them up by name and finds none.

    # ---- Beacon poles x4 at Unity (+/-22, +/-14z). ----
    for i, (ux, uz) in enumerate(((22.0, 14.0), (22.0, -14.0),
                                  (-22.0, 14.0), (-22.0, -14.0))):
        bx, by = ux, -uz
        objects.append(add_cylinder("Beacon_%02d_Pole" % (i + 1), 8,
                                    0.09, 3.2, 1.6, dark, x=bx, y=by))
        objects.append(add_sphere("Beacon_%02d" % (i + 1), 10, 6, 0.17,
                                  (bx, by, 3.32), amber))

    # ---- Steam vent grates x5 (particle anchors for the code).
    # V6: two extra grates near the boss (Unity (+/-6.5, 16z)),
    # so the boss's venting has steam close at hand. ----
    vent_unity = [(-16.0, -12.0), (17.0, -6.0), (3.0, 19.0),
                  (6.5, 16.0), (-6.5, 16.0)]
    for i, (ux, uz) in enumerate(vent_unity):
        vx, vy = ux, -uz
        objects.append(add_cylinder("Vent_%02d" % (i + 1), 16, 1.1,
                                    0.07, 0.035, dark, x=vx, y=vy))
        for j, off in enumerate((-0.5, 0.0, 0.5)):
            objects.append(add_box("Vent_%02d_Slat_%d" % (i + 1, j + 1),
                                   (vx + off, vy, 0.09), 0.0,
                                   (0.09, 0.95, 0.02), rock))

    # ---- Broken pipe run along the east wall (+X). ----
    for j, pz in enumerate((0.45, 0.90)):
        objects.append(add_cylinder(
            "Lava_Pipe_%d" % (j + 1), 10, 0.20, 13.0, pz, dark,
            x=24.5, y=2.0, rotation=(math.radians(90.0), 0.0, 0.0)))
    objects.append(add_sphere("Lava_Pipe_Elbow", 8, 5, 0.24,
                              (24.5, -4.5, 0.45), dark))

    # ================= V2 DRESSING (2026-11-11) =================
    # Self-lit pieces that define the enclosure in the fight's
    # lighting. All scenery: nothing enters the combat lanes
    # (checked in verify — crystals stay at r >= 17.5,
    # conduits stop at r 12.5, clear of every cover boulder).

    # ---- Crystal clusters: emissive cyan shards embedded in the
    # wall ring (9 clusters) + 2 floor clusters flanking the boss
    # end. Each cluster is 4 shards (4-sided cones, apex up),
    # tilted as if growing out of the rock. ----
    shard_no = 0

    def crystal_cluster(cx, cy, cz):
        nonlocal shard_no
        for _ in range(4):
            shard_no += 1
            # V6: shards grow larger (the clusters read at the
            # new cavern scale).
            depth = rng.uniform(2.0, 4.0)
            rad = rng.uniform(0.38, 0.72)
            ox = rng.uniform(-0.7, 0.7)
            oy = rng.uniform(-0.7, 0.7)
            tilt = rng.uniform(-0.45, 0.45)
            objects.append(add_cone(
                "Crystal_%02d" % shard_no, 4, rad, 0.02, depth,
                (cx + ox, cy + oy, cz + depth * 0.38), cyan,
                rotation=(tilt, rng.uniform(-0.45, 0.45),
                          rng.uniform(0.0, math.pi))))

    for i in range(9):
        ang = math.radians(i * 40.0 + 5.0)
        rr = rng.uniform(24.3, 25.3)
        crystal_cluster(math.cos(ang) * rr, math.sin(ang) * rr,
                        rng.uniform(0.4, 3.2))
    # V3: 4 more clusters high on the upper wall tier.
    for i in range(4):
        ang = math.radians(i * 90.0 + 25.0)
        rr = rng.uniform(24.3, 25.3)
        crystal_cluster(math.cos(ang) * rr, math.sin(ang) * rr,
                        rng.uniform(9.0, 12.0))
    # V6: 2 more clusters high on the new third tier.
    for i, deg in enumerate((65.0, 245.0)):
        ang = math.radians(deg)
        rr = rng.uniform(24.3, 25.3)
        crystal_cluster(math.cos(ang) * rr, math.sin(ang) * rr,
                        rng.uniform(26.0, 29.0))
    crystal_cluster(-7.5, -17.5, 0.0)
    crystal_cluster(7.5, -17.5, 0.0)

    # ---- Floor conduits: emissive cyan runs on the 4 diagonals,
    # from the wall (r 24) in to r 12.5, each ending in a dark
    # junction box. Thin boxes lying on the floor (z top ~0.05,
    # far below any figure's feet clearance concerns — scenery
    # only, no colliders in Unity). ----
    for i, deg in enumerate((45.0, 135.0, 225.0, 315.0)):
        ang = math.radians(deg)
        dx, dy = math.cos(ang), math.sin(ang)
        objects.append(add_box(
            "Conduit_%02d" % (i + 1),
            (dx * 18.25, dy * 18.25, 0.026), ang,
            (5.75, 0.11, 0.025), cyan))
        objects.append(add_box(
            "ConduitBox_%02d" % (i + 1),
            (dx * 12.3, dy * 12.3, 0.16), ang,
            (0.45, 0.45, 0.16), dark))

    # ---- Stalagmites REMOVED in V9: the 8 rock cones around
    # the arena edge were the last cave dressing standing on
    # the floor (Patrick: 'leftover from a previous layout').
    # The floor is 100% facility now; the rock walls and
    # ceiling stay. ----

    # ================= V6 DRESSING (2026-11-13) =================
    # Hexagonal basalt columns: the columnar-jointing read at
    # cathedral scale. Standing columns ring the arena against
    # the walls (centres r ~27.3, radius <= 1.5, so their nearest
    # face stays outside the r-25 camera cylinder — checked in
    # verify); 3 fallen segments lie on the floor edge, clear of
    # the conduit diagonals and the combat lanes.

    for i in range(10):
        ang = math.radians(i * 36.0 + 18.0)
        rr = 27.3 + rng.uniform(-0.4, 0.4)
        rad = rng.uniform(1.0, 1.5)
        top = rng.uniform(20.0, 33.0)
        objects.append(add_cylinder(
            "BasaltColumn_%02d" % (i + 1), 6, rad, top, top / 2.0, rock,
            x=math.cos(ang) * rr, y=math.sin(ang) * rr,
            rotation=(rng.uniform(-0.03, 0.03), rng.uniform(-0.03, 0.03), 0.0)))
    # (The 3 fallen column segments that lay here in V6/V7 were
    # REMOVED in V8 — no rocks on the facility floor.)

    # ================= V7 DRESSING (2026-11-13) =================
    # The working facility (Patrick's mock-up pick A + B): two
    # illuminated transport lift shafts flanking the boss, and
    # machinery at the heroes' flanks. All scenery. NOTE: the
    # wall rock's displaced surface reaches in to r ~22.5 behind
    # the boss (measured on the V7 build — the first V7 placement
    # at r 26-29 buried the towers inside the chunks), so the
    # towers stand proud of the wall face at r ~20, rotated to
    # face the arena centre; verify measures their clearance
    # from every wall vertex.

    def _rot(lx, ly, yaw):
        c, s = math.cos(yaw), math.sin(yaw)
        return (lx * c - ly * s, lx * s + ly * c)

    # ---- Lift shafts x2: a tall amber glow panel at the back
    # (the lit interior), two guide rails in front of it, side
    # posts with rungs (the truss), a top beam, a base housing,
    # and a lift cab parked partway up — at different heights in
    # the two shafts, as in the mock-up. Each tower is built in
    # local space and rotated to face the arena centre. ----
    def lift_shaft(tag, cx, cy, yaw, cab_z):
        def piece(suffix, lx, ly, lz, scale, material):
            ox, oy = _rot(lx, ly, yaw)
            objects.append(add_box("LiftShaft_%s_%s" % (tag, suffix),
                                   (cx + ox, cy + oy, lz), yaw,
                                   scale, material))

        piece("Glow", 0.0, -1.35, 10.5, (1.55, 0.08, 10.0), amber)
        for side in (-1.0, 1.0):
            piece("Post", side * 1.75, 0.0, 11.0, (0.28, 0.28, 11.0), dark)
            piece("Rail", side * 0.8, -1.05, 10.5, (0.20, 0.10, 10.0), dark)
        for rz in (4.0, 9.0, 14.0, 19.0):
            piece("Rung", 0.0, 0.0, rz, (1.75, 0.09, 0.09), dark)
        piece("Beam", 0.0, 0.0, 22.2, (1.9, 0.35, 0.35), dark)
        piece("Base", 0.0, 0.1, 0.5, (2.0, 0.9, 0.5), dark)
        # The lift cab: deck, back panel, lit front strip.
        piece("Cab", 0.0, -0.75, cab_z, (1.5, 0.55, 0.12), dark)
        piece("CabBack", 0.0, -1.28, cab_z + 1.15, (1.5, 0.06, 1.15), dark)
        piece("CabLight", 0.0, -0.18, cab_z + 0.35, (0.9, 0.05, 0.08), amber)

    lift_shaft("01", -10.4, -17.3, math.radians(-31.0), 12.5)
    lift_shaft("02", 10.4, -17.3, math.radians(31.0), 7.5)

    # ---- Supply crates: dark boxes with an amber top strip.
    # Two stacks at the flanks, singles by the shaft bases. ----
    def crate(tag, cx, cy, cz, yaw=0.0):
        objects.append(add_box("Crate_%s" % tag, (cx, cy, cz), yaw,
                               (1.1, 0.75, 0.55), dark))
        ox, oy = _rot(0.0, 0.72, yaw)
        objects.append(add_box("Crate_%s_Strip" % tag,
                               (cx + ox, cy + oy, cz + 0.42), yaw,
                               (1.0, 0.04, 0.07), amber))

    crate("01", 19.2, 13.0, 0.55)
    crate("02", 21.6, 13.2, 0.55)
    crate("03", 20.4, 13.1, 1.65, yaw=math.radians(90.0))
    crate("04", -20.8, 15.8, 0.55)
    crate("05", -18.4, 16.0, 0.55)
    crate("06", -19.6, 15.9, 1.65)
    crate("07", 14.8, -14.8, 0.55)
    crate("08", -14.8, -14.8, 0.55)

    # ---- The parked crawler (left flank): two tracks, a hull,
    # a cab, a lit nose strip and a thin mast. ----
    def crawler_part(tag, lx, ly, lz, scale, material):
        ox, oy = _rot(lx, ly, 0.55)
        objects.append(add_box("Crawler_%s" % tag,
                               (-18.5 + ox, 10.0 + oy, lz), 0.55,
                               scale, material))

    crawler_part("TrackL", 0.0, 0.95, 0.40, (1.9, 0.42, 0.40), dark)
    crawler_part("TrackR", 0.0, -0.95, 0.40, (1.9, 0.42, 0.40), dark)
    crawler_part("Hull", 0.0, 0.0, 1.05, (1.7, 0.95, 0.45), dark)
    crawler_part("Cab", -0.55, 0.0, 1.78, (0.7, 0.68, 0.48), dark)
    crawler_part("Nose", 1.72, 0.0, 1.15, (0.06, 0.5, 0.08), amber)
    mox, moy = _rot(-0.55, 0.0, 0.55)
    objects.append(add_cylinder("Crawler_Mast", 6, 0.045, 1.6, 2.75,
                                dark, x=-18.5 + mox, y=10.0 + moy))

    # ---- Floodlight towers x2: base, pole, head, and a lit
    # panel facing the arena. ----
    def floodlight(tag, sx):
        objects.append(add_box("Flood_%s_Base" % tag,
                               (sx, 5.0, 0.15), 0.0, (0.5, 0.5, 0.15), dark))
        objects.append(add_cylinder("Flood_%s_Pole" % tag, 8, 0.09, 5.6,
                                    2.95, dark, x=sx, y=5.0))
        objects.append(add_box("Flood_%s_Head" % tag,
                               (sx, 5.0, 5.85), 0.0, (0.95, 0.18, 0.5), dark))
        inward = -0.55 if sx > 0 else 0.55
        objects.append(add_box("Flood_%s_Glow" % tag,
                               (sx + inward, 5.0, 5.85), 0.0,
                               (0.05, 0.75, 0.38), amber))

    floodlight("01", 22.0)
    floodlight("02", -22.0)

    # ---- Gantry portal frames over the two crate stacks. ----
    def gantry(tag, cx, cy):
        for side in (-1.0, 1.0):
            objects.append(add_box("Gantry_%s_Post" % tag,
                                   (cx + side * 2.6, cy, 2.6), 0.0,
                                   (0.16, 0.16, 2.6), dark))
        objects.append(add_box("Gantry_%s_Beam" % tag,
                               (cx, cy, 5.3), 0.0,
                               (2.75, 0.16, 0.16), dark))

    gantry("01", 20.4, 13.1)
    gantry("02", -19.6, 15.9)

    # ================= V8 FLOOR DRESSING (2026-11-13) ==========
    # The clean facility floor: painted markings, walkways to
    # the shafts, canisters, consoles and a hauler. All flat or
    # low equipment — nothing on the floor is rock any more.

    # ---- Hazard ring around the boss pad: 28 amber segments.
    for i in range(28):
        a = i * (2.0 * math.pi / 28.0)
        objects.append(add_box(
            "HazardRing_%02d" % (i + 1),
            (math.cos(a) * 6.4, -10.0 + math.sin(a) * 6.4, 0.012),
            a + math.pi / 2.0, (0.62, 0.14, 0.012), amber))

    # ---- Walkways: dark strips with amber guide edges,
    # running from the arena centre out to each shaft base. ----
    def strip(tag, x1, y1, x2, y2, half_w, z, half_h, material):
        dx, dy = x2 - x1, y2 - y1
        length = math.hypot(dx, dy)
        theta = math.atan2(-dx, dy)
        objects.append(add_box(tag, ((x1 + x2) / 2.0, (y1 + y2) / 2.0, z),
                               theta, (half_w, length / 2.0, half_h),
                               material))

    def walkway(tag, sx):
        x1, y1 = sx * 0.30, -1.5
        x2, y2 = sx * 0.92, -15.6
        strip("Walkway_%s" % tag, x1, y1, x2, y2, 0.85, 0.010, 0.010, dark)
        dx, dy = x2 - x1, y2 - y1
        length = math.hypot(dx, dy)
        px, py = dy / length, -dx / length
        for side in (-1.0, 1.0):
            ox, oy = px * side * 0.98, py * side * 0.98
            strip("Walkway_%s_Edge" % tag, x1 + ox, y1 + oy,
                  x2 + ox, y2 + oy, 0.07, 0.012, 0.012, amber)

    walkway("L", -10.4)
    walkway("R", 10.4)

    # ---- Canister groups: dark barrels with amber bands. ----
    def barrels(tag, cx, cy):
        spots = ((0.0, 0.0), (0.95, 0.35), (0.45, -0.8))
        for i, (lx, ly) in enumerate(spots):
            objects.append(add_cylinder("Barrel_%s_%d" % (tag, i + 1),
                                        12, 0.42, 1.15, 0.575, dark,
                                        x=cx + lx, y=cy + ly))
            objects.append(add_cylinder("Barrel_%s_%d_Band" % (tag, i + 1),
                                        12, 0.435, 0.16, 0.82, amber,
                                        x=cx + lx, y=cy + ly))

    barrels("01", 13.9, -11.5)
    barrels("02", -14.8, -11.0)
    barrels("03", 19.5, 2.5)

    # ---- Consoles: two control desks whose lit cyan screens
    # face the arena, standing beside the shafts. ----
    def console(tag, cx, cy, yaw):
        def piece(suffix, lx, ly, lz, scale, material):
            ox, oy = _rot(lx, ly, yaw)
            objects.append(add_box("Console_%s_%s" % (tag, suffix),
                                   (cx + ox, cy + oy, lz), yaw,
                                   scale, material))

        piece("Body", 0.0, 0.0, 0.55, (0.95, 0.38, 0.55), dark)
        piece("Back", 0.0, 0.30, 1.30, (0.95, 0.08, 0.75), dark)
        piece("Screen", 0.0, 0.24, 1.32, (0.80, 0.04, 0.50), cyan)

    console("01", -7.4, -13.6, math.radians(-31.0))
    console("02", 7.4, -13.6, math.radians(31.0))

    # ---- The flatbed hauler (right flank, opposite the
    # crawler): deck, cab with a lit nose, a deck crate, wheels.
    def hauler_part(tag, lx, ly, lz, scale, material, cyl=False):
        yaw = -0.45
        ox, oy = _rot(lx, ly, yaw)
        if cyl:
            objects.append(add_cylinder("Hauler_%s" % tag, 12, scale[0],
                                        scale[1], lz, material,
                                        x=18.0 + ox, y=4.5 + oy,
                                        rotation=(math.pi / 2.0, 0.0, yaw)))
        else:
            objects.append(add_box("Hauler_%s" % tag,
                                   (18.0 + ox, 4.5 + oy, lz), yaw,
                                   scale, material))

    hauler_part("Deck", 0.0, 0.0, 0.62, (2.3, 1.0, 0.18), dark)
    hauler_part("Cab", 1.65, 0.0, 1.05, (0.65, 0.85, 0.55), dark)
    hauler_part("CabLight", 2.32, 0.0, 1.15, (0.05, 0.55, 0.09), amber)
    hauler_part("DeckCrate", -0.9, 0.0, 1.28, (0.95, 0.8, 0.48), dark)
    for wx in (-1.5, 1.5):
        for wy in (-1.02, 1.02):
            hauler_part("Wheel", wx, wy, 0.34, (0.34, 0.25), dark, cyl=True)

    # ---- Industrial platforms x2 against the wall behind the
    # boss (Blender y -21.5 = Unity z +21.5), each with an amber
    # edge strip facing the arena. Corners reach r ~25.9, so they
    # read as built into the wall. ----
    for i, sx in enumerate((-8.0, 8.0)):
        objects.append(add_box(
            "Platform_%02d" % (i + 1), (sx, -21.5, 0.25), 0.0,
            (3.5, 1.75, 0.25), dark))
        objects.append(add_box(
            "Platform_%02d_Strip" % (i + 1), (sx, -19.85, 0.52), 0.0,
            (3.5, 0.07, 0.05), amber))

    # ---- Amber hazard strips flanking the boss start on the
    # floor (clear of the r 4.5 scorch ring). ----
    for i, sx in enumerate((-5.8, 5.8)):
        objects.append(add_box(
            "HazardStrip_%02d" % (i + 1), (sx, -10.0, 0.03), 0.0,
            (0.9, 0.14, 0.02), amber))

    # ---- Backdrop shell: one big dark sphere around everything,
    # normals flipped inward (below), so no gap between rocks can
    # ever show through to the void behind the set. V6: grown to
    # r 46 (centre z 4) so the raised ceiling slabs (tops ~40)
    # stay inside it at every radius. ----
    shell = add_sphere("Lava_Shell", 24, 16, 46.0, (0.0, 0.0, 4.0), rock)
    objects.append(shell)

    recalc_normals(objects)
    # Flip the shell's normals inward (recalc forced them outward).
    bpy.ops.object.select_all(action="DESELECT")
    shell.select_set(True)
    bpy.context.view_layer.objects.active = shell
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.flip_normals()
    bpy.ops.object.mode_set(mode="OBJECT")
    shell.select_set(False)
    return objects


def make_uv_atlas(objects):
    """Pack every mesh object's UV islands into one 0-1 atlas
    (Lightmap Pack), as with the other project models."""
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
    Python (zlib) — Blender's image.save() blackens generated
    pixels, so the PNG is written directly. Rows are flipped to
    match Blender's bottom-to-top pixel order."""
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


def _save_image(arr, filename):
    S = arr.shape[0]
    rgba = np.ones((S, S, 4), dtype=np.float32)
    rgba[..., :3] = arr
    img = bpy.data.images.new(filename, S, S, alpha=False)
    img.pixels.foreach_set(rgba.ravel())
    path = os.path.join(SCRIPT_DIR, filename)
    write_png(path, arr)
    img.filepath = path
    img.file_format = "PNG"
    return img


def _blotches(S, xx, yy, rng, count, strength_max, clip_max, r_lo=80, r_hi=320):  # V4: radii doubled with the 2048 atlas so blotch scale matches V3
    out = np.zeros((S, S), dtype=np.float32)
    for _ in range(count):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(r_lo, r_hi)
        strength = rng.uniform(0.08, strength_max)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        out += strength * np.exp(-d2 / (2.0 * (r / 2.5) ** 2))
    return np.clip(out, 0.0, clip_max)


def make_basalt_texture(filename):
    """Basalt rock: blue-grey base (V4 lifted), coarse tonal
    blotches in both directions, strata banding (V4: soft
    horizontal brightness waves warped by the blotch field, so
    the walls carry large-scale structure instead of reading
    flat), fine grain speckle, long vertical fracture streaks,
    and sparse pale dust settled in patches. Airless lunar
    rock."""
    S = ATLAS_SIZE
    rng = np.random.default_rng(811)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    base = np.array(BASALT, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    tone = _blotches(S, xx, yy, rng, 60, 0.22, 0.40)
    arr *= (1.0 - tone[..., None] * 0.7)
    light = _blotches(S, xx, yy, rng, 44, 0.20, 0.34)
    arr *= (1.0 + light[..., None] * 0.9)
    # Strata banding (V4): two soft sine waves down the texture,
    # phase-warped by the dark-blotch field so the bands wobble
    # like real sedimentary layers instead of ruler stripes.
    warp = tone * 9.0
    band = (1.0 + 0.075 * np.sin((yy / S) * np.pi * 2.0 * 6.0 + warp)
            + 0.045 * np.sin((yy / S) * np.pi * 2.0 * 17.0 + warp * 1.7))
    arr *= band[..., None]
    # Vertical fracture streaks (columnar jointing hints).
    for _ in range(44):
        x = int(rng.uniform(0, S))
        y0 = int(rng.uniform(0, S * 0.5))
        length = int(rng.uniform(120, S - y0))
        wdt = int(rng.uniform(3, 6))
        drift = 0
        for y in range(y0, min(S, y0 + length), 8):
            drift += int(rng.integers(-2, 3))
            xs = max(0, min(S - wdt, x + drift))
            arr[y:y + 8, xs:xs + wdt] *= 0.5
    speckle = rng.uniform(0.88, 1.12, (S, S)).astype(np.float32)
    arr *= speckle[..., None]
    dust = _blotches(S, xx, yy, rng, 20, 0.20, 0.30, 60, 180)
    pale = np.array((0.30, 0.31, 0.33), dtype=np.float32)
    arr = arr * (1.0 - dust[..., None] * 0.5) + pale * (dust[..., None] * 0.5)
    arr = np.clip(arr, 0.0, 1.0)
    return _save_image(arr, filename)


def make_floor_texture(filename):
    """Cracked basalt plates over regolith: big polygonal-feel
    plates (512 px cells, 16 px cracks; 256 px sub-cracks), per-
    plate brightness, pale dust drifted along the cracks, small
    impact pocks, fine speckle."""
    S = ATLAS_SIZE
    rng = np.random.default_rng(823)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    CELL, SUB = 256, 128  # V8: finer plates for the polished facility floor (V4-V7: 512/256)
    cx = np.mod(xx, CELL); cy = np.mod(yy, CELL)
    line = ((cx < 10) | (cy < 10)).astype(np.float32)
    sx = np.mod(xx, SUB); sy = np.mod(yy, SUB)
    subline = ((sx < 4) | (sy < 4)).astype(np.float32)
    cell_x = (xx // CELL).astype(np.int32)
    cell_y = (yy // CELL).astype(np.int32)
    h = np.sin(cell_x * 127.1 + cell_y * 311.7) * 43758.5453
    h = h - np.floor(h)
    cell_bright = 0.82 + 0.36 * h
    base = np.array(FLOOR_BASE, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= cell_bright[..., None]
    arr *= (1.0 - subline[..., None] * 0.22)
    # Dust halo along the cracks: blur the line mask, lighten the
    # ring next to (not on) the crack.
    soft = line.copy()
    for _ in range(3):
        soft = (soft + np.roll(soft, 2, 0) + np.roll(soft, -2, 0)
                + np.roll(soft, 2, 1) + np.roll(soft, -2, 1)) / 5.0
    halo = np.clip(soft - line, 0.0, 1.0)
    pale = np.array((0.66, 0.67, 0.69), dtype=np.float32)  # V8: pale dust against the polished plates
    arr = arr * (1.0 - halo[..., None] * 0.45) + pale * (halo[..., None] * 0.45)
    arr *= (1.0 - line[..., None] * 0.70)
    # Impact pocks: a few small dark gaussian dots (V8: the
    # facility floor is kept clean — 40, down from 130).
    for _ in range(40):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(3, 10)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        arr *= (1.0 - 0.5 * np.exp(-d2 / (2.0 * (r / 2.0) ** 2))[..., None])
    speckle = rng.uniform(0.90, 1.10, (S, S)).astype(np.float32)
    arr *= speckle[..., None]
    arr = np.clip(arr, 0.0, 1.0)
    return _save_image(arr, filename)


def make_glow_texture(rgb, seed, filename):
    """Saturated glow texture with fine speckle (the emission
    strength on the material does the glowing)."""
    S = ATLAS_SIZE
    rng = np.random.default_rng(seed)
    speckle = rng.uniform(0.88, 1.06, (S, S)).astype(np.float32)
    base = np.array(rgb, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= speckle[..., None]
    arr = np.clip(arr, 0.0, 1.0)
    return _save_image(arr, filename)


def apply_textures(textured_materials):
    """Generate one texture per textured material and plug it into
    Base Color (base colour set to white so Unity's multiply
    returns exactly the texture)."""
    info = []
    for mat in textured_materials:
        filename = TEXTURE_NAMES[mat.name]
        if mat.name == "LavaRock":
            img = make_basalt_texture(filename)
        elif mat.name == "LavaFloor":
            img = make_floor_texture(filename)
        elif mat.name == "LavaGlowAmber":
            img = make_glow_texture(GLOW_AMBER, 829, filename)
        elif mat.name == "LavaGlowCyan":
            img = make_glow_texture(GLOW_CYAN, 831, filename)
        else:
            continue
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
    # House rule: the scene is pure (clear_scene removed
    # everything else), so export the whole scene with
    # use_selection=False — no selection state to read.
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
    for obj in objects:
        if obj.type != "MESH":
            continue
        obj.data.calc_loop_triangles()
        total_tris += len(obj.data.loop_triangles)
    print("Lava Tube v9 build:")
    print("  objects=%d  total_tris=%d" % (len(objects), total_tris))
    pts = [o.matrix_world @ v.co for o in objects
           if o.type == "MESH" and o.name != "Lava_Shell"
           for v in o.data.vertices]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    print("  bounds x=[%.2f, %.2f] y=[%.2f, %.2f] z=[%.2f, %.2f]"
          % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    print("  exported: %s" % OUTPUT_FBX)
    print("  fbx bytes: %d" % os.path.getsize(OUTPUT_FBX))
    for mat_name, path, size in TEXTURE_INFO:
        print("  texture %s: %s (%d bytes)" % (mat_name, path, size))


def verify(objects):
    """Checks on the built set (Blender coords)."""
    ok = True

    def check(cond, msg):
        nonlocal ok
        print("  [%s] %s" % ("PASS" if cond else "FAIL", msg))
        if not cond:
            ok = False

    by_name = {o.name: o for o in objects}
    total_tris = 0
    for obj in objects:
        if obj.type != "MESH":
            continue
        obj.data.calc_loop_triangles()
        total_tris += len(obj.data.loop_triangles)
    check(total_tris <= 60000, "total tris %d (spec <= 60000)" % total_tris)
    check("Lava_Shell" in by_name, "backdrop shell present")
    pts = [o.matrix_world @ v.co for o in objects
           if o.type == "MESH" and o.name != "Lava_Shell"
           for v in o.data.vertices]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    check(max(xs) <= 36.0 and min(xs) >= -36.0,
          "bounds x in +/-36 (got [%.2f, %.2f])" % (min(xs), max(xs)))
    check(max(ys) <= 36.0 and min(ys) >= -36.0,
          "bounds y in +/-36 (got [%.2f, %.2f])" % (min(ys), max(ys)))
    check(max(zs) <= 41.5, "ceiling height %.2f (spec <= 41.5; V6 slabs top out ~40.7 with displacement — the gameplay constraint is the underside, ~34, far clear of the slam peak 20.2)" % max(zs))
    # The floor's top surface must sit exactly at z=0 (gameplay plane).
    floor = by_name.get("Lava_Floor")
    if floor is not None:
        fz = [floor.matrix_world @ v.co for v in floor.data.vertices]
        fmax = max(p.z for p in fz)
        check(abs(fmax) < 1e-4, "floor top at z=0 (got %.4f)" % fmax)
    # Gameplay pieces by name. (V8: the cover boulders are
    # gone — the floor is facility now; spec is zero.)
    boulders = [o for o in objects if o.name.startswith("CoverBoulder_")]
    check(len(boulders) == 0,
          "cover boulders: %d (spec 0, removed in V8)" % len(boulders))
    domes = [o for o in objects
             if len(o.name) == 9 and o.name.startswith("Beacon_")]
    check(len(domes) == 4, "beacon domes: %d (spec 4)" % len(domes))
    vents = [o for o in objects
             if len(o.name) == 7 and o.name.startswith("Vent_")]
    check(len(vents) == 5, "vent grates: %d (spec 5)" % len(vents))
    # Boulders inside the arena, clear of the boss start and gate.
    for b in boulders:
        bx, by = b.location.x, b.location.y
        r = math.hypot(bx, by)
        check(6.0 <= r <= ARENA_R, "%s at r=%.1f inside the arena"
              % (b.name, r))
    # Ceiling slabs keep the crack points open.
    slabs = [o for o in objects if o.name.startswith("Lava_Ceiling_")]
    for (cx, cy) in CRACKS:
        nearest = min(math.hypot(o.location.x - cx, o.location.y - cy)
                      for o in slabs)
        check(nearest >= 6.5, "crack (%.0f, %.0f) open (nearest slab %.1f)"
              % (cx, cy, nearest))
    # Walls ring the arena, not inside it.
    walls = [o for o in objects if o.name.startswith("Lava_Wall_")]
    check(len(walls) == 12 and all(
        math.hypot(o.location.x, o.location.y) >= 27.0 for o in walls),
        "12 wall chunks ringing at r >= 27")
    # V2 dressing counts + combat-lane clearance.
    crystals = [o for o in objects if o.name.startswith("Crystal_")]
    check(len(crystals) >= 55,
          "crystal shards: %d (spec >= 55)" % len(crystals))
    check(all(math.hypot(o.location.x, o.location.y) >= 17.5
              for o in crystals),
          "crystals clear of the combat lanes (r >= 17.5)")
    conduits = [o for o in objects if o.name.startswith("Conduit_")]
    check(len(conduits) == 4, "floor conduits: %d (spec 4)" % len(conduits))
    stalag = [o for o in objects if o.name.startswith("Stalagmite_")]
    check(len(stalag) == 0,
          "stalagmites: %d (spec 0, removed in V9)" % len(stalag))
    plats = [o for o in objects
             if len(o.name) == 11 and o.name.startswith("Platform_")]
    check(len(plats) == 2, "platforms: %d (spec 2)" % len(plats))
    # V3: upper wall tier present, and no stalactite hangs low
    # over the boss start (Blender (0, -10), slam arc to z ~20).
    wall_top = [o for o in objects if o.name.startswith("Lava_WallTop_")]
    check(len(wall_top) == 12,
          "upper wall tier: %d (spec 12)" % len(wall_top))
    low_over_boss = []
    for o in objects:
        if not o.name.startswith("Lava_Stalactite_"):
            continue
        if math.hypot(o.location.x, o.location.y + 10.0) >= 3.0:
            continue
        corners = [o.matrix_world @ Vector(c) for c in o.bound_box]
        if min(c.z for c in corners) < 21.0:
            low_over_boss.append(o.name)
    check(not low_over_boss,
          "no stalactite low over the boss start (offenders: %s)"
          % (", ".join(low_over_boss) if low_over_boss else "none"))
    # V6: third wall tier present; every stalactite tip stays
    # above z 25.5 (they hang from the raised ceiling now);
    # standing columns keep their nearest face outside the
    # r-25 camera cylinder; fallen columns actually lie down
    # (flat bound box, long horizontal span).
    wall_high = [o for o in objects if o.name.startswith("Lava_WallHigh_")]
    check(len(wall_high) == 12,
          "third wall tier: %d (spec 12)" % len(wall_high))
    low_tips = []
    for o in objects:
        if not o.name.startswith("Lava_Stalactite_"):
            continue
        corners = [o.matrix_world @ Vector(c) for c in o.bound_box]
        if min(c.z for c in corners) < 25.5:
            low_tips.append(o.name)
    check(not low_tips,
          "all stalactite tips above z 25.5 (offenders: %s)"
          % (", ".join(low_tips) if low_tips else "none"))
    columns = [o for o in objects if o.name.startswith("BasaltColumn_")]
    check(len(columns) == 10,
          "standing basalt columns: %d (spec 10)" % len(columns))
    check(all(math.hypot(o.location.x, o.location.y) - 1.5 >= 25.2
              for o in columns),
          "columns clear of the camera cylinder (nearest face r >= 25.2)")
    # V8: the floor dressing counts.
    ring = [o for o in objects if o.name.startswith("HazardRing_")]
    check(len(ring) == 28, "hazard ring segments: %d (spec 28)" % len(ring))
    barrel_bodies = [o for o in objects if o.name.startswith("Barrel_")
                     and not o.name.endswith("_Band")]
    check(len(barrel_bodies) == 9,
          "barrels: %d (spec 9)" % len(barrel_bodies))
    consoles = [o for o in objects if o.name.startswith("Console_")]
    check(len(consoles) == 6,
          "console pieces: %d (spec 6)" % len(consoles))
    hauler = [o for o in objects if o.name.startswith("Hauler_")]
    check(len(hauler) == 8, "hauler pieces: %d (spec 8)" % len(hauler))
    fallen = [o for o in objects if o.name.startswith("FallenColumn_")]
    check(len(fallen) == 0,
          "fallen columns: %d (spec 0, removed in V8)" % len(fallen))
    for o in fallen:
        corners = [o.matrix_world @ Vector(c) for c in o.bound_box]
        fxs = [c.x for c in corners]; fys = [c.y for c in corners]
        fzs = [c.z for c in corners]
        check(max(fzs) - min(fzs) <= 2.6
              and max(max(fxs) - min(fxs), max(fys) - min(fys)) >= 4.5,
              "%s lies flat (z-span %.2f, horizontal span %.2f)"
              % (o.name, max(fzs) - min(fzs),
                 max(max(fxs) - min(fxs), max(fys) - min(fys))))
    # V7: the facility dressing — two lift shafts against the
    # wall (every piece at r >= 25.5), crawler, floodlights,
    # crates, and the floor material gone metallic.
    shaft_glow = [o for o in objects
                  if o.name.startswith("LiftShaft_") and o.name.endswith("_Glow")]
    check(len(shaft_glow) == 2,
          "lift shaft glow panels: %d (spec 2)" % len(shaft_glow))
    shaft_pieces = [o for o in objects if o.name.startswith("LiftShaft_")]
    check(len(shaft_pieces) == 28,
          "lift shaft pieces: %d (spec 28)" % len(shaft_pieces))
    # The towers stand proud of the wall face: every shaft
    # vertex at r <= 22.0 (the wall rock's displaced surface
    # reaches in to r ~22.5 behind the boss), plus a measured
    # clearance from the shaft pieces and the crate bodies to
    # every wall vertex — the check whose absence buried the
    # first V7 placement inside the chunks.
    dress_verts = []
    dress_locs = []
    for o in objects:
        if o.name.startswith("LiftShaft_") or (
                len(o.name) == 8 and o.name.startswith("Crate_")):
            dress_locs.append(o.location)
            dress_verts.extend(o.matrix_world @ v.co for v in o.data.vertices)
    shaft_only_max_r = 0.0
    for o in shaft_pieces:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            shaft_only_max_r = max(shaft_only_max_r, math.hypot(w.x, w.y))
    check(shaft_only_max_r <= 22.0,
          "lift shaft max vertex r %.2f (spec <= 22.0)" % shaft_only_max_r)
    wall_verts = []
    for o in objects:
        if o.name.startswith("Lava_Wall_") or o.name.startswith("Lava_WallTop_"):
            wall_verts.extend(o.matrix_world @ v.co for v in o.data.vertices)
    near_wall = [w for w in wall_verts
                 if min(math.hypot(w.x - loc.x, w.y - loc.y)
                        for loc in dress_locs) < 9.0]
    min_d2 = 1e18
    for s in dress_verts:
        for w in near_wall:
            d2 = (s.x - w.x) ** 2 + (s.y - w.y) ** 2 + (s.z - w.z) ** 2
            if d2 < min_d2:
                min_d2 = d2
    min_clear = math.sqrt(min_d2)
    check(min_clear >= 0.35,
          "dressing clearance to wall rock: %.2f (spec >= 0.35)" % min_clear)
    cabs = [o for o in objects
            if o.name.startswith("LiftShaft_") and o.name.endswith("_Cab")]
    check(len(cabs) == 2, "lift cabs: %d (spec 2)" % len(cabs))
    crates = [o for o in objects
              if len(o.name) == 8 and o.name.startswith("Crate_")]
    check(len(crates) == 8, "crates: %d (spec 8)" % len(crates))
    crawler = [o for o in objects if o.name.startswith("Crawler_")]
    check(len(crawler) == 6, "crawler pieces: %d (spec 6)" % len(crawler))
    flood_glow = [o for o in objects
                  if o.name.startswith("Flood_") and o.name.endswith("_Glow")]
    check(len(flood_glow) == 2,
          "floodlight panels: %d (spec 2)" % len(flood_glow))
    floor_mat_obj = bpy.data.materials.get("LavaFloor")
    floor_metallic = -1.0
    if floor_mat_obj is not None and floor_mat_obj.use_nodes:
        bsdf = floor_mat_obj.node_tree.nodes.get("Principled BSDF")
        if bsdf is not None:
            floor_metallic = bsdf.inputs["Metallic"].default_value
    check(floor_metallic >= 0.5,
          "floor material metallic %.2f (spec >= 0.5)" % floor_metallic)
    bare = [o.name for o in objects
            if o.type == "MESH" and len(o.data.materials) == 0]
    check(not bare, "every mesh has a material (bare: %s)"
          % (", ".join(bare) if bare else "none"))
    print("  verification: %s" % ("ALL PASS" if ok else "FAILURES ABOVE"))
    return ok


if __name__ == "__main__":
    built = build()
    textured_mats = [m for m in bpy.data.materials
                     if m.name in TEXTURE_NAMES]
    make_uv_atlas(built)
    TEXTURE_INFO = apply_textures(textured_mats)
    export(built)
    report(built)
    print("Verification:")
    verify(built)
