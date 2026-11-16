"""MakeTerrainNormalMap.py - bakes ExteriorTerrainNormal.png
(the terrain normal map, graphics step 3) from the V13
terrain script's own road data, so the cobble relief always
matches the tint's roads. Pure numpy + Pillow. It can run
in Blender's Text Editor, but Blender's bundled Python has
numpy only - Pillow must be installed into it once, from
PowerShell:

    & "F:\\Blender Foundation\\Blender 5.2\\5.2\\python\\bin\\python.exe" -m pip install pillow

Then Run Script works. (A system Python with numpy +
Pillow installed works too.)

It writes ExteriorTerrainNormal.png next to itself; copy
that into Assets/_Project/Resources/Textures (keep the
name) and set its Texture Type to Normal Map once.
Relief: fBm grain on the fields + domed cobblestones.
Seeded and deterministic within one environment (the
grain rides on Pillow's resize, whose rounding varies
slightly between Pillow versions).
"""
import numpy as np
from PIL import Image
import os
import sys, types

# Own directory. Under Blender's Text Editor __file__ is
# unreliable (it resolves to the drive root), so fall back
# to the text block's real filepath when the terrain
# script isn't found beside __file__.
_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if not os.path.exists(os.path.join(_SCRIPT_DIR, 'BuildExteriorTerrainV13.py')):
    try:
        import bpy as _bpy
        for _t in _bpy.data.texts:
            if _t.name.startswith('MakeTerrainNormalMap') and _t.filepath:
                _SCRIPT_DIR = os.path.dirname(os.path.abspath(_t.filepath))
                break
    except Exception:
        pass
SRC = os.path.join(_SCRIPT_DIR, 'BuildExteriorTerrainV13.py')
class _Dummy:
    def __init__(self, *a, **k): pass
    def __call__(self, *a, **k): return _Dummy()
# Stub the Blender modules ONLY when running outside
# Blender (plain Python). Inside Blender they are real and
# must never be touched: an earlier version overwrote the
# live mathutils module's Vector/Matrix with _Dummy even
# in Blender, which poisoned the whole session - every
# later script doing "from mathutils import Matrix" got
# the dummy and died (BuildMapSitesV6, 2026-11-16). A
# session poisoned by the old version needs a Blender
# restart; this version cannot poison one.
try:
    import bpy as _probe_bpy
    _IN_BLENDER = True
except Exception:
    _IN_BLENDER = False
if not _IN_BLENDER:
    for _m in ('bpy', 'bmesh'):
        sys.modules.setdefault(_m, types.ModuleType(_m))
    _mu = types.ModuleType('mathutils')
    _mu.Vector = _Dummy
    _mu.Matrix = _Dummy
    sys.modules['mathutils'] = _mu
ns = {'__file__': SRC, '__name__': 'terrain_v13_defs'}
with open(SRC) as f:
    _code = f.read()
assert _code.rstrip().endswith('main()')
_code = _code.rstrip()[:-len('main()')]  # script ends with a bare main() call
exec(compile(_code, SRC, 'exec'), ns)
_ss = ns['_smoothstep_np']
_segments = ns['_road_draw_segments']()
_junctions = ns['ROAD_JUNCTIONS']

W = H = 4096
rng = np.random.default_rng(20261116)

# ---- grain: fBm value noise, octaves in world-unit cells ----
grain = np.zeros((H, W), dtype=np.float32)
for cell_u, amp in ((24.0, 0.30), (12.0, 0.26), (6.0, 0.22),
                    (3.0, 0.18), (1.5, 0.14), (0.75, 0.10)):
    nx = max(2, int(1120.0 / cell_u)); nz = max(2, int(960.0 / cell_u))
    grid = rng.random((nz + 1, nx + 1), dtype=np.float32)
    im = Image.fromarray(grid, mode='F').resize((W, H), Image.BILINEAR)
    grain += np.asarray(im, dtype=np.float32) * amp
    del im, grid
grain -= grain.mean()
print('grain range', float(grain.min()), float(grain.max()))

# ---- cobble dome height, chunked over rows ----
cob = np.zeros((H, W), dtype=np.float32)
cs = 1.25
for r0 in range(0, H, 512):
    r1 = min(H, r0 + 512)
    rows = np.arange(r0, r1, dtype=np.float32)
    cols = np.arange(W, dtype=np.float32)
    xx = ((cols[None, :] + 0.5) / W * 1120.0 - 560.0).astype(np.float32)
    zz = (480.0 - (rows[:, None] + 0.5) / H * 960.0).astype(np.float32)
    rdist = np.full((r1 - r0, W), 1e9, dtype=np.float32)
    for ((ax, az), (bx, bz)) in _segments:
        if (max(ax, bx) < float(xx.min()) - 8.0 or min(ax, bx) > float(xx.max()) + 8.0
                or max(az, bz) < float(zz.min()) - 8.0 or min(az, bz) > float(zz.max()) + 8.0):
            continue
        dx, dz = bx - ax, bz - az
        L2 = dx * dx + dz * dz
        tt = np.clip(((xx - ax) * dx + (zz - az) * dz) / L2, 0.0, 1.0)
        rdist = np.minimum(rdist, np.hypot(xx - (ax + tt * dx), zz - (az + tt * dz)))
    for (jx, jz) in _junctions:
        if jx < float(xx.min()) - 10.0 or jx > float(xx.max()) + 10.0 \
                or jz < float(zz.min()) - 10.0 or jz > float(zz.max()) + 10.0:
            continue
        rdist = np.minimum(rdist, np.hypot(xx - jx, zz - jz) - 2.5)
    ralpha = (1.0 - _ss(2.35, 3.05, rdist))
    row = np.floor(zz / cs)
    cu = xx / cs + 0.5 * np.mod(row, 2.0)
    cv = zz / cs
    cellx = np.floor(cu); cellz = np.floor(cv)
    fx = cu - cellx; fz = cv - cellz
    bord = np.minimum(np.minimum(fx, 1.0 - fx), np.minimum(fz, 1.0 - fz)) * cs
    dome = _ss(0.0, 0.30, bord)
    hsh = np.sin(cellx * 127.1 + cellz * 311.7) * 43758.5453
    jitter = 0.85 + 0.30 * (hsh - np.floor(hsh))
    cob[r0:r1] = (ralpha * (0.25 + 0.75 * dome) * jitter).astype(np.float32)
    del xx, zz, rdist, ralpha
print('cobble coverage', float((cob > 0.01).mean()))

# ---- combine (heights in ~world units) + gradients ----
height = grain * 0.60 + cob * 0.085
del grain, cob
gx = np.gradient(height, axis=1) / (1120.0 / W)   # dh/dx
gz = -np.gradient(height, axis=0) / (960.0 / H)   # dh/dz (row+ = z-)
del height
S = 5.0
nx = -gx * S; ny = -gz * S; nz = np.ones_like(nx)
del gx, gz
inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
rgb = np.stack([(nx * inv * 0.5 + 0.5), (ny * inv * 0.5 + 0.5),
                (nz * inv * 0.5 + 0.5)], axis=-1)
out = os.path.join(_SCRIPT_DIR, 'ExteriorTerrainNormal.png')
Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8)).save(out)
print('saved', out, os.path.getsize(out))

# hillshade sanity image (light from NW)
shade = np.clip(0.55 + (nx * -0.55 + ny * 0.75 + nz * 0.35) * 0.45, 0, 1)
Image.fromarray((shade * 255).astype(np.uint8)).resize((1024, 1024)).save(
    os.path.join(_SCRIPT_DIR, 'normal_hillshade.png'))
print('hillshade saved')
