"""Shared modelling kit for the Battle of Nusantara Blender scripts.

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.

Parts are built with bmesh (no viewport operators except join), refined with subdivision and bevels, shaded
smooth with sharp edges by angle, and coloured through a vertex colour attribute "Col". Two shared materials:
"Fixed" shows the vertex colour; "Tint" parts are painted white so the game can multiply them by a unit colour.
bake_ao() bakes ambient occlusion with Cycles and multiplies it into "Col", so the game gets contact shadows and
crevices without textures.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

TAU = math.tau


def hexcolor(value):
    value = value.lstrip("#")
    srgb = [int(value[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in srgb)


def shared_material(name, roughness=0.62, metallic=0.05):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    tree = mat.node_tree
    shader = next(n for n in tree.nodes if n.type == "BSDF_PRINCIPLED")
    shader.inputs["Roughness"].default_value = roughness
    shader.inputs["Metallic"].default_value = metallic
    attribute = next((n for n in tree.nodes if n.type == "VERTEX_COLOR"), None) or tree.nodes.new("ShaderNodeVertexColor")
    attribute.layer_name = "Col"
    tree.links.new(attribute.outputs["Color"], shader.inputs["Base Color"])
    return mat


def materials():
    return shared_material("Tint"), shared_material("Fixed")


def fresh_scene(name):
    for stale in [s for s in bpy.data.scenes if s.name == name]:
        for obj in list(stale.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.scenes.remove(stale)
    scene = bpy.data.scenes.new(name)
    if bpy.context.window:
        bpy.context.window.scene = scene
    scene.render.fps = 24
    return scene


# ------------------------------------------------------------------------------------------------ bmesh primitives
def _orient(bm, start, end):
    """Rotates a mesh built along +Z (from 0 to its length) so that it runs from start to end."""
    start, end = Vector(start), Vector(end)
    axis = end - start
    rotation = Vector((0, 0, 1)).rotation_difference(axis.normalized()).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation(start) @ rotation, verts=bm.verts)


def bm_box(size, taper=1.0, taper_axis="z"):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= size[0]
        v.co.y *= size[1]
        v.co.z *= size[2]
        if taper != 1.0 and v.co.z < 0:
            v.co.x *= taper
            v.co.y *= taper
    return bm


def bm_sphere(radius, segments=16, rings=10, scale=(1, 1, 1)):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=radius)
    for v in bm.verts:
        v.co.x *= scale[0]
        v.co.y *= scale[1]
        v.co.z *= scale[2]
    return bm


def bm_ico(radius, subdivisions=2, scale=(1, 1, 1)):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdivisions, radius=radius)
    for v in bm.verts:
        v.co.x *= scale[0]
        v.co.y *= scale[1]
        v.co.z *= scale[2]
    return bm


def bm_tube(start, end, r0, r1, segments=12, caps=True, squash=1.0):
    """A tapered cylinder from start to end; squash flattens it front to back (for limbs)."""
    length = (Vector(end) - Vector(start)).length
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=segments, radius1=r0, radius2=r1, depth=length)
    for v in bm.verts:
        v.co.z += length / 2
        v.co.y *= squash
    _orient(bm, start, end)
    return bm


def bm_cone(r0, r1, depth, segments=16, caps=True):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=segments, radius1=r0, radius2=r1, depth=depth)
    return bm


def bm_torus(major, minor, segments=24, sides=8):
    bm = bmesh.new()
    rings = []
    for i in range(segments):
        a = i / segments * TAU
        ring = []
        for j in range(sides):
            b = j / sides * TAU
            r = major + minor * math.cos(b)
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), minor * math.sin(b))))
        rings.append(ring)
    for i in range(segments):
        for j in range(sides):
            a, b = rings[i], rings[(i + 1) % segments]
            bm.faces.new((a[j], b[j], b[(j + 1) % sides], a[(j + 1) % sides]))
    return bm


def bm_blade(length, width, thickness, sections=10, wave=0.0, waves=0, curve=0.0, tip=0.18):
    """A double-edged blade along -Y from the origin: diamond cross-section, tapering to a point.
    wave/waves make a keris luk; curve bends it sideways (rencong, golok, parang)."""
    bm = bmesh.new()
    rows = []
    for i in range(sections + 1):
        t = i / sections
        y = -t * length
        w = width * (1 - max(0.0, (t - (1 - tip)) / tip)) if t > 1 - tip else width
        w = max(w, 0.002)
        x0 = wave * math.sin(t * waves * math.pi) + curve * t * t
        rows.append([bm.verts.new((x0 - w / 2, y, 0)), bm.verts.new((x0, y, thickness / 2)),
                     bm.verts.new((x0 + w / 2, y, 0)), bm.verts.new((x0, y, -thickness / 2))])
    for i in range(sections):
        a, b = rows[i], rows[i + 1]
        for j in range(4):
            bm.faces.new((a[j], a[(j + 1) % 4], b[(j + 1) % 4], b[j]))
    bm.faces.new(list(reversed(rows[0])))
    bm.faces.new(rows[-1])
    return bm


def bm_loft(rings, segments=16, power=2.6, caps=True):
    """Lofts superellipse rings stacked along Z: each ring is (z, rx, ry) or (z, rx, ry, cx, cy).
    power 2 is an ellipse; higher values square it off. Good for torsos, heads, limbs, skirts and roofs."""
    bm = bmesh.new()
    loops = []
    exponent = 2 / power
    for ring in rings:
        z, rx, ry = ring[0], ring[1], ring[2]
        cx, cy = (ring[3], ring[4]) if len(ring) > 3 else (0.0, 0.0)
        loop = []
        for i in range(segments):
            a = i / segments * TAU
            c, s = math.cos(a), math.sin(a)
            x = cx + rx * math.copysign(abs(c) ** exponent, c)
            y = cy + ry * math.copysign(abs(s) ** exponent, s)
            loop.append(bm.verts.new((x, y, z)))
        loops.append(loop)
    for a, b in zip(loops, loops[1:]):
        for i in range(segments):
            j = (i + 1) % segments
            bm.faces.new((a[i], a[j], b[j], b[i]))
    if caps:
        bottom = bm.faces.new(list(reversed(loops[0])))
        top = bm.faces.new(loops[-1])
        bmesh.ops.poke(bm, faces=[bottom, top])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def bm_rock(radius, seed, roughness=0.28, subdivisions=2, flatten=0.75):
    """A rock: an icosphere pushed in and out by a few deterministic waves, flattened at the base."""
    bm = bm_ico(radius, subdivisions)
    for v in bm.verts:
        p = v.co.normalized()
        n = (math.sin(p.x * 5.1 + seed) * math.sin(p.y * 4.3 + seed * 1.7) * math.sin(p.z * 3.7 + seed * 0.6)
             + 0.5 * math.sin(p.x * 11 + p.z * 9 + seed * 2.3))
        v.co *= 1 + roughness * n
        v.co.z *= flatten
        if v.co.z < -radius * 0.25:
            v.co.z = -radius * 0.25
    return bm


# ------------------------------------------------------------------------------------------------ objects
class Builder:
    """Collects coloured parts for one model; each part may follow one bone (rigid skinning)."""

    def __init__(self, tint, fixed, collection=None):
        self.parts = []
        self.tint, self.fixed = tint, fixed
        self.collection = collection or bpy.context.scene.collection

    def add(self, bm, color, bone=None, at=(0, 0, 0), rotation=(0, 0, 0), tint=False, subdivide=0, bevel=0.0,
            bevel_segments=2, smooth=True, angle=40.0, name="part"):
        from mathutils import Euler
        matrix = Matrix.Translation(Vector(at)) @ Euler(rotation).to_matrix().to_4x4()
        bmesh.ops.transform(bm, matrix=matrix, verts=bm.verts)
        mesh = bpy.data.meshes.new(name)
        bm.to_mesh(mesh)
        bm.free()
        obj = bpy.data.objects.new(name, mesh)
        self.collection.objects.link(obj)
        if bevel > 0:
            modifier = obj.modifiers.new("Bevel", "BEVEL")
            modifier.width = bevel
            modifier.segments = bevel_segments
            modifier.limit_method = "ANGLE"
            modifier.harden_normals = False
        if subdivide > 0:
            modifier = obj.modifiers.new("Subdivision", "SUBSURF")
            modifier.levels = subdivide
            modifier.render_levels = subdivide
        if obj.modifiers:
            apply_modifiers(obj)
        mesh = obj.data
        if smooth:
            mesh.shade_smooth()
            mesh.set_sharp_from_angle(angle=math.radians(angle))
        else:
            mesh.shade_flat()
        mesh.materials.append(self.tint if tint else self.fixed)
        attribute = mesh.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
        rgb = (1.0, 1.0, 1.0) if tint else hexcolor(color)
        for item in attribute.data:
            item.color = (*rgb, 1.0)
        self.parts.append((obj, bone))
        return obj

    def join(self, name):
        for obj, bone in self.parts:
            if bone:
                group = obj.vertex_groups.new(name=bone)
                group.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
        objects = [obj for obj, _ in self.parts]
        with bpy.context.temp_override(active_object=objects[0], object=objects[0], selected_objects=objects, selected_editable_objects=objects):
            bpy.ops.object.join()
        body = objects[0]
        body.name = name
        body.data.name = name
        colors = body.data.color_attributes
        colors.active_color = colors["Col"]
        colors.render_color_index = colors.find("Col")
        self.parts = []
        return body


def apply_modifiers(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    mesh = bpy.data.meshes.new_from_object(evaluated)
    old = obj.data
    obj.modifiers.clear()
    obj.data = mesh
    bpy.data.meshes.remove(old)


def triangles(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


# ------------------------------------------------------------------------------------------------ ambient occlusion
def bake_ao(objects, scene, strength=0.75, samples=48, distance=0.6, ground=True):
    """Bakes ambient occlusion per object (others hidden) and multiplies it into the "Col" attribute.
    A temporary ground plane adds contact shadow under feet and bases."""
    previous_engine = scene.render.engine
    try:
        scene.render.engine = "CYCLES"
    except TypeError:
        return False
    scene.cycles.samples = samples
    scene.cycles.device = "CPU"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("BakeWorld")
    scene.world.light_settings.distance = distance
    floor = None
    if ground:
        mesh = bpy.data.meshes.new("AOFloor")
        bm = bmesh.new()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=20)
        bm.to_mesh(mesh)
        bm.free()
        floor = bpy.data.objects.new("AOFloor", mesh)
        scene.collection.objects.link(floor)
    everything = [o for o in scene.objects if o.type == "MESH"]
    hidden = {o.name: o.hide_render for o in everything}
    view_layer = scene.view_layers[0]
    try:
        for target in objects:
            for other in everything:
                other.hide_render = other is not target and other is not floor
            colors = target.data.color_attributes
            ao = colors.get("AO") or colors.new("AO", "FLOAT_COLOR", "POINT")
            colors.active_color = ao
            for o in view_layer.objects:
                o.select_set(False)
            target.select_set(True)
            view_layer.objects.active = target
            with bpy.context.temp_override(scene=scene, view_layer=view_layer, active_object=target, object=target, selected_objects=[target]):
                bpy.ops.object.bake(type="AO", target="VERTEX_COLORS")
            base, occlusion = colors["Col"], colors["AO"]
            mesh = target.data
            values = [0.0] * len(mesh.vertices)
            for i, item in enumerate(occlusion.data):
                values[i] = item.color[0]
            for loop in mesh.loops:
                shade = (1 - strength) + strength * values[loop.vertex_index]
                c = base.data[loop.index].color
                base.data[loop.index].color = (c[0] * shade, c[1] * shade, c[2] * shade, 1.0)
            colors.remove(colors["AO"])
            colors.active_color = colors["Col"]
            colors.render_color_index = colors.find("Col")
    finally:
        for other in everything:
            if other.name in hidden:
                other.hide_render = hidden[other.name]
        if floor:
            bpy.data.objects.remove(floor, do_unlink=True)
        try:
            scene.render.engine = previous_engine
        except TypeError:
            pass
    return True


def export_glb(scene, path, animations=False, skins=False):
    with bpy.context.temp_override(scene=scene, view_layer=scene.view_layers[0]):
        options = dict(filepath=str(path), export_format="GLB", use_selection=False, use_active_scene=True, export_yup=True,
                       export_animations=animations, export_skins=skins, export_materials="EXPORT", export_cameras=False, export_lights=False,
                       export_texcoords=False)
        if animations:
            options.update(export_animation_mode="NLA_TRACKS", export_force_sampling=True, export_optimize_animation_size=True)
        try:
            bpy.ops.export_scene.gltf(**options, export_vertex_color="ACTIVE")
        except TypeError:
            options.pop("export_texcoords", None)
            bpy.ops.export_scene.gltf(**options, export_colors=True)
