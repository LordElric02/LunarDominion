# BuildMapSitesV3.py — Lunar Dominion: map site models,
# spruce-up pass (Patrick 2026-11-12, after the terrain V3
# close-up: resource nodes read bland, and the enemy camp
# showed bare moon surface in the gaps between its parts).
#
# WHAT CHANGED vs V2: every site now sits on a solid rock
# MOUND that covers its whole footprint (no bare ground
# inside a site, ever) with rubble blended around the rim,
# and each model gains detail:
#   BiomassNodeV3 — 7 emerald shards (one hero shard), 5
#     pods, glowing crystal chips on a green-dark mound.
#   AlloyNodeV3   — 8 silver blades, half-buried ingots,
#     more rubble, on a bigger mound.
#   HeliumNodeV3  — rock ring tightened to 8 near-touching
#     boulders + an inner stone row, taller vent, and a
#     glowing cyan pool so the centre reads solid light.
#   EnemyCampV3   — 7 thick spires (centre + tight ring)
#     planted in a dark purple mound, 4 eggs nestled
#     between them, bigger ember core + ember chips.
# The boss lair is NOT rebuilt (Patrick didn't ask; the V2
# lair stays as installed).
#
# WHAT IT GENERATES (next to this script; install by
# renaming each over the existing file in Resources/Models,
# e.g. BiomassNodeV3.fbx -> BiomassNode.fbx):
#   BiomassNodeV3.fbx / AlloyNodeV3.fbx / HeliumNodeV3.fbx
#   EnemyCampV3.fbx
#
# Same authoring scale as V2 (ExteriorView instantiates at
# 2x): ~6 units wide, ground at z=0, radially composed.
# Coloured materials only, no textures.
#
# Run headless:
#   blender -b --python BuildMapSitesV3.py
# Prints VERIFY: ALL PASS or the failing checks.

import bpy
import math
from mathutils import Matrix
import os
import random

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

OBJECTS = []


def mat(name, color, emission=None, strength=0.0, roughness=0.85, metallic=0.0):
    m = bpy.data.materials.new(name=name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength
    return m


BASALT = mat("Basalt", (0.16, 0.17, 0.19), roughness=0.95)
ROCK_GREY = mat("RockGrey", (0.30, 0.31, 0.34), roughness=0.95)
EMERALD = mat("Emerald", (0.10, 0.55, 0.25), emission=(0.15, 0.95, 0.35), strength=1.6, roughness=0.3)
POD_GREEN = mat("PodGreen", (0.05, 0.25, 0.10), roughness=0.35)
SILVER = mat("AlloySilver", (0.72, 0.78, 0.85), roughness=0.35, metallic=0.85)
VENT_DARK = mat("VentDark", (0.10, 0.10, 0.12), roughness=0.7, metallic=0.4)
CYAN_GLOW = mat("HeliumGlow", (0.35, 0.85, 0.95), emission=(0.30, 0.95, 1.0), strength=2.2, roughness=0.3)
CYAN_SHARD = mat("HeliumShard", (0.30, 0.75, 0.90), emission=(0.25, 0.85, 1.0), strength=1.4, roughness=0.3)
SPIRE = mat("CampSpire", (0.07, 0.05, 0.11), roughness=0.9)
EMBER = mat("CampEmber", (0.45, 0.12, 0.05), emission=(1.0, 0.30, 0.10), strength=1.8, roughness=0.5)
EGG = mat("CampEgg", (0.12, 0.08, 0.16), roughness=0.45)
BLACK = mat("LairBlack", (0.01, 0.01, 0.012), roughness=1.0)
AMBER = mat("LairAmber", (0.55, 0.32, 0.08), emission=(1.0, 0.62, 0.15), strength=1.8, roughness=0.5)
BIO_ROCK = mat("BioRock", (0.13, 0.17, 0.13), roughness=0.95)
CAMP_ROCK = mat("CampRock", (0.11, 0.09, 0.15), roughness=0.95)


def _finish(obj, material, flat=False):
    if material is not None:
        obj.data.materials.append(material)
    if flat:
        for p in obj.data.polygons:
            p.use_smooth = False
    OBJECTS.append(obj)
    return obj


def _snapshot():
    return set(bpy.data.objects)


def _created(before):
    # The object a primitive-add just made, found through bpy.data.
    # Reading the active object back through the context raises
    # AttributeError in Blender 5.2 GUI Text Editor runs in
    # Patrick's setup (hit 2026-11-12), so avoid the context here.
    new = [o for o in bpy.data.objects if o not in before]
    return new[0]


def _bake_scale(obj):
    # transform_apply without the operator (same reason): bake the
    # object's scale into its mesh data along the local axes.
    sx, sy, sz = obj.scale
    if (sx, sy, sz) != (1.0, 1.0, 1.0):
        obj.data.transform(Matrix(((sx, 0, 0, 0), (0, sy, 0, 0), (0, 0, sz, 0), (0, 0, 0, 1))))
        obj.scale = (1.0, 1.0, 1.0)


def ico(name, loc, scale, material, subdiv=1, flat=True):
    before = _snapshot()
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdiv, radius=1.0, location=loc)
    obj = _created(before)
    obj.name = name
    obj.scale = scale
    _bake_scale(obj)
    return _finish(obj, material, flat)


def sphere(name, loc, scale, material):
    before = _snapshot()
    bpy.ops.mesh.primitive_uv_sphere_add(radius=1.0, location=loc)
    obj = _created(before)
    obj.name = name
    obj.scale = scale
    _bake_scale(obj)
    return _finish(obj, material)


def box(name, loc, scale, material, rot=(0.0, 0.0, 0.0)):
    before = _snapshot()
    bpy.ops.mesh.primitive_cube_add(location=loc)
    obj = _created(before)
    obj.name = name
    obj.scale = scale
    obj.rotation_euler = rot
    _bake_scale(obj)
    return _finish(obj, material, flat=True)


def cone(name, loc, radius, depth, material, rot=(0.0, 0.0, 0.0), vertices=7):
    before = _snapshot()
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=radius, radius2=0.0, depth=depth, location=loc)
    obj = _created(before)
    obj.name = name
    obj.rotation_euler = rot
    return _finish(obj, material, flat=True)


def cyl(name, loc, radius, depth, material, vertices=20):
    before = _snapshot()
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=loc)
    obj = _created(before)
    obj.name = name
    return _finish(obj, material)


def build_biomass(rng):
    # Solid green-dark mound first (fills the footprint).
    ico("Bio_Mound", (0, 0, 0.30), (3.0, 3.0, 0.85), BIO_ROCK, subdiv=2)
    # Hero shard + ring of six.
    hero = ico("Bio_ShardHero", (0, 0, 1.95), (0.48, 0.48, 1.95), EMERALD, subdiv=1)
    hero.rotation_euler = (0.10, -0.06, 0.0)
    for i in range(6):
        ang = i * 1.05 + 0.35
        r = rng.uniform(1.15, 1.55)
        length = rng.uniform(1.7, 2.7)
        shard = ico("Bio_Shard%d" % i, (math.cos(ang) * r, math.sin(ang) * r, length * 0.50 + 0.35),
                    (0.36, 0.36, length * 0.5), EMERALD, subdiv=1)
        shard.rotation_euler = (math.sin(ang) * 0.38, -math.cos(ang) * 0.38, 0.0)
    for i in range(5):
        ang = i * 1.35 + 0.9
        sphere("Bio_Pod%d" % i, (math.cos(ang) * 1.95, math.sin(ang) * 1.95, 0.55), (0.44, 0.44, 0.56), POD_GREEN)
    # Glowing chips sprinkled on the mound.
    for i in range(6):
        ang = i * 1.13 + 0.2
        r = rng.uniform(1.9, 2.5)
        ico("Bio_Chip%d" % i, (math.cos(ang) * r, math.sin(ang) * r, 0.42), (0.20, 0.20, 0.30), EMERALD, subdiv=1)
    # Rim rubble to blend the mound into the ground.
    for i in range(5):
        ang = i * 1.45 + 0.6
        ico("Bio_Rubble%d" % i, (math.cos(ang) * 3.1, math.sin(ang) * 3.1, 0.18), (0.45, 0.45, 0.30), BIO_ROCK, subdiv=1)


def build_alloy(rng):
    ico("Alloy_Mound", (0, 0, 0.32), (3.1, 3.1, 0.95), ROCK_GREY, subdiv=2)
    for i in range(8):
        ang = i * 0.79 + 0.25
        r = 0.45 if i == 0 else rng.uniform(1.0, 1.5)
        length = rng.uniform(1.8, 3.1)
        box("Alloy_Shard%d" % i,
            (math.cos(ang) * r, math.sin(ang) * r, length * 0.50 + 0.30),
            (0.34, 0.17, length * 0.5), SILVER,
            rot=(math.sin(ang) * 0.55, -math.cos(ang) * 0.55, ang))
    # Half-buried ingots.
    for i in range(3):
        ang = i * 2.2 + 1.1
        box("Alloy_Ingot%d" % i,
            (math.cos(ang) * 2.15, math.sin(ang) * 2.15, 0.30),
            (0.55, 0.32, 0.22), SILVER, rot=(0.25, 0.0, ang))
    for i in range(6):
        ang = i * 1.12 + 0.5
        box("Alloy_Rubble%d" % i,
            (math.cos(ang) * 2.6, math.sin(ang) * 2.6, 0.22),
            (0.42, 0.30, 0.24), ROCK_GREY, rot=(0.0, 0.0, ang))


def build_helium(rng):
    ico("He_Mound", (0, 0, 0.26), (2.9, 2.9, 0.75), BASALT, subdiv=2)
    # Outer ring: 8 boulders close enough to nearly touch.
    for i in range(8):
        ang = i * math.pi / 4.0 + 0.20
        ico("He_Rock%d" % i, (math.cos(ang) * 1.95, math.sin(ang) * 1.95, 0.55), (0.95, 0.95, 0.78), BASALT, subdiv=1)
    # Inner row of smaller stones.
    for i in range(5):
        ang = i * 1.26 + 0.7
        ico("He_Stone%d" % i, (math.cos(ang) * 1.25, math.sin(ang) * 1.25, 0.40), (0.55, 0.55, 0.45), BASALT, subdiv=1)
    cyl("He_Vent", (0, 0, 0.65), 0.85, 1.3, VENT_DARK)
    # Glowing pool: a solid disc of light at the vent's mouth.
    cyl("He_Pool", (0, 0, 1.28), 0.78, 0.10, CYAN_GLOW)
    cyl("He_Core", (0, 0, 1.05), 0.55, 0.5, CYAN_GLOW)
    for i in range(5):
        ang = i * 1.35 + 0.8
        length = rng.uniform(1.2, 2.0)
        shard = ico("He_Shard%d" % i, (math.cos(ang) * 1.45, math.sin(ang) * 1.45, length * 0.38 + 0.25), (0.32, 0.32, length * 0.5), CYAN_SHARD, subdiv=1)
        shard.rotation_euler = (math.sin(ang) * 0.42, -math.cos(ang) * 0.42, 0.0)


def build_camp(rng):
    # Solid dark mound — the whole camp stands on it.
    ico("Camp_Mound", (0, 0, 0.32), (3.2, 3.2, 0.95), CAMP_ROCK, subdiv=2)
    # Centre spire + a tight ring of six, bases overlapping.
    cone("Camp_SpireC", (0, 0, 2.45), 1.05, 4.9, SPIRE,
         rot=(0.06, -0.04, 0.0))
    for i in range(6):
        ang = i * 1.05 + 0.35
        h = rng.uniform(2.4, 3.6)
        cone("Camp_Spire%d" % i,
             (math.cos(ang) * 1.55, math.sin(ang) * 1.55, h * 0.5 + 0.35),
             rng.uniform(0.85, 1.05), h, SPIRE,
             rot=(math.sin(ang) * 0.16, -math.cos(ang) * 0.16, 0.0))
    # Ember heart + chips, nestled at the spires' feet.
    ico("Camp_Core", (0.75, -0.7, 0.80), (0.62, 0.62, 0.62), EMBER, subdiv=1)
    for i in range(3):
        ang = i * 2.3 + 0.4
        ico("Camp_Ember%d" % i, (math.cos(ang) * 1.05, math.sin(ang) * 1.05, 0.62), (0.26, 0.26, 0.26), EMBER, subdiv=1)
    for i in range(4):
        ang = i * 1.62 + 1.15
        sphere("Camp_Egg%d" % i, (math.cos(ang) * 2.15, math.sin(ang) * 2.15, 0.62), (0.44, 0.44, 0.62), EGG)
    # Rim rocks on the mound edge.
    for i in range(5):
        ang = i * 1.38 + 0.8
        ico("Camp_Rock%d" % i, (math.cos(ang) * 2.95, math.sin(ang) * 2.95, 0.35), (0.65, 0.65, 0.48), CAMP_ROCK, subdiv=1)


def build_lair(rng):
    # Rock arc around a black cave mouth that faces Blender -Y
    # (Unity +Z — the side the War Robot stands on).
    arc = [(-3.4, 0.8, 2.6), (-2.2, 1.8, 3.4), (0.0, 2.4, 3.9), (2.2, 1.8, 3.4), (3.4, 0.8, 2.6),
           (-3.0, -0.6, 1.9), (3.0, -0.6, 1.9), (0.0, 3.4, 2.6)]
    for i, (x, y, s) in enumerate(arc):
        ico("Lair_Rock%d" % i, (x, y, s * 0.38), (s * 0.62, s * 0.62, s * 0.55), BASALT, subdiv=2)
    # The opening: a black disc, recessed into the arc.
    before = _snapshot()
    bpy.ops.mesh.primitive_circle_add(vertices=24, radius=1.9, fill_type="NGON", location=(0, -0.9, 1.55))
    disc = _created(before)
    disc.name = "Lair_Mouth"
    disc.rotation_euler = (math.radians(90), 0.0, 0.0)
    disc.scale = (1.0, 1.25, 1.0)
    _finish(disc, BLACK, flat=True)
    # Amber light strips flanking the mouth.
    box("Lair_StripL", (-2.15, -0.75, 1.1), (0.10, 0.10, 1.0), AMBER)
    box("Lair_StripR", (2.15, -0.75, 1.1), (0.10, 0.10, 1.0), AMBER)
    # Stalagmites by the entrance.
    cone("Lair_StagL", (-2.8, -1.8, 0.8), 0.5, 1.6, BASALT, vertices=8)
    cone("Lair_StagR", (2.9, -1.6, 0.65), 0.42, 1.3, BASALT, vertices=8)


BUILDERS = [
    ("BiomassNodeV3", build_biomass),
    ("AlloyNodeV3", build_alloy),
    ("HeliumNodeV3", build_helium),
    ("EnemyCampV3", build_camp),
]


def export_model(name):
    # use_selection=False on purpose (Blender 5.2 GUI exporter
    # AttributeError on context.selected_objects — see
    # BuildExteriorTerrainV2.py). At export time the scene holds
    # ONLY this model's objects (each model is deleted right
    # after its export), so exporting everything is exact.
    path = os.path.join(SCRIPT_DIR, name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=False,
        object_types={"MESH"},
        use_mesh_modifiers=True,
        apply_scale_options="FBX_SCALE_ALL",
        apply_unit_scale=True,
        axis_forward="-Z",
        axis_up="Y",
    )
    return path


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # Materials were created at import time against the old
    # scene's data; recreate them after the reset.
    global BASALT, ROCK_GREY, EMERALD, POD_GREEN, SILVER, VENT_DARK
    global CYAN_GLOW, CYAN_SHARD, SPIRE, EMBER, EGG, BLACK, AMBER
    global BIO_ROCK, CAMP_ROCK
    BASALT = mat("Basalt", (0.16, 0.17, 0.19), roughness=0.95)
    ROCK_GREY = mat("RockGrey", (0.30, 0.31, 0.34), roughness=0.95)
    EMERALD = mat("Emerald", (0.10, 0.55, 0.25), emission=(0.15, 0.95, 0.35), strength=1.6, roughness=0.3)
    POD_GREEN = mat("PodGreen", (0.05, 0.25, 0.10), roughness=0.35)
    SILVER = mat("AlloySilver", (0.72, 0.78, 0.85), roughness=0.35, metallic=0.85)
    VENT_DARK = mat("VentDark", (0.10, 0.10, 0.12), roughness=0.7, metallic=0.4)
    CYAN_GLOW = mat("HeliumGlow", (0.35, 0.85, 0.95), emission=(0.30, 0.95, 1.0), strength=2.2, roughness=0.3)
    CYAN_SHARD = mat("HeliumShard", (0.30, 0.75, 0.90), emission=(0.25, 0.85, 1.0), strength=1.4, roughness=0.3)
    SPIRE = mat("CampSpire", (0.07, 0.05, 0.11), roughness=0.9)
    EMBER = mat("CampEmber", (0.45, 0.12, 0.05), emission=(1.0, 0.30, 0.10), strength=1.8, roughness=0.5)
    EGG = mat("CampEgg", (0.12, 0.08, 0.16), roughness=0.45)
    BLACK = mat("LairBlack", (0.01, 0.01, 0.012), roughness=1.0)
    AMBER = mat("LairAmber", (0.55, 0.32, 0.08), emission=(1.0, 0.62, 0.15), strength=1.8, roughness=0.5)
    BIO_ROCK = mat("BioRock", (0.13, 0.17, 0.13), roughness=0.95)
    CAMP_ROCK = mat("CampRock", (0.11, 0.09, 0.15), roughness=0.95)

    results = {}
    for name, builder in BUILDERS:
        del OBJECTS[:]
        rng = random.Random(hash(name) & 0xFFFF)
        builder(rng)
        tris = sum(len(o.data.polygons) for o in OBJECTS)
        # Bounds across the model's objects (world = local here).
        xs, ys, zs = [], [], []
        for o in OBJECTS:
            for corner in o.bound_box:
                v = o.matrix_world @ __import__("mathutils").Vector(corner)
                xs.append(v.x)
                ys.append(v.y)
                zs.append(v.z)
        results[name] = (len(OBJECTS), tris, max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs), min(zs))
        path = export_model(name)
        print("Exported %s: %d objects" % (name, len(OBJECTS)))
        for o in list(OBJECTS):
            bpy.data.objects.remove(o, do_unlink=True)
    ok = True
    for name, (count, tris, w, d, h, zmin) in results.items():
        good = count >= 3 and w <= 10.5 and d <= 10.5 and 1.5 <= h <= 6.5 and -1.0 <= zmin <= 0.6
        print("  %s %s: objects=%d tris=%d w=%.1f d=%.1f h=%.1f zmin=%.2f" % ("PASS" if good else "FAIL", name, count, tris, w, d, h, zmin))
        ok = ok and good
    print("VERIFY: " + ("ALL PASS" if ok else "FAILURES PRESENT"))


main()
