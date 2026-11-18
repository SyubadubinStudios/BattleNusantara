"""Run through Blender MCP. Adds an isolated scene and exports an original game prop."""
import bpy
import os
import math
from pathlib import Path

# The repository root: two folders above this script. When the script is run through exec(), pass __file__ in its globals
# or set NUSANTARA_ROOT.
ROOT = Path(os.environ["NUSANTARA_ROOT"]) if os.environ.get("NUSANTARA_ROOT") else Path(globals().get("__file__", "")).resolve().parents[2]
output = ROOT / "src/BattleNusantara.UI/wwwroot/models"
output.mkdir(parents=True, exist_ok=True)
previous_scene = bpy.context.window.scene
scene = bpy.data.scenes.new("Nusantara_Quantum_Shrine")
bpy.context.window.scene = scene

def material(name, color, metallic=0.0, glow=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    shader = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = 0.58
    shader.inputs["Emission Color"].default_value = (*color, 1)
    shader.inputs["Emission Strength"].default_value = glow
    return mat

stone = material("Andesite", (0.13, 0.22, 0.25))
gold = material("Aged brass", (0.64, 0.44, 0.20), 0.65)
light = material("Quantum jade", (0.18, 0.8, 0.71), 0.3, 2)

def cube(name, location, scale, mat):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(mat)
    bevel = obj.modifiers.new("Worn edges", "BEVEL")
    bevel.width = 0.05
    bevel.segments = 2
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    return obj

try:
    for tier in range(5):
        width = 10 - tier * 1.45
        cube("Terrace_%d" % tier, (0, 0, tier * 0.7 + 0.35), (width, width, 0.7), stone)
        cube("Brass_inlay_%d" % tier, (0, 0, tier * 0.7 + 0.69), (width + 0.03, width + 0.03, 0.05), gold)
    for x, y in [(-3.6, -3.6), (3.6, -3.6), (-3.6, 3.6), (3.6, 3.6)]:
        bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.65, radius2=0.24, depth=1.8, location=(x, y, 2.1))
        bpy.context.object.data.materials.append(stone)
        bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=0.26, location=(x, y, 3.1))
        bpy.context.object.data.materials.append(gold)
    bpy.ops.mesh.primitive_cone_add(vertices=8, radius1=1.7, radius2=0.4, depth=2.5, location=(0, 0, 4.6))
    bpy.context.object.data.materials.append(stone)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.9, location=(0, 0, 6.7))
    core = bpy.context.object
    core.name = "Prasasti_Core"
    core.data.materials.append(light)
    core.rotation_euler.z = 0
    core.keyframe_insert(data_path="rotation_euler", frame=1)
    core.rotation_euler.z = 2 * math.pi
    core.keyframe_insert(data_path="rotation_euler", frame=121)
    scene.frame_end = 120
    for step in range(8):
        cube("Entrance_step", (0, -5.9 + step * .4, .12 + step * .14), (2.2, .8, .24 + step * .28), stone)
    bpy.ops.export_scene.gltf(filepath=str(output / "quantum-shrine.glb"), export_format="GLB", use_selection=False, use_active_scene=True, export_animations=True)
    bpy.data.libraries.write(str(ROOT / "assets/blender/quantum-shrine.blend"), {scene}, fake_user=True)
    print("Exported", output / "quantum-shrine.glb", "objects", len(scene.objects))
finally:
    bpy.context.window.scene = previous_scene
