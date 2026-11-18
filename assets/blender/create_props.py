"""Run through Blender MCP. Builds the static props of the game and exports
src/BattleNusantara.UI/wwwroot/models/props.glb with one node per prop:
tower, outpost, relic, artifact, ore, flotsam.

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios. Original low-poly designs.

Like the characters, colours are vertex colours; parts on the "Tint" material take the team or unit colour.
Each prop stands on Z = 0 and faces -Y.
"""
import bpy
import os
import math
from pathlib import Path

# The repository root: two folders above this script. When the script is run through exec(), pass __file__ in its globals
# or set NUSANTARA_ROOT.
ROOT = Path(os.environ["NUSANTARA_ROOT"]) if os.environ.get("NUSANTARA_ROOT") else Path(globals().get("__file__", "")).resolve().parents[2]
OUTPUT = ROOT / "src/BattleNusantara.UI/wwwroot/models"

for stale in [s for s in bpy.data.scenes if s.name == "Nusantara_Props"]:
    for obj in list(stale.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.scenes.remove(stale)
previous = bpy.context.window.scene
scene = bpy.data.scenes.new("Nusantara_Props")
bpy.context.window.scene = scene


def hexcolor(value):
    value = value.lstrip("#")
    srgb = [int(value[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in srgb)


def shared_material(name):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    tree = mat.node_tree
    shader = next(n for n in tree.nodes if n.type == "BSDF_PRINCIPLED")
    attribute = next((n for n in tree.nodes if n.type == "VERTEX_COLOR"), None) or tree.nodes.new("ShaderNodeVertexColor")
    attribute.layer_name = "Col"
    tree.links.new(attribute.outputs["Color"], shader.inputs["Base Color"])
    return mat


TINT, FIXED = shared_material("Tint"), shared_material("Fixed")
parts = []


def finish(obj, color, tint=False):
    obj.data.materials.append(TINT if tint else FIXED)
    attribute = obj.data.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    rgb = hexcolor(color)
    for item in attribute.data:
        item.color = (*rgb, 1.0)
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    parts.append(obj)
    return obj


def box(location, size, color, tint=False, rotation=(0, 0, 0), taper=1.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if taper != 1.0:
        for vertex in obj.data.vertices:
            if vertex.co.z > 0:
                vertex.co.x *= taper
                vertex.co.y *= taper
    return finish(obj, color, tint)


def cone(location, r1, r2, depth, color, vertices=8, rotation=(0, 0, 0), tint=False):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=r1, radius2=r2, depth=depth, location=location, rotation=rotation)
    return finish(bpy.context.object, color, tint)


def ball(location, radius, color, tint=False, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=radius, location=location)
    obj = bpy.context.object
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, color, tint)


def join(name):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    prop = bpy.context.object
    prop.name = name
    prop.data.name = name
    prop.data.color_attributes.active_color = prop.data.color_attributes["Col"]
    prop.data.color_attributes.render_color_index = prop.data.color_attributes.find("Col")
    parts.clear()
    return prop


stone, dark_stone, wood, thatch, gold = "#8a8f86", "#5d625b", "#6a4a2a", "#a88a52", "#d4b478"

# Tower: a stepped candi tower, about 9 units tall at game scale (the game scales it by 1).
for tier in range(4):
    width = 3.4 - tier * 0.6
    box((0, 0, 0.6 + tier * 1.6), (width, width, 1.4), stone if tier % 2 == 0 else dark_stone, taper=0.88)
    box((0, 0, 1.35 + tier * 1.6), (width + 0.2, width + 0.2, 0.18), gold if tier == 3 else dark_stone)
box((0, -1.72, 0.9), (0.9, 0.12, 1.4), "#2a2a2a")  # doorway
cone((0, 0, 7.3), 0.8, 0.0, 1.6, stone, vertices=8)
ball((0, 0, 8.3), 0.45, gold, tint=True)
for sign in (1, -1):
    box((1.2 * sign, -1.75, 2.3), (0.5, 0.1, 0.5), gold, tint=True)
join("tower")

# Outpost: a pendopo, open hall on four posts with a joglo roof, about 6 by 6.
box((0, 0, 0.2), (5.2, 5.2, 0.4), stone)
for sx in (1, -1):
    for sy in (1, -1):
        box((2 * sx, 2 * sy, 1.6), (0.3, 0.3, 2.4), wood)
cone((0, 0, 3.2), 3.9, 2.0, 0.9, thatch, vertices=4, rotation=(0, 0, math.pi / 4))
cone((0, 0, 4.2), 2.0, 0.25, 1.4, thatch, vertices=4, rotation=(0, 0, math.pi / 4))
box((0, 0, 4.95), (0.5, 0.5, 0.3), gold, tint=True)
box((0, -2.75, 2.7), (1.6, 0.08, 0.5), gold, tint=True)  # banner board
box((1.4, 1.2, 0.6), (0.7, 0.7, 0.4), "#a07a44")  # supply crates
box((1.3, 0.4, 0.55), (0.5, 0.5, 0.3), "#8a6a3a")
join("outpost")

# Relic: a prasasti, an engraved stone stele with a rounded top and a glowing inscription.
box((0, 0, 0.15), (1.4, 0.9, 0.3), dark_stone)
box((0, 0, 1.2), (1.0, 0.35, 1.9), stone, taper=0.92)
cone((0, 0, 2.25), 0.52, 0.2, 0.3, stone, vertices=6, rotation=(math.pi / 2, 0, 0))
for line in range(4):
    box((0, -0.19, 0.7 + line * 0.32), (0.7 - line * 0.08, 0.02, 0.06), "#f2d27a", tint=True)
join("relic")

# Artifact: the Jagat Heirloom Keris, standing point down above a small stone.
blade = box((0, 0, 1.1), (0.12, 0.04, 1.3), "#d8dde0")
for vertex in blade.data.vertices:
    vertex.co.x += 0.04 * math.sin(vertex.co.z * 9)
box((0, 0, 1.85), (0.36, 0.08, 0.1), gold)
box((0, 0, 2.15), (0.1, 0.1, 0.5), "#5a3a22", rotation=(0, 0.25, 0))
cone((0, 0, 0.15), 0.5, 0.35, 0.3, dark_stone, vertices=6)
join("artifact")

# Ore: a cluster of rock with gold veins.
for k, (x, y, r) in enumerate([(0, 0, 1.1), (0.9, 0.4, 0.7), (-0.8, 0.5, 0.6), (0.2, -0.8, 0.55)]):
    ball((x, y, r * 0.6), r, dark_stone if k % 2 else stone, scale=(1, 1, 0.8))
for k, (x, y, z) in enumerate([(0.3, -0.6, 0.9), (-0.4, -0.3, 1.2), (0.7, -0.1, 0.7), (-0.9, 0.0, 0.5)]):
    cone((x, y, z), 0.22, 0.0, 0.5, gold, vertices=4, rotation=(0.4 * k, 0.6, 0.2 * k))
join("ore")

# Flotsam: broken planks around a sea chest.
box((0, 0, 0.3), (0.9, 0.6, 0.55), "#7a5230")
box((0, 0, 0.62), (0.95, 0.65, 0.1), "#5a3a22")
box((0, -0.31, 0.35), (0.16, 0.04, 0.2), gold)
for k in range(4):
    angle = k * 1.4
    box((1.0 * math.cos(angle), 1.0 * math.sin(angle), 0.06), (1.3, 0.22, 0.08), "#8a6a44", rotation=(0, 0, angle + 0.6))
join("flotsam")

props = [o for o in scene.objects if o.type == "MESH"]
with bpy.context.temp_override(scene=scene, view_layer=scene.view_layers[0]):
    options = dict(filepath=str(OUTPUT / "props.glb"), export_format="GLB", use_selection=False, use_active_scene=True, export_yup=True,
                   export_animations=False, export_materials="EXPORT", export_cameras=False, export_lights=False)
    try:
        bpy.ops.export_scene.gltf(**options, export_vertex_color="ACTIVE")
    except TypeError:
        bpy.ops.export_scene.gltf(**options, export_colors=True)
bpy.data.libraries.write(str(ROOT / "assets/blender/props.blend"), {scene}, fake_user=True)
bpy.context.window.scene = previous
result = {"props": sorted(p.name for p in props), "triangles": {p.name: sum(len(f.vertices) - 2 for f in p.data.polygons) for p in props}, "bytes": (OUTPUT / "props.glb").stat().st_size}
print(result)
