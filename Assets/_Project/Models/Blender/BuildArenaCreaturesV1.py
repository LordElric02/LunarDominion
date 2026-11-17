# BuildArenaCreaturesV1.py - ARENA FIGHT CREATURES
# (Patrick 2026-11-16: the simulated camp fight with
# tinted troop minis "needs more work" - he picked the
# full arena swap: dedicated creature models that replace
# BOTH the stylized army figures and the camp structure
# for the duration of a camp fight; the camp structure
# returns if the army loses and vanishes if it wins.)
# V1 is the Tier 1 vertical slice: the Spore Crawler at
# arena scale. The other five tiers join this same script
# as their own builders once the slice is Play-checked.
#
# Authoring rules (same as the site/terrain scripts):
#   * Head toward +X (the troops' convention - after the
#     FBX import the front sits at Unity -Z, and the code
#     wraps the model in a facing pivot that flips it, so
#     never bake a facing rotation into the model itself).
#   * Ground plane is z=0; the lowest point of the model
#     rests at (or a hair above) z=0.
#   * Scale is baked into the mesh (no transform_apply,
#     no context reads - see the house rules in AGENTS.md).
# Install: ArenaSporeCrawler.fbx goes into
# Assets/_Project/Resources/Models/ with its name
# unchanged. Patrick's Text Editor runs land the export
# at F:\ - pick it up from there.
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
    # Spore Crawler: mossy green flesh (the T1 camp's pods
    # grown into a beast), violet-dark chitin, bone fangs,
    # teal spore glow - the T1 nest palette, alive.
    g["FLESH"] = mat("CrawlerFlesh", (0.30, 0.42, 0.20), roughness=0.55)
    g["FLESH_DK"] = mat("CrawlerFleshDark", (0.18, 0.27, 0.13), roughness=0.6)
    g["FLESH_LT"] = mat("CrawlerFleshLight", (0.45, 0.58, 0.30), roughness=0.5)
    g["POD"] = mat("CrawlerPod", (0.16, 0.48, 0.18), roughness=0.28)
    g["POD_LT"] = mat("CrawlerPodLight", (0.36, 0.68, 0.26), roughness=0.3)
    g["CHITIN"] = mat("CrawlerChitin", (0.22, 0.11, 0.28), roughness=0.65)
    g["BONE"] = mat("CrawlerBone", (0.78, 0.72, 0.60), roughness=0.6)
    g["MOSS"] = mat("CrawlerMoss", (0.16, 0.22, 0.14), roughness=0.95)
    g["TEAL"] = mat("CrawlerTealGlow", (0.30, 0.95, 0.85), emission=(0.25, 0.95, 0.82), strength=2.4, roughness=0.35)
    g["AMBER"] = mat("CrawlerAmberGlow", (0.55, 0.32, 0.08), emission=(1.0, 0.62, 0.15), strength=2.2, roughness=0.5)
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
    # bpy.data (never the context - Blender 5.2 GUI rule).
    new = [o for o in bpy.data.objects if o not in before]
    return new[0]


def _bake_scale(obj):
    # Identity matrix with assigned elements: the nested
    # tuple form raises TypeError in Blender 5.2.
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


# ----------------------------------------------------------------
# TIER 1 - SPORE CRAWLER (arena scale: ~9.5 long, ~4.2 high)
# ----------------------------------------------------------------

def build_spore_crawler(rng):
    # Moss mat it stands on - grounds the silhouette.
    ico("Cr_Mat", (0, 0, 0.22), (3.9, 2.9, 0.45), M["MOSS"], subdiv=2)
    # Body + head (head toward +X).
    ico("Cr_Body", (0, 0, 1.95), (3.4, 2.2, 1.7), M["FLESH"], subdiv=2)
    ico("Cr_Belly", (-0.3, 0, 1.15), (2.9, 1.9, 1.1), M["FLESH_DK"], subdiv=2)
    ico("Cr_Head", (3.1, 0, 1.75), (1.5, 1.35, 1.1), M["FLESH_LT"], subdiv=2)
    ico("Cr_Jaw", (3.6, 0, 1.0), (1.0, 1.0, 0.5), M["FLESH_DK"], subdiv=1)
    # Eyes: amber pair low, one teal seer-eye high.
    sphere("Cr_EyeL", (4.35, 0.55, 2.15), (0.22, 0.22, 0.22), M["AMBER"])
    sphere("Cr_EyeR", (4.35, -0.55, 2.15), (0.22, 0.22, 0.22), M["AMBER"])
    sphere("Cr_EyeTop", (4.05, 0, 2.65), (0.26, 0.26, 0.26), M["TEAL"])
    # Fangs pointing forward (+X): cone apex is +Z, so a
    # +90-degree Y rotation lays it onto +X.
    cone("Cr_FangL", (4.35, 0.7, 1.15), 0.18, 1.1, M["BONE"], rot=(0.0, math.pi / 2, 0.0), vertices=6)
    cone("Cr_FangR", (4.35, -0.7, 1.15), 0.18, 1.1, M["BONE"], rot=(0.0, math.pi / 2, 0.0), vertices=6)
    cone("Cr_FangC", (4.5, 0, 0.95), 0.15, 0.9, M["BONE"], rot=(0.0, math.pi / 2, 0.0), vertices=6)
    # Spore pods along the spine, each with a teal glow dot
    # on its upper surface (the nest's pustules, grown).
    pod_xs = [1.9, 0.7, -0.6, -1.9]
    for i, px in enumerate(pod_xs):
        r = 0.85 - abs(i - 1.5) * 0.12
        sphere("Cr_Pod%d" % i, (px, 0, 3.35), (r, r, r * 1.15),
               M["POD"] if i % 2 == 0 else M["POD_LT"])
        sphere("Cr_PodDot%d" % i, (px + r * 0.3, 0, 3.35 + r * 0.95),
               (0.13, 0.13, 0.13), M["TEAL"])
    # Chitin spine spikes between the pods.
    for i, px in enumerate([1.3, 0.05, -1.25]):
        cone("Cr_Spike%d" % i, (px, 0, 3.6), 0.30, 1.1, M["CHITIN"], vertices=6)
    # Legs: four a side, tilted boxes planted on the mat.
    for side in (1, -1):
        for i, lx in enumerate([2.4, 0.9, -0.7, -2.2]):
            leg = box("Cr_Leg%d_%d" % (side, i), (lx, side * 2.15, 1.30),
                      (0.30, 0.30, 1.30), M["FLESH_DK"],
                      rot=(side * 0.48, 0.0, 0.0))
    # Tail: cone laid onto -X with a pod at its base.
    cone("Cr_Tail", (-3.9, 0, 2.0), 0.70, 2.4, M["FLESH"], rot=(0.0, -math.pi / 2, 0.0), vertices=7)
    sphere("Cr_TailPod", (-3.1, 0, 2.6), (0.55, 0.55, 0.62), M["POD_LT"])


BUILDERS = [
    ("ArenaSporeCrawler", build_spore_crawler),
]


def export_model(name):
    # use_selection=False on purpose (Blender 5.2 GUI
    # exporter AttributeError). At export time the scene
    # holds ONLY this model's objects.
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
        rng = random.Random(__import__("zlib").crc32(name.encode()) & 0xFFFF)
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
        export_model(name)
        print("Exported %s: %d objects" % (name, len(OBJECTS)))
        for o in list(OBJECTS):
            bpy.data.objects.remove(o, do_unlink=True)
    ok = True
    for name, (count, tris, w, d, h, zmin) in results.items():
        # Arena scale checks: the crawler is authored along
        # +X (length), ~9-11 long, ~6-8 wide with legs,
        # ~3.5-5 high, resting on z=0.
        good = count >= 28 and tris <= 20000 and 8.0 <= w <= 11.5 \
            and 5.0 <= d <= 8.5 and 3.2 <= h <= 5.2 and -0.30 <= zmin <= 0.40
        print("  %s %s: objects=%d tris=%d len=%.1f wid=%.1f h=%.1f zmin=%.2f"
              % ("PASS" if good else "FAIL", name, count, tris, w, d, h, zmin))
        ok = ok and good
    print("VERIFY: " + ("ALL PASS" if ok else "FAILURES PRESENT"))


main()
