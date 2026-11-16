# BuildMapSitesV6.py - BRIGHT DAY LUSH SITE REDESIGN
# (Patrick 2026-11-16: the V5 sites "were built for the
# original lunar landscape - update them for this more
# lush landscape and increase their definition/quality",
# approved from the mockup panels, option E). Fork of V5:
# same install names, same footprints and authoring scales
# (nodes placed at 2x by ExteriorView.BuildMapPoints,
# camps at 1x), so no code changes ride along. What
# changes is the art direction and the detail level:
#   * Palette: V5's near-black spires and dark violet-grey
#     rock read as lunar relics under the bright-day sky.
#     V6 grows the sites out of the living biome instead -
#     violet flesh, mossy stone, chitin and bone, with the
#     world's teal glow and each resource's own colour.
#   * Definition: every model roughly doubles its part
#     count (surface bumps, tendril roots, glow accents ON
#     the surface where they can be seen, tip orbs that
#     give the silhouettes a read at full map zoom).
#   BiomassNodeV6 - Glowpod Bloom: glowing pods on stalks
#     over a mossy mound, emerald heart-shards, leaf skirts.
#   AlloyNodeV6   - Ore Bloom: V5's copper/silver eruption
#     kept, plus patina oxidation, a silver hero blade and
#     denser ember glints.
#   HeliumNodeV6  - Vent Garden: the vent and cyan pool
#     stay; pale spires become glowing bulbs on stalks,
#     with gas bubbles frozen mid-rise above them.
#   EnemyCampT1V6 - Spore Nest: flesh domes with bumps,
#     amber pustules, a chitin spire with a glow orb, a
#     dark maw at the front (the E2 concept, detailed).
#   EnemyCampT2V6 - Stalker Spires: the tall blades stay
#     (tier identity) in chitin, with teal tip orbs and
#     nodules, on a flesh-and-moss mound.
#   EnemyCampT3V6 - Tyrant Maw: bone fangs with chitin
#     collars around the ember throat, flesh bulges and
#     an outer spire ring with amber tips.
# The lair is NOT rebuilt. Install: rename each export
# over the existing file in Resources/Models
# (BiomassNodeV6.fbx -> BiomassNode.fbx, ...).
# The V5 notes follow in git history.
#
import bpy
import math
from mathutils import Matrix, Vector
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


def make_materials():
    g = {}
    g["FLESH"] = mat("SiteFlesh", (0.40, 0.18, 0.48), roughness=0.5)
    g["FLESH_DK"] = mat("SiteFleshDark", (0.26, 0.10, 0.34), roughness=0.55)
    g["FLESH_LT"] = mat("SiteFleshLight", (0.56, 0.33, 0.63), roughness=0.5)
    # T3-only bone/ash set (Patrick 2026-11-16: T1 and T3
    # both read violet - T3 goes bleached bone instead;
    # T1 keeps the violet flesh he likes).
    g["ASH"] = mat("SiteAsh", (0.58, 0.50, 0.40), roughness=0.6)
    g["ASH_DK"] = mat("SiteAshDark", (0.36, 0.30, 0.23), roughness=0.65)
    g["ASH_LT"] = mat("SiteAshLight", (0.74, 0.67, 0.55), roughness=0.55)
    g["CHITIN"] = mat("SiteChitin", (0.22, 0.11, 0.28), roughness=0.65)
    g["BONE"] = mat("SiteBone", (0.78, 0.72, 0.60), roughness=0.6)
    g["MOSS"] = mat("SiteMossRock", (0.24, 0.31, 0.20), roughness=0.95)
    g["MOSS_DK"] = mat("SiteMossDark", (0.16, 0.22, 0.14), roughness=0.95)
    g["EMBER"] = mat("SiteEmber", (0.45, 0.12, 0.05), emission=(1.0, 0.32, 0.10), strength=2.4, roughness=0.5)
    g["AMBER"] = mat("SiteAmber", (0.55, 0.32, 0.08), emission=(1.0, 0.62, 0.15), strength=2.2, roughness=0.5)
    g["TEAL"] = mat("SiteTealGlow", (0.30, 0.95, 0.85), emission=(0.25, 0.95, 0.82), strength=2.4, roughness=0.35)
    g["POD"] = mat("SitePod", (0.16, 0.48, 0.18), roughness=0.28)
    g["POD_LT"] = mat("SitePodLight", (0.36, 0.68, 0.26), roughness=0.3)
    g["LEAF"] = mat("SiteLeaf", (0.13, 0.38, 0.15), roughness=0.7)
    g["STALK"] = mat("SiteStalk", (0.20, 0.36, 0.16), roughness=0.8)
    g["EMERALD"] = mat("SiteEmerald", (0.10, 0.55, 0.25), emission=(0.15, 0.95, 0.35), strength=2.0, roughness=0.3)
    g["COPPER"] = mat("SiteCopper", (0.62, 0.30, 0.14), roughness=0.38, metallic=0.9)
    g["SILVER"] = mat("SiteSilver", (0.72, 0.78, 0.85), roughness=0.35, metallic=0.85)
    g["PATINA"] = mat("SitePatina", (0.24, 0.52, 0.44), roughness=0.45, metallic=0.6)
    g["RUST"] = mat("SiteRustRock", (0.30, 0.17, 0.11), roughness=0.95)
    g["CYAN_GLOW"] = mat("SiteHeliumGlow", (0.35, 0.85, 0.95), emission=(0.30, 0.95, 1.0), strength=2.2, roughness=0.3)
    g["CYAN_SHARD"] = mat("SiteHeliumShard", (0.30, 0.75, 0.90), emission=(0.25, 0.85, 1.0), strength=1.4, roughness=0.3)
    g["VENT_DK"] = mat("SiteVentDark", (0.12, 0.12, 0.14), roughness=0.6, metallic=0.4)
    g["PALE"] = mat("SitePaleRock", (0.46, 0.50, 0.43), roughness=0.95)
    return g


M = make_materials()


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
    # The object a primitive-add just made, found through
    # bpy.data (never the context - see the V5 notes).
    new = [o for o in bpy.data.objects if o not in before]
    return new[0]


def _bake_scale(obj):
    # Bake scale via an Identity matrix with assigned
    # elements: building Matrix(((...))) from nested tuples
    # raises TypeError at Mesh.transform in Blender 5.2
    # (Patrick's GUI, hit 2026-11-16) though it works in
    # 4.2 - the Identity form is valid in both.
    sx, sy, sz = obj.scale
    if (sx, sy, sz) != (1.0, 1.0, 1.0):
        m = Matrix.Identity(4)
        m[0][0] = sx
        m[1][1] = sy
        m[2][2] = sz
        obj.data.transform(m)
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


def _lean_rot(a, theta):
    # Euler tipping a +Z cone toward the centre when placed
    # at ring angle a (theta > 0 leans inward, < 0 outward).
    return (math.sin(a) * math.sin(theta), -math.cos(a) * math.sin(theta), 0.0)


def _lean_centre(a, r, h, theta):
    # Cone centre so its base sits on the ring point at
    # angle a while the cone leans by theta.
    return (math.cos(a) * r - math.cos(a) * math.sin(theta) * h * 0.5,
            math.sin(a) * r - math.sin(a) * math.sin(theta) * h * 0.5,
            math.cos(theta) * h * 0.5)


def _lean_tip(a, r, h, theta):
    # World position of the leaning cone's apex (base on the
    # ring point, apex direction from _lean_rot's euler).
    s = math.sin(theta)
    return (math.cos(a) * (r - s * h), math.sin(a) * (r - s * h),
            math.cos(theta) * h)


# ----------------------------------------------------------------
# NODES
# ----------------------------------------------------------------

def build_biomass(rng):
    # GLOWPOD BLOOM: pods on stalks over a mossy mound.
    ico("Bio_Mound", (0, 0, 0.30), (3.1, 3.1, 0.95), M["MOSS"], subdiv=2)
    ico("Bio_MoundB", (0.9, -0.7, 0.22), (2.0, 2.0, 0.62), M["MOSS_DK"], subdiv=2)
    pods = [(0.0, 0.4, 2.9, 1.05), (1.6, 0.9, 2.2, 0.82), (-1.5, 1.1, 1.9, 0.74),
            (1.2, -1.4, 2.5, 0.90), (-1.3, -1.2, 1.6, 0.66)]
    for i, (px, py, h, r) in enumerate(pods):
        cyl("Bio_Stalk%d" % i, (px, py, h * 0.5 + 0.3), 0.15, h, M["STALK"], vertices=8)
        pod = sphere("Bio_Pod%d" % i, (px, py, h + 0.3 + r * 0.55), (r, r, r * 1.18),
                     M["POD"] if i % 2 == 0 else M["POD_LT"])
        # Glow dots ON the pod's surface (upper-outside).
        for k in range(3):
            da = rng.uniform(0, 6.28)
            dx, dy = math.cos(da) * r * 0.82, math.sin(da) * r * 0.82
            sphere("Bio_Dot%d_%d" % (i, k), (px + dx, py + dy, h + 0.3 + r * 0.55 + r * 0.72),
                   (0.11, 0.11, 0.11), M["TEAL"])
    # Emerald heart-shards at the centre.
    hero = ico("Bio_ShardHero", (0, 0, 1.55), (0.34, 0.34, 1.15), M["EMERALD"], subdiv=1)
    hero.rotation_euler = (0.10, -0.06, 0.0)
    for i in range(3):
        ang = i * 2.1 + 0.5
        shard = ico("Bio_Shard%d" % i, (math.cos(ang) * 0.75, math.sin(ang) * 0.75, 1.05),
                    (0.22, 0.22, 0.62), M["EMERALD"], subdiv=1)
        shard.rotation_euler = (math.sin(ang) * 0.35, -math.cos(ang) * 0.35, 0.0)
    # Leaf skirt + glow spores + rim rubble.
    for i in range(4):
        ang = i * 1.57 + 0.6
        leaf = ico("Bio_Leaf%d" % i, (math.cos(ang) * 2.9, math.sin(ang) * 2.9, 0.30),
                   (1.45, 0.58, 0.16), M["LEAF"], subdiv=1)
        leaf.rotation_euler = (0.0, 0.0, -ang)
    for i in range(3):
        ang = i * 2.2 + 0.4
        sphere("Bio_Spore%d" % i, (math.cos(ang) * 2.3, math.sin(ang) * 2.3, 0.72),
               (0.17, 0.17, 0.17), M["TEAL"])
    for i in range(5):
        ang = i * 1.45 + 0.6
        ico("Bio_Rubble%d" % i, (math.cos(ang) * 3.15, math.sin(ang) * 3.15, 0.18),
            (0.45, 0.45, 0.30), M["MOSS_DK"], subdiv=1)


def build_alloy(rng):
    # ORE BLOOM: copper eruption, patina, silver hero blade.
    ico("Alloy_Mound", (0, 0, 0.32), (3.2, 3.2, 1.0), M["RUST"], subdiv=2)
    hero = box("Alloy_Hero", (-0.5, -0.4, 2.35), (0.42, 0.20, 2.05), M["SILVER"],
               rot=(0.30, -0.22, 0.7))
    for i in range(8):
        ang = i * 0.79 + 0.25
        r = rng.uniform(1.0, 1.60)
        length = rng.uniform(2.0, 3.3)
        blade_mat = M["SILVER"] if i in (3, 6) else M["COPPER"]
        box("Alloy_Shard%d" % i,
            (math.cos(ang) * r, math.sin(ang) * r, length * 0.50 + 0.30),
            (0.36, 0.18, length * 0.5), blade_mat,
            rot=(math.sin(ang) * 0.55, -math.cos(ang) * 0.55, ang))
    for i in range(3):
        ang = i * 2.2 + 1.1
        box("Alloy_Ingot%d" % i,
            (math.cos(ang) * 2.15, math.sin(ang) * 2.15, 0.30),
            (0.55, 0.32, 0.22), M["COPPER"], rot=(0.25, 0.0, ang))
    # Patina oxidation patches on the mound's slopes.
    for i in range(4):
        ang = i * 1.65 + 0.9
        patch = ico("Alloy_Patina%d" % i, (math.cos(ang) * 1.9, math.sin(ang) * 1.9, 0.95),
                    (0.72, 0.72, 0.20), M["PATINA"], subdiv=1)
        patch.rotation_euler = (math.sin(ang) * 0.5, -math.cos(ang) * 0.5, 0.0)
    for i in range(5):
        ang = i * 1.35 + 0.8
        r = rng.uniform(1.5, 2.4)
        box("Alloy_Glint%d" % i,
            (math.cos(ang) * r, math.sin(ang) * r, 0.80),
            (0.22, 0.14, 0.10), M["EMBER"], rot=(0.0, 0.0, ang))
    for i in range(6):
        ang = i * 1.12 + 0.5
        rubble_mat = M["PATINA"] if i == 2 else M["RUST"]
        box("Alloy_Rubble%d" % i,
            (math.cos(ang) * 2.65, math.sin(ang) * 2.65, 0.22),
            (0.42, 0.30, 0.24), rubble_mat, rot=(0.0, 0.0, ang))


def build_helium(rng):
    # VENT GARDEN: vent + pool stay; bulbs on stalks join.
    ico("He_Mound", (0, 0, 0.26), (2.9, 2.9, 0.75), M["PALE"], subdiv=2)
    for i in range(3):
        ang = i * 2.1 + 0.8
        ico("He_Moss%d" % i, (math.cos(ang) * 2.2, math.sin(ang) * 2.2, 0.42),
            (0.85, 0.85, 0.26), M["MOSS"], subdiv=1)
    for i in range(8):
        ang = i * math.pi / 4.0 + 0.20
        ico("He_Rock%d" % i, (math.cos(ang) * 1.95, math.sin(ang) * 1.95, 0.55),
            (0.95, 0.95, 0.78), M["PALE"], subdiv=1)
    for i in range(5):
        ang = i * 1.26 + 0.7
        ico("He_Stone%d" % i, (math.cos(ang) * 1.25, math.sin(ang) * 1.25, 0.40),
            (0.55, 0.55, 0.45), M["MOSS_DK"], subdiv=1)
    cyl("He_Vent", (0, 0, 0.65), 0.85, 1.3, M["VENT_DK"])
    cyl("He_Pool", (0, 0, 1.28), 0.78, 0.10, M["CYAN_GLOW"])
    cyl("He_Core", (0, 0, 1.05), 0.55, 0.5, M["CYAN_GLOW"])
    cyl("He_Halo", (0, 0, 0.80), 1.70, 0.07, M["CYAN_GLOW"])
    # Glowing bulbs on pale stalks around the vent.
    for i, (ang, h, r) in enumerate(((0.8, 3.1, 0.52), (2.9, 2.5, 0.44), (5.0, 3.6, 0.58),
                                     (1.9, 2.0, 0.38), (4.1, 2.2, 0.40))):
        bx, by = math.cos(ang) * 1.55, math.sin(ang) * 1.55
        cyl("He_Stalk%d" % i, (bx, by, h * 0.5 + 0.3), 0.09, h, M["PALE"], vertices=8)
        sphere("He_Bulb%d" % i, (bx, by, h + 0.3 + r * 0.6), (r, r, r * 1.1), M["CYAN_SHARD"])
        sphere("He_Tip%d" % i, (bx, by, h + 0.3 + r * 1.55), (0.15, 0.15, 0.15), M["CYAN_GLOW"])
    # Gas bubbles frozen mid-rise.
    for i, (ang, rr, z, r) in enumerate(((0.4, 0.5, 3.4, 0.20), (2.4, 0.7, 4.1, 0.15),
                                        (4.4, 0.4, 4.7, 0.24), (5.6, 0.8, 3.8, 0.13))):
        sphere("He_Bubble%d" % i, (math.cos(ang) * rr, math.sin(ang) * rr, z),
               (r, r, r), M["CYAN_GLOW"])


# ----------------------------------------------------------------
# CAMPS
# ----------------------------------------------------------------

def build_camp_t1(rng):
    # SPORE NEST: flesh domes, amber pustules, spire + orb,
    # a dark maw at the front (Blender -Y = Unity +Z face).
    ico("Nest_Dome", (0, 0, 2.00), (5.2, 5.2, 3.6), M["FLESH"], subdiv=2)
    for i, (ang, dist, r) in enumerate(((0.4, 5.6, 3.1), (1.8, 6.0, 2.5), (3.1, 5.4, 3.4),
                                        (4.4, 5.9, 2.3), (5.6, 5.2, 2.8))):
        ico("Nest_Dome%d" % i, (math.cos(ang) * dist, math.sin(ang) * dist, 0.95),
            (r, r, r * 0.72), M["FLESH"] if r > 2.6 else M["FLESH_DK"], subdiv=2)
    # Surface bumps on the main dome.
    for i in range(8):
        ang = rng.uniform(0, 6.28)
        el = rng.uniform(0.45, 1.20)
        rr = rng.uniform(0.45, 0.85)
        ico("Nest_Bump%d" % i,
            (5.2 * math.cos(el) * math.cos(ang), 5.2 * math.cos(el) * math.sin(ang),
             2.00 + 3.6 * math.sin(el)),
            (rr, rr, rr * 0.8), M["FLESH_LT"], subdiv=1)
    # Chitin spire with a glowing amber orb.
    cone("Nest_Spire", (0, 0, 6.9), 1.5, 5.6, M["CHITIN"], vertices=9)
    sphere("Nest_Orb", (0, 0, 10.0), (0.55, 0.55, 0.55), M["AMBER"])
    # Amber pustules on the dome shoulders.
    for i in range(9):
        ang = rng.uniform(0, 6.28)
        el = rng.uniform(0.55, 1.25)
        rr = rng.uniform(0.30, 0.50)
        sphere("Nest_Pustule%d" % i,
               (5.25 * math.cos(el) * math.cos(ang), 5.25 * math.cos(el) * math.sin(ang),
                2.00 + 3.65 * math.sin(el)),
               (rr, rr, rr), M["AMBER"])
    # Teal sacs at the dome base.
    for i in range(3):
        a = i * 2.1 + 1.5
        sphere("Nest_Sac%d" % i, (math.cos(a) * 4.9, math.sin(a) * 4.9, 1.15),
               (0.62, 0.62, 0.78), M["TEAL"])
    # The maw: a dark mouth with an ember heart, front side.
    maw = ico("Nest_Maw", (0, -4.85, 1.55), (1.35, 0.55, 1.05), M["FLESH_DK"], subdiv=1)
    sphere("Nest_MawGlow", (0, -5.30, 1.55), (0.45, 0.30, 0.45), M["EMBER"])
    # Tendril roots crawling out of the nest.
    for i in range(6):
        a = i * 1.047 + 0.4
        cone("Nest_Tendril%d" % i, _lean_centre(a, 4.6, 4.4, -1.22), 0.45, 4.4,
             M["FLESH_DK"], rot=_lean_rot(a, -1.22), vertices=7)
    # Eggs on the skirt + mossy rocks.
    for i in range(4):
        a = i * (math.pi / 2.0) + 0.8
        sphere("Nest_Egg%d" % i, (math.cos(a) * 6.1, math.sin(a) * 6.1, 0.80),
               (0.90, 0.90, 1.25), M["FLESH_LT"])
    for i in range(3):
        a = i * 2.2 + 0.2
        ico("Nest_Rock%d" % i, (math.cos(a) * 7.6, math.sin(a) * 7.6, 0.35),
            (1.05, 0.9, 0.6), M["MOSS"], subdiv=1)


def build_camp_t2(rng):
    # STALKER SPIRES, revised (Patrick 2026-11-16 Play
    # check: T2 "could be improved" next to T1 - it read
    # sparse and wiry on its disc). Same identity - tall
    # chitin blades over a camp mound - but grown denser
    # and fleshier like the T1 nest: a taller flesh mound
    # with bulges, FIVE blades with chitin collars and
    # teal tip orbs, five watcher sacs on stalks, and a
    # wider ring of spikes and tendrils filling the disc.
    ico("Stalk_Mound", (0, 0, 0.55), (6.2, 6.2, 1.9), M["FLESH_DK"], subdiv=2)
    for i in range(3):
        a = i * 2.1 + 0.7
        ico("Stalk_Flesh%d" % i, (math.cos(a) * 3.6, math.sin(a) * 3.6, 0.95),
            (2.2, 2.2, 0.95), M["FLESH"], subdiv=1)
    for i in range(5):
        a = rng.uniform(0, 6.28)
        r = rng.uniform(2.6, 4.6)
        ico("Stalk_Bump%d" % i, (math.cos(a) * r, math.sin(a) * r, 1.95),
            (0.55, 0.55, 0.42), M["FLESH_LT"], subdiv=1)
    blades = ((90, 2.6, 14.0), (210, 2.6, 13.0), (330, 2.6, 11.5),
              (150, 3.4, 9.5), (30, 3.4, 8.0))
    for i, (deg, rr, h) in enumerate(blades):
        a = math.radians(deg)
        lean = -0.14 if h > 10.0 else -0.20
        cone("Stalk_Collar%d" % i, _lean_centre(a, rr, 2.0, lean), 1.95, 2.0, M["CHITIN"],
             rot=_lean_rot(a, lean), vertices=8)
        cone("Stalk_Blade%d" % i, _lean_centre(a, rr, h, lean), 1.30, h, M["CHITIN"],
             rot=_lean_rot(a, lean), vertices=8)
        tip = _lean_tip(a, rr, h, lean)
        sphere("Stalk_Tip%d" % i, tip, (0.34, 0.34, 0.34), M["TEAL"])
        if h > 10.0:
            for k in range(2):
                ta = a + 0.5 + k * 1.1
                cone("Stalk_Root%d_%d" % (i, k), _lean_centre(ta, 3.1, 3.0, -1.15), 0.34, 3.0,
                     M["FLESH_DK"], rot=_lean_rot(ta, -1.15), vertices=6)
    # Watcher sacs on stalks - the stalkers' eyes.
    for i, deg in enumerate((150, 300, 30, 200, 340)):
        a = math.radians(deg)
        sh = 2.2 + (i % 2) * 0.8
        cyl("Stalk_Stalk%d" % i, (math.cos(a) * 4.6, math.sin(a) * 4.6, sh * 0.5 + 0.4),
            0.16, sh, M["CHITIN"], vertices=8)
        sphere("Stalk_Sac%d" % i, (math.cos(a) * 4.6, math.sin(a) * 4.6, sh + 0.4 + 0.75),
               (0.88, 0.88, 1.02), M["TEAL"] if i % 2 == 0 else M["EMBER"])
    for i in range(8):
        a = i * 0.79 + 0.7
        ico("Stalk_Nodule%d" % i, (math.cos(a) * 2.3, math.sin(a) * 2.3, 1.55),
            (0.32, 0.32, 0.38), M["TEAL"], subdiv=1)
    for i in range(7):
        a = i * (2.0 * math.pi / 7.0) + 0.5
        cone("Stalk_Spike%d" % i, (math.cos(a) * 5.4, math.sin(a) * 5.4, 1.75), 0.85, 3.9,
             M["CHITIN"], vertices=6)
    for i in range(3):
        a = i * 2.3 + 1.1
        ico("Stalk_Rock%d" % i, (math.cos(a) * 7.0, math.sin(a) * 7.0, 0.3),
            (0.95, 0.85, 0.55), M["MOSS"], subdiv=1)


def build_camp_t3(rng):
    # TYRANT MAW: bone fangs around the ember throat, flesh
    # bulges on the skirt, outer spires with amber tips.
    ico("Maw_Skirt", (0, 0, 0.5), (10.5, 10.5, 2.0), M["ASH_DK"], subdiv=2)
    for i in range(5):
        a = i * 1.35 + 0.5
        r = rng.uniform(2.1, 3.2)
        ico("Maw_Bulge%d" % i, (math.cos(a) * 7.6, math.sin(a) * 7.6, 1.15),
            (r, r, r * 0.55), M["ASH"], subdiv=1)
    for i in range(6):
        a = rng.uniform(0, 6.28)
        r = rng.uniform(4.2, 6.4)
        ico("Maw_Bump%d" % i, (math.cos(a) * r, math.sin(a) * r, 2.15),
            (0.65, 0.65, 0.5), M["ASH_LT"], subdiv=1)
    sphere("Maw_Throat", (0, 0, 2.05), (2.6, 2.6, 1.1), M["EMBER"])
    for i in range(6):
        a = i * (math.pi / 3.0) + 0.25
        h = 9.0 + (i % 3) * 1.2
        cone("Maw_Collar%d" % i, _lean_centre(a, 6.8, 2.4, 0.31), 2.15, 2.4, M["CHITIN"],
             rot=_lean_rot(a, 0.31), vertices=8)
        cone("Maw_Fang%d" % i, _lean_centre(a, 6.8, h, 0.31), 1.55, h, M["BONE"],
             rot=_lean_rot(a, 0.31), vertices=8)
        ico("Maw_Ember%d" % i, (math.cos(a) * 4.6, math.sin(a) * 4.6, 1.95),
            (0.5, 0.5, 0.5), M["EMBER"], subdiv=1)
    for i in range(3):
        a = i * 2.1 + 1.2
        sphere("Maw_Sac%d" % i, (math.cos(a) * 5.4, math.sin(a) * 5.4, 2.4),
               (0.80, 0.80, 0.95), M["TEAL"])
    for i in range(5):
        a = i * (2.0 * math.pi / 5.0) + 0.9
        h = 3.6 + (i % 2) * 1.3
        cone("Maw_Spire%d" % i, (math.cos(a) * 9.3, math.sin(a) * 9.3, h * 0.5), 0.85, h,
             M["CHITIN"], vertices=6)
        sphere("Maw_Tip%d" % i, (math.cos(a) * 9.3, math.sin(a) * 9.3, h + 0.25),
               (0.22, 0.22, 0.22), M["AMBER"])
    for i in range(3):
        a = i * 2.15 + 0.9
        ico("Maw_Rock%d" % i, (math.cos(a) * 11.6, math.sin(a) * 11.6, 0.55),
            (2.2, 1.9, 1.4), M["MOSS"], subdiv=1)


BUILDERS = [
    ("BiomassNodeV6", build_biomass),
    ("AlloyNodeV6", build_alloy),
    ("HeliumNodeV6", build_helium),
    ("EnemyCampT1V6", build_camp_t1),
    ("EnemyCampT2V6", build_camp_t2),
    ("EnemyCampT3V6", build_camp_t3),
]


def export_model(name):
    # use_selection=False on purpose (Blender 5.2 GUI
    # exporter AttributeError - see the terrain scripts).
    # At export time the scene holds ONLY this model's
    # objects, so exporting everything is exact.
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
    global M
    M = make_materials()

    results = {}
    for name, builder in BUILDERS:
        del OBJECTS[:]
        rng = random.Random(__import__("zlib").crc32(name.encode()) & 0xFFFF)  # stable seed (hash() varies per process)
        builder(rng)
        tris = sum(len(o.data.polygons) for o in OBJECTS)
        xs, ys, zs = [], [], []
        for o in OBJECTS:
            for corner in o.bound_box:
                v = o.matrix_world @ Vector(corner)
                xs.append(v.x)
                ys.append(v.y)
                zs.append(v.z)
        results[name] = (len(OBJECTS), tris, max(xs) - min(xs), max(ys) - min(ys),
                         max(zs) - min(zs), min(zs))
        path = export_model(name)
        print("Exported %s: %d objects" % (name, len(OBJECTS)))
        for o in list(OBJECTS):
            bpy.data.objects.remove(o, do_unlink=True)
    ok = True
    for name, (count, tris, w, d, h, zmin) in results.items():
        # Bounds caps carried from V5 (same footprints and
        # authoring scales, so discs and tap targets are
        # untouched); V6 adds a definition floor on part
        # count and a sanity cap on polygons per model.
        good = count >= 28 and tris <= 14000 and w <= 26.0 and d <= 26.0 \
            and 1.5 <= h <= 16.0 and -1.6 <= zmin <= 0.6
        print("  %s %s: objects=%d tris=%d w=%.1f d=%.1f h=%.1f zmin=%.2f"
              % ("PASS" if good else "FAIL", name, count, tris, w, d, h, zmin))
        ok = ok and good
    print("VERIFY: " + ("ALL PASS" if ok else "FAILURES PRESENT"))


main()
