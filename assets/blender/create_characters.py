"""Run through Blender MCP. Builds every character of the game on one shared rig and exports
src/BattleNusantara.UI/wwwroot/models/characters.glb (24 variants, clips Idle, Walk, Attack, Cast, Hit, Death)
and src/BattleNusantara.UI/wwwroot/models/naga.glb (the MOBA boss, its own serpent rig).

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.

All designs are original low-poly characters for this prototype. The superhero faction uses our own heroes
(Satria Bledhek, Rara Angkasa); no licensed comic character is modelled.

Colours live in a vertex colour attribute. Parts marked as tint use the material "Tint", which the game multiplies
by the unit colour; everything else uses "Fixed". A character therefore costs at most two draw calls.
Skinning is rigid: every part follows exactly one bone.
"""
import bpy
import os
import math
from pathlib import Path
from mathutils import Quaternion, Vector

# The repository root: two folders above this script. When the script is run through exec(), pass __file__ in its globals
# or set NUSANTARA_ROOT.
ROOT = Path(os.environ["NUSANTARA_ROOT"]) if os.environ.get("NUSANTARA_ROOT") else Path(globals().get("__file__", "")).resolve().parents[2]
OUTPUT = ROOT / "src/BattleNusantara.UI/wwwroot/models"
OUTPUT.mkdir(parents=True, exist_ok=True)


def fresh_scene(name):
    for stale in [s for s in bpy.data.scenes if s.name == name]:
        for obj in list(stale.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.scenes.remove(stale)
    scene = bpy.data.scenes.new(name)
    bpy.context.window.scene = scene
    scene.render.fps = 24
    return scene


def hexcolor(value):
    value = value.lstrip("#")
    srgb = [int(value[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    # Vertex colours are stored linear.
    return tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in srgb)


def shared_material(name):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    tree = mat.node_tree
    shader = next(n for n in tree.nodes if n.type == "BSDF_PRINCIPLED")
    shader.inputs["Roughness"].default_value = 0.6
    shader.inputs["Metallic"].default_value = 0.1
    attribute = next((n for n in tree.nodes if n.type == "VERTEX_COLOR"), None) or tree.nodes.new("ShaderNodeVertexColor")
    attribute.layer_name = "Col"
    tree.links.new(attribute.outputs["Color"], shader.inputs["Base Color"])
    return mat


TINT, FIXED = shared_material("Tint"), shared_material("Fixed")


class Builder:
    """Collects parts for one character; each part is (object, bone)."""

    def __init__(self):
        self.parts = []

    def finish(self, obj, color, bone, tint=False):
        obj.data.materials.append(TINT if tint else FIXED)
        attribute = obj.data.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
        rgb = hexcolor(color)
        for item in attribute.data:
            item.color = (*rgb, 1.0)
        for polygon in obj.data.polygons:
            polygon.use_smooth = False
        self.parts.append((obj, bone))
        return obj

    def box(self, location, size, color, bone, taper=1.0, tint=False, rotation=(0, 0, 0)):
        bpy.ops.mesh.primitive_cube_add(size=1, location=location, rotation=rotation)
        obj = bpy.context.object
        obj.scale = size
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        if taper != 1.0:
            for vertex in obj.data.vertices:
                if vertex.co.z < 0:
                    vertex.co.x *= taper
                    vertex.co.y *= taper
        return self.finish(obj, color, bone, tint)

    def cone(self, location, r1, r2, depth, color, bone, vertices=8, rotation=(0, 0, 0), tint=False):
        bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=r1, radius2=r2, depth=depth, location=location, rotation=rotation)
        return self.finish(bpy.context.object, color, bone, tint)

    def ball(self, location, radius, color, bone, scale=(1, 1, 1), tint=False, subdivisions=1):
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=radius, location=location)
        obj = bpy.context.object
        obj.scale = scale
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        return self.finish(obj, color, bone, tint)

    def join(self, name):
        for obj, bone in self.parts:
            group = obj.vertex_groups.new(name=bone)
            group.add([v.index for v in obj.data.vertices], 1.0, "REPLACE")
        bpy.ops.object.select_all(action="DESELECT")
        for obj, _ in self.parts:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = self.parts[0][0]
        bpy.ops.object.join()
        body = bpy.context.object
        body.name = name
        body.data.name = name
        body.data.color_attributes.active_color = body.data.color_attributes["Col"]
        body.data.color_attributes.render_color_index = body.data.color_attributes.find("Col")
        return body


# ------------------------------------------------------------------------------------------------ character specs
SKIN = {"dark": "#6e4630", "brown": "#8a5a3c", "tan": "#a8754f", "pale": "#d8cfc0", "green": "#4f7a3a", "ghost": "#a8e4dc", "stone": "#7d8582"}

CHARACTERS = {
    # Playable heroes.
    "gajah-mada": dict(skin="brown", bulk=1.15, top="#6b2a1e", skirt="#3b2a1f", accent="#d4b478", head="crown", right="keris", left="shield", cape="#7a2a1f"),
    "cut-nyak-dien": dict(skin="tan", top="#26262b", skirt="#26262b", long=True, accent="#c9a24a", head="veil", right="rencong"),
    "nyi-roro-kidul": dict(skin="tan", top="#1f6b5a", skirt="#1a5a4c", long=True, accent="#e2c46a", head="crown_tall", hair="#151515", right="staff", cape="#2a8a72"),
    "republik": dict(skin="brown", top="#3d5470", skirt="#2c3c52", accent="#7fe0ff", head="helmet", right="rifle", pads="#5a7290"),
    "bledhek": dict(skin="tan", top="#e8eef2", skirt="#2d6db5", accent="#7fd0ff", head="iket", right="lightning", bracer="#7fd0ff"),
    "rara-angkasa": dict(skin="tan", bulk=1.1, top="#8c86b8", skirt="#5b5590", accent="#e8e4ff", head="band", fins="#c9c4f0", cape="#6a5fb0", fists=True),
    # AI allies.
    "diponegoro": dict(skin="brown", top="#ecebe4", skirt="#e0ddd2", long=True, accent="#c9a24a", head="turban", right="keris"),
    "pattimura": dict(skin="dark", top=None, skirt="#a32a24", accent="#d23a2e", head="iket_red", right="parang", left="salawaku"),
    "si-pitung": dict(skin="tan", top="#1c1c1f", skirt="#1c1c1f", accent="#b23a33", head="peci", right="golok"),
    "barong": dict(skin="stone", bulk=1.25, top="#c9a24a", skirt="#a8323a", accent="#f0d070", head="barong", claws="#f4ecd0", fur=True),
    # AI enemies (Kalatidha).
    "rangda": dict(skin="pale", top="#f0ece4", skirt="#2b2b2b", long=True, accent="#c23a3a", head="rangda", hair="#f2f2f2", claws="#2b2b2b"),
    "leak": dict(skin="ghost", top="#2b1f33", skirt="#2b1f33", long=True, accent="#ff7a5a", head="hood", claws="#ffd0b0"),
    "buto-ijo": dict(skin="green", bulk=1.4, top=None, skirt="#5a3a22", accent="#d4b478", head="horns", right="club"),
    "kuntilanak": dict(skin="pale", top="#f4f2ec", skirt="#f4f2ec", long=True, accent="#e8e2d8", head="hair_face", hair="#121212"),
    "pocong": dict(skin="pale", top="#f2efe6", skirt="#f2efe6", long=True, accent="#f2efe6", head="shroud", legs="#f2efe6"),
    # Troops and Adventure enemies.
    "prajurit": dict(skin="brown", top="#7a5a3a", skirt="#5a4630", accent="#d4b478", head="cap", right="spear", left="shield"),
    "prajurit-kalatidha": dict(skin="dark", top="#4a1f2a", skirt="#2e1a20", accent="#d17791", head="mask_horn", right="sword"),
    "guardian": dict(skin="stone", top="#5d5550", skirt="#4a443f", accent="#c8789a", head="mask_stone", right="spear"),
    "warden": dict(skin="dark", bulk=1.1, top="#3a2a4a", skirt="#2a1f36", accent="#b06a9c", head="horns_small", right="sword", left="shield"),
    "kalatidha": dict(skin="dark", bulk=1.3, top="#2a1a2e", skirt="#1f1424", long=True, accent="#e57c86", head="crown_spike", right="staff", cape="#3a1a30"),
    "spirit": dict(skin="ghost", top="#7fd6d0", skirt="#5fb8b2", long=True, accent="#c8fff8", head="hood_pale"),
    # Workers and NPCs.
    "worker": dict(skin="tan", top="#8a6a44", skirt="#6a5236", accent="#e9c368", head="caping", right="hoe", basket=True),
    "keeper": dict(skin="brown", top="#6a5a44", skirt="#4a3e30", long=True, accent="#83c9ba", head="blangkon", right="cane", beard="#d8d4cc"),
    "captain": dict(skin="tan", top="#24324a", skirt="#3a3026", accent="#e0b07a", head="wide_hat", right="sword", beard="#8a8076"),
}


def build_character(name, spec):
    b = Builder()
    bulk = spec.get("bulk", 1.0)
    skin = SKIN[spec["skin"]]
    accent = spec["accent"]
    top = spec["top"] or skin
    arm_x = 0.37 + (bulk - 1) * 0.22
    # Body.
    b.box((0, 0, 1.33), (0.56 * bulk, 0.30 * bulk, 0.46), top, "spine", taper=0.72)
    if spec.get("fur"):
        b.ball((0, 0.02, 1.4), 0.36 * bulk, top, "spine", scale=(1, .8, 1))
    b.box((0, 0, 1.08), (0.44 * bulk, 0.27 * bulk, 0.08), accent, "hips", tint=True)
    if spec.get("long"):
        b.cone((0, 0, 0.62), 0.42 * bulk, 0.22 * bulk, 0.88, spec["skirt"], "hips")
    else:
        b.cone((0, 0, 0.86), 0.36 * bulk, 0.22 * bulk, 0.42, spec["skirt"], "hips")
    b.box((0, 0, 1.6), (0.12, 0.12, 0.1), skin, "spine")
    head_scale = (1, 1, 1.05)
    b.ball((0, 0, 1.76), 0.17, skin, "head", scale=head_scale)
    # Eyes give every face a direction.
    for side in (1, -1):
        b.box((0.06 * side, -0.155, 1.79), (0.04, 0.02, 0.03), "#141414", "head")
    for side, sign in (("L", 1), ("R", -1)):
        pad = spec.get("pads")
        b.ball((arm_x * sign, 0, 1.5), 0.13 * (1.3 if pad else 1) * bulk, pad or accent, f"upper_arm.{side}", tint=not pad)
        b.box((arm_x * sign, 0, 1.33), (0.13 * bulk, 0.13 * bulk, 0.32), top if spec.get("long") or spec["top"] else skin, f"upper_arm.{side}", taper=0.85)
        b.box((arm_x * sign, 0, 1.03), (0.12 * bulk, 0.12 * bulk, 0.3), skin, f"forearm.{side}", taper=0.8)
        b.box((arm_x * sign, 0, 0.98), (0.14 * bulk, 0.14 * bulk, 0.12), spec.get("bracer", accent), f"forearm.{side}", tint="bracer" not in spec)
        if spec.get("fists"):
            b.box((arm_x * sign, 0, 0.84), (0.17 * bulk, 0.17 * bulk, 0.14), spec.get("fins", accent), f"forearm.{side}")
        if spec.get("claws"):
            for k in (-1, 0, 1):
                b.cone((arm_x * sign + 0.035 * k, -0.02, 0.8), 0.02, 0.0, 0.14, spec["claws"], f"forearm.{side}", vertices=4, rotation=(math.pi, 0, 0))
        legs = spec.get("legs", skin if not spec.get("long") else spec["skirt"])
        b.box((0.13 * sign * bulk, 0, 0.72), (0.17 * bulk, 0.18 * bulk, 0.46), legs, f"thigh.{side}", taper=0.8)
        b.box((0.13 * sign * bulk, 0, 0.28), (0.14 * bulk, 0.15 * bulk, 0.44), spec.get("legs", spec["skirt"]), f"shin.{side}", taper=0.75)
        b.box((0.13 * sign * bulk, -0.06, 0.04), (0.15 * bulk, 0.3, 0.08), spec.get("legs", "#3a2a1f"), f"shin.{side}")
    # Extras.
    if spec.get("cape"):
        b.box((0, 0.2 * bulk, 1.0), (0.5 * bulk, 0.04, 0.9), spec["cape"], "spine", taper=1.3)
    if spec.get("fins"):
        for sign in (1, -1):
            b.cone((0.42 * sign * bulk, 0.18, 1.62), 0.12, 0.0, 0.55, spec["fins"], "spine", vertices=4, rotation=(0.5, 0.9 * sign, 0))
    if spec.get("basket"):
        b.cone((0, 0.24, 1.25), 0.16, 0.22, 0.32, "#a07a44", "spine", vertices=8)
    if spec.get("beard"):
        b.cone((0, -0.12, 1.62), 0.1, 0.02, 0.18, spec["beard"], "head", vertices=6, rotation=(math.pi, 0, 0))
    if spec.get("hair"):
        hair = spec["hair"]
        b.ball((0, 0.04, 1.8), 0.19, hair, "head", scale=(1, 1, 1.05))
        b.box((0, 0.12, 1.45), (0.3, 0.08, 0.6), hair, "spine")
    head_gear(b, spec.get("head"), accent, spec)
    weapon(b, spec.get("right"), arm_x, accent)
    off_hand(b, spec.get("left"), arm_x, accent)
    return b.join(name)


def head_gear(b, kind, accent, spec):
    gold = "#d4b478"
    if kind == "crown":
        b.box((0, 0, 1.86), (0.37, 0.37, 0.05), gold, "head")
        b.cone((0, 0, 1.97), 0.18, 0.06, 0.18, gold, "head", vertices=6)
    elif kind == "crown_tall":
        b.box((0, 0, 1.86), (0.36, 0.36, 0.05), gold, "head")
        for k in range(5):
            angle = k * 2 * math.pi / 5
            b.cone((0.15 * math.cos(angle), 0.15 * math.sin(angle), 1.98), 0.05, 0.0, 0.22, gold, "head", vertices=4)
    elif kind == "crown_spike":
        b.box((0, 0, 1.88), (0.4, 0.4, 0.06), "#3a2030", "head")
        for k in range(6):
            angle = k * 2 * math.pi / 6
            b.cone((0.17 * math.cos(angle), 0.17 * math.sin(angle), 2.0), 0.05, 0.0, 0.3, accent, "head", vertices=4, tint=True)
        horns(b, "#e8dcc0", 1.4)
    elif kind == "veil":
        b.ball((0, 0.02, 1.79), 0.205, "#26262b", "head", scale=(1, 1, 1.08))
        b.box((0, 0.1, 1.55), (0.36, 0.14, 0.32), "#26262b", "spine")
    elif kind == "helmet":
        b.ball((0, 0.01, 1.82), 0.2, "#4a6080", "head", scale=(1, 1.05, 0.9))
        b.box((0, -0.16, 1.79), (0.26, 0.04, 0.07), accent, "head", tint=True)
    elif kind == "iket":
        b.box((0, 0, 1.84), (0.36, 0.36, 0.06), accent, "head", tint=True)
        b.cone((0, 0.2, 1.84), 0.06, 0.0, 0.16, accent, "head", vertices=4, rotation=(-1.2, 0, 0), tint=True)
    elif kind == "iket_red":
        b.box((0, 0, 1.84), (0.36, 0.36, 0.06), "#d23a2e", "head")
    elif kind == "band":
        b.box((0, 0, 1.86), (0.36, 0.36, 0.04), accent, "head", tint=True)
        b.cone((0, -0.17, 1.9), 0.05, 0.0, 0.12, accent, "head", vertices=4, tint=True)
        b.ball((0, 0.06, 1.84), 0.18, "#2a2235", "head")
    elif kind == "turban":
        b.ball((0, 0.01, 1.88), 0.22, "#f0efe8", "head", scale=(1, 1, 0.7))
        b.ball((0, -0.17, 1.88), 0.04, accent, "head", tint=True)
    elif kind == "peci":
        b.cone((0, 0, 1.9), 0.17, 0.16, 0.12, "#141414", "head", vertices=8)
    elif kind == "cap":
        b.ball((0, 0, 1.86), 0.19, "#8a6a3a", "head", scale=(1, 1, 0.6))
        b.box((0, 0, 1.84), (0.39, 0.39, 0.03), accent, "head", tint=True)
    elif kind == "caping":
        b.cone((0, 0, 1.95), 0.42, 0.0, 0.2, "#c9a86a", "head", vertices=10)
    elif kind == "blangkon":
        b.ball((0, 0.01, 1.86), 0.19, "#5a3a22", "head", scale=(1, 1, 0.65))
        b.ball((0, 0.17, 1.8), 0.07, "#5a3a22", "head")
    elif kind == "wide_hat":
        b.cone((0, 0, 1.9), 0.38, 0.36, 0.04, "#2a2420", "head", vertices=12)
        b.cone((0, 0, 1.98), 0.17, 0.15, 0.16, "#2a2420", "head", vertices=8)
    elif kind == "horns":
        horns(b, "#e8dcc0", 1.6)
        b.box((0, -0.15, 1.7), (0.18, 0.04, 0.05), "#f4f0e0", "head")  # tusks line
    elif kind == "horns_small":
        horns(b, "#cfc6b0", 1.0)
    elif kind == "barong":
        b.ball((0, -0.05, 1.82), 0.3, "#c23a2e", "head", scale=(1, 1, 0.9))
        b.box((0, -0.3, 1.78), (0.28, 0.08, 0.1), "#f4ecd0", "head")
        b.cone((0, 0.05, 2.08), 0.28, 0.05, 0.3, "#f0d070", "head", vertices=6)
        for sign in (1, -1):
            b.ball((0.1 * sign, -0.3, 1.9), 0.05, "#ffffff", "head")
    elif kind == "rangda":
        b.ball((0, -0.03, 1.78), 0.22, "#f4f0e8", "head")
        b.box((0, -0.2, 1.7), (0.16, 0.04, 0.05), "#c23a3a", "head")
        for k in (-1, 1):
            b.cone((0.05 * k, -0.22, 1.66), 0.02, 0.0, 0.1, "#ffffff", "head", vertices=4, rotation=(math.pi, 0, 0))
        b.ball((0, 0.06, 1.85), 0.26, "#f2f2f2", "head", scale=(1.15, 1, 1.15))
        b.box((0, 0.14, 1.35), (0.5, 0.1, 0.8), "#f2f2f2", "spine")
    elif kind == "hood":
        b.cone((0, 0.02, 1.84), 0.25, 0.0, 0.42, "#2b1f33", "head", vertices=8)
        b.ball((0, -0.08, 1.78), 0.08, accent, "head", tint=True)
    elif kind == "hood_pale":
        b.cone((0, 0.02, 1.84), 0.24, 0.0, 0.4, "#5fb8b2", "head", vertices=8)
    elif kind == "hair_face":
        b.ball((0, 0.0, 1.8), 0.205, "#121212", "head", scale=(1, 1, 1.08))
        b.box((0, -0.12, 1.62), (0.3, 0.06, 0.4), "#121212", "head")
        b.box((0, 0.12, 1.3), (0.34, 0.08, 0.9), "#121212", "spine")
    elif kind == "shroud":
        b.ball((0, 0, 1.78), 0.2, "#f2efe6", "head", scale=(1, 1, 1.1))
        b.cone((0, 0, 2.0), 0.06, 0.0, 0.16, "#f2efe6", "head", vertices=6)
        b.box((0, 0, 1.96), (0.12, 0.12, 0.03), "#cfc8b8", "head")
    elif kind == "mask_horn":
        b.box((0, -0.16, 1.77), (0.26, 0.04, 0.24), "#4a1f2a", "head")
        horns(b, "#d8cfc0", 0.8)
    elif kind == "mask_stone":
        b.box((0, -0.15, 1.77), (0.3, 0.06, 0.3), "#8a908c", "head")
        b.box((0, 0, 1.9), (0.32, 0.32, 0.08), "#6d736f", "head")


def horns(b, color, size):
    for sign in (1, -1):
        b.cone((0.15 * sign, 0, 1.95), 0.05 * size, 0.0, 0.18 * size, color, "head", vertices=5, rotation=(0, 0.6 * sign, 0))


def weapon(b, kind, arm_x, accent):
    x, z, bone = -arm_x, 0.86, "forearm.R"
    steel, wood, brass = "#c0c8cc", "#6a4a2a", "#c9a24a"
    if kind in ("sword", "keris", "golok", "parang", "rencong"):
        length = {"sword": 0.76, "keris": 0.5, "golok": 0.55, "parang": 0.62, "rencong": 0.36}[kind]
        b.box((x, -0.04, z), (0.05, 0.2, 0.05), brass if kind != "golok" else wood, bone)
        width = 0.11 if kind == "parang" else 0.07
        blade = b.box((x, -0.14 - length / 2, z), (width, length, 0.025), steel, bone)
        if kind == "keris":  # A wavy blade, approximated by alternating offsets.
            for vertex in blade.data.vertices:
                vertex.co.x += 0.025 * math.sin(vertex.co.y * 40)
        if kind == "rencong":
            b.box((x, -0.04, z - 0.07), (0.05, 0.05, 0.1), brass, bone)
    elif kind == "spear":
        b.box((x, -0.35, z), (0.04, 1.4, 0.04), wood, bone)
        b.cone((x, -1.1, z), 0.06, 0.0, 0.22, steel, bone, vertices=4, rotation=(math.pi / 2, 0, 0))
    elif kind == "staff":
        b.box((x, -0.05, z), (0.05, 0.05, 1.3), wood, bone)
        b.ball((x, -0.05, z + 0.7), 0.1, accent, bone, tint=True)
    elif kind == "cane":
        b.box((x, -0.05, z - 0.42), (0.04, 0.04, 0.84), wood, bone)
    elif kind == "rifle":
        b.box((x, -0.25, z), (0.08, 0.6, 0.1), "#2a3440", bone)
        b.box((x, -0.58, z + 0.02), (0.04, 0.12, 0.04), accent, bone, tint=True)
    elif kind == "club":
        b.cone((x, -0.38, z), 0.14, 0.05, 0.7, wood, bone, vertices=6, rotation=(math.pi / 2, 0, 0))
    elif kind == "hoe":
        b.box((x, -0.05, z - 0.2), (0.04, 0.04, 1.1), wood, bone)
        b.box((x, -0.15, z - 0.75), (0.12, 0.22, 0.04), steel, bone)
    elif kind == "lightning":
        # A blade of jagged light: three offset segments.
        for k in range(3):
            b.box((x + (0.04 if k % 2 else -0.04), -0.2 - k * 0.22, z), (0.05, 0.24, 0.03), "#bfeaff", bone, rotation=(0, 0, 0.5 if k % 2 else -0.5))


def off_hand(b, kind, arm_x, accent):
    if kind == "shield":
        b.cone((arm_x + 0.1, -0.02, 1.02), 0.3, 0.26, 0.05, "#a07a44", "forearm.L", vertices=10, rotation=(0, math.pi / 2, 0))
        b.cone((arm_x + 0.14, -0.02, 1.02), 0.1, 0.02, 0.07, accent, "forearm.L", vertices=8, rotation=(0, math.pi / 2, 0), tint=True)
    elif kind == "salawaku":
        b.box((arm_x + 0.1, -0.02, 1.0), (0.04, 0.16, 0.8), "#5a3a22", "forearm.L")
        b.box((arm_x + 0.125, -0.02, 1.0), (0.02, 0.06, 0.6), "#f0e8d0", "forearm.L")


# ------------------------------------------------------------------------------------------------ rig and clips
BONES = {
    "root": ((0, 0, 0), (0, 0, 0.3), None),
    "hips": ((0, 0, 0.95), (0, 0, 1.1), "root"),
    "spine": ((0, 0, 1.1), (0, 0, 1.58), "hips"),
    "head": ((0, 0, 1.62), (0, 0, 1.98), "spine"),
}
for _side, _sign in (("L", 1), ("R", -1)):
    BONES[f"upper_arm.{_side}"] = ((0.37 * _sign, 0, 1.5), (0.37 * _sign, 0, 1.18), "spine")
    BONES[f"forearm.{_side}"] = ((0.37 * _sign, 0, 1.18), (0.37 * _sign, 0, 0.86), f"upper_arm.{_side}")
    BONES[f"thigh.{_side}"] = ((0.13 * _sign, 0, 0.95), (0.13 * _sign, 0, 0.5), "hips")
    BONES[f"shin.{_side}"] = ((0.13 * _sign, 0, 0.5), (0.13 * _sign, 0, 0.04), f"thigh.{_side}")

X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))


def make_rig(scene, name, bones):
    data = bpy.data.armatures.new(name)
    rig = bpy.data.objects.new(name, data)
    scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for bone_name, (head, tail, parent) in bones.items():
        bone = data.edit_bones.new(bone_name)
        bone.head, bone.tail = head, tail
        if parent:
            bone.parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    for pose_bone in rig.pose.bones:
        pose_bone.rotation_mode = "QUATERNION"
    return rig


def animate(rig, clips, lift_bone):
    data = rig.data

    def turn(bone, axis, degrees):
        rest = data.bones[bone].matrix_local.to_3x3()
        return Quaternion(rest.inverted() @ axis, math.radians(degrees))

    def key(frame, rotations, lift):
        for pose_bone in rig.pose.bones:
            rotation = Quaternion()
            for axis, degrees in rotations.get(pose_bone.name, []):
                rotation = turn(pose_bone.name, axis, degrees) @ rotation
            pose_bone.rotation_quaternion = rotation
            pose_bone.keyframe_insert("rotation_quaternion", frame=frame)
        lifted = rig.pose.bones[lift_bone]
        lifted.location = (0, lift, 0)  # Bone-local Y points up the bone.
        lifted.keyframe_insert("location", frame=frame)

    rig.animation_data_create()
    actions = []
    for name, frames in clips:
        stale = bpy.data.actions.get(name)
        if stale:
            bpy.data.actions.remove(stale)
        action = bpy.data.actions.new(name)
        rig.animation_data.action = action
        for frame, rotations, lift in frames:
            key(frame, rotations, lift)
        actions.append(action)
    rig.animation_data.action = None
    for action in actions:
        track = rig.animation_data.nla_tracks.new()
        track.name = action.name
        strip = track.strips.new(action.name, int(action.frame_range[0]), action)
        strip.name = action.name
    for pose_bone in rig.pose.bones:
        pose_bone.rotation_quaternion = Quaternion()
        pose_bone.location = (0, 0, 0)
    return actions


def stride(phase):
    swing = math.sin(phase)
    return {
        "thigh.L": [(X, -34 * swing)], "thigh.R": [(X, 34 * swing)],
        "shin.L": [(X, 38 * max(0.0, -math.cos(phase)) + 6)], "shin.R": [(X, 38 * max(0.0, math.cos(phase)) + 6)],
        "upper_arm.L": [(X, 26 * swing), (Y, -6)], "upper_arm.R": [(X, -20 * swing), (Y, 6)],
        "forearm.L": [(X, -28)], "forearm.R": [(X, -32)],
        "spine": [(X, -5), (Z, 6 * swing)], "head": [(Z, -4 * swing)],
    }


GUARD = {"upper_arm.L": [(X, -12), (Y, -8)], "forearm.L": [(X, -42)], "upper_arm.R": [(X, -8), (Y, 8)], "forearm.R": [(X, -30)]}
BREATHE = {**GUARD, "spine": [(X, -2.5)], "head": [(X, 3), (Z, 8)], "upper_arm.L": [(X, -15), (Y, -8)], "upper_arm.R": [(X, -11), (Y, 8)]}
HUMAN_CLIPS = [
    ("Idle", [(1, GUARD, 0.0), (24, BREATHE, 0.012), (48, GUARD, 0.0)]),
    ("Walk", [(1 + i * 3, stride(i * math.pi / 4), 0.025 * abs(math.cos(i * math.pi / 4))) for i in range(9)]),
    ("Attack", [
        (1, GUARD, 0.0),
        (6, {"upper_arm.R": [(X, -150), (Y, 14)], "forearm.R": [(X, -55)], "spine": [(X, 8), (Z, -24)], "upper_arm.L": [(X, -30), (Y, -10)], "forearm.L": [(X, -70)], "thigh.L": [(X, -16)], "thigh.R": [(X, 12)]}, 0.02),
        (10, {"upper_arm.R": [(X, -48), (Y, -10)], "forearm.R": [(X, -8)], "spine": [(X, -16), (Z, 26)], "upper_arm.L": [(X, 12), (Y, -14)], "forearm.L": [(X, -60)], "thigh.L": [(X, -30)], "shin.L": [(X, 24)], "thigh.R": [(X, 18)]}, -0.05),
        (13, {"upper_arm.R": [(X, -20), (Y, -16)], "forearm.R": [(X, -4)], "spine": [(X, -18), (Z, 30)], "upper_arm.L": [(X, 12), (Y, -14)], "forearm.L": [(X, -60)], "thigh.L": [(X, -30)], "shin.L": [(X, 24)], "thigh.R": [(X, 18)]}, -0.06),
        (20, GUARD, 0.0),
    ]),
    ("Cast", [
        (1, GUARD, 0.0),
        (8, {"upper_arm.R": [(X, -165), (Y, 20)], "upper_arm.L": [(X, -165), (Y, -20)], "forearm.R": [(X, -10)], "forearm.L": [(X, -10)], "spine": [(X, 12)], "head": [(X, 14)]}, 0.05),
        (14, {"upper_arm.R": [(X, -95), (Y, 6)], "upper_arm.L": [(X, -95), (Y, -6)], "forearm.R": [(X, 0)], "forearm.L": [(X, 0)], "spine": [(X, -16)], "head": [(X, -6)], "thigh.L": [(X, -22)], "shin.L": [(X, 18)], "thigh.R": [(X, 14)]}, -0.04),
        (24, GUARD, 0.0),
    ]),
    ("Hit", [
        (1, GUARD, 0.0),
        (4, {"spine": [(X, 18)], "head": [(X, 14), (Z, 10)], "upper_arm.L": [(X, 20), (Y, -20)], "upper_arm.R": [(X, 20), (Y, 20)], "forearm.L": [(X, -20)], "forearm.R": [(X, -20)]}, -0.03),
        (11, GUARD, 0.0),
    ]),
    ("Death", [
        (1, GUARD, 0.0),
        (6, {"spine": [(X, 20)], "head": [(X, 20)], "upper_arm.L": [(X, 30), (Y, -40)], "upper_arm.R": [(X, 30), (Y, 40)], "thigh.L": [(X, -20)], "shin.L": [(X, 40)]}, -0.1),
        (16, {"root": [(X, -82)], "spine": [(X, 10)], "head": [(X, 25)], "upper_arm.L": [(X, -10), (Y, -70)], "upper_arm.R": [(X, -10), (Y, 70)], "thigh.L": [(X, -10)], "thigh.R": [(X, 6)]}, 0.0),
        (30, {"root": [(X, -90)], "spine": [(X, 6)], "head": [(X, 20)], "upper_arm.L": [(X, -10), (Y, -75)], "upper_arm.R": [(X, -10), (Y, 75)]}, 0.0),
    ]),
]


def export(scene, path):
    with bpy.context.temp_override(scene=scene, view_layer=scene.view_layers[0]):
        options = dict(filepath=str(path), export_format="GLB", use_selection=False, use_active_scene=True, export_yup=True,
                       export_animations=True, export_animation_mode="NLA_TRACKS", export_force_sampling=True, export_optimize_animation_size=True,
                       export_skins=True, export_materials="EXPORT", export_cameras=False, export_lights=False)
        try:
            bpy.ops.export_scene.gltf(**options, export_vertex_color="ACTIVE")
        except TypeError:
            bpy.ops.export_scene.gltf(**options, export_colors=True)


def build_characters():
    scene = fresh_scene("Nusantara_Characters")
    rig = make_rig(scene, "CharacterRig", BONES)
    meshes = []
    for index, (name, spec) in enumerate(CHARACTERS.items()):
        body = build_character(name, spec)
        body.parent = rig
        modifier = body.modifiers.new("Armature", "ARMATURE")
        modifier.object = rig
        meshes.append(body)
    animate(rig, HUMAN_CLIPS, "hips")
    # Spread the variants out for inspection in Blender; the exported meshes keep their own origin at the rig.
    path = OUTPUT / "characters.glb"
    export(scene, path)
    return scene, rig, meshes, path


# ------------------------------------------------------------------------------------------------ naga
NAGA_BONES = {"base": ((0, 0, 0), (0, 0, 0.4), None)}
_previous = "base"
for _i in range(7):
    NAGA_BONES[f"seg{_i}"] = ((0, 0, 0.4 + _i * 0.45), (0, 0, 0.85 + _i * 0.45), _previous)
    _previous = f"seg{_i}"


def build_naga():
    scene = fresh_scene("Nusantara_Naga")
    rig = make_rig(scene, "NagaRig", NAGA_BONES)
    b = Builder()
    # Coiled tail on the ground.
    for k in range(10):
        angle = k * 0.62
        radius = 0.9 - k * 0.04
        b.ball((radius * math.cos(angle), radius * math.sin(angle), 0.18), 0.26 - k * 0.008, "#3f7a4a" if k % 2 else "#4f8a54", "base", scale=(1, 1, .7))
    for i in range(7):
        z = 0.62 + i * 0.45
        r = 0.32 - i * 0.022
        b.cone((0, 0, z), r, r * 0.92, 0.47, "#3f7a4a", f"seg{i}", vertices=8)
        b.box((0, -r * 0.85, z), (r * 1.1, 0.06, 0.4), "#e8d58a", f"seg{i}")  # belly scales
        b.cone((0, r * 0.95, z + 0.1), 0.07, 0.0, 0.2, "#d4b478", f"seg{i}", vertices=4, rotation=(-0.6, 0, 0))  # dorsal fin
    head_z = 0.62 + 6 * 0.45 + 0.35
    b.box((0, -0.12, head_z), (0.34, 0.5, 0.26), "#3f7a4a", "seg6")
    b.box((0, -0.42, head_z - 0.06), (0.26, 0.24, 0.14), "#4f8a54", "seg6")
    for sign in (1, -1):
        b.ball((0.14 * sign, -0.3, head_z + 0.08), 0.05, "#ffe27a", "seg6")
        b.cone((0.1 * sign, 0.08, head_z + 0.25), 0.05, 0.0, 0.3, "#e8dcc0", "seg6", vertices=5, rotation=(-0.4, 0.3 * sign, 0))
    # Naga Basuki wears a crown.
    b.box((0, -0.05, head_z + 0.17), (0.3, 0.3, 0.06), "#d4b478", "seg6", tint=True)
    for k in range(5):
        b.cone((-0.12 + k * 0.06, -0.05, head_z + 0.28), 0.03, 0.0, 0.14, "#d4b478", "seg6", vertices=4, tint=True)
    body = b.join("naga")
    body.parent = rig
    modifier = body.modifiers.new("Armature", "ARMATURE")
    modifier.object = rig

    def sway(phase, amount=10):
        return {f"seg{i}": [(X, amount * 0.6 * math.sin(phase + i * 0.7)), (Y, amount * math.sin(phase * 0.5 + i * 0.5))] for i in range(7)}

    clips = [
        ("Idle", [(1 + k * 6, sway(k * math.pi / 4), 0.0) for k in range(9)]),
        ("Walk", [(1 + k * 3, sway(k * math.pi / 4, 16), 0.0) for k in range(9)]),
        ("Attack", [
            (1, sway(0), 0.0),
            (8, {**{f"seg{i}": [(X, 12)] for i in range(7)}}, 0.0),
            (13, {**{f"seg{i}": [(X, -16 if i > 2 else -6)] for i in range(7)}}, 0.0),
            (22, sway(0), 0.0),
        ]),
        ("Cast", [(1, sway(0), 0.0), (12, {f"seg{i}": [(X, 8)] for i in range(7)}, 0.0), (24, sway(0), 0.0)]),
        ("Hit", [(1, sway(0), 0.0), (4, {f"seg{i}": [(X, 10)] for i in range(7)}, 0.0), (11, sway(0), 0.0)]),
        ("Death", [(1, sway(0), 0.0), (12, {"seg0": [(X, -40)], **{f"seg{i}": [(X, -12)] for i in range(1, 7)}}, 0.0), (30, {"seg0": [(X, -75)], **{f"seg{i}": [(X, -8)] for i in range(1, 7)}}, 0.0)]),
    ]
    animate(rig, clips, "base")
    path = OUTPUT / "naga.glb"
    export(scene, path)
    return scene, path


previous = bpy.context.window.scene
character_scene, rig, meshes, characters_path = build_characters()
naga_scene, naga_path = build_naga()
bpy.data.libraries.write(str(ROOT / "assets/blender/characters.blend"), {character_scene, naga_scene}, fake_user=True)
bpy.context.window.scene = previous
result = {
    "variants": len(meshes),
    "triangles": {m.name: sum(len(p.vertices) - 2 for p in m.data.polygons) for m in meshes},
    "characters_bytes": characters_path.stat().st_size,
    "naga_bytes": naga_path.stat().st_size,
}
print(result)
