# BuildMapSitesV5.py - ALIEN TWILIGHT SITE REDESIGN
# (Patrick 2026-11-15: after the V6 terrain landed, "the
# resource nodes/enemy camps need a redesign to this new
# look"). Fork of V4: same install names, same footprints,
# same authoring scales (nodes are placed at 2x by
# ExteriorView.BuildMapPoints; camp slot structures at 1x),
# so no code changes ride along. What changes is the look:
#   BiomassNodeV5 - a living crystal bloom: fleshy mound,
#     bigger emerald shards around a glowing core, spore
#     pods and teal glow-fruit. (In V4 the node blended into
#     the V6 terrain's ambient flora crystals; scale + the
#     bright core separate it.)
#   AlloyNodeV5   - a rust-metal ore eruption: copper blades
#     and ingots in dark rust rock, hot ember glints - ties
#     to the biome's rust flats (was silver-on-grey).
#   HeliumNodeV5  - pale spires grown taller around the vent,
#     with a halo of spilled cyan light on the mound.
#   EnemyCampT1V5 - Spore Nest: 6 spires, teal bioluminescent
#     sacs and glow-fruit added to the ember core design.
#   EnemyCampT2V5 - Stalker Spires: teal glow nodules join
#     the ember sacs; a fifth ground spike.
#   EnemyCampT3V5 - Tyrant Maw: teal sacs between the fangs
#     and an outer ring of small spires.
# The lair is NOT rebuilt. Install: rename each export over
# the existing file in Resources/Models (BiomassNodeV5.fbx ->
# BiomassNode.fbx, EnemyCampT1V5.fbx -> EnemyCampT1.fbx, ...).
# The V4/V3 notes follow below.
#
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
EMERALD = mat("Emerald", (0.10, 0.55, 0.25), emission=(0.15, 0.95, 0.35), strength=2.0, roughness=0.3)
POD_GREEN = mat("PodGreen", (0.05, 0.25, 0.10), roughness=0.35)
SILVER = mat("AlloySilver", (0.72, 0.78, 0.85), roughness=0.35, metallic=0.85)
VENT_DARK = mat("VentDark", (0.10, 0.10, 0.12), roughness=0.7, metallic=0.4)
CYAN_GLOW = mat("HeliumGlow", (0.35, 0.85, 0.95), emission=(0.30, 0.95, 1.0), strength=2.2, roughness=0.3)
CYAN_SHARD = mat("HeliumShard", (0.30, 0.75, 0.90), emission=(0.25, 0.85, 1.0), strength=1.4, roughness=0.3)
SPIRE = mat("CampSpire", (0.07, 0.05, 0.11), roughness=0.9)
EMBER = mat("CampEmber", (0.45, 0.12, 0.05), emission=(1.0, 0.30, 0.10), strength=2.2, roughness=0.5)
EGG = mat("CampEgg", (0.12, 0.08, 0.16), roughness=0.45)
BLACK = mat("LairBlack", (0.01, 0.01, 0.012), roughness=1.0)
AMBER = mat("LairAmber", (0.55, 0.32, 0.08), emission=(1.0, 0.62, 0.15), strength=1.8, roughness=0.5)
BIO_ROCK = mat("BioRock", (0.13, 0.17, 0.13), roughness=0.95)
CAMP_ROCK = mat("CampRock", (0.11, 0.09, 0.15), roughness=0.95)
TEAL_GLOW = mat("TealGlow", (0.30, 0.95, 0.85), emission=(0.25, 0.95, 0.82), strength=2.4, roughness=0.35)
BIO_FLESH = mat("BioFlesh", (0.16, 0.21, 0.13), roughness=0.5)
COPPER = mat("AlloyCopper", (0.62, 0.30, 0.14), roughness=0.38, metallic=0.9)
RUST_ROCK = mat("RustRock", (0.27, 0.155, 0.105), roughness=0.95)


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
    # V5: a living crystal bloom. Fleshy mound first.
    ico("Bio_Mound", (0, 0, 0.30), (3.1, 3.1, 0.95), BIO_FLESH, subdiv=2)
    # Glowing core nestled between the shards.
    sphere("Bio_Core", (0, 0, 0.95), (0.55, 0.55, 0.55), EMERALD)
    # Hero shard + ring of seven, all larger than V3/V4 so the
    # node out-scales the terrain's ambient flora crystals.
    hero = ico("Bio_ShardHero", (0, 0, 2.30), (0.55, 0.55, 2.30), EMERALD, subdiv=1)
    hero.rotation_euler = (0.10, -0.06, 0.0)
    for i in range(7):
        ang = i * 0.90 + 0.35
        r = rng.uniform(1.20, 1.60)
        length = rng.uniform(2.0, 3.2)
        shard = ico("Bio_Shard%d" % i, (math.cos(ang) * r, math.sin(ang) * r, length * 0.50 + 0.35),
                    (0.38, 0.38, length * 0.5), EMERALD, subdiv=1)
        shard.rotation_euler = (math.sin(ang) * 0.38, -math.cos(ang) * 0.38, 0.0)
    for i in range(5):
        ang = i * 1.35 + 0.9
        sphere("Bio_Pod%d" % i, (math.cos(ang) * 2.0, math.sin(ang) * 2.0, 0.55), (0.46, 0.46, 0.58), POD_GREEN)
    # Teal glow-fruit on the mound's rim - the biome's signature.
    for i in range(3):
        ang = i * 2.2 + 0.4
        ico("Bio_Fruit%d" % i, (math.cos(ang) * 2.4, math.sin(ang) * 2.4, 0.52), (0.30, 0.30, 0.36), TEAL_GLOW, subdiv=1)
    # Glowing chips sprinkled on the mound.
    for i in range(8):
        ang = i * 0.83 + 0.2
        r = rng.uniform(1.9, 2.6)
        ico("Bio_Chip%d" % i, (math.cos(ang) * r, math.sin(ang) * r, 0.42), (0.20, 0.20, 0.30), EMERALD, subdiv=1)
    # Rim rubble to blend the mound into the ground.
    for i in range(5):
        ang = i * 1.45 + 0.6
        ico("Bio_Rubble%d" % i, (math.cos(ang) * 3.15, math.sin(ang) * 3.15, 0.18), (0.45, 0.45, 0.30), BIO_ROCK, subdiv=1)


def build_alloy(rng):
    # V5: a rust-metal ore eruption - copper blades in dark
    # rust rock, two silver blades kept as accents.
    ico("Alloy_Mound", (0, 0, 0.32), (3.2, 3.2, 1.0), RUST_ROCK, subdiv=2)
    for i in range(9):
        ang = i * 0.70 + 0.25
        r = 0.45 if i == 0 else rng.uniform(1.0, 1.55)
        length = rng.uniform(2.0, 3.4)
        blade_mat = SILVER if i in (0, 4) else COPPER
        box("Alloy_Shard%d" % i,
            (math.cos(ang) * r, math.sin(ang) * r, length * 0.50 + 0.30),
            (0.36, 0.18, length * 0.5), blade_mat,
            rot=(math.sin(ang) * 0.55, -math.cos(ang) * 0.55, ang))
    # Half-buried copper ingots.
    for i in range(3):
        ang = i * 2.2 + 1.1
        box("Alloy_Ingot%d" % i,
            (math.cos(ang) * 2.15, math.sin(ang) * 2.15, 0.30),
            (0.55, 0.32, 0.22), COPPER, rot=(0.25, 0.0, ang))
    # Hot glints - small ember chips glowing on the mound.
    for i in range(4):
        ang = i * 1.7 + 0.8
        r = rng.uniform(1.6, 2.4)
        box("Alloy_Glint%d" % i,
            (math.cos(ang) * r, math.sin(ang) * r, 0.78),
            (0.22, 0.14, 0.10), EMBER, rot=(0.0, 0.0, ang))
    for i in range(6):
        ang = i * 1.12 + 0.5
        box("Alloy_Rubble%d" % i,
            (math.cos(ang) * 2.65, math.sin(ang) * 2.65, 0.22),
            (0.42, 0.30, 0.24), RUST_ROCK, rot=(0.0, 0.0, ang))


def build_helium(rng):
    # V5: the vent stays, but the pale spires grow tall around
    # it and a halo of cyan light spills across the mound top.
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
    # Halo of spilled light around the vent.
    cyl("He_Halo", (0, 0, 0.80), 1.70, 0.07, CYAN_GLOW)
    # Three tall spires + two short shards, leaning outward.
    for i, length in enumerate((2.9, 3.5, 4.0)):
        ang = i * 2.1 + 0.8
        shard = ico("He_Spire%d" % i, (math.cos(ang) * 1.40, math.sin(ang) * 1.40, length * 0.38 + 0.30),
                    (0.34, 0.34, length * 0.5), CYAN_SHARD, subdiv=1)
        shard.rotation_euler = (math.sin(ang) * 0.40, -math.cos(ang) * 0.40, 0.0)
    for i in range(2):
        ang = i * 2.6 + 2.0
        length = rng.uniform(1.3, 1.7)
        shard = ico("He_Shard%d" % i, (math.cos(ang) * 1.50, math.sin(ang) * 1.50, length * 0.38 + 0.25),
                    (0.32, 0.32, length * 0.5), CYAN_SHARD, subdiv=1)
        shard.rotation_euler = (math.sin(ang) * 0.42, -math.cos(ang) * 0.42, 0.0)


def _lean_rot(a, theta):
    # Euler tipping a +Z cone toward the centre when placed at
    # ring angle a (theta > 0 leans inward, < 0 outward).
    return (math.sin(a) * math.sin(theta), -math.cos(a) * math.sin(theta), 0.0)


def _lean_centre(a, r, h, theta):
    # Cone centre so its base sits on the ring point at angle a
    # while the cone leans by theta (see _lean_rot).
    return (math.cos(a) * r - math.cos(a) * math.sin(theta) * h * 0.5,
            math.sin(a) * r - math.sin(a) * math.sin(theta) * h * 0.5,
            math.cos(theta) * h * 0.5)


def build_camp_t1(rng):
    # SPORE NEST (Tier 1) V5: six spires leaning over the ember
    # core, teal bioluminescent sacs grown onto the spires and
    # glow-fruit on the skirt - the camp joins the biome.
    ico("Nest_Mound", (0, 0, 0.55), (6.8, 6.8, 1.6), CAMP_ROCK, subdiv=2)
    for i in range(6):
        a = i * (2.0 * math.pi / 6.0) + 0.4
        h = 5.5 + (i % 3) * 1.1
        cone("Nest_Spire%d" % i, _lean_centre(a, 3.6, h, 0.21), 1.5, h, SPIRE,
             rot=_lean_rot(a, 0.21), vertices=7)
    sphere("Nest_Core", (0, 0, 1.95), (1.5, 1.5, 0.8), EMBER)
    for i in range(3):
        a = i * 2.1 + 0.9
        ico("Nest_Ember%d" % i, (math.cos(a) * 2.3, math.sin(a) * 2.3, 1.75), (0.42, 0.42, 0.42), EMBER, subdiv=1)
    # Teal sacs clinging to the spire ring.
    for i in range(3):
        a = i * 2.1 + 1.5
        sphere("Nest_Sac%d" % i, (math.cos(a) * 3.1, math.sin(a) * 3.1, 2.6), (0.62, 0.62, 0.78), TEAL_GLOW)
    for i in range(4):
        a = i * (math.pi / 2.0) + 0.8
        sphere("Nest_Egg%d" % i, (math.cos(a) * 5.6, math.sin(a) * 5.6, 0.85), (0.95, 0.95, 1.3), EGG)
    # Glow-fruit scattered on the skirt.
    for i in range(4):
        a = i * 1.7 + 0.3
        ico("Nest_Fruit%d" % i, (math.cos(a) * 5.9, math.sin(a) * 5.9, 0.70), (0.28, 0.28, 0.34), TEAL_GLOW, subdiv=1)
    for i in range(3):
        a = i * 2.2 + 0.2
        ico("Nest_Rock%d" % i, (math.cos(a) * 7.4, math.sin(a) * 7.4, 0.35), (1.05, 0.9, 0.6), CAMP_ROCK, subdiv=1)


def build_camp_t2(rng):
    # STALKER SPIRES (Tier 2) V5: the three blades and ember
    # sacs stay; teal glow nodules grow on the mound and a
    # third sac joins in teal.
    ico("Stalk_Mound", (0, 0, 0.45), (6.2, 6.2, 1.3), CAMP_ROCK, subdiv=2)
    for i, h in enumerate((12.0, 13.5, 14.5)):
        a = math.radians(90 + i * 120)
        cone("Stalk_Blade%d" % i, _lean_centre(a, 2.6, h, -0.14), 1.15, h, SPIRE,
             rot=_lean_rot(a, -0.14), vertices=6)
    for i, a in enumerate((math.radians(150), math.radians(300))):
        cyl("Stalk_Stalk%d" % i, (math.cos(a) * 4.6, math.sin(a) * 4.6, 1.1), 0.16, 2.2, SPIRE, vertices=8)
        sphere("Stalk_Sac%d" % i, (math.cos(a) * 4.6, math.sin(a) * 4.6, 2.45), (0.85, 0.85, 1.0), EMBER)
    # A third stalk, this one teal.
    a = math.radians(30)
    cyl("Stalk_Stalk2", (math.cos(a) * 4.6, math.sin(a) * 4.6, 1.1), 0.16, 2.2, SPIRE, vertices=8)
    sphere("Stalk_Sac2", (math.cos(a) * 4.6, math.sin(a) * 4.6, 2.45), (0.80, 0.80, 0.95), TEAL_GLOW)
    # Teal nodules glowing on the mound between the blades.
    for i in range(3):
        a = i * 2.1 + 0.7
        ico("Stalk_Nodule%d" % i, (math.cos(a) * 2.3, math.sin(a) * 2.3, 0.95), (0.34, 0.34, 0.40), TEAL_GLOW, subdiv=1)
    for i in range(5):
        a = i * (2.0 * math.pi / 5.0) + 0.5
        cone("Stalk_Spike%d" % i, (math.cos(a) * 5.2, math.sin(a) * 5.2, 1.55), 0.8, 3.5, SPIRE, vertices=6)
    for i in range(3):
        a = i * 2.3 + 1.1
        ico("Stalk_Rock%d" % i, (math.cos(a) * 7.0, math.sin(a) * 7.0, 0.3), (0.95, 0.85, 0.55), CAMP_ROCK, subdiv=1)


def build_camp_t3(rng):
    # TYRANT MAW (Tier 3) V5: the maw gains teal sacs between
    # its fangs and an outer ring of small spires - a grown
    # stronghold of the biome, not just a rock pile.
    ico("Maw_Skirt", (0, 0, 0.5), (10.5, 10.5, 2.0), CAMP_ROCK, subdiv=2)
    sphere("Maw_Throat", (0, 0, 2.05), (2.6, 2.6, 1.1), EMBER)
    for i in range(6):
        a = i * (math.pi / 3.0) + 0.25
        h = 9.0 + (i % 3) * 1.2
        cone("Maw_Fang%d" % i, _lean_centre(a, 6.8, h, 0.31), 1.7, h, SPIRE,
             rot=_lean_rot(a, 0.31), vertices=7)
    for i in range(4):
        a = i * (math.pi / 2.0) + 0.6
        ico("Maw_Ember%d" % i, (math.cos(a) * 4.6, math.sin(a) * 4.6, 1.95), (0.5, 0.5, 0.5), EMBER, subdiv=1)
    # Teal sacs nestled between the fangs.
    for i in range(3):
        a = i * 2.1 + 1.2
        sphere("Maw_Sac%d" % i, (math.cos(a) * 5.4, math.sin(a) * 5.4, 2.4), (0.80, 0.80, 0.95), TEAL_GLOW)
    # Outer ring of small spires.
    for i in range(5):
        a = i * (2.0 * math.pi / 5.0) + 0.9
        h = 3.6 + (i % 2) * 1.3
        cone("Maw_Spire%d" % i, (math.cos(a) * 9.3, math.sin(a) * 9.3, h * 0.5), 0.85, h, SPIRE, vertices=6)
    for i in range(3):
        a = i * 2.15 + 0.9
        ico("Maw_Rock%d" % i, (math.cos(a) * 11.6, math.sin(a) * 11.6, 0.55), (2.2, 1.9, 1.4), CAMP_ROCK, subdiv=1)


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
    ("BiomassNodeV5", build_biomass),
    ("AlloyNodeV5", build_alloy),
    ("HeliumNodeV5", build_helium),
    ("EnemyCampT1V5", build_camp_t1),
    ("EnemyCampT2V5", build_camp_t2),
    ("EnemyCampT3V5", build_camp_t3),
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
    global BIO_ROCK, CAMP_ROCK, TEAL_GLOW, BIO_FLESH, COPPER, RUST_ROCK
    BASALT = mat("Basalt", (0.16, 0.17, 0.19), roughness=0.95)
    ROCK_GREY = mat("RockGrey", (0.30, 0.31, 0.34), roughness=0.95)
    EMERALD = mat("Emerald", (0.10, 0.55, 0.25), emission=(0.15, 0.95, 0.35), strength=2.0, roughness=0.3)
    POD_GREEN = mat("PodGreen", (0.05, 0.25, 0.10), roughness=0.35)
    SILVER = mat("AlloySilver", (0.72, 0.78, 0.85), roughness=0.35, metallic=0.85)
    VENT_DARK = mat("VentDark", (0.10, 0.10, 0.12), roughness=0.7, metallic=0.4)
    CYAN_GLOW = mat("HeliumGlow", (0.35, 0.85, 0.95), emission=(0.30, 0.95, 1.0), strength=2.2, roughness=0.3)
    CYAN_SHARD = mat("HeliumShard", (0.30, 0.75, 0.90), emission=(0.25, 0.85, 1.0), strength=1.4, roughness=0.3)
    SPIRE = mat("CampSpire", (0.07, 0.05, 0.11), roughness=0.9)
    EMBER = mat("CampEmber", (0.45, 0.12, 0.05), emission=(1.0, 0.30, 0.10), strength=2.2, roughness=0.5)
    EGG = mat("CampEgg", (0.12, 0.08, 0.16), roughness=0.45)
    BLACK = mat("LairBlack", (0.01, 0.01, 0.012), roughness=1.0)
    AMBER = mat("LairAmber", (0.55, 0.32, 0.08), emission=(1.0, 0.62, 0.15), strength=1.8, roughness=0.5)
    BIO_ROCK = mat("BioRock", (0.13, 0.17, 0.13), roughness=0.95)
    CAMP_ROCK = mat("CampRock", (0.11, 0.09, 0.15), roughness=0.95)
    TEAL_GLOW = mat("TealGlow", (0.30, 0.95, 0.85), emission=(0.25, 0.95, 0.82), strength=2.4, roughness=0.35)
    BIO_FLESH = mat("BioFlesh", (0.16, 0.21, 0.13), roughness=0.5)
    COPPER = mat("AlloyCopper", (0.62, 0.30, 0.14), roughness=0.38, metallic=0.9)
    RUST_ROCK = mat("RustRock", (0.27, 0.155, 0.105), roughness=0.95)

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
        # V4 caps: the camps are authored at FINAL size (the view
        # places them at scale 1 now, not 2), so the camp bounds
        # roughly double V3's; the nodes keep V3's sizes and pass
        # under the same looser caps. zmin: mound bottoms sink
        # ~1 unit into the flat site discs, as V3's did.
        good = count >= 3 and w <= 26.0 and d <= 26.0 and 1.5 <= h <= 16.0 and -1.6 <= zmin <= 0.6
        print("  %s %s: objects=%d tris=%d w=%.1f d=%.1f h=%.1f zmin=%.2f" % ("PASS" if good else "FAIL", name, count, tris, w, d, h, zmin))
        ok = ok and good
    print("VERIFY: " + ("ALL PASS" if ok else "FAILURES PRESENT"))


main()
