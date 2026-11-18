"""Run through Blender MCP after create_characters.py. Exports src/BattleNusantara.UI/wwwroot/models/characters-lod.glb:
every character variant decimated to about a third of its triangles, on the same CharacterRig with the same clips.
The power-saving quality draws these; the high quality keeps characters.glb.

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.

The source scene is left as it was: the Decimate modifiers exist only during the export.
"""
import os
import sys
from pathlib import Path

import bpy

ROOT = Path(os.environ["NUSANTARA_ROOT"]) if os.environ.get("NUSANTARA_ROOT") else Path(globals().get("__file__", "")).resolve().parents[2]
OUTPUT = ROOT / "src/BattleNusantara.UI/wwwroot/models"
sys.path.insert(0, str(ROOT / "assets/blender"))
RATIO = .34

scene = bpy.data.scenes.get("Nusantara_Characters")
appended = False
if scene is None:
    # Bring the character scene in from the saved .blend when this Blender session did not build it.
    with bpy.data.libraries.load(str(ROOT / "assets/blender/characters.blend"), link=False) as (source, target):
        target.scenes = ["Nusantara_Characters"]
    scene = target.scenes[0]
    appended = True

rig = next(o for o in scene.objects if o.type == "ARMATURE" and o.name.startswith("CharacterRig"))
meshes = [o for o in scene.objects if o.type == "MESH" and o.parent == rig]
for body in meshes:
    modifier = body.modifiers.new("LOD", "DECIMATE")
    modifier.decimate_type = "COLLAPSE"
    modifier.ratio = RATIO
    modifier.use_collapse_triangulate = True
    # Decimate the rest pose, before the armature deforms it.
    body.modifiers.move(len(body.modifiers) - 1, 0)

depsgraph = scene.view_layers[0].depsgraph
depsgraph.update()
triangles = {}
for body in meshes:
    evaluated = body.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    mesh.calc_loop_triangles()
    triangles[body.name] = len(mesh.loop_triangles)
    evaluated.to_mesh_clear()

path = OUTPUT / "characters-lod.glb"
try:
    with bpy.context.temp_override(scene=scene, view_layer=scene.view_layers[0]):
        options = dict(filepath=str(path), export_format="GLB", use_selection=False, use_active_scene=True, export_yup=True,
                       export_animations=True, export_skins=True, export_materials="EXPORT", export_cameras=False, export_lights=False,
                       export_apply=True, export_animation_mode="NLA_TRACKS", export_force_sampling=True, export_optimize_animation_size=True)
        try:
            bpy.ops.export_scene.gltf(**options, export_texcoords=False, export_vertex_color="ACTIVE")
        except TypeError:
            bpy.ops.export_scene.gltf(**options, export_colors=True)
finally:
    for body in meshes:
        modifier = body.modifiers.get("LOD")
        if modifier:
            body.modifiers.remove(modifier)
    if appended:
        bpy.data.scenes.remove(scene)

print({"variants": len(meshes), "average_triangles": round(sum(triangles.values()) / max(1, len(triangles))), "total": sum(triangles.values()),
       "bytes": path.stat().st_size, "triangles": triangles})
