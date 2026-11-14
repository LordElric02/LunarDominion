# BuildAres09V31.py — Lunar Dominion: A.R.E.S.-09 "Core-Cracker",
# V3.1 readability revision (Patrick 2026-11-13).
#
# V3 installed and verified correct, but its detail was sized
# for a close-up and did not read from the fight camera (dark
# housing on a dark pauldron, small pod, texture-level wear).
# V3.1 keeps every V3 part, the rig and the envelope, and:
#   - scales the signature weapons up ~40% (rotary cluster,
#     missile pod, plasma barrel, drill) so the SILHOUETTE
#     changes at distance;
#   - adds light: a glowing cyan ring at the rotary muzzle
#     face, glowing red tips in the six missile tubes, hazard
#     trim across both pauldron fronts;
#   - raises wear contrast (brighter scuffs, stronger rust,
#     darker grime, stronger heat tint) and lifts the base
#     gunmetal a step lighter against the dark cave.
# TARGET EXTRAS (added later the same evening, Patrick's
# choice from his front-view target image): the plating goes
# to the target's pale weathered grey (GUNMETAL 0.55/0.57/
# 0.60), both forearms gain massive forward cannons, and the
# VISOR SWITCHES TO BLUE (glow cyan, widened) — Patrick's
# explicit call, overriding the V3 red-visor decision.
# Forked from BuildAres09V3.py — the original V3 header notes
# below still describe the pass as a whole.
#
# BuildAres09V3.py — Lunar Dominion: A.R.E.S.-09 "Core-Cracker",
# V3 definition pass (Patrick 2026-11-13, from his cinematic
# target image + written spec: "add more definition to the
# robot as specified in this spec").
#
# WHAT CHANGED FROM V2: DEFINITION, per the spec — the rig,
# every controller-critical name, the height (2.97) and the
# footprint are IDENTICAL again; all new geometry is
# "AresV3_" prefixed and parented into the existing rig.
#   - ROTARY CANNON (right shoulder): a true 6-barrel rotary
#     cluster in a ringed housing over the v1 rack — barrels,
#     muzzle tips, chrome hub, hazard rim, and a feed cable
#     down to the ammo drum.
#   - MISSILE POD (left shoulder): a housing on the pauldron
#     with 6 rimmed missile tubes on its front face, hazard
#     rim, skirt and a power cable.
#   - ARM WEAPONS: a plasma emitter (glowing tip) on the left
#     forearm, a rotary drill beside the right hand cannon,
#     arm hydraulic pistons, forearm outer plates.
#   - LAYERED PLATING: stepped hunch slabs behind the neck,
#     heat vent slats under the chest, chest side vents, a
#     back vent cluster, auxiliary exhausts, chest-top seam
#     strips, thigh front plates with seams, calf pistons,
#     ankle guards, foot side plates, a brow guard and cheek
#     guards on the head.
#   - WEAR: the gunmetal texture gains rust streaks, more
#     scuffs and heavier grime; the heat texture gains more
#     burnt discoloration (new seeds — panels re-roll).
# V3 kept the visor RED-ORANGE (hostile read). SUPERSEDED in
# V3.1: Patrick chose the blue visor from his target images
# (2026-11-13) — the Visor now uses the cyan glow material,
# widened. The sensor-pod eyes and antenna tip stay red.
#
# WHAT CHANGED FROM V1: geometry detail only. The rig, every
# controller-critical name, all materials/textures, the height
# (2.97) and the footprint are IDENTICAL to v1 — the turn-based
# fight and the dormant real-time controller see the same model,
# richer. Added (all "AresV2_" prefixed, all parented into the
# existing rig so code poses carry them): under-shoulder arms
# with elbow balls and end effectors (manipulator claw left,
# cannon right), layered pauldron shells, chest pec plates and
# glowing core-frame strips, thigh plates, shin guards, chrome
# hip actuators, knee spikes, a back power pack with glowing
# cells, head sensor pods with red eyes, a second antenna, and
# an ammo drum under the rotary rack.
#
# WHAT IT IS: a bipedal heavy mech built for CODE-DRIVEN animation —
# the named Empty hierarchy below is the rig. The boss controller
# (AresBossController) finds these nodes by name and rotates them:
# Torso yaws to track heroes and drives the Thermal Sweep, the legs
# bend for the Seismic Slam crouch, the visor pulses on telegraph.
# CONTROLLER-CRITICAL NAMES — do not rename these objects:
#   Ares09 (root), Hips, Torso, Head, LegL_Hip, LegR_Hip,
#   LegL_Knee, LegR_Knee, CoreChest, Visor, SweepLens.
# All other meshes use the Ares_ prefix.
#
# DESIGN (Blender coords: x = Unity x, y = -Unity z, z = up):
# front is +X (visor/core/lens face +X), same convention as the War
# Robot. Height ~2.97, footprint +/-1.2. The lair view places it at
# 3x scale, so it stands ~8.9 units tall in Unity against ~3.5-unit
# heroes. No ground disc — it stands on the lava tube arena floor.
#
# Look: scuffed gunmetal plating (2048 texture), burnt-orange heat
# discoloration on the chest plate / exhausts / lens dish (its own
# heat-tinted texture), emissive neon cyan strips along the chassis
# seams (thin glow-material geometry: waist ring, shins, shoulders,
# drone bay), a saturated RED-ORANGE visor bar (hostile read — cyan
# is the game's friendly accent), and an orange core glowing behind
# grill slats in the chest. Hazard stripes on the feet and shoulder
# rims. Stencil text decals are NOT modelled: this pipeline's
# per-material textures cannot place text on a specific panel.
#
# Textures: 6 PNGs via the v5 numpy pipeline — pure-Python zlib PNG
# encoder (never Blender image.save()), Lightmap Pack UVs.
#   Ares09_Gunmetal.png (2048), Ares09_Heat.png, Ares09_Hazard.png,
#   Ares09_GlowCyan.png, Ares09_GlowRed.png, Ares09_GlowCore.png.
#
# How to run (headless, no Blender UI needed):
#   blender --background --python BuildAres09V31.py
# From inside Blender: open the Scripting workspace, open this file,
# Run Script. Written/tested against Blender 4.2.3 LTS here; uses no
# deprecated APIs, so it should also run on Blender 5.2 LTS.
# NOTE: the script exports the FBX + PNGs next to wherever the script
# file was opened from (SCRIPT_DIR). If you open it from F:\ or
# Downloads, the files land there — check the "exported:" lines.
# Install: rename Ares09V31.fbx to Ares09.fbx (in Explorer, before
# copying, so the Unity .meta GUID survives) and copy it + the 6
# PNGs (regenerated identically from fixed seeds) over the old
# files in Assets/_Project/Resources/Models/ (ignore the .fbm
# sidecar, never rename the PNGs — the FBX references them by name).

import math
import os
import struct
import zlib

import bpy
import numpy as np
from mathutils import Quaternion, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FBX = os.path.join(SCRIPT_DIR, "Ares09V31.fbx")
ATLAS_SIZE = 2048          # gunmetal (the boss gets double resolution)
SMALL_SIZE = 1024          # every other texture
TEXTURE_NAMES = {
    "AresGunmetal": "Ares09_Gunmetal.png",
    "AresHeat": "Ares09_Heat.png",
    "AresHazard": "Ares09_Hazard.png",
    "AresGlowCyan": "Ares09_GlowCyan.png",
    "AresGlowRed": "Ares09_GlowRed.png",
    "AresGlowCore": "Ares09_GlowCore.png",
}

# Palette (linear RGB).
GUNMETAL = (0.55, 0.57, 0.60)     # weathered light-grey plating (V3.1 target extras: lifted to the pale worn grey of Patrick's front-view target image; V3.1 first step was (0.36, 0.39, 0.44), V3 original (0.30, 0.33, 0.38))
DARK_JOINT = (0.12, 0.13, 0.16)   # joints / dark machinery
CHROME = (0.62, 0.65, 0.70)       # hydraulic piston rods
HAZARD_YELLOW = (0.95, 0.75, 0.08)
HAZARD_BLACK = (0.04, 0.04, 0.04)
GLOW_CYAN = (0.10, 0.85, 1.00)    # seam strips / lens / antenna tip
GLOW_RED = (1.00, 0.16, 0.07)     # visor (saturated, hostile)
GLOW_CORE = (1.00, 0.45, 0.10)    # chest core

MAX_HEIGHT = 3.05


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


def add_sphere(name, segments, rings, radius, location, material,
               scale=(1.0, 1.0, 1.0)):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=radius, location=location
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def add_box(name, location, rotation_z, scale, material):
    """Box with `scale` as half-extents (the cube primitive is 2 units).
    Kept axis-aligned (rotation_z 0.0) for wide boxes: a rotated wide
    box's corners reach half-extent x (cos + sin) and can silently
    exceed the envelope (the Nova v5 lesson)."""
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=(0.0, 0.0, rotation_z))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def add_empty(name, location):
    """A named Empty — one joint of the code-animation rig."""
    bpy.ops.object.empty_add(type="PLAIN_AXES", location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.empty_display_size = 0.15
    return obj


def parent_to(child, parent):
    """Parent child -> parent keeping the child's world transform, so
    rotating the parent swings the child around the parent's origin
    (the joint pivot)."""
    bpy.ops.object.select_all(action="DESELECT")
    child.select_set(True)
    parent.select_set(True)
    bpy.context.view_layer.objects.active = parent
    bpy.ops.object.parent_set(type="OBJECT", keep_transform=True)


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in list(bpy.data.meshes):
        bpy.data.meshes.remove(block)
    for block in list(bpy.data.materials):
        bpy.data.materials.remove(block)


def recalc_normals(objects):
    """Safety net (the Helium-3 dish lesson): force every face normal
    outward so Unity never culls a flipped surface."""
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

    gunmetal = make_material("AresGunmetal", GUNMETAL, metallic=0.85, roughness=0.42)
    heat = make_material("AresHeat", GUNMETAL, metallic=0.85, roughness=0.46)
    dark = make_material("AresDark", DARK_JOINT, metallic=0.35, roughness=0.62)
    chrome = make_material("AresChrome", CHROME, metallic=1.0, roughness=0.18)
    hazard = make_material("AresHazard", HAZARD_YELLOW, metallic=0.2, roughness=0.60)
    glow_cyan = make_material("AresGlowCyan", GLOW_CYAN, metallic=0.1,
                              roughness=0.3, emission=2.5)
    glow_red = make_material("AresGlowRed", GLOW_RED, metallic=0.1,
                             roughness=0.3, emission=3.0)
    glow_core = make_material("AresGlowCore", GLOW_CORE, metallic=0.1,
                              roughness=0.3, emission=3.0)

    objects = []   # every object (meshes + empties), for export/report
    meshes = []    # meshes only, for normals

    def B(*args, **kwargs):
        obj = add_box(*args, **kwargs)
        objects.append(obj); meshes.append(obj)
        return obj

    def C(*args, **kwargs):
        obj = add_cylinder(*args, **kwargs)
        objects.append(obj); meshes.append(obj)
        return obj

    def S(*args, **kwargs):
        obj = add_sphere(*args, **kwargs)
        objects.append(obj); meshes.append(obj)
        return obj

    def E(name, location):
        obj = add_empty(name, location)
        objects.append(obj)
        return obj

    # ================= Rig empties (pivots) =================
    root = E("Ares09", (0.0, 0.0, 0.0))
    hips = E("Hips", (0.0, 0.0, 1.17))
    leg_hip = {}
    leg_knee = {}
    for side, s in (("L", 1.0), ("R", -1.0)):
        leg_hip[side] = E("Leg%s_Hip" % side, (0.0, s * 0.34, 1.08))
        leg_knee[side] = E("Leg%s_Knee" % side, (0.0, s * 0.37, 0.55))
    torso = E("Torso", (0.0, 0.0, 1.36))
    head = E("Head", (0.03, 0.0, 2.30))
    shoulder = {}
    for side, s in (("L", 1.0), ("R", -1.0)):
        shoulder[side] = E("Shoulder%s" % side, (0.0, s * 0.68, 2.22))

    # ================= Hips / pelvis =================
    pelvis = B("Ares_Pelvis", (0.0, 0.0, 1.20), 0.0, (0.34, 0.44, 0.13), gunmetal)
    waist_low = C("Ares_WaistRingLow", 16, 0.44, 0.07, 1.345, dark)
    hip_guards = []
    for side, s in (("L", 1.0), ("R", -1.0)):
        hip_guards.append(B("Ares_HipGuard%s" % side, (0.0, s * 0.48, 1.12),
                            0.0, (0.26, 0.07, 0.16), gunmetal))

    # ================= Legs (mirrored) =================
    leg_parts = {}   # side -> (hip-parented list, knee-parented list)
    for side, s in (("L", 1.0), ("R", -1.0)):
        hip_kids = []
        knee_kids = []
        # Thigh: big armoured box, hip pivot (1.08) to knee (0.55).
        hip_kids.append(B("Ares_Thigh%s" % side, (0.0, s * 0.36, 0.84),
                          0.0, (0.17, 0.15, 0.27), gunmetal))
        # Knee block at the knee pivot.
        hip_kids.append(B("Ares_KneeBlock%s" % side, (0.03, s * 0.37, 0.56),
                          0.0, (0.15, 0.14, 0.09), dark))
        # Hydraulic sleeve on the back of the thigh (rod telescopes in).
        hip_kids.append(C("Ares_PistonSleeve%s" % side, 8, 0.055, 0.34,
                          0.82, dark, x=-0.13, y=s * 0.36))
        # Shin: armoured box below the knee.
        knee_kids.append(B("Ares_Shin%s" % side, (0.0, s * 0.37, 0.34),
                           0.0, (0.14, 0.12, 0.19), gunmetal))
        # Chrome piston rod reaching up into the sleeve.
        knee_kids.append(C("Ares_PistonRod%s" % side, 8, 0.032, 0.32,
                           0.62, chrome, x=-0.13, y=s * 0.37))
        # Cyan seam strip down the shin front.
        knee_kids.append(B("Ares_ShinSeam%s" % side, (0.145, s * 0.37, 0.34),
                           0.0, (0.010, 0.030, 0.150), glow_cyan))
        # Foot: wide stomper plate + hazard top + toe claws + heel spur.
        knee_kids.append(B("Ares_Foot%s" % side, (0.06, s * 0.37, 0.07),
                           0.0, (0.30, 0.19, 0.07), gunmetal))
        knee_kids.append(B("Ares_FootHazard%s" % side, (0.06, s * 0.37, 0.148),
                           0.0, (0.27, 0.16, 0.012), hazard))
        for t, ty in enumerate((-0.11, 0.0, 0.11)):
            knee_kids.append(B("Ares_Toe%s_%d" % (side, t + 1),
                               (0.40, s * 0.37 + ty, 0.05), 0.0,
                               (0.09, 0.045, 0.05), dark))
        knee_kids.append(B("Ares_HeelSpur%s" % side, (-0.24, s * 0.37, 0.06),
                           0.0, (0.08, 0.10, 0.06), dark))
        leg_parts[side] = (hip_kids, knee_kids)

    # ================= Torso =================
    waist_ring = C("Ares_WaistRing", 16, 0.43, 0.08, 1.38, dark)
    waist_seams = []
    waist_seams.append(B("Ares_WaistSeamF", (0.435, 0.0, 1.38), 0.0,
                         (0.010, 0.180, 0.022), glow_cyan))
    waist_seams.append(B("Ares_WaistSeamB", (-0.435, 0.0, 1.38), 0.0,
                         (0.010, 0.180, 0.022), glow_cyan))
    for side, s in (("L", 1.0), ("R", -1.0)):
        waist_seams.append(B("Ares_WaistSeam%s" % side, (0.0, s * 0.435, 1.38),
                             0.0, (0.180, 0.010, 0.022), glow_cyan))
    abdomen = B("Ares_Abdomen", (0.0, 0.0, 1.47), 0.0, (0.36, 0.44, 0.07), dark)
    chest = B("Ares_Chest", (0.0, 0.0, 1.92), 0.0, (0.46, 0.56, 0.36), gunmetal)
    chest_plate = B("Ares_ChestPlate", (0.47, 0.0, 1.96), 0.0,
                    (0.025, 0.42, 0.27), heat)
    # The core: glowing disc (controller-critical name "CoreChest")
    # behind four dark grill slats on the chest front.
    core = C("CoreChest", 16, 0.16, 0.04, 1.78, glow_core, x=0.475,
             rotation=(0.0, math.radians(90.0), 0.0))
    grill = []
    for i, gz in enumerate((1.66, 1.74, 1.82, 1.90)):
        grill.append(B("Ares_CoreGrill_%d" % (i + 1), (0.50, 0.0, gz), 0.0,
                       (0.015, 0.175, 0.020), dark))
    back_plate = B("Ares_BackPlate", (-0.45, 0.0, 1.92), 0.0,
                   (0.04, 0.48, 0.28), gunmetal)
    # Drone bay on the back: dark hatch + a cyan seam line.
    drone_bay = B("Ares_DroneBay", (-0.50, 0.0, 1.98), 0.0,
                  (0.03, 0.30, 0.20), dark)
    bay_seam = B("Ares_BaySeam", (-0.538, 0.0, 1.98), 0.0,
                 (0.008, 0.280, 0.020), glow_cyan)
    # Exhaust stacks (heat-tinted) on the back top, with dark caps.
    stacks = []
    stack_caps = []
    for side, s in (("L", 1.0), ("R", -1.0)):
        stacks.append(C("Ares_Exhaust%s" % side, 10, 0.075, 0.48, 2.42,
                        heat, x=-0.36, y=s * 0.30))
        stack_caps.append(C("Ares_StackCap%s" % side, 10, 0.090, 0.045, 2.68,
                            dark, x=-0.36, y=s * 0.30))
    # Cables from hips up the torso sides.
    cables = []
    for side, s in (("L", 1.0), ("R", -1.0)):
        cables.append(C("Ares_Cable%s" % side, 6, 0.028, 0.52, 1.30,
                        dark, x=0.0, y=s * 0.52))

    # ================= Head =================
    neck = C("Ares_Neck", 10, 0.14, 0.12, 2.32, dark, x=0.03)
    head_block = B("Ares_HeadBlock", (0.05, 0.0, 2.50), 0.0,
                   (0.20, 0.21, 0.13), dark)
    # The visor: one wide glowing bar (controller-critical
    # "Visor"). V3.1: BLUE (glow cyan) and widened, per
    # Patrick's target images — his explicit choice 2026-11-13.
    visor = B("Visor", (0.265, 0.0, 2.52), 0.0, (0.020, 0.185, 0.050),
              glow_cyan)
    antenna = C("Ares_Antenna", 6, 0.012, 0.30, 2.78, gunmetal,
                x=-0.06, y=0.12)
    antenna_tip = S("Ares_AntennaTip", 6, 4, 0.022, (-0.06, 0.12, 2.95),
                    glow_cyan)
    head_fins = []
    for side, s in (("L", 1.0), ("R", -1.0)):
        head_fins.append(B("Ares_HeadFin%s" % side, (-0.02, s * 0.24, 2.52),
                           0.0, (0.14, 0.015, 0.09), gunmetal))

    # ================= Shoulders =================
    shoulder_kids = {}
    for side, s in (("L", 1.0), ("R", -1.0)):
        kids = []
        kids.append(B("Ares_ShoulderPlate%s" % side, (0.02, s * 0.72, 2.30),
                      0.0, (0.30, 0.16, 0.20), gunmetal))
        kids.append(B("Ares_ShoulderRim%s" % side, (0.02, s * 0.72, 2.515),
                      0.0, (0.27, 0.13, 0.015), hazard))
        kids.append(B("Ares_ShoulderSeam%s" % side, (0.02, s * 0.885, 2.30),
                      0.0, (0.240, 0.010, 0.030), glow_cyan))
        shoulder_kids[side] = kids
    # LEFT (+Y): the Thermal Sweep lens — heat dish + glowing lens
    # (controller-critical "SweepLens"), facing +X.
    lens_dish = add_cone("Ares_LensDish", 12, 0.05, 0.17, 0.13, 2.30,
                         heat, x=0.36, y=0.72,
                         rotation=(0.0, math.radians(90.0), 0.0))
    objects.append(lens_dish); meshes.append(lens_dish)
    sweep_lens = S("SweepLens", 10, 6, 0.055, (0.43, 0.72, 2.30),
                   glow_cyan, (0.5, 1.0, 1.0))
    # RIGHT (-Y): rotary rack — block + three barrels + muzzle rings.
    rack = B("Ares_RackBlock", (0.10, -0.72, 2.12), 0.0,
             (0.16, 0.14, 0.10), dark)
    barrels = []
    muzzles = []
    for i, by in enumerate((-0.79, -0.72, -0.65)):
        barrels.append(C("Ares_Barrel_%d" % (i + 1), 8, 0.035, 0.34, 2.12,
                         gunmetal, x=0.33, y=by,
                         rotation=(0.0, math.radians(90.0), 0.0)))
        muzzles.append(C("Ares_Muzzle_%d" % (i + 1), 8, 0.047, 0.05, 2.12,
                         dark, x=0.51, y=by,
                         rotation=(0.0, math.radians(90.0), 0.0)))

    # ================= V2 DETAIL (2026-11-11) =================
    # Extra geometry, all "AresV2_" prefixed, collected per rig
    # joint and parented in the parenting section below — when
    # code rotates a joint, the detail rides with it. Envelopes
    # respect the v1 checks: footprint +/-1.20, top < 2.85 (the
    # v1 antenna tip stays the tallest point), nothing below z 0.
    v2_hip = {"L": [], "R": []}
    v2_knee = {"L": [], "R": []}
    v2_torso = []
    v2_head = []
    v2_shoulder = {"L": [], "R": []}

    for side, s in (("L", 1.0), ("R", -1.0)):
        # Pauldron shell layered over the shoulder plate.
        v2_shoulder[side].append(B(
            "AresV2_Pauldron%s" % side, (0.02, s * 0.75, 2.46), 0.0,
            (0.33, 0.185, 0.095), gunmetal))
        # Actuator ball centred on the shoulder pivot itself.
        v2_shoulder[side].append(S(
            "AresV2_ShoulderBall%s" % side, 8, 5, 0.115,
            (0.0, s * 0.68, 2.22), dark))
        # The arm: upper arm hanging from the plate, elbow ball,
        # forearm with a cyan front strip.
        v2_shoulder[side].append(B(
            "AresV2_UpperArm%s" % side, (0.0, s * 0.76, 1.92), 0.0,
            (0.085, 0.085, 0.17), gunmetal))
        v2_shoulder[side].append(S(
            "AresV2_Elbow%s" % side, 8, 5, 0.095,
            (0.0, s * 0.76, 1.72), dark))
        v2_shoulder[side].append(B(
            "AresV2_Forearm%s" % side, (0.02, s * 0.76, 1.52), 0.0,
            (0.10, 0.095, 0.16), gunmetal))
        v2_shoulder[side].append(B(
            "AresV2_ForearmSeam%s" % side, (0.125, s * 0.76, 1.52), 0.0,
            (0.008, 0.030, 0.120), glow_cyan))
        # Chest pec plate + a glowing strip framing the core grill.
        v2_torso.append(B(
            "AresV2_PecPlate%s" % side, (0.49, s * 0.345, 1.94), 0.0,
            (0.018, 0.145, 0.29), gunmetal))
        v2_torso.append(B(
            "AresV2_CoreFrame%s" % side, (0.512, s * 0.205, 1.78), 0.0,
            (0.008, 0.014, 0.155), glow_cyan))
        # Thigh outer plate (hip) + knee spike (hip: the knee
        # block hangs off the hip pivot in v1).
        v2_hip[side].append(B(
            "AresV2_ThighPlate%s" % side, (0.0, s * 0.528, 0.84), 0.0,
            (0.145, 0.016, 0.225), gunmetal))
        spike = add_cone("AresV2_KneeSpike%s" % side, 6, 0.05, 0.005,
                         0.16, 0.56, dark, x=0.22, y=s * 0.37,
                         rotation=(0.0, math.radians(90.0), 0.0))
        objects.append(spike); meshes.append(spike)
        v2_hip[side].append(spike)
        # Chrome hip actuator, axis along Y through the hip pivot.
        v2_hip[side].append(C(
            "AresV2_HipActuator%s" % side, 8, 0.058, 0.34, 1.08,
            chrome, x=0.0, y=s * 0.34,
            rotation=(math.radians(90.0), 0.0, 0.0)))
        # Shin guard plate (knee).
        v2_knee[side].append(B(
            "AresV2_ShinGuard%s" % side, (0.152, s * 0.37, 0.34), 0.0,
            (0.014, 0.095, 0.155), dark))
        # Head sensor pod + red eye, just outside the head fin.
        v2_head.append(B(
            "AresV2_SensorPod%s" % side, (0.05, s * 0.295, 2.50), 0.0,
            (0.10, 0.028, 0.055), dark))
        v2_head.append(S(
            "AresV2_SensorEye%s" % side, 6, 4, 0.018,
            (0.155, s * 0.295, 2.50), glow_red))

    # End effectors: LEFT a 3-finger manipulator claw, RIGHT a
    # short cannon under the forearm.
    for f, off in enumerate((-0.07, 0.0, 0.07)):
        v2_shoulder["L"].append(B(
            "AresV2_Claw_%d" % (f + 1), (0.06, 0.76 + off, 1.30),
            off * 1.2, (0.07, 0.020, 0.070), dark))
    v2_shoulder["R"].append(C(
        "AresV2_HandCannon", 8, 0.045, 0.30, 1.38, gunmetal,
        x=0.16, y=-0.76, rotation=(0.0, math.radians(90.0), 0.0)))
    v2_shoulder["R"].append(C(
        "AresV2_HandMuzzle", 8, 0.058, 0.05, 1.38, dark,
        x=0.32, y=-0.76, rotation=(0.0, math.radians(90.0), 0.0)))
    # Ammo drum under the rotary rack (right shoulder).
    v2_shoulder["R"].append(C(
        "AresV2_AmmoDrum", 10, 0.095, 0.12, 1.94, dark,
        x=0.10, y=-0.72, rotation=(math.radians(90.0), 0.0, 0.0)))
    # Back power pack (above the drone bay, biting into the
    # chest's top-back corner) + two glowing cells.
    v2_torso.append(B("AresV2_PowerPack", (-0.50, 0.0, 2.33), 0.0,
                      (0.05, 0.24, 0.105), dark))
    for side, s in (("L", 1.0), ("R", -1.0)):
        v2_torso.append(C(
            "AresV2_PackCell%s" % side, 8, 0.042, 0.05, 2.33,
            glow_cyan, x=-0.565, y=s * 0.11,
            rotation=(0.0, math.radians(90.0), 0.0)))
    # Second, shorter antenna (right side) with a red tip.
    v2_head.append(C("AresV2_Antenna2", 6, 0.010, 0.20, 2.70,
                     gunmetal, x=-0.06, y=-0.12))
    v2_head.append(S("AresV2_Antenna2Tip", 6, 4, 0.016,
                     (-0.06, -0.12, 2.815), glow_red))

    # ================= V3 DEFINITION (2026-11-13) =================
    # Patrick's spec pass: heavier weapons, layered plating,
    # more machinery. All "AresV3_" prefixed, collected per rig
    # joint like the v2 detail and parented below. Envelopes
    # respect the standing checks: footprint +/-1.20, top < 2.85
    # (the v1 antenna tip stays the tallest point), z >= 0.
    v3_hip = {"L": [], "R": []}
    v3_knee = {"L": [], "R": []}
    v3_torso = []
    v3_head = []
    v3_shoulder = {"L": [], "R": []}

    # ---- RIGHT shoulder: the rotary cannon cluster. A ringed
    # housing over the v1 rack, six barrels around a chrome hub,
    # muzzle tips, hazard rim, feed cable to the ammo drum. ----
    v3_shoulder["R"].append(B(
        "AresV3_RotaryHousing", (0.14, -0.72, 2.28), 0.0,
        (0.24, 0.16, 0.16), dark))
    v3_shoulder["R"].append(C(
        "AresV3_RotaryRingB", 12, 0.15, 0.05, 2.28, gunmetal,
        x=0.0, y=-0.72, rotation=(0.0, math.radians(90.0), 0.0)))
    v3_shoulder["R"].append(C(
        "AresV3_RotaryRingF", 12, 0.15, 0.04, 2.28, glow_cyan,
        x=0.66, y=-0.72, rotation=(0.0, math.radians(90.0), 0.0)))
    v3_shoulder["R"].append(B(
        "AresV3_RotaryHazard", (0.30, -0.72, 2.455), 0.0,
        (0.04, 0.14, 0.016), hazard))
    for i in range(6):
        ang = math.radians(60.0 * i)
        by = -0.72 + 0.088 * math.cos(ang)
        bz = 2.28 + 0.088 * math.sin(ang)
        v3_shoulder["R"].append(C(
            "AresV3_RotaryBarrel_%d" % (i + 1), 8, 0.036, 0.58, bz,
            gunmetal, x=0.42, y=by,
            rotation=(0.0, math.radians(90.0), 0.0)))
        v3_shoulder["R"].append(C(
            "AresV3_RotaryMuzzle_%d" % (i + 1), 8, 0.044, 0.05, bz,
            dark, x=0.72, y=by,
            rotation=(0.0, math.radians(90.0), 0.0)))
    v3_shoulder["R"].append(C(
        "AresV3_RotaryHub", 8, 0.042, 0.62, 2.28, chrome,
        x=0.42, y=-0.72, rotation=(0.0, math.radians(90.0), 0.0)))
    v3_shoulder["R"].append(C(
        "AresV3_FeedCable", 6, 0.024, 0.30, 2.05, dark,
        x=0.06, y=-0.79, rotation=(0.0, math.radians(45.0), 0.0)))

    # ---- LEFT shoulder: the missile pod. Housing on the
    # pauldron, six rimmed tubes on the front face. ----
    v3_shoulder["L"].append(B(
        "AresV3_MissilePod", (-0.04, 0.74, 2.65), 0.0,
        (0.21, 0.14, 0.10), gunmetal))
    v3_shoulder["L"].append(B(
        "AresV3_PodSkirt", (-0.04, 0.74, 2.56), 0.0,
        (0.23, 0.155, 0.02), dark))
    v3_shoulder["L"].append(B(
        "AresV3_PodHazard", (0.175, 0.74, 2.71), 0.0,
        (0.016, 0.12, 0.04), hazard))
    pod_i = 0
    for pz in (2.60, 2.70):
        for py in (0.665, 0.74, 0.815):
            pod_i += 1
            v3_shoulder["L"].append(C(
                "AresV3_MissileRim_%d" % pod_i, 10, 0.048, 0.025, pz,
                chrome, x=0.175, y=py,
                rotation=(0.0, math.radians(90.0), 0.0)))
            v3_shoulder["L"].append(C(
                "AresV3_MissileTube_%d" % pod_i, 10, 0.036, 0.03, pz,
                dark, x=0.18, y=py,
                rotation=(0.0, math.radians(90.0), 0.0)))
            v3_shoulder["L"].append(S(
                "AresV3_MissileTip_%d" % pod_i, 8, 5, 0.020,
                (0.20, py, pz), glow_red))
    v3_shoulder["L"].append(C(
        "AresV3_PodCable", 6, 0.020, 0.30, 2.47, dark,
        x=-0.24, y=0.82))

    # ---- V3.1: hazard trim across both pauldron fronts — a
    # band of warning colour at shoulder height that reads at
    # fight distance. ----
    for side, s in (("L", 1.0), ("R", -1.0)):
        v3_shoulder[side].append(B(
            "AresV3_PauldronTrim%s" % side, (0.355, s * 0.75, 2.46),
            0.0, (0.012, 0.15, 0.032), hazard))

    # ---- Arms (both): hydraulic piston behind the upper arm,
    # layered forearm plate; LEFT a plasma emitter, RIGHT a
    # rotary drill beside the hand cannon. ----
    for side, s in (("L", 1.0), ("R", -1.0)):
        v3_shoulder[side].append(C(
            "AresV3_ArmSleeve%s" % side, 8, 0.034, 0.17, 1.99, dark,
            x=-0.115, y=s * 0.76))
        v3_shoulder[side].append(C(
            "AresV3_ArmRod%s" % side, 8, 0.020, 0.15, 1.845, chrome,
            x=-0.115, y=s * 0.76))
        v3_shoulder[side].append(B(
            "AresV3_ForearmPlate%s" % side, (0.02, s * 0.875, 1.52),
            0.0, (0.09, 0.018, 0.13), gunmetal))
    v3_shoulder["L"].append(C(
        "AresV3_PlasmaBarrel", 8, 0.038, 0.16, 1.52, gunmetal,
        x=0.17, y=0.76, rotation=(0.0, math.radians(90.0), 0.0)))
    v3_shoulder["L"].append(S(
        "AresV3_PlasmaTip", 8, 5, 0.030, (0.26, 0.76, 1.52),
        glow_cyan))
    v3_shoulder["R"].append(B(
        "AresV3_DrillMount", (0.10, -0.86, 1.38), 0.0,
        (0.05, 0.04, 0.06), dark))
    drill = add_cone("AresV3_Drill", 8, 0.06, 0.004, 0.28, 1.38,
                     chrome, x=0.27, y=-0.88,
                     rotation=(0.0, math.radians(90.0), 0.0))
    objects.append(drill); meshes.append(drill)
    v3_shoulder["R"].append(drill)

    # ---- V3.1 target extras: massive forearm cannons (both
    # arms), per Patrick's front-view target image — big
    # forward barrels that change the arm silhouette. ----
    for side, s in (("L", 1.0), ("R", -1.0)):
        v3_shoulder[side].append(B(
            "AresV3_ForeCannon%s" % side, (0.10, s * 0.76, 1.45),
            0.0, (0.13, 0.11, 0.11), gunmetal))
        v3_shoulder[side].append(C(
            "AresV3_ForeBarrel%s" % side, 10, 0.055, 0.42, 1.47,
            dark, x=0.32, y=s * 0.76,
            rotation=(0.0, math.radians(90.0), 0.0)))
        v3_shoulder[side].append(C(
            "AresV3_ForeBarrelLow%s" % side, 8, 0.034, 0.34, 1.36,
            gunmetal, x=0.28, y=s * 0.76,
            rotation=(0.0, math.radians(90.0), 0.0)))
        v3_shoulder[side].append(C(
            "AresV3_ForeMuzzle%s" % side, 10, 0.068, 0.05, 1.47,
            dark, x=0.52, y=s * 0.76,
            rotation=(0.0, math.radians(90.0), 0.0)))
        v3_shoulder[side].append(B(
            "AresV3_ForeHazard%s" % side, (0.10, s * 0.76, 1.575),
            0.0, (0.10, 0.08, 0.012), hazard))

    # ---- Torso: stepped hunch behind the neck, heat slats
    # under the chest, side vents, back vent cluster, auxiliary
    # exhausts, seam strips on the chest top. ----
    v3_torso.append(B("AresV3_HunchLow", (-0.34, 0.0, 2.38), 0.0,
                      (0.12, 0.38, 0.06), gunmetal))
    v3_torso.append(B("AresV3_HunchHigh", (-0.44, 0.0, 2.50), 0.0,
                      (0.08, 0.32, 0.05), gunmetal))
    for i, sz in enumerate((1.50, 1.44, 1.38)):
        v3_torso.append(B("AresV3_ChestSlat_%d" % (i + 1),
                          (0.375, 0.0, sz), 0.0,
                          (0.015, 0.30, 0.018), heat))
    for side, s in (("L", 1.0), ("R", -1.0)):
        v3_torso.append(B("AresV3_SideVent%s" % side,
                          (0.30, s * 0.585, 1.95), 0.0,
                          (0.02, 0.015, 0.12), dark))
        v3_torso.append(C("AresV3_AuxExhaust%s" % side, 8, 0.042,
                          0.26, 2.44, heat, x=-0.42, y=s * 0.46))
        v3_torso.append(C("AresV3_AuxCap%s" % side, 8, 0.052,
                          0.04, 2.58, dark, x=-0.42, y=s * 0.46))
        v3_torso.append(B("AresV3_TopSeam%s" % side,
                          (0.10, s * 0.40, 2.285), 0.0,
                          (0.20, 0.012, 0.008), glow_cyan))
    v3_torso.append(B("AresV3_BackVentPanel", (-0.505, 0.0, 1.715),
                      0.0, (0.015, 0.26, 0.06), heat))
    for i, vz in enumerate((1.685, 1.715, 1.745)):
        v3_torso.append(B("AresV3_BackVent_%d" % (i + 1),
                          (-0.525, 0.0, vz), 0.0,
                          (0.012, 0.24, 0.012), dark))

    # ---- Legs: layered thigh front plates with a seam, calf
    # pistons, ankle guards, foot side plates. ----
    for side, s in (("L", 1.0), ("R", -1.0)):
        v3_hip[side].append(B(
            "AresV3_ThighFront%s" % side, (0.185, s * 0.36, 0.84),
            0.0, (0.016, 0.115, 0.22), gunmetal))
        v3_hip[side].append(B(
            "AresV3_ThighSeam%s" % side, (0.205, s * 0.36, 0.84),
            0.0, (0.006, 0.014, 0.17), glow_cyan))
        v3_knee[side].append(C(
            "AresV3_CalfSleeve%s" % side, 8, 0.036, 0.16, 0.42, dark,
            x=-0.155, y=s * 0.37))
        v3_knee[side].append(C(
            "AresV3_CalfRod%s" % side, 8, 0.020, 0.14, 0.26, chrome,
            x=-0.155, y=s * 0.37))
        v3_knee[side].append(B(
            "AresV3_AnkleGuard%s" % side, (0.155, s * 0.37, 0.16),
            0.0, (0.014, 0.10, 0.07), gunmetal))
        v3_knee[side].append(B(
            "AresV3_FootPlate%s" % side, (0.06, s * 0.575, 0.07),
            0.0, (0.24, 0.016, 0.055), dark))

    # ---- Head: brow guard over the visor, cheek guards, chin
    # guard (the visor itself stays red-orange — see header). ----
    v3_head.append(B("AresV3_BrowGuard", (0.24, 0.0, 2.60), 0.0,
                     (0.05, 0.19, 0.025), gunmetal))
    v3_head.append(B("AresV3_ChinGuard", (0.20, 0.0, 2.40), 0.0,
                     (0.05, 0.12, 0.03), gunmetal))
    for side, s in (("L", 1.0), ("R", -1.0)):
        v3_head.append(B("AresV3_CheekGuard%s" % side,
                         (0.16, s * 0.20, 2.44), 0.0,
                         (0.06, 0.02, 0.05), dark))

    recalc_normals(meshes)

    # ================= Parenting (the rig) =================
    parent_to(hips, root)
    for obj in (pelvis, waist_low, hip_guards[0], hip_guards[1]):
        parent_to(obj, hips)
    for side in ("L", "R"):
        parent_to(leg_hip[side], hips)
        hip_kids, knee_kids = leg_parts[side]
        for obj in hip_kids:
            parent_to(obj, leg_hip[side])
        parent_to(leg_knee[side], leg_hip[side])
        for obj in knee_kids:
            parent_to(obj, leg_knee[side])
    parent_to(torso, hips)
    for obj in (waist_ring, abdomen, chest, chest_plate, core, back_plate,
                drone_bay, bay_seam, stacks[0], stacks[1], stack_caps[0],
                stack_caps[1], cables[0], cables[1],
                waist_seams[0], waist_seams[1], waist_seams[2], waist_seams[3],
                grill[0], grill[1], grill[2], grill[3]):
        parent_to(obj, torso)
    parent_to(head, torso)
    for obj in (neck, head_block, visor, antenna, antenna_tip,
                head_fins[0], head_fins[1]):
        parent_to(obj, head)
    for side in ("L", "R"):
        parent_to(shoulder[side], torso)
        for obj in shoulder_kids[side]:
            parent_to(obj, shoulder[side])
    parent_to(lens_dish, shoulder["L"])
    parent_to(sweep_lens, shoulder["L"])
    parent_to(rack, shoulder["R"])
    for obj in barrels + muzzles:
        parent_to(obj, shoulder["R"])
    # V2 detail rides the same joints.
    for side in ("L", "R"):
        for obj in v2_hip[side]:
            parent_to(obj, leg_hip[side])
        for obj in v2_knee[side]:
            parent_to(obj, leg_knee[side])
        for obj in v2_shoulder[side]:
            parent_to(obj, shoulder[side])
    for obj in v2_torso:
        parent_to(obj, torso)
    for obj in v2_head:
        parent_to(obj, head)
    # V3 definition rides the same joints.
    for side in ("L", "R"):
        for obj in v3_hip[side]:
            parent_to(obj, leg_hip[side])
        for obj in v3_knee[side]:
            parent_to(obj, leg_knee[side])
        for obj in v3_shoulder[side]:
            parent_to(obj, shoulder[side])
    for obj in v3_torso:
        parent_to(obj, torso)
    for obj in v3_head:
        parent_to(obj, head)

    bpy.context.view_layer.update()
    return objects


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


def _save_image(arr, filename):
    """Register a Blender image from an array and write its PNG twin
    next to the script; returns the Blender image."""
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


def _panel_base(S, cell, sub, rng):
    """Shared panel-grid scaffolding: returns (line, subline,
    cell_bright, rivet) masks for a size-S texture."""
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    cx = np.mod(xx, cell)
    cy = np.mod(yy, cell)
    line = ((cx < cell // 16 + 2) | (cy < cell // 16 + 2)).astype(np.float32)
    sx = np.mod(xx, sub)
    sy = np.mod(yy, sub)
    subline = ((sx < 2) | (sy < 2)).astype(np.float32)
    cell_x = (xx // cell).astype(np.int32)
    cell_y = (yy // cell).astype(np.int32)
    h = np.sin(cell_x * 127.1 + cell_y * 311.7) * 43758.5453
    h = h - np.floor(h)
    cell_bright = 0.90 + 0.20 * h
    dx = np.minimum(cx, cell - cx)
    dy = np.minimum(cy, cell - cy)
    dist_corner = np.sqrt(dx * dx + dy * dy)
    rivet = (dist_corner < cell * 0.055).astype(np.float32)
    return yy, xx, line, subline, cell_bright, rivet


def _grime(S, xx, yy, rng, count, strength_max, clip_max):
    grime = np.zeros((S, S), dtype=np.float32)
    for _ in range(count):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(40, 140) * (S / 1024.0)
        strength = rng.uniform(0.08, strength_max)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        grime += strength * np.exp(-d2 / (2.0 * (r / 2.5) ** 2))
    return np.clip(grime, 0.0, clip_max)


def make_metal_texture(rgb, seed, filename, size, cell, sub):
    """Panelled metal with rivets, per-cell variation, grime, and
    scuff streaks (short pale scratches — the mech reads used, not
    showroom)."""
    S = size
    rng = np.random.default_rng(seed)
    yy, xx, line, subline, cell_bright, rivet = _panel_base(S, cell, sub, rng)
    base = np.array(rgb, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= cell_bright[..., None]
    arr *= (1.0 - line[..., None] * 0.75)
    arr *= (1.0 - subline[..., None] * 0.25)
    arr *= (1.0 + rivet[..., None] * 0.25)
    # Scuffs: short axis-aligned pale streaks.
    for _ in range(380):
        gx, gy = rng.uniform(0, S, 2)
        length = rng.uniform(8, 60) * (S / 1024.0)
        horiz = rng.uniform() < 0.5
        if horiz:
            x0, x1 = int(gx), int(min(S - 1, gx + length))
            y0 = int(gy)
            arr[y0:y0 + 2, x0:x1] *= 1.55
        else:
            y0, y1 = int(gy), int(min(S - 1, gy + length))
            x0 = int(gx)
            arr[y0:y1, x0:x0 + 2] *= 1.55
    # Rust streaks (V3): thin vertical runs of orange-brown
    # fading downward — the weep of old water and heat cycles
    # down the plating.
    rust = np.array((0.50, 0.20, 0.06), dtype=np.float32)
    for _ in range(60):
        gx = int(rng.uniform(0, S))
        gy = int(rng.uniform(0, S))
        length = int(rng.uniform(60, 260) * (S / 1024.0))
        width = int(rng.uniform(2, 5))
        alpha = rng.uniform(0.28, 0.55)
        y1 = min(S, gy + length)
        if y1 <= gy:
            continue
        fade = np.linspace(1.0, 0.25, y1 - gy, dtype=np.float32)
        strip = arr[gy:y1, gx:gx + width]
        a = alpha * fade[:, None, None]
        arr[gy:y1, gx:gx + width] = strip * (1.0 - a) + rust * a
    grime = _grime(S, xx, yy, rng, 64, 0.25, 0.42)
    arr *= (1.0 - grime[..., None])
    arr = np.clip(arr, 0.0, 1.0)
    return _save_image(arr, filename)


def make_heat_texture(filename):
    """Gunmetal panels with burnt heat discoloration: blotches tinted
    through the heat palette (burnt orange -> straw -> blue-purple),
    heaviest overall warmth, heavier grime. Used on the chest plate,
    exhaust stacks, and the sweep lens dish — parts that run hot."""
    S = SMALL_SIZE
    rng = np.random.default_rng(307)
    yy, xx, line, subline, cell_bright, rivet = _panel_base(S, 48, 24, rng)
    base = np.array(GUNMETAL, dtype=np.float32) * 0.9
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= cell_bright[..., None]
    arr *= (1.0 - line[..., None] * 0.75)
    arr *= (1.0 - subline[..., None] * 0.25)
    palette = [np.array((0.62, 0.26, 0.07), dtype=np.float32),   # burnt orange
               np.array((0.66, 0.47, 0.16), dtype=np.float32),   # straw
               np.array((0.28, 0.22, 0.42), dtype=np.float32),   # blue-purple
               np.array((0.45, 0.16, 0.05), dtype=np.float32)]   # deep rust
    for _ in range(44):
        gx, gy = rng.uniform(0, S, 2)
        r = rng.uniform(60, 220)
        strength = rng.uniform(0.45, 0.85)
        d2 = (xx - gx) ** 2 + (yy - gy) ** 2
        mask = np.exp(-d2 / (2.0 * (r / 2.2) ** 2)) * strength
        colour = palette[int(rng.integers(0, len(palette)))]
        arr = arr * (1.0 - mask[..., None]) + colour * mask[..., None]
    grime = _grime(S, xx, yy, rng, 56, 0.30, 0.55)
    arr *= (1.0 - grime[..., None])
    arr = np.clip(arr, 0.0, 1.0)
    return _save_image(arr, filename)


def make_hazard_texture(filename):
    """Diagonal yellow/black hazard stripes (64 px bands) with slight
    noise and grime, for the foot tops and shoulder rims."""
    S = SMALL_SIZE
    rng = np.random.default_rng(401)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    band = (((xx + yy) // 64) % 2).astype(np.float32)  # 0 yellow, 1 black
    yellow = np.array(HAZARD_YELLOW, dtype=np.float32)
    black = np.array(HAZARD_BLACK, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * yellow
    arr = np.where(band[..., None] > 0.5, black, arr)
    noise = rng.uniform(0.90, 1.05, (S, S)).astype(np.float32)
    arr *= noise[..., None]
    grime = _grime(S, xx, yy, rng, 24, 0.18, 0.35)
    arr *= (1.0 - grime[..., None])
    arr = np.clip(arr, 0.0, 1.0)
    return _save_image(arr, filename)


def make_glow_texture(rgb, seed, filename):
    """Saturated glow texture: flat spec colour with fine speckle
    variation. The emission strength on the material does the
    glowing; the texture keeps the base colour saturated (pale
    tints wash out in Unity — the Hydroponics lesson)."""
    S = SMALL_SIZE
    rng = np.random.default_rng(seed)
    speckle = rng.uniform(0.88, 1.06, (S, S)).astype(np.float32)
    base = np.array(rgb, dtype=np.float32)
    arr = np.ones((S, S, 3), dtype=np.float32) * base
    arr *= speckle[..., None]
    arr = np.clip(arr, 0.0, 1.0)
    return _save_image(arr, filename)


def make_core_texture(filename):
    """The chest core: a radial gradient — white-hot centre through
    orange to a dark red-brown rim, with speckle and bright flecks,
    so the disc reads as a hot reactor behind the grill slats."""
    S = SMALL_SIZE
    rng = np.random.default_rng(503)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    r = np.sqrt((xx - S / 2) ** 2 + (yy - S / 2) ** 2) / (S / 2)  # 0 centre
    centre = np.array((1.00, 0.96, 0.86), dtype=np.float32)
    mid = np.array((1.00, 0.45, 0.10), dtype=np.float32)
    rim = np.array((0.22, 0.05, 0.02), dtype=np.float32)
    t1 = np.clip(r / 0.45, 0.0, 1.0)[..., None]
    t2 = np.clip((r - 0.45) / 0.55, 0.0, 1.0)[..., None]
    arr = centre * (1.0 - t1) + mid * t1
    arr = arr * (1.0 - t2) + rim * t2
    speckle = rng.uniform(0.90, 1.05, (S, S)).astype(np.float32)
    arr *= speckle[..., None]
    flecks = (rng.uniform(0, 1, (S, S)) > 0.997).astype(np.float32)
    arr *= (1.0 + flecks[..., None] * 0.6)
    arr = np.clip(arr, 0.0, 1.0)
    return _save_image(arr, filename)


def apply_textures(textured_materials):
    """Generate one texture per textured material, plug it into the
    material's Base Color, and set the base colour to white so
    Unity's (map x colour) multiply returns exactly the texture.
    Returns a list of (material name, filepath, bytes)."""
    info = []
    for mat in textured_materials:
        filename = TEXTURE_NAMES[mat.name]
        if mat.name == "AresGunmetal":
            img = make_metal_texture(GUNMETAL, 619, filename,
                                     ATLAS_SIZE, 64, 32)
        elif mat.name == "AresHeat":
            img = make_heat_texture(filename)
        elif mat.name == "AresHazard":
            img = make_hazard_texture(filename)
        elif mat.name == "AresGlowCyan":
            img = make_glow_texture(GLOW_CYAN, 607, filename)
        elif mat.name == "AresGlowRed":
            img = make_glow_texture(GLOW_RED, 613, filename)
        elif mat.name == "AresGlowCore":
            img = make_core_texture(filename)
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
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=OUTPUT_FBX,
        use_selection=True,
        object_types={"MESH", "EMPTY"},
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
    mesh_count = 0
    print("A.R.E.S.-09 v2 build:")
    for obj in objects:
        if obj.type != "MESH":
            continue
        mesh_count += 1
        mesh = obj.data
        mesh.calc_loop_triangles()
        total_tris += len(mesh.loop_triangles)
    print("  objects=%d (meshes=%d)  total_tris=%d"
          % (len(objects), mesh_count, total_tris))
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
    """Checks on the built model (Blender coords)."""
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
    check(800 <= total_tris <= 9000,
          "total tris %d (spec 800-9000)" % total_tris)
    pts = [o.matrix_world @ v.co for o in objects if o.type == "MESH"
           for v in o.data.vertices]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    check(abs(min(xs)) <= 1.20 and abs(max(xs)) <= 1.20,
          "footprint x in +/-1.20 (got [%.2f, %.2f])" % (min(xs), max(xs)))
    check(abs(min(ys)) <= 1.20 and abs(max(ys)) <= 1.20,
          "footprint y in +/-1.20 (got [%.2f, %.2f])" % (min(ys), max(ys)))
    check(abs(min(zs)) < 1e-4, "flat-based at z=0 (min z %.3f)" % min(zs))
    check(2.85 <= max(zs) <= MAX_HEIGHT,
          "height %.2f (spec 2.85-%.2f)" % (max(zs), MAX_HEIGHT))
    # Controller-critical names must exist.
    critical = ("Ares09", "Hips", "Torso", "Head", "LegL_Hip", "LegR_Hip",
                "LegL_Knee", "LegR_Knee", "CoreChest", "Visor", "SweepLens")
    missing = [n for n in critical if n not in by_name]
    check(not missing, "controller-critical names present (missing: %s)"
          % (", ".join(missing) if missing else "none"))
    # Rig parenting: the joints must hang off the right parents.
    def parent_name(name):
        obj = by_name.get(name)
        return obj.parent.name if obj is not None and obj.parent else None
    check(parent_name("Torso") == "Hips", "Torso parented to Hips")
    check(parent_name("Head") == "Torso", "Head parented to Torso")
    check(parent_name("LegL_Hip") == "Hips", "LegL_Hip parented to Hips")
    check(parent_name("LegL_Knee") == "LegL_Hip",
          "LegL_Knee parented to LegL_Hip")
    check(parent_name("LegR_Knee") == "LegR_Hip",
          "LegR_Knee parented to LegR_Hip")
    check(parent_name("CoreChest") == "Torso", "CoreChest parented to Torso")
    check(parent_name("Visor") == "Head", "Visor parented to Head")
    check(parent_name("SweepLens") == "ShoulderL",
          "SweepLens parented to ShoulderL")
    # V2 detail: present in number and riding the right joints.
    v2_parts = [o for o in objects if o.name.startswith("AresV2_")]
    check(len(v2_parts) >= 30,
          "v2 detail parts: %d (spec >= 30)" % len(v2_parts))
    check(parent_name("AresV2_UpperArmL") == "ShoulderL",
          "AresV2_UpperArmL parented to ShoulderL")
    check(parent_name("AresV2_ForearmR") == "ShoulderR",
          "AresV2_ForearmR parented to ShoulderR")
    check(parent_name("AresV2_PecPlateL") == "Torso",
          "AresV2_PecPlateL parented to Torso")
    check(parent_name("AresV2_ThighPlateL") == "LegL_Hip",
          "AresV2_ThighPlateL parented to LegL_Hip")
    check(parent_name("AresV2_ShinGuardR") == "LegR_Knee",
          "AresV2_ShinGuardR parented to LegR_Knee")
    check(parent_name("AresV2_SensorPodL") == "Head",
          "AresV2_SensorPodL parented to Head")
    check(parent_name("AresV2_PowerPack") == "Torso",
          "AresV2_PowerPack parented to Torso")
    # Visor and core must face +X (the model's front).
    visor = by_name.get("Visor")
    if visor is not None:
        vx = visor.matrix_world.translation.x
        check(vx > 0.20, "visor on the front (world x %.2f)" % vx)
    core = by_name.get("CoreChest")
    if core is not None:
        cx = core.matrix_world.translation.x
        check(cx > 0.40, "core on the chest front (world x %.2f)" % cx)
    # V3 definition: present in number and riding the right joints.
    v3_parts = [o for o in objects if o.name.startswith("AresV3_")]
    check(len(v3_parts) >= 60,
          "v3 definition parts: %d (spec >= 60)" % len(v3_parts))
    check(parent_name("AresV3_RotaryHousing") == "ShoulderR",
          "AresV3_RotaryHousing parented to ShoulderR")
    check(parent_name("AresV3_MissilePod") == "ShoulderL",
          "AresV3_MissilePod parented to ShoulderL")
    check(parent_name("AresV3_ChestSlat_1") == "Torso",
          "AresV3_ChestSlat_1 parented to Torso")
    check(parent_name("AresV3_ThighFrontL") == "LegL_Hip",
          "AresV3_ThighFrontL parented to LegL_Hip")
    check(parent_name("AresV3_CalfSleeveR") == "LegR_Knee",
          "AresV3_CalfSleeveR parented to LegR_Knee")
    check(parent_name("AresV3_BrowGuard") == "Head",
          "AresV3_BrowGuard parented to Head")
    tips = [o for o in objects
            if o.name.startswith("AresV3_MissileTip_")]
    check(len(tips) == 6,
          "missile glow tips: %d (spec 6)" % len(tips))
    # Every mesh carries a material (no untextured grey in Unity).
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
