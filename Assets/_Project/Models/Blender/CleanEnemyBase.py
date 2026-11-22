# CleanEnemyBase.py  (v2 - join + decimate)
# Cleans the CGTrader "building space - base" FBX (tony-feijo) for Lunar Dominion.
#
# What it does:
#   1. Imports the raw EnemyBase.fbx (the seller's whole Cinema 4D scene).
#   2. Deletes the C4D scene junk: Cielo_Fisico (physical sky), Suelo (floor),
#      Cinema_4D_Editor (editor camera) and anything parented under them.
#   3. Bakes every remaining mesh's world transform into its geometry, flattens
#      the hierarchy (the parts were parked ~1.1 million units from the origin),
#      recentres the building on the origin and grounds it at Z = 0.
#   4. JOINS all parts (641 of them in the raw import) into ONE mesh and
#      DECIMATES it to TARGET_TRIS - one draw call, phone-viable.
#   5. Exports F:\EnemyBase_Clean.fbx and writes F:\EnemyBase_Clean_Report.txt
#      with the building's real dimensions and tri counts before/after.
#
# Run it headless from PowerShell (output survives, unlike the GUI console):
#   & "F:\Blender Foundation\Blender 5.2\blender.exe" --background --python "<path>\CleanEnemyBase.py"
# or from Blender's Text Editor (Run Script), like the other build scripts.
# The FBX and the report land at the F:\ root; copy the FBX into
# Assets/_Project/Resources/Models yourself.
#
# NOTE: do NOT raise SystemExit in this script - Blender treats it as a quit
# and closes, GUI and console alike (learned 2026-11-22). Errors raise
# RuntimeError instead, which the Text Editor reports without closing.
#
# House rules honoured: no bpy.context.active_object / selected_objects reads,
# no transform_apply (transforms are baked into mesh data directly), joining
# and decimation are done with plain data APIs (no ops that need a selection).

import os

try:
    import bpy
    import bmesh
    from mathutils import Matrix, Vector
except Exception as exc:
    raise RuntimeError("CleanEnemyBase.py must be run inside Blender.") from exc

# ---------------------------------------------------------------------------
# CONFIG - edit the source path if your project lives somewhere else.
# ---------------------------------------------------------------------------
SOURCE_FBX = r"F:\Unity\Projects\LunarDominion\Assets\_Project\Resources\Models\EnemyBase.fbx"
OUT_FBX = r"F:\EnemyBase_Clean.fbx"
OUT_REPORT = r"F:\EnemyBase_Clean_Report.txt"
TARGET_TRIS = 80000  # decimation goal for the joined mesh

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


def tri_count(mesh):
    mesh.calc_loop_triangles()
    return len(mesh.loop_triangles)


def join_meshes(meshes):
    """Concatenate baked meshes (all at identity transforms) into one mesh.

    Pure data-API join: vertices, loops, polygons, smooth flags and the
    active UV layer (if any) are copied with running offsets. Materials are
    dropped on purpose - the model is untextured clay and Unity assigns the
    camp materials in code.
    """
    verts = []
    loop_vidx = []
    loop_uv = []
    poly_start = []
    poly_total = []
    poly_smooth = []
    any_uv = any(m.uv_layers.active is not None for m in meshes)

    vert_cursor = 0
    loop_cursor = 0
    for mesh in meshes:
        nv = len(mesh.vertices)
        co = [0.0] * (nv * 3)
        mesh.vertices.foreach_get("co", co)
        verts.extend(co)

        nl = len(mesh.loops)
        lv = [0] * nl
        mesh.loops.foreach_get("vertex_index", lv)
        loop_vidx.extend(i + vert_cursor for i in lv)

        if any_uv:
            layer = mesh.uv_layers.active
            if layer is not None:
                uv = [0.0] * (nl * 2)
                layer.data.foreach_get("uv", uv)
                loop_uv.extend(uv)
            else:
                loop_uv.extend([0.0] * (nl * 2))

        for poly in mesh.polygons:
            poly_start.append(loop_cursor + poly.loop_start)
            poly_total.append(poly.loop_total)
            poly_smooth.append(poly.use_smooth)

        vert_cursor += nv
        loop_cursor += nl

    joined = bpy.data.meshes.new("EnemyBase")
    joined.vertices.add(len(verts) // 3)
    joined.vertices.foreach_set("co", verts)
    joined.loops.add(len(loop_vidx))
    joined.loops.foreach_set("vertex_index", loop_vidx)
    joined.polygons.add(len(poly_start))
    joined.polygons.foreach_set("loop_start", poly_start)
    joined.polygons.foreach_set("loop_total", poly_total)
    joined.polygons.foreach_set("use_smooth", poly_smooth)
    if any_uv:
        layer = joined.uv_layers.new(name="UVMap")
        layer.data.foreach_set("uv", loop_uv)
    joined.update()
    joined.validate()
    return joined


def decimate_to(mesh, target_tris):
    """Collapse-decimate the mesh toward target_tris. Returns final tris."""
    current = tri_count(mesh)
    print("Decimate: %d tris, target %d." % (current, target_tris))
    for attempt in range(2):
        if current <= target_tris:
            break
        factor = max(0.02, min(0.95, target_tris / float(current)))
        bm = bmesh.new()
        bm.from_mesh(mesh)
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.decimate(bm, geom=geom, factor=factor)
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        new = tri_count(mesh)
        print("  pass %d: factor %.3f -> %d tris." % (attempt + 1, factor, new))
        if new >= current:  # no progress - stop rather than loop forever
            current = new
            break
        current = new
    return current


def main():
    if not os.path.isfile(SOURCE_FBX):
        raise RuntimeError(
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
        raise RuntimeError("No mesh objects left after cleanup - aborting, nothing exported.")

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

    # -- 6. Join all parts into one mesh, then decimate to TARGET_TRIS. -----
    part_count = len(meshes)
    tris_before = sum(tri_count(o.data) for o in meshes)
    print("Joining %d parts (%d tris)..." % (part_count, tris_before))
    joined = join_meshes([o.data for o in meshes])
    for obj in meshes:
        remove_object(obj)
    tris_after = decimate_to(joined, TARGET_TRIS)

    base_obj = bpy.data.objects.new("EnemyBase", joined)
    bpy.context.scene.collection.objects.link(base_obj)

    # -- 7. Export (scene holds ONLY the building, so export everything). ---
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX,
        use_selection=False,
        object_types={"MESH"},
        apply_unit_scale=True,
    )

    # -- 8. Report. -----------------------------------------------------------
    report = (
        "EnemyBase_Clean - cleanup report (v2 join + decimate)\n"
        "Source: %s\n"
        "Exported: %s\n"
        "Parts joined: %d -> 1 mesh\n"
        "Dimensions (Blender units, cm-authored source): "
        "X %.2f  Y %.2f  Z (height) %.2f\n"
        "Tris before decimation: %d\n"
        "Tris after decimation: %d (target %d)\n"
        "Verts after: %d\n"
        "Deleted junk: %s\n"
        % (
            SOURCE_FBX,
            OUT_FBX,
            part_count,
            size.x,
            size.y,
            size.z,
            tris_before,
            tris_after,
            TARGET_TRIS,
            len(joined.vertices),
            ", ".join(sorted(deleted)) if deleted else "(none)",
        )
    )
    with open(OUT_REPORT, "w", encoding="utf-8") as handle:
        handle.write(report)
    print(report)
    print("Done. FBX: %s  Report: %s" % (OUT_FBX, OUT_REPORT))


main()
