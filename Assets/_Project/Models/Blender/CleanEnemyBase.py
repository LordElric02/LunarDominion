# CleanEnemyBase.py
# Cleans the CGTrader "building space - base" FBX (tony-feijo) for Lunar Dominion.
#
# What it does:
#   1. Imports the raw EnemyBase.fbx (the seller's whole Cinema 4D scene).
#   2. Deletes the C4D scene junk: Cielo_Fisico (physical sky), Suelo (floor),
#      Cinema_4D_Editor (editor camera) and anything parented under them.
#   3. Bakes every remaining mesh's world transform into its geometry, flattens
#      the hierarchy (the parts were parked ~1.1 million units from the origin),
#      recentres the building on the origin and grounds it at Z = 0.
#   4. Exports F:\EnemyBase_Clean.fbx and writes F:\EnemyBase_Clean_Report.txt
#      with the building's real dimensions and poly counts.
#
# Run it from Blender's Text Editor (Run Script), like the other build scripts.
# The FBX and the report land at the F:\ root; copy the FBX into
# Assets/_Project/Resources/Models yourself, replacing the raw import.
#
# House rules honoured: no bpy.context.active_object / selected_objects reads,
# no transform_apply (transforms are baked into mesh data directly).

import os

try:
    import bpy
    from mathutils import Matrix, Vector
except Exception:
    raise SystemExit("CleanEnemyBase.py must be run inside Blender.")

# ---------------------------------------------------------------------------
# CONFIG - edit the source path if your project lives somewhere else.
# ---------------------------------------------------------------------------
SOURCE_FBX = r"F:\Unity\Projects\LunarDominion\Assets\_Project\Resources\Models\EnemyBase.fbx"
OUT_FBX = r"F:\EnemyBase_Clean.fbx"
OUT_REPORT = r"F:\EnemyBase_Clean_Report.txt"

# C4D scene junk, matched by name prefix. Everything parented under a junk
# object goes with it.
JUNK_PREFIXES = ("Cielo_Fisico", "Cinema_4D_Editor", "Suelo")


def descendants(obj):
    """All objects below obj in the hierarchy (obj itself not included)."""
    out = []
    stack = list(obj.children)
    while stack:
        child = stack.pop()
        out.append(child)
        stack.extend(child.children)
    return out


def remove_object(obj):
    data = obj.data if obj.type == "MESH" else None
    bpy.data.objects.remove(obj, do_unlink=True)
    if data is not None and data.users == 0:
        bpy.data.meshes.remove(data)


def world_bounds(objects):
    """Combined world-space bounds of the given mesh objects."""
    mins = Vector((float("inf"),) * 3)
    maxs = Vector((float("-inf"),) * 3)
    for obj in objects:
        for corner in obj.bound_box:
            world = obj.matrix_world @ Vector(corner)
            mins = Vector(map(min, mins, world))
            maxs = Vector(map(max, maxs, world))
    return mins, maxs


def main():
    if not os.path.isfile(SOURCE_FBX):
        raise SystemExit(
            "Source FBX not found:\n  %s\nEdit SOURCE_FBX at the top of this script." % SOURCE_FBX
        )

    # -- 1. Start from an empty scene (data API only, no context reads). ----
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)

    # -- 2. Import the raw FBX. ---------------------------------------------
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=SOURCE_FBX)
    imported = [o for o in bpy.data.objects if o not in before]
    print("Imported %d objects." % len(imported))

    # -- 3. Delete the C4D junk (sky, floor, editor camera) + their kids. ---
    deleted = []
    for obj in list(bpy.data.objects):
        if obj.name.startswith(JUNK_PREFIXES):
            for child in descendants(obj):
                deleted.append(child.name)
                remove_object(child)
            deleted.append(obj.name)
            remove_object(obj)
    print("Deleted junk: %s" % (", ".join(sorted(deleted)) if deleted else "(none found)"))

    # -- 4. Keep only meshes; bake world transforms into the geometry. ------
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit("No mesh objects left after cleanup - aborting, nothing exported.")

    baked_data = set()
    for obj in meshes:
        if obj.data.name in baked_data:
            obj.data = obj.data.copy()  # shared mesh data: give it its own copy
        obj.data.transform(obj.matrix_world)
        baked_data.add(obj.data.name)

    # Flatten: drop every non-mesh object (the Nulo_ nulls etc.) and reset
    # each mesh to the identity transform - its geometry is already in place.
    for obj in list(bpy.data.objects):
        if obj.type != "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)
    for obj in meshes:
        if obj.parent is not None:
            obj.parent = None
        obj.matrix_world = Matrix.Identity(4)

    # -- 5. Recentre on the origin and ground at Z = 0. ----------------------
    mins, maxs = world_bounds(meshes)
    centre = (mins + maxs) * 0.5
    shift = Matrix.Translation(Vector((-centre.x, -centre.y, -mins.z)))
    for obj in meshes:
        obj.data.transform(shift)

    mins, maxs = world_bounds(meshes)
    size = maxs - mins

    # -- 6. Export (scene holds ONLY the building, so export everything). ---
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX,
        use_selection=False,
        object_types={"MESH"},
        apply_unit_scale=True,
    )

    # -- 7. Report. -----------------------------------------------------------
    lines = []
    total_tris = 0
    total_verts = 0
    for obj in sorted(meshes, key=lambda o: o.name):
        mesh = obj.data
        mesh.calc_loop_triangles()
        tris = len(mesh.loop_triangles)
        total_tris += tris
        total_verts += len(mesh.vertices)
        lines.append("  %-24s %8d tris  %8d verts" % (obj.name, tris, len(mesh.vertices)))

    report = (
        "EnemyBase_Clean - cleanup report\n"
        "Source: %s\n"
        "Exported: %s\n"
        "Parts kept: %d\n"
        "Dimensions (Blender units = metres at Unity scale 1): "
        "X %.2f  Y %.2f  Z (height) %.2f\n"
        "Totals: %d tris, %d verts\n"
        "Deleted junk: %s\n\n"
        "Parts:\n%s\n"
        % (
            SOURCE_FBX,
            OUT_FBX,
            len(meshes),
            size.x,
            size.y,
            size.z,
            total_tris,
            total_verts,
            ", ".join(sorted(deleted)) if deleted else "(none)",
            "\n".join(lines),
        )
    )
    with open(OUT_REPORT, "w", encoding="utf-8") as handle:
        handle.write(report)
    print(report)
    print("Done. FBX: %s  Report: %s" % (OUT_FBX, OUT_REPORT))


main()
