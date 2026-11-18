"""Run through Blender MCP. Builds the Quantum Shrine, the centrepiece of the menu and the two bases, and exports
src/BattleNusantara.UI/wwwroot/models/quantum-shrine.glb (one animation: the floating core turns).

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios. An original design inspired by Javanese candi architecture.

v0.14: square terraces with balustrades and carved panels, gates and stairs on four sides, three round terraces of
lattice stupas, the main stupa with its spire, and the floating quantum core. Stone colours are vertex colours with
baked ambient occlusion; the core and the inlays use the emissive "Glow" material. Footprint about 10 x 10 m.
"""
import importlib
import math
import os
import sys
from pathlib import Path

import bpy

# The repository root: two folders above this script. When the script is run through exec(), pass __file__ in its globals
# or set NUSANTARA_ROOT.
ROOT = Path(os.environ["NUSANTARA_ROOT"]) if os.environ.get("NUSANTARA_ROOT") else Path(globals().get("__file__", "")).resolve().parents[2]
OUTPUT = ROOT / "src/BattleNusantara.UI/wwwroot/models"
OUTPUT.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(ROOT / "assets/blender"))
import nusantara_kit as kit  # noqa: E402

importlib.reload(kit)
PI, TAU = math.pi, math.tau

previous = bpy.context.window.scene if bpy.context.window else None
scene = kit.fresh_scene("Nusantara_Quantum_Shrine")
scene.frame_end = 120
TINT, FIXED = kit.materials()
STONE, STONE_DARK, STONE_LIGHT, MOSS, GOLD = "#7f8a86", "#56605c", "#9aa39c", "#5f7c4c", "#c9a24a"


def glow_material():
    mat = bpy.data.materials.get("Glow") or bpy.data.materials.new("Glow")
    mat.use_nodes = True
    shader = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    shader.inputs["Base Color"].default_value = (0.18, 0.8, 0.71, 1)
    shader.inputs["Emission Color"].default_value = (0.25, 0.95, 0.82, 1)
    shader.inputs["Emission Strength"].default_value = 3.0
    shader.inputs["Roughness"].default_value = 0.3
    return mat


GLOW = glow_material()
b = kit.Builder(TINT, FIXED, scene.collection)


def rotated(x, y, side):
    a = side * PI / 2
    return x * math.cos(a) - y * math.sin(a), x * math.sin(a) + y * math.cos(a)


def stupa(at, size, lattice=False):
    x, y, z = at
    b.add(kit.bm_loft([(0, .52 * size, .52 * size), (.1 * size, .48 * size, .48 * size), (.18 * size, .44 * size, .44 * size)], segments=8, power=2.2), STONE_DARK, at=(x, y, z))
    bell = [(.18 * size, .44 * size, .44 * size), (.45 * size, .46 * size, .46 * size), (.75 * size, .36 * size, .36 * size), (.92 * size, .18 * size, .18 * size)]
    b.add(kit.bm_loft(bell, segments=8, power=2.0 if not lattice else 3.2), STONE if not lattice else STONE_LIGHT, at=(x, y, z))
    b.add(kit.bm_box((.22 * size, .22 * size, .14 * size)), STONE_DARK, at=(x, y, z + .98 * size))
    b.add(kit.bm_loft([(0, .07 * size, .07 * size), (.4 * size, .02 * size, .02 * size)], segments=6, power=2), STONE_LIGHT, at=(x, y, z + 1.05 * size))


# Five square terraces, each with a balustrade of carved panels and small corner stupas.
z = 0.0
for tier in range(5):
    w = 10 - tier * 1.5
    h = .62
    b.add(kit.bm_box((w, w, h)), STONE if tier % 2 == 0 else STONE_LIGHT, at=(0, 0, z + h / 2), bevel=.05, angle=30)
    b.add(kit.bm_box((w + .08, w + .08, .1)), STONE_DARK, at=(0, 0, z + .05), bevel=.02, bevel_segments=1)
    # Relief band: recessed panels on the riser.
    panels = max(3, int(w / 1.2))
    for side in range(4):
        for k in range(panels):
            offset = -w / 2 + (k + .5) * w / panels
            if abs(offset) < .9:
                continue  # the stair passes here
            px, py = rotated(offset, -w / 2 - .005, side)
            b.add(kit.bm_box((w / panels * .7, .05, h * .5)), STONE_DARK, at=(px, py, z + h * .5), rotation=(0, 0, side * PI / 2), smooth=False)
    if tier < 4:
        # Balustrade on the edge of the next level.
        bw = w - .35
        for side in range(4):
            for half in (-1, 1):
                length = bw / 2 - 1.0
                cx = half * (1.0 + length / 2)
                px, py = rotated(cx, -bw / 2, side)
                b.add(kit.bm_box((length, .22, .45)), STONE_LIGHT, at=(px, py, z + h + .225), rotation=(0, 0, side * PI / 2), bevel=.03, bevel_segments=1, angle=30)
        if tier % 2 == 0:
            for sx in (-1, 1):
                for sy in (-1, 1):
                    stupa((sx * (bw / 2), sy * (bw / 2), z + h + .45), .55)
    z += h
top_square = z
# Stairs and gates on the four sides.
for side in range(4):
    for step in range(10):
        depth = 10 / 2 - step * .48
        px, py = rotated(0, -depth, side)
        b.add(kit.bm_box((1.5, .5, .31)), STONE_LIGHT if step % 2 else STONE, at=(px, py, step * .31 + .155), rotation=(0, 0, side * PI / 2), bevel=.015, bevel_segments=1, angle=30)
    for tier in range(4):
        w = 10 - tier * 1.5
        gz = tier * .62 + .62
        for half in (-1, 1):
            px, py = rotated(half * .9, -(w - .35) / 2, side)
            b.add(kit.bm_box((.3, .3, 1.1)), STONE, at=(px, py, gz + .55), rotation=(0, 0, side * PI / 2), bevel=.03, bevel_segments=1, angle=30)
        px, py = rotated(0, -(w - .35) / 2, side)
        b.add(kit.bm_box((2.1, .34, .32)), STONE_LIGHT, at=(px, py, gz + 1.25), rotation=(0, 0, side * PI / 2), bevel=.03, bevel_segments=1, angle=30)
        b.add(kit.bm_loft([(0, .3, .08), (.22, .36, .1), (.38, .14, .06)], segments=10, power=2.6), GOLD, at=(px, py, gz + 1.38), rotation=(0, 0, side * PI / 2))
# Three round terraces of lattice stupas around the main stupa.
z = top_square
for ring, (radius, count) in enumerate([(3.15, 12), (2.35, 9), (1.6, 6)]):
    b.add(kit.bm_loft([(z, radius + .45, radius + .45), (z + .32, radius + .42, radius + .42)], segments=32, power=2), STONE if ring % 2 == 0 else STONE_LIGHT, angle=30)
    for k in range(count):
        a = k / count * TAU
        stupa((radius * math.cos(a), radius * math.sin(a), z + .32), .62 - ring * .05, lattice=True)
    z += .32
b.add(kit.bm_loft([(z, 1.15, 1.15), (z + .15, 1.12, 1.12), (z + .6, 1.18, 1.18), (z + 1.2, .98, .98), (z + 1.65, .55, .55), (z + 1.8, .3, .3)], segments=24, power=2), STONE_LIGHT, angle=40)
b.add(kit.bm_box((.5, .5, .3)), STONE_DARK, at=(0, 0, z + 1.95), bevel=.02, bevel_segments=1)
b.add(kit.bm_loft([(z + 2.1, .18, .18), (z + 2.6, .12, .12), (z + 2.62, .2, .2), (z + 2.7, .2, .2), (z + 3.0, .08, .08), (z + 3.02, .14, .14), (z + 3.08, .14, .14), (z + 3.5, .02, .02)], segments=10, power=2), GOLD)
spire_top = z + 3.5
# Gold inlay lines that the bloom pass picks up.
for tier in range(5):
    w = 10 - tier * 1.5
    b.add(kit.bm_loft([(tier * .62 + .56, w / 2 + .012, w / 2 + .012), (tier * .62 + .6, w / 2 + .012, w / 2 + .012)], segments=4, power=40), GOLD, angle=10)
# Moss creeping over the lowest terrace.
for k in range(14):
    a = k * 2.399
    r = 4.6 + (k % 3) * .25
    b.add(kit.bm_sphere(.35 + (k % 4) * .08, 8, 5, scale=(1.4, 1.0, .35)), MOSS, at=(r * math.cos(a), r * math.sin(a), .64 + (k % 2) * .62))
shrine = b.join("Quantum_Shrine")
kit.bake_ao([shrine], scene, strength=.75, samples=128, distance=1.5)

# The floating core and its rings: separate objects, so the glTF keeps the turning animation.
core_bm = kit.bm_ico(.9, 2)
mesh = bpy.data.meshes.new("Prasasti_Core")
core_bm.to_mesh(mesh)
core_bm.free()
core = bpy.data.objects.new("Prasasti_Core", mesh)
scene.collection.objects.link(core)
mesh.materials.append(GLOW)
core.location = (0, 0, spire_top + 1.3)
for k, (radius, tilt) in enumerate([(1.45, .5), (1.75, -.35)]):
    ring_bm = kit.bm_torus(radius, .045, 48, 6)
    ring_mesh = bpy.data.meshes.new(f"Core_Ring_{k}")
    ring_bm.to_mesh(ring_mesh)
    ring_bm.free()
    ring = bpy.data.objects.new(f"Core_Ring_{k}", ring_mesh)
    scene.collection.objects.link(ring)
    ring_mesh.materials.append(GLOW)
    ring_mesh.shade_smooth()
    ring.parent = core
    ring.rotation_euler = (tilt, k * .6, 0)
core.rotation_euler.z = 0
core.keyframe_insert(data_path="rotation_euler", frame=1)
core.rotation_euler.z = TAU
core.keyframe_insert(data_path="rotation_euler", frame=121)
# A steady spin: linear keys, so the loop has no ease at its seam. Blender 4.4+ keeps F-curves in channel bags.
action = core.animation_data.action
curves = list(getattr(action, "fcurves", []) or [])
for layer in getattr(action, "layers", []):
    for strip in layer.strips:
        for bag in getattr(strip, "channelbags", []):
            curves.extend(bag.fcurves)
for fcurve in curves:
    for point in fcurve.keyframe_points:
        point.interpolation = "LINEAR"

with bpy.context.temp_override(scene=scene, view_layer=scene.view_layers[0]):
    options = dict(filepath=str(OUTPUT / "quantum-shrine.glb"), export_format="GLB", use_selection=False, use_active_scene=True, export_yup=True,
                   export_animations=True, export_materials="EXPORT", export_cameras=False, export_lights=False)
    try:
        bpy.ops.export_scene.gltf(**options, export_vertex_color="ACTIVE")
    except TypeError:
        bpy.ops.export_scene.gltf(**options, export_colors=True)
bpy.data.libraries.write(str(ROOT / "assets/blender/quantum-shrine.blend"), {scene}, fake_user=True)
try:
    if previous and bpy.context.window:
        bpy.context.window.scene = previous
except ReferenceError:
    pass
result = {"triangles": kit.triangles(shrine), "bytes": (OUTPUT / "quantum-shrine.glb").stat().st_size, "height": round(spire_top + 2.2, 2)}
print(result)
