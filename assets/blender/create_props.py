"""Run through Blender MCP. Builds the static props of the game and exports
src/BattleNusantara.UI/wwwroot/models/props.glb with one node per prop:
tower, outpost, relic, artifact, ore, flotsam, chest, loot_minor, loot_major, loot_legend.

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios. Original designs.

v0.14: bevelled stonework, carved bands and niches, a joglo roof, ornaments, and baked ambient occlusion. Colours
are vertex colours; parts on the "Tint" material are white and take the team or unit colour in the game.
Each prop stands on Z = 0 and faces -Y. Shared helpers live in nusantara_kit.py next to this script.
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
sys.path.insert(0, str(ROOT / "assets/blender"))
import nusantara_kit as kit  # noqa: E402

importlib.reload(kit)
PI, TAU = math.pi, math.tau

previous = bpy.context.window.scene if bpy.context.window else None
scene = kit.fresh_scene("Nusantara_Props")
TINT, FIXED = kit.materials()
STONE, STONE_DARK, STONE_LIGHT, MOSS = "#8d9189", "#62675f", "#a6a99e", "#5d7a46"
WOOD, WOOD_DARK, THATCH, GOLD = "#7a5432", "#4e3420", "#a8894f", "#d4b478"


def builder():
    return kit.Builder(TINT, FIXED, scene.collection)


def stepped_base(b, width, tiers, height, color=STONE, dark=STONE_DARK):
    """A plinth of bevelled tiers with a dark recess under each lip."""
    z = 0.0
    for i in range(tiers):
        w = width - i * width * .08
        b.add(kit.bm_box((w, w, height)), color if i % 2 == 0 else STONE_LIGHT, at=(0, 0, z + height / 2), bevel=.04, bevel_segments=2, angle=30)
        b.add(kit.bm_box((w * .97, w * .97, height * .18)), dark, at=(0, 0, z + height * 1.02), angle=30)
        z += height
    return z


def niche_band(b, z, width, height, count, color=STONE_DARK, depth=.06):
    """Recessed panels around a square body, read as carved niches."""
    step = width / count
    for side in range(4):
        angle = side * PI / 2
        for k in range(count):
            offset = -width / 2 + step * (k + .5)
            x, y = offset, -width / 2 - .01
            c, s = math.cos(angle), math.sin(angle)
            b.add(kit.bm_box((step * .62, depth, height)), color, at=(x * c - y * s, x * s + y * c, z), rotation=(0, 0, angle), bevel=.015, bevel_segments=1, angle=30)


def stupa(b, at, size, color=STONE, finial=STONE_LIGHT):
    x, y, z = at
    b.add(kit.bm_loft([(0, .5 * size, .5 * size), (.1 * size, .45 * size, .45 * size), (.45 * size, .42 * size, .42 * size), (.75 * size, .24 * size, .24 * size),
                       (.85 * size, .08 * size, .08 * size), (1.15 * size, .02 * size, .02 * size)], segments=8, power=2.2), color, at=(x, y, z))


# ------------------------------------------------------------------------------------------------ tower
# A candi tower guarding a MOBA lane: plinth with stairs, a body with niches, a tiered roof crowned with a team-coloured
# jewel. About 9 m tall; the game scales it by 1.1.
b = builder()
top = stepped_base(b, 3.6, 2, .45)
for k in range(5):  # stairs on the front
    b.add(kit.bm_box((1.0, .32, .18 * (k + 1))), STONE_LIGHT, at=(0, -1.95 + k * .25, .09 * (k + 1)), bevel=.02, bevel_segments=1, angle=30)
b.add(kit.bm_box((2.6, 2.6, 2.6)), STONE, at=(0, 0, top + 1.3), bevel=.05, angle=30)
niche_band(b, top + 1.35, 2.6, 1.5, 3)
b.add(kit.bm_box((.9, .08, 1.7)), "#1c1a18", at=(0, -1.33, top + 1.05), bevel=.02, bevel_segments=1)  # doorway
b.add(kit.bm_loft([(top + 1.9, .5, .06, 0, -1.34), (top + 2.25, .62, .06, 0, -1.34), (top + 2.4, .3, .06, 0, -1.34)], segments=12, power=3), GOLD, at=(0, 0, 0))  # kala head
b.add(kit.bm_box((2.85, 2.85, .22)), STONE_LIGHT, at=(0, 0, top + 2.65), bevel=.04, angle=30)
z = top + 2.76
for tier in range(4):
    w = 2.3 - tier * .48
    h = .62 - tier * .06
    b.add(kit.bm_box((w, w, h), taper=1.06), STONE if tier % 2 == 0 else STONE_LIGHT, at=(0, 0, z + h / 2), bevel=.04, angle=30)
    for sx in (-1, 1):
        for sy in (-1, 1):
            stupa(b, (sx * w * .46, sy * w * .46, z + h), .34 - tier * .05)
    z += h
b.add(kit.bm_loft([(z, .32, .32), (z + .5, .22, .22), (z + 1.1, .06, .06), (z + 1.25, .01, .01)], segments=12, power=2), STONE_LIGHT)
b.add(kit.bm_ico(.38, 2), "#ffffff", at=(0, 0, z + 1.65), tint=True)
b.add(kit.bm_torus(.55, .04, 24, 6), GOLD, at=(0, 0, z + 1.65), rotation=(PI / 2.6, 0, .4))
for sx in (-1, 1):  # team cloth hanging from the eaves
    b.add(kit.bm_box((.5, .04, 1.1), taper=.7), "#ffffff", at=(sx * .75, -1.34, top + 2.0), tint=True, angle=30)
tower = b.join("tower")

# ------------------------------------------------------------------------------------------------ outpost
# A pendopo: a raised stone floor, four soko guru posts and twelve outer posts under a joglo roof.
b = builder()
b.add(kit.bm_box((5.4, 5.4, .45)), STONE, at=(0, 0, .225), bevel=.06, angle=30)
b.add(kit.bm_box((5.2, 5.2, .06)), "#8a6a44", at=(0, 0, .48))  # timber floor
for k in range(3):
    b.add(kit.bm_box((1.4, .35, .15 * (k + 1))), STONE_LIGHT, at=(0, -2.85 + k * .28, .075 * (k + 1)), bevel=.02, bevel_segments=1)
for sx in (-1, 1):
    for sy in (-1, 1):
        b.add(kit.bm_box((.26, .26, 2.9)), WOOD_DARK, at=(sx * .9, sy * .9, 1.95), bevel=.03, bevel_segments=1, angle=30)
        b.add(kit.bm_box((.38, .38, .14)), GOLD, at=(sx * .9, sy * .9, .58), bevel=.02, bevel_segments=1)
for k in range(12):
    side, slot = divmod(k, 3)
    t = (slot - 1) * 2.1
    x, y = [(t, -2.35), (2.35, t), (-t, 2.35), (-2.35, -t)][side]
    b.add(kit.bm_box((.18, .18, 2.2)), WOOD, at=(x, y, 1.6), bevel=.025, bevel_segments=1, angle=30)
b.add(kit.bm_box((4.9, 4.9, .16)), WOOD_DARK, at=(0, 0, 2.72), bevel=.03, angle=30)
# Joglo roof: a wide lower skirt, a steeper middle and the high brunjung, with a ridge.
b.add(kit.bm_loft([(2.65, 3.45, 3.45), (3.2, 2.35, 2.35)], segments=4, power=2, caps=False), THATCH, rotation=(0, 0, PI / 4), angle=20)
b.add(kit.bm_loft([(3.18, 2.4, 2.4), (3.9, 1.35, 1.35)], segments=4, power=2, caps=False), "#9a7c45", rotation=(0, 0, PI / 4), angle=20)
b.add(kit.bm_loft([(3.86, 1.4, 1.1), (5.1, .25, .12)], segments=4, power=2), THATCH, rotation=(0, 0, PI / 4), angle=20)
b.add(kit.bm_box((1.3, .14, .14)), WOOD_DARK, at=(0, 0, 5.12), bevel=.02, bevel_segments=1)
for sx in (-1, 1):
    b.add(kit.bm_cone(.1, .02, .45, 6), GOLD, at=(sx * .66, 0, 5.3), rotation=(0, sx * .5, 0))
for k in range(4):  # roof edge trim
    a = k * PI / 2
    b.add(kit.bm_box((4.95, .1, .1)), GOLD, at=(2.44 * math.sin(a), -2.44 * math.cos(a), 2.66), rotation=(0, 0, a), tint=True)
b.add(kit.bm_box((.7, .7, .55)), "#a07a44", at=(1.6, 1.4, .78), bevel=.03, bevel_segments=1)
b.add(kit.bm_box((.55, .55, .4)), "#8a6a3a", at=(1.55, .65, .7), rotation=(0, 0, .3), bevel=.03, bevel_segments=1)
for k in range(3):
    b.add(kit.bm_sphere(.26, 10, 6, scale=(1, .8, 1.1)), "#d8c8a0", at=(-1.5 + k * .45, 1.6, .74))
b.add(kit.bm_tube((-2.2, -2.2, .48), (-2.2, -2.2, 4.2), .05, .04, 8), WOOD_DARK)
b.add(kit.bm_box((.04, .9, 1.4), taper=.6), "#ffffff", at=(-2.2, -1.73, 3.4), tint=True)
outpost = b.join("outpost")

# ------------------------------------------------------------------------------------------------ relic
# A prasasti: an inscribed stele with a rounded top on a lotus base, carved glyph lines that glow, and moss.
b = builder()
b.add(kit.bm_box((1.5, 1.0, .3)), STONE_DARK, at=(0, 0, .15), bevel=.05, angle=30)
b.add(kit.bm_loft([(.3, .62, .42), (.42, .55, .37), (.5, .6, .4)], segments=16, power=3), STONE)
b.add(kit.bm_loft([(.5, .5, .18), (1.4, .47, .17), (2.0, .44, .16), (2.22, .36, .15), (2.36, .2, .13), (2.42, .04, .05)], segments=16, power=4), STONE_LIGHT, angle=30)
b.add(kit.bm_box((.82, .02, 1.4)), STONE_DARK, at=(0, -.165, 1.3))
for line in range(6):
    b.add(kit.bm_box((.62 - (line % 2) * .12, .02, .05)), "#ffffff", at=(0, -.178, .8 + line * .22), tint=True, bevel=.008, bevel_segments=1)
for k, (x, z, r) in enumerate([(.38, .62, .14), (-.4, .75, .1), (.3, 2.1, .09)]):
    b.add(kit.bm_sphere(r, 8, 5, scale=(1, .4, .8)), MOSS, at=(x, -.16, z))
relic = b.join("relic")

# ------------------------------------------------------------------------------------------------ artifact
# The Jagat Heirloom Keris floating point-down over a pedestal ringed with light.
b = builder()
b.add(kit.bm_loft([(0, .55, .55), (.12, .5, .5), (.2, .38, .38), (.45, .32, .32), (.55, .42, .42)], segments=8, power=2.6), STONE_DARK, rotation=(0, 0, PI / 8))
b.add(kit.bm_torus(.48, .025, 28, 6), "#ffffff", at=(0, 0, .6), tint=True)
blade = kit.bm_blade(1.25, .14, .035, sections=24, wave=.045, waves=7, tip=.18)
b.add(blade, "#d8dde0", at=(0, 0, 2.2), rotation=(PI / 2, 0, PI / 2), angle=25)
b.add(kit.bm_loft([(0, .16, .05), (.06, .2, .06), (.1, .12, .05)], segments=10, power=2.4), GOLD, at=(0, 0, 2.18))
b.add(kit.bm_loft([(0, .055, .055), (.25, .06, .06), (.42, .08, .07), (.48, .05, .05)], segments=10, power=2), "#5a3a22", at=(0, 0, 2.26), rotation=(0, .25, 0))
b.add(kit.bm_ico(.035, 1), "#ffffff", at=(0, -.06, 2.24), tint=True)
artifact = b.join("artifact")

# ------------------------------------------------------------------------------------------------ ore
# A gold mine: weathered rocks split by veins and crystals of gold, with a small cart rail.
b = builder()
for k, (x, y, r, seed) in enumerate([(0, 0, 1.15, 1.3), (1.0, .45, .75, 2.1), (-.85, .55, .65, 3.4), (.25, -.8, .55, 4.7), (-.6, -.55, .45, 5.9)]):
    b.add(kit.bm_rock(r, seed, roughness=.22, subdivisions=2, flatten=.8), STONE if k % 2 == 0 else STONE_DARK, at=(x, y, r * .45), angle=55)
for k, (x, y, z, rx, rz) in enumerate([(.3, -.55, 1.0, .4, .2), (-.35, -.4, 1.25, -.3, .5), (.7, -.15, .8, .2, -.4), (-.85, .1, .6, .1, .9), (.05, -.2, 1.45, -.1, .1), (1.05, .2, .95, .5, .3)]):
    b.add(kit.bm_cone(.16 + (k % 3) * .04, 0, .55 + (k % 2) * .25, 6), GOLD if k % 3 else "#f2d27a", at=(x, y, z), rotation=(rx, rz, k))
b.add(kit.bm_box((.9, .55, .4), taper=1.2), WOOD, at=(1.55, -.85, .32), rotation=(0, 0, .5), bevel=.03, bevel_segments=1)
for k in range(5):
    b.add(kit.bm_ico(.11, 1), GOLD, at=(1.45 + (k % 3) * .14, -.85 + (k // 3) * .14, .58), rotation=(0, 0, k))
ore = b.join("ore")

# ------------------------------------------------------------------------------------------------ flotsam
# Washed up from the Captain's ship: a banded sea chest, a barrel, a coil of rope and broken planks.
b = builder()
b.add(kit.bm_box((.95, .62, .52)), "#7a5230", at=(0, 0, .26), bevel=.035, angle=30)
b.add(kit.bm_loft([(0, .48, .31), (.12, .46, .3), (.2, .36, .26)], segments=4, power=8, caps=True), "#6a4628", at=(0, 0, .52))
for x in (-.32, 0, .32):
    b.add(kit.bm_box((.06, .65, .74)), "#4a4a46", at=(x, 0, .37), bevel=.01, bevel_segments=1)
b.add(kit.bm_box((.14, .04, .16)), GOLD, at=(0, -.33, .42), bevel=.01, bevel_segments=1)
b.add(kit.bm_loft([(0, .26, .26), (.18, .3, .3), (.36, .26, .26)], segments=14, power=2), "#8a6034", at=(1.0, .35, .02), rotation=(PI / 2, 0, .3))
b.add(kit.bm_torus(.24, .06, 16, 6), "#b89a6a", at=(-.95, .4, .07))
b.add(kit.bm_torus(.17, .055, 14, 6), "#b89a6a", at=(-.95, .4, .17))
for k in range(4):
    angle = k * 1.4 + .3
    b.add(kit.bm_box((1.35, .24, .07)), "#8a6a44" if k % 2 else "#6e5234", at=(1.05 * math.cos(angle), 1.05 * math.sin(angle), .05), rotation=(0, 0, angle + .6), bevel=.015, bevel_segments=1)
flotsam = b.join("flotsam")

# ------------------------------------------------------------------------------------------------ loot
# Treasure dropped by neutral camps. The game floats and spins them; the gem parts are "Tint" and glow by tier.
b = builder()
b.add(kit.bm_box((1.1, .72, .55)), "#7a4a24", at=(0, 0, .275), bevel=.04, angle=30)
lid = kit.bm_loft([(0, .55, .36), (.14, .54, .35), (.26, .42, .3)], segments=4, power=8)
b.add(lid, "#8a5428", at=(0, 0, .55))
for x in (-.4, 0, .4):
    b.add(kit.bm_box((.07, .76, .86)), GOLD, at=(x, 0, .42), bevel=.01, bevel_segments=1)
b.add(kit.bm_box((.18, .05, .2)), GOLD, at=(0, -.38, .5), bevel=.01, bevel_segments=1)
for k in range(6):
    b.add(kit.bm_ico(.07, 1), GOLD, at=(-.3 + k * .12, .05 * math.sin(k), .82))
b.add(kit.bm_ico(.1, 1), "#ffffff", at=(0, 0, .86), tint=True)
chest = b.join("chest")

b = builder()  # A clay jar of jamu, the herbal tonic.
b.add(kit.bm_loft([(0, .16, .16), (.08, .24, .24), (.3, .27, .27), (.48, .17, .17), (.56, .09, .09), (.64, .1, .1)], segments=14, power=2), "#b0653a")
b.add(kit.bm_loft([(.62, .09, .09), (.72, .07, .07)], segments=10, power=2), "#e8d8b0")
b.add(kit.bm_loft([(.2, .272, .272), (.26, .272, .272)], segments=14, power=2), "#ffffff", tint=True)
loot_minor = b.join("loot_minor")

b = builder()  # A golden amulet with a gem.
b.add(kit.bm_torus(.32, .05, 24, 6), GOLD, rotation=(PI / 2, 0, 0))
b.add(kit.bm_loft([(-.03, .2, .2), (.03, .2, .2)], segments=6, power=2), GOLD, rotation=(PI / 2, 0, 0))
b.add(kit.bm_ico(.13, 1), "#ffffff", at=(0, -.05, 0), tint=True)
for k in range(8):
    a = k / 8 * TAU
    b.add(kit.bm_ico(.03, 1), "#f2d27a", at=(.32 * math.cos(a), 0, .32 * math.sin(a)))
loot_major = b.join("loot_major")

b = builder()  # The mustika: a great faceted gem held in a golden cradle.
b.add(kit.bm_loft([(-.45, .02, .02), (-.1, .3, .3), (.05, .32, .32), (.4, .02, .02)], segments=8, power=2), "#ffffff", tint=True, smooth=False)
for k in range(4):
    a = k / 4 * TAU
    b.add(kit.bm_tube((.36 * math.cos(a), .36 * math.sin(a), -.35), (.3 * math.cos(a), .3 * math.sin(a), .1), .025, .02, 6), GOLD)
b.add(kit.bm_torus(.36, .03, 24, 4), GOLD, at=(0, 0, -.32))
loot_legend = b.join("loot_legend")

props = [tower, outpost, relic, artifact, ore, flotsam, chest, loot_minor, loot_major, loot_legend]
kit.bake_ao(props, scene, strength=.75, samples=128, distance=1.2)
kit.export_glb(scene, OUTPUT / "props.glb")
bpy.data.libraries.write(str(ROOT / "assets/blender/props.blend"), {scene}, fake_user=True)
try:
    if previous and bpy.context.window:
        bpy.context.window.scene = previous
except ReferenceError:
    pass
result = {"props": sorted(p.name for p in props), "triangles": {p.name: kit.triangles(p) for p in props}, "bytes": (OUTPUT / "props.glb").stat().st_size}
print(result)
