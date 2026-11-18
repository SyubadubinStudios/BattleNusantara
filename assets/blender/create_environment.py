"""Run through Blender MCP. Builds the environment kit used to dress every map and exports
src/BattleNusantara.UI/wwwroot/models/environment.glb, one node per object. The game places them with instancing.

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios. Original designs.

Vegetation (names starting tree_, bamboo, banana, bush_, grass_, reeds, fern) sways in the wind in the game.
Glowing parts (lamp, flame) use the emissive "Glow" material so the bloom pass catches them.
Every object stands on Z = 0. Colours are vertex colours with baked ambient occlusion.
"""
import importlib
import math
import os
import sys
from pathlib import Path

import bmesh
import bpy

ROOT = Path(os.environ["NUSANTARA_ROOT"]) if os.environ.get("NUSANTARA_ROOT") else Path(globals().get("__file__", "")).resolve().parents[2]
OUTPUT = ROOT / "src/BattleNusantara.UI/wwwroot/models"
sys.path.insert(0, str(ROOT / "assets/blender"))
import nusantara_kit as kit  # noqa: E402

importlib.reload(kit)
PI, TAU = math.pi, math.tau

previous = bpy.context.window.scene if bpy.context.window else None
scene = kit.fresh_scene("Nusantara_Environment")
TINT, FIXED = kit.materials()


def glow(name, color, strength):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    shader = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    shader.inputs["Base Color"].default_value = (*kit.hexcolor(color), 1)
    shader.inputs["Emission Color"].default_value = (*kit.hexcolor(color), 1)
    shader.inputs["Emission Strength"].default_value = strength
    return mat


LAMP = glow("Glow", "#ffcf7a", 4.0)
LEAF, LEAF_DARK, LEAF_LIGHT, LEAF_YELLOW = "#4f8a3c", "#2f5e2e", "#78a84a", "#9cae4a"
BARK, BARK_DARK, BAMBOO = "#6b5038", "#4a3626", "#9aa857"
STONE, STONE_DARK, STONE_LIGHT, MOSS = "#868b84", "#5c615b", "#a3a69c", "#5e7c45"
WOOD, WOOD_DARK, THATCH = "#7a5634", "#4e3622", "#b0915a"
objects = []


def builder():
    return kit.Builder(TINT, FIXED, scene.collection)


def finish(b, name, glow_parts=()):
    obj = b.join(name)
    objects.append(obj)
    return obj


def frond(b, base, yaw, pitch, length, width, color, droop=.35, sections=6):
    """A drooping palm or banana leaf: a blade bent downward along its length."""
    bm = kit.bm_blade(length, width, .02, sections=sections, tip=.35)
    for v in bm.verts:
        t = -v.co.y / length
        v.co.z -= droop * length * t * t
    b.add(bm, color, at=base, rotation=(pitch, 0, yaw), angle=60)


def gable(width, depth, height):
    """A thatched gable roof: two sloping planes meeting at a ridge along X, closed at both ends."""
    bm = bmesh.new()
    w, d = width / 2, depth / 2
    corners = [bm.verts.new(p) for p in ((-w, -d, 0), (w, -d, 0), (w, d, 0), (-w, d, 0))]
    ridge = [bm.verts.new((-w, 0, height)), bm.verts.new((w, 0, height))]
    under = [bm.verts.new((x, y, -.12)) for x, y, _ in ((-w, -d, 0), (w, -d, 0), (w, d, 0), (-w, d, 0))]
    bm.faces.new((corners[0], corners[1], ridge[1], ridge[0]))
    bm.faces.new((corners[2], corners[3], ridge[0], ridge[1]))
    bm.faces.new((corners[3], corners[0], ridge[0]))
    bm.faces.new((corners[1], corners[2], ridge[1]))
    for k in range(4):
        bm.faces.new((corners[k], under[k], under[(k + 1) % 4], corners[(k + 1) % 4]))
    bm.faces.new(list(reversed(under)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def foliage_blob(b, at, radius, seed, color):
    b.add(kit.bm_rock(radius, seed, roughness=.22, subdivisions=2, flatten=.85), color, at=at, angle=70)


# ------------------------------------------------------------------------------------------------ vegetation
# Coconut palm: a leaning, ringed trunk with a crown of drooping fronds and coconuts.
b = builder()
points = [(0, 0, 0)]
for k in range(1, 7):
    t = k / 6
    points.append((.9 * t * t, .2 * t, 7.2 * t))
for k in range(6):
    b.add(kit.bm_tube(points[k], points[k + 1], .26 - k * .025, .26 - (k + 1) * .025, 10), BARK if k % 2 else BARK_DARK, angle=50)
top = points[-1]
for k in range(9):
    yaw = k / 9 * TAU
    frond(b, top, yaw, .25, 3.2, .55, LEAF if k % 2 else LEAF_DARK, droop=.45, sections=5)
for k in range(4):
    a = k * 1.7
    b.add(kit.bm_sphere(.17, 8, 6), "#6a5a2a", at=(top[0] + .22 * math.cos(a), top[1] + .22 * math.sin(a), top[2] - .25))
finish(b, "tree_palm")

# Broadleaf rain tree: a flared trunk, three limbs and a wide crown of leaf masses.
b = builder()
b.add(kit.bm_loft([(0, .75, .75), (.25, .5, .5), (1.2, .36, .36), (3.0, .3, .3)], segments=10, power=2.6), BARK, angle=50)
for k in range(5):  # buttress roots
    a = k / 5 * TAU
    b.add(kit.bm_tube((0, 0, .7), (1.1 * math.cos(a), 1.1 * math.sin(a), 0), .16, .06, 6), BARK_DARK)
for k, (x, y, z) in enumerate([(1.4, .3, 4.6), (-1.2, .6, 4.9), (.2, -1.1, 5.1)]):
    b.add(kit.bm_tube((0, 0, 2.8), (x, y, z), .22, .12, 8), BARK)
for k, (x, y, z, r) in enumerate([(1.6, .4, 5.2, 1.7), (-1.4, .7, 5.4, 1.6), (.2, -1.3, 5.7, 1.6), (0, .2, 6.3, 1.9), (.4, 1.2, 5.6, 1.4)]):
    foliage_blob(b, (x, y, z), r, k * 1.37 + .4, [LEAF, LEAF_DARK, LEAF_LIGHT][k % 3])
finish(b, "tree_broad")

# Cemara (casuarina): a tall slender tree of stacked, ragged cones.
b = builder()
b.add(kit.bm_tube((0, 0, 0), (0, 0, 7.5), .2, .08, 8), BARK_DARK)
for k in range(5):
    cone = kit.bm_cone(1.5 - k * .26, .05, 2.0, 10)
    for v in cone.verts:
        if v.co.z < 0:
            v.co.x *= 1 + .18 * math.sin(v.co.y * 7 + k)
            v.co.y *= 1 + .18 * math.cos(v.co.x * 6 + k)
    b.add(cone, LEAF_DARK if k % 2 else "#3d6b3a", at=(0, 0, 2.2 + k * 1.25), angle=60)
finish(b, "tree_cemara")

# Bamboo clump: leaning stalks with nodes and leaf sprays.
b = builder()
for k in range(7):
    a = k * 2.4
    base = (.35 * math.cos(a), .35 * math.sin(a), 0)
    tip = (1.1 * math.cos(a), 1.1 * math.sin(a), 6.2 + (k % 3) * .7)
    b.add(kit.bm_tube(base, tip, .09, .05, 7), BAMBOO if k % 2 else "#7f9447")
    for n in range(1, 4):
        t = n / 4
        p = tuple(base[i] + (tip[i] - base[i]) * t for i in range(3))
        b.add(kit.bm_torus(.085 - t * .03, .016, 6, 3), "#6e7d3a", at=p)
    for n in range(3):
        frond(b, tip, a + n * 2.1, .6, .9, .14, LEAF_LIGHT if n % 2 else LEAF, droop=.3, sections=4)
finish(b, "bamboo")

# Banana plant: a pseudo-stem and broad, torn leaves.
b = builder()
b.add(kit.bm_loft([(0, .28, .28), (1.6, .2, .2), (2.4, .16, .16)], segments=10, power=2), "#7a8a4a")
for k in range(7):
    frond(b, (0, 0, 2.3), k / 7 * TAU, -.35, 2.0, .7, LEAF_LIGHT if k % 2 else LEAF, droop=.6)
b.add(kit.bm_sphere(.18, 8, 6, scale=(1, 1, 1.6)), "#7a2a3a", at=(.25, 0, 1.9))
finish(b, "banana")

# Bushes: leaf masses; the flowering one carries frangipani and hibiscus blooms.
b = builder()
for k, (x, y, r) in enumerate([(0, 0, .9), (.7, .3, .65), (-.6, .4, .6), (.2, -.5, .55)]):
    foliage_blob(b, (x, y, r * .55), r, k * 2.1 + 1.0, [LEAF_DARK, LEAF][k % 2])
finish(b, "bush_a")
b = builder()
for k, (x, y, r) in enumerate([(0, 0, .8), (.6, .2, .55), (-.55, .3, .5)]):
    foliage_blob(b, (x, y, r * .55), r, k * 1.7 + 3.0, [LEAF, LEAF_LIGHT][k % 2])
for k in range(14):
    a = k * 2.399
    r = .4 + (k % 4) * .17
    color = ["#f4efe2", "#e2506a", "#f2c84a", "#f4efe2"][k % 4]
    b.add(kit.bm_sphere(.09, 6, 4, scale=(1, 1, .6)), color, at=(r * math.cos(a), r * math.sin(a), .55 + (k % 3) * .2))
finish(b, "bush_flower")

# Grass tufts and reeds.
for name, count, height, color in (("grass_a", 9, .55, "#6f9a44"), ("grass_b", 7, .9, "#86a650"), ("reeds", 10, 1.8, "#8a9a50")):
    b = builder()
    for k in range(count):
        a = k / count * TAU + k * .3
        bm = kit.bm_blade(height * (.7 + (k % 3) * .15), .08 if name != "reeds" else .05, .01, sections=3, tip=.5)
        b.add(bm, color, at=(.12 * math.cos(a), .12 * math.sin(a), 0), rotation=(-PI / 2 + .35 * math.cos(k), 0, a), smooth=False)
    if name == "reeds":
        for k in range(3):
            b.add(kit.bm_loft([(0, .035, .035), (.25, .04, .04), (.3, .01, .01)], segments=6, power=2), "#5a3a22", at=(.1 * math.cos(k * 2), .1 * math.sin(k * 2), 1.5))
    finish(b, name)

# Fern: a low rosette of fronds.
b = builder()
for k in range(9):
    frond(b, (0, 0, .15), k / 9 * TAU, -.6, 1.1, .3, LEAF_DARK if k % 2 else LEAF, droop=.5)
finish(b, "fern")

# ------------------------------------------------------------------------------------------------ rocks
for name, radius, seed, color in (("rock_a", 1.0, 1.7, STONE), ("rock_b", .8, 4.2, STONE_DARK), ("rock_c", .6, 7.9, STONE_LIGHT)):
    b = builder()
    b.add(kit.bm_rock(radius, seed, roughness=.25, subdivisions=2, flatten=.7), color, at=(0, 0, radius * .25), angle=55)
    b.add(kit.bm_sphere(radius * .55, 8, 5, scale=(1.2, 1, .25)), MOSS, at=(radius * .15, 0, radius * .62))
    finish(b, name)
b = builder()
for k, (x, y, z, r) in enumerate([(0, 0, 1.2, 2.2), (1.8, .6, .8, 1.5), (-1.7, .4, .9, 1.6), (.4, 1.4, 2.4, 1.3), (-.6, -1.2, .6, 1.1)]):
    b.add(kit.bm_rock(r, k * 2.3 + .9, roughness=.3, subdivisions=2, flatten=.95), STONE if k % 2 else STONE_DARK, at=(x, y, z), angle=50)
b.add(kit.bm_sphere(1.4, 10, 6, scale=(1.3, 1, .2)), MOSS, at=(.2, .2, 3.3))
finish(b, "cliff")
b = builder()
b.add(kit.bm_rock(.6, 3.3, roughness=.15, subdivisions=1, flatten=.18), "#8a8a7e", angle=60)
finish(b, "path_stone")

# ------------------------------------------------------------------------------------------------ ruins and culture
# A broken pillar on its base, with fallen drums.
b = builder()
b.add(kit.bm_box((1.3, 1.3, .5)), STONE_DARK, at=(0, 0, .25), bevel=.05, angle=30)
pillar = kit.bm_loft([(.5, .48, .48), (1.6, .45, .45), (2.6, .43, .43), (3.2, .42, .42)], segments=8, power=2)
for v in pillar.verts:
    if v.co.z > 3.0:
        v.co.z -= .5 * (.5 + .5 * math.sin(v.co.x * 9 + v.co.y * 5))
b.add(pillar, STONE, angle=40)
for k, (x, y) in enumerate([(1.3, .5), (.9, -1.1)]):
    b.add(kit.bm_loft([(0, .42, .42), (.6, .42, .42)], segments=8, power=2), STONE_LIGHT, at=(x, y, .42), rotation=(PI / 2, 0, k * 1.2))
b.add(kit.bm_sphere(.5, 8, 5, scale=(1.3, 1, .3)), MOSS, at=(0, 0, .52))
finish(b, "ruin_pillar")

# A broken wall with a relief band and fallen blocks.
b = builder()
heights = [2.6, 2.4, 2.8, 2.0, 1.4, 1.8, 2.5, 2.2]
for k, h in enumerate(heights):
    b.add(kit.bm_box((.78, .9, h)), STONE if k % 2 else STONE_LIGHT, at=(-2.73 + k * .78, 0, h / 2), bevel=.04, bevel_segments=1, angle=30)
    b.add(kit.bm_box((.6, .05, .45)), STONE_DARK, at=(-2.73 + k * .78, -.46, 1.05), smooth=False)
b.add(kit.bm_box((6.4, 1.05, .3)), STONE_DARK, at=(0, 0, .15), bevel=.04, bevel_segments=1)
for k, (x, y, r) in enumerate([(1.2, -1.2, .4), (2.4, -.9, .3), (-1.6, -1.1, .35)]):
    b.add(kit.bm_box((r * 1.8, r * 1.4, r * 1.1)), STONE, at=(x, y, r * .55), rotation=(0, 0, k), bevel=.04, bevel_segments=1)
b.add(kit.bm_sphere(.7, 8, 5, scale=(1.6, .7, .3)), MOSS, at=(-.8, 0, 2.55))
finish(b, "ruin_wall")

# Candi bentar: a split gate, two mirrored stepped towers with a passage between them.
b = builder()
for side in (-1, 1):
    x = side * 1.55
    z = 0
    for tier, (w, h) in enumerate([(1.6, 1.2), (1.4, 1.4), (1.2, .9), (.95, .8), (.7, .6), (.45, .5)]):
        b.add(kit.bm_box((w, 1.5 - tier * .14, h)), STONE if tier % 2 == 0 else "#9a5a3a", at=(x + side * (1.6 - w) / 2 * .3, 0, z + h / 2), bevel=.035, bevel_segments=1, angle=30)
        z += h
    b.add(kit.bm_cone(.18, .02, .5, 6), STONE_LIGHT, at=(x, 0, z + .25))
for k in range(4):
    b.add(kit.bm_box((1.4, 2.2 - k * .3, .18)), STONE_LIGHT, at=(0, 0, .09 + k * .18), bevel=.02, bevel_segments=1)
finish(b, "gate_bentar")

# Dwarapala: a kneeling temple guardian with a club, on a plinth.
b = builder()
b.add(kit.bm_box((1.3, 1.5, .5)), STONE_DARK, at=(0, 0, .25), bevel=.04)
b.add(kit.bm_loft([(.5, .5, .55), (.9, .55, .5), (1.4, .5, .42), (1.85, .42, .32), (2.0, .2, .2)], segments=12, power=2.2), STONE, angle=50)
b.add(kit.bm_loft([(1.95, .1, .1), (2.05, .26, .27, 0, -.04), (2.35, .3, .3, 0, -.03), (2.62, .24, .24), (2.75, .08, .08)], segments=12, power=2), STONE, angle=55)
for sx in (-1, 1):
    b.add(kit.bm_sphere(.07, 8, 5), STONE_LIGHT, at=(sx * .1, -.28, 2.38))
    b.add(kit.bm_cone(.035, 0, .1, 5), STONE_LIGHT, at=(sx * .07, -.27, 2.18), rotation=(PI, 0, 0))
    b.add(kit.bm_tube((sx * .5, 0, 1.75), (sx * .55, -.3, 1.15), .12, .1, 8), STONE)
b.add(kit.bm_loft([(0, .1, .1), (.9, .16, .16), (1.25, .22, .22), (1.35, .1, .1)], segments=8, power=2), STONE_DARK, at=(.6, -.35, .55))
b.add(kit.bm_sphere(.45, 8, 5, scale=(1.4, 1, .3)), MOSS, at=(-.2, .2, 1.9))
finish(b, "statue")

# A small stupa on a square plinth.
b = builder()
b.add(kit.bm_box((1.6, 1.6, .4)), STONE_DARK, at=(0, 0, .2), bevel=.04)
b.add(kit.bm_loft([(.4, .7, .7), (.55, .62, .62), (1.1, .66, .66), (1.6, .45, .45), (1.85, .15, .15)], segments=12, power=2), STONE, angle=50)
b.add(kit.bm_box((.25, .25, .2)), STONE_DARK, at=(0, 0, 1.95))
b.add(kit.bm_loft([(0, .08, .08), (.6, .02, .02)], segments=6, power=2), STONE_LIGHT, at=(0, 0, 2.05))
finish(b, "stupa_small")

# Stone lantern with a glowing lamp chamber.
b = builder()
b.add(kit.bm_box((.7, .7, .25)), STONE_DARK, at=(0, 0, .125), bevel=.03)
b.add(kit.bm_loft([(.25, .16, .16), (1.3, .13, .13)], segments=8, power=3), STONE)
b.add(kit.bm_box((.55, .55, .1)), STONE_LIGHT, at=(0, 0, 1.35), bevel=.02)
for sx in (-1, 1):
    for sy in (-1, 1):
        b.add(kit.bm_box((.07, .07, .42)), STONE, at=(sx * .22, sy * .22, 1.61))
b.add(kit.bm_loft([(1.82, .4, .4), (1.95, .3, .3), (2.15, .05, .05)], segments=4, power=2), STONE, rotation=(0, 0, PI / 4))
lamp = b.add(kit.bm_box((.3, .3, .32)), "#ffcf7a", at=(0, 0, 1.6))
lamp.data.materials.clear()
lamp.data.materials.append(LAMP)
finish(b, "lantern")

# Obor: a bamboo torch with a burning bowl.
b = builder()
b.add(kit.bm_tube((0, 0, 0), (0, 0, 2.4), .05, .045, 7), BAMBOO)
b.add(kit.bm_loft([(2.35, .08, .08), (2.55, .16, .16), (2.6, .15, .15)], segments=8, power=2), "#5a4a3a")
flame = b.add(kit.bm_loft([(2.55, .12, .12), (2.75, .1, .1), (3.05, .01, .01)], segments=8, power=2), "#ffa040")
flame.data.materials.clear()
flame.data.materials.append(LAMP)
finish(b, "torch")

# Umbul-umbul: a tall, curved bamboo pole with a long streamer.
b = builder()
pole = [(0, 0, 0), (0, 0, 2.5), (.1, 0, 5.0), (.45, 0, 7.0), (1.1, 0, 8.3)]
for k in range(4):
    b.add(kit.bm_tube(pole[k], pole[k + 1], .07 - k * .012, .07 - (k + 1) * .012, 7), BAMBOO)
streamer = kit.bm_box((.5, .02, 4.2))
for v in streamer.verts:
    v.co.x += .25 * math.sin(v.co.z * 1.4)
b.add(streamer, "#d23a2e", at=(.55, 0, 5.4), rotation=(0, -.12, 0), smooth=False)
b.add(kit.bm_box((.5, .025, 1.2)), "#f2ece0", at=(.62, 0, 3.0), rotation=(0, -.1, 0), smooth=False)
finish(b, "banner")

# Bamboo fence: a 3 m section.
b = builder()
for k in range(11):
    b.add(kit.bm_tube((-1.5 + k * .3, 0, 0), (-1.5 + k * .3, 0, 1.0 + (k % 2) * .12), .05, .045, 6), BAMBOO if k % 2 else "#8a9a4a")
for z in (.35, .8):
    b.add(kit.bm_tube((-1.6, -.06, z), (1.6, -.06, z), .035, .035, 6), "#7a6a3a")
finish(b, "fence")

# Rumah panggung: a stilt hut with bamboo walls, a thatched gable roof and a ladder.
b = builder()
for sx in (-1, 1):
    for sy in (-1, 0, 1):
        b.add(kit.bm_tube((sx * 1.6, sy * 1.2, 0), (sx * 1.6, sy * 1.2, 1.4), .1, .09, 6), WOOD_DARK)
b.add(kit.bm_box((3.6, 2.9, .15)), WOOD, at=(0, 0, 1.45), bevel=.02, bevel_segments=1)
b.add(kit.bm_box((3.3, 2.5, 1.7)), "#b49a62", at=(0, 0, 2.38), bevel=.02, bevel_segments=1, angle=30)
b.add(kit.bm_box((.75, .06, 1.2)), "#3a2a1a", at=(0, -1.26, 2.15))
for sx in (-1, 1):
    b.add(kit.bm_box((.6, .06, .5)), "#3a2a1a", at=(sx * 1.05, -1.26, 2.5))
b.add(gable(4.3, 3.6, 1.45), THATCH, at=(0, 0, 3.18), angle=20)
b.add(kit.bm_box((.12, 3.9, .12)), WOOD_DARK, at=(0, 0, 4.62))
for k in range(5):
    b.add(kit.bm_box((.6, .05, .05)), WOOD, at=(0, -1.75 - k * .12, .2 + k * .3))
for sx in (-1, 1):
    b.add(kit.bm_tube((sx * .3, -1.7, 0), (sx * .3, -1.5, 1.5), .04, .04, 6), WOOD)
finish(b, "hut")

# Jukung: an outrigger canoe with a small sail.
b = builder()
hull = kit.bm_loft([(-2.6, .02, .02), (-2.2, .22, .2), (-1.0, .38, .32), (0, .42, .34), (1.0, .38, .32), (2.2, .22, .2), (2.7, .02, .02)], segments=10, power=2.4)
for v in hull.verts:
    if v.co.y > 0:
        v.co.y *= .4
    v.co.y += .32 * (abs(v.co.z) / 2.7) ** 2
b.add(hull, "#8a4a2a", rotation=(0, PI / 2, 0), angle=40)
b.add(kit.bm_box((5.0, .05, .08)), "#f2ece0", at=(0, 0, .3))
for x in (-1.0, 1.0):
    b.add(kit.bm_tube((x, -1.7, .3), (x, 1.7, .3), .04, .04, 6), WOOD_DARK)
for sy in (-1, 1):
    b.add(kit.bm_loft([(-2.2, .02, .02), (-1.8, .1, .1), (1.8, .1, .1), (2.2, .02, .02)], segments=6, power=2), "#e8e0d0", at=(0, sy * 1.7, .12), rotation=(0, PI / 2, 0))
b.add(kit.bm_tube((.3, 0, .2), (.3, 0, 3.3), .05, .04, 6), WOOD_DARK)
sail = bmesh.new()
v0, v1, v2 = sail.verts.new((.3, 0, 3.2)), sail.verts.new((.3, 0, .7)), sail.verts.new((2.0, 0, .9))
sail.faces.new((v0, v1, v2))
b.add(sail, "#e2506a", angle=1, smooth=False)
finish(b, "boat")

# Dermaga: a short pier on posts.
b = builder()
for k in range(7):
    b.add(kit.bm_box((1.8, .42, .1)), WOOD if k % 2 else "#8a6a44", at=(0, -1.35 + k * .45, .9), bevel=.015, bevel_segments=1)
for sx in (-1, 1):
    for k in range(3):
        b.add(kit.bm_tube((sx * .8, -1.3 + k * 1.3, -2.0), (sx * .8, -1.3 + k * 1.3, 1.2), .1, .1, 6), WOOD_DARK)
finish(b, "pier")

# Market goods: crates, a barrel and sacks.
b = builder()
b.add(kit.bm_box((.8, .8, .7)), "#9a7448", at=(0, 0, .35), bevel=.03, bevel_segments=1)
b.add(kit.bm_box((.6, .6, .5)), "#86613c", at=(.75, .2, .25), rotation=(0, 0, .3), bevel=.03, bevel_segments=1)
b.add(kit.bm_box((.55, .55, .45)), "#a58058", at=(.05, .1, .93), rotation=(0, 0, .5), bevel=.03, bevel_segments=1)
b.add(kit.bm_loft([(0, .3, .3), (.35, .35, .35), (.7, .3, .3)], segments=12, power=2), "#7a5232", at=(-.75, .3, 0))
for k in range(3):
    b.add(kit.bm_sphere(.3, 8, 6, scale=(1, .8, 1.2)), "#d8c8a0", at=(-.5 + k * .45, -.75, .32))
finish(b, "goods")

# Lily pads with a lotus.
b = builder()
for k in range(6):
    a = k * 2.2
    r = .4 + (k % 3) * .35
    pad = kit.bm_loft([(0, .32 + (k % 2) * .1, .32 + (k % 2) * .1), (.03, .3 + (k % 2) * .1, .3 + (k % 2) * .1)], segments=12, power=2)
    b.add(pad, "#4f8a3c" if k % 2 else "#3e7432", at=(r * math.cos(a), r * math.sin(a), 0), smooth=False)
for k in range(6):
    a = k / 6 * TAU
    b.add(kit.bm_blade(.25, .12, .02, sections=3, tip=.6), "#f0a0c0", at=(.1 * math.cos(a), .1 * math.sin(a), .05), rotation=(-1.0, 0, a + PI / 2))
finish(b, "lily")

# ------------------------------------------------------------------------------------------------ landscapes
# A dead tree for barren land: a grey trunk with bare, crooked branches.
b = builder()
b.add(kit.bm_loft([(0, .4, .4), (.4, .26, .26), (2.6, .2, .2), (3.6, .12, .12)], segments=8, power=2.4), "#6e6458", angle=50)
for k, (x, y, z, l) in enumerate([(1.4, .3, 4.8, 1), (-1.2, .6, 4.4, 1), (.3, -1.1, 4.9, 1), (.9, 1.0, 3.6, .7)]):
    b.add(kit.bm_tube((0, 0, 2.6 + k * .2), (x, y, z), .11, .04, 6), "#6e6458")
    b.add(kit.bm_tube((x, y, z), (x * 1.3, y * 1.3 + .3, z + .6), .04, .015, 5), "#5e564c")
finish(b, "tree_dead")

# A dry shrub of the savanna and the sand sea.
b = builder()
for k in range(11):
    a = k * 2.399
    b.add(kit.bm_tube((0, 0, 0), (.6 * math.cos(a), .6 * math.sin(a), .55 + (k % 3) * .2), .025, .008, 4), "#8a7444" if k % 2 else "#a08a54")
b.add(kit.bm_sphere(.35, 8, 5, scale=(1, 1, .5)), "#9a8a50", at=(0, 0, .25))
finish(b, "shrub_dry")

# A rice field plot: still water inside a dike, rows of young rice.
b = builder()
b.add(kit.bm_box((8.4, 6.4, .2)), "#6a5a3a", at=(0, 0, .02), bevel=.08, angle=30)
b.add(kit.bm_box((7.6, 5.6, .06)), "#5a8a8a", at=(0, 0, .11))
for row in range(7):
    for col in range(9):
        b.add(kit.bm_cone(.09, .01, .45, 4), "#7ab84a" if (row + col) % 3 else "#8ac85a", at=(-3.2 + col * .8, -2.4 + row * .8, .32))
finish(b, "sawah")

# A mountain: a rocky peak on a green base, used far beyond the arena (the game scales it up).
b = builder()
peak = kit.bm_ico(1.0, 3)
for v in peak.verts:
    p = v.co.normalized()
    height = max(0.0, p.z)
    noise = math.sin(p.x * 7.3) * math.sin(p.y * 6.1) * .12 + math.sin(p.x * 17 + p.y * 13) * .04
    v.co.x *= 1.1 + noise
    v.co.y *= 1.1 + noise
    v.co.z = max(-.05, v.co.z) * (1.6 + noise * 2)
b.add(peak, "#6a6a62", angle=55)
b.add(kit.bm_loft([(-.05, 1.25, 1.25), (.25, 1.05, 1.05), (.45, .7, .7)], segments=24, power=2), "#4a7a3a", angle=40)
finish(b, "mountain")

# A volcano: a broad cone with a crater, ash at the top and a glowing throat.
b = builder()
cone = kit.bm_loft([(0, 1.3, 1.3), (.35, 1.0, 1.0), (.9, .5, .5), (1.15, .26, .26), (1.17, .2, .2), (1.0, .14, .14)], segments=28, power=2, caps=False)
for v in cone.verts:
    a = math.atan2(v.co.y, v.co.x)
    v.co.x *= 1 + .06 * math.sin(a * 5) * (1 - v.co.z)
    v.co.y *= 1 + .06 * math.sin(a * 5) * (1 - v.co.z)
b.add(cone, "#5a5248", angle=55)
b.add(kit.bm_loft([(-.02, 1.35, 1.35), (.3, 1.04, 1.04)], segments=28, power=2), "#4a6a34", angle=40)
b.add(kit.bm_loft([(.7, .66, .66), (1.12, .3, .3)], segments=28, power=2, caps=False), "#7a7268", angle=55)
throat = b.add(kit.bm_loft([(.98, .15, .15), (1.0, .13, .13)], segments=16, power=2), "#ff6a2a")
throat.data.materials.clear()
throat.data.materials.append(glow("Lava", "#ff5a1a", 6.0))
finish(b, "volcano")

# A sand dune: a soft, wind-shaped mound.
b = builder()
dune = kit.bm_sphere(1.0, 20, 10)
for v in dune.verts:
    v.co.z = max(0.0, v.co.z) * .35 * (1 + .25 * v.co.x)
    v.co.y *= .6
b.add(dune, "#d8c08a", angle=60)
finish(b, "dune")

kit.bake_ao(objects, scene, strength=.7, samples=96, distance=1.0)
kit.export_glb(scene, OUTPUT / "environment.glb")
bpy.data.libraries.write(str(ROOT / "assets/blender/environment.blend"), {scene}, fake_user=True)
try:
    if previous and bpy.context.window:
        bpy.context.window.scene = previous
except ReferenceError:
    pass
result = {"objects": len(objects), "triangles": {o.name: kit.triangles(o) for o in objects}, "bytes": (OUTPUT / "environment.glb").stat().st_size}
print(result)
