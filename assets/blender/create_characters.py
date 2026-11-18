"""Run through Blender MCP. Builds every character of the game on one shared rig and exports
src/BattleNusantara.UI/wwwroot/models/characters.glb (24 variants, clips Idle, Walk, Attack, Cast, Hit, Death)
and src/BattleNusantara.UI/wwwroot/models/naga.glb (the MOBA boss, its own serpent rig).

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.

All designs are original stylized characters for this game. The superhero faction uses our own heroes
(Satria Bledhek, Rara Angkasa); no licensed comic character is modelled.

v0.14: bodies are lofted from superellipse sections (waist, chest, shoulders), heads have a jaw, nose, eyes,
brows, ears and hair, hands and shoes are modelled, hard-surface parts are bevelled, and ambient occlusion is
baked into the vertex colours. Parts on the "Tint" material are white and take the unit colour in the game.
Skinning stays rigid: every part follows exactly one bone, with ball joints covering the seams.
Shared modelling helpers live in nusantara_kit.py next to this script.
"""
import importlib
import math
import os
import sys
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector

# The repository root: two folders above this script. When the script is run through exec(), pass __file__ in its globals
# or set NUSANTARA_ROOT.
ROOT = Path(os.environ["NUSANTARA_ROOT"]) if os.environ.get("NUSANTARA_ROOT") else Path(globals().get("__file__", "")).resolve().parents[2]
OUTPUT = ROOT / "src/BattleNusantara.UI/wwwroot/models"
OUTPUT.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(ROOT / "assets/blender"))
import nusantara_kit as kit  # noqa: E402

importlib.reload(kit)
PI = math.pi

SKIN = {"dark": "#6e4630", "brown": "#8a5a3c", "tan": "#a8754f", "pale": "#d8cfc0", "green": "#4f7a3a", "ghost": "#a8e4dc", "stone": "#7d8582",
        "tuyul": "#c8b49a", "fur_brown": "#7a5a3a", "tiger": "#d8892f", "croc": "#5d7a3a", "fur_dark": "#3a2e2a", "flame": "#ff8a2a"}
HAIR = "#1a1210"

CHARACTERS = {
    # Playable heroes.
    "gajah-mada": dict(skin="brown", bulk=1.15, top="#6b2a1e", skirt="#3b2a1f", accent="#d4b478", head="crown", right="keris", left="shield", cape="#7a2a1f", beard="#2a1a12", hair_style="none"),
    "cut-nyak-dien": dict(skin="tan", top="#26262b", skirt="#26262b", long=True, accent="#c9a24a", head="veil", right="rencong"),
    "nyi-roro-kidul": dict(skin="tan", top="#1f6b5a", skirt="#1a5a4c", long=True, accent="#e2c46a", head="crown_tall", hair="#151515", right="staff", cape="#2a8a72"),
    "republik": dict(skin="brown", top="#3d5470", skirt="#2c3c52", accent="#7fe0ff", head="helmet", right="rifle", pads="#5a7290", armour=True),
    "bledhek": dict(skin="tan", top="#e8eef2", skirt="#2d6db5", accent="#7fd0ff", head="iket", right="lightning", bracer="#7fd0ff"),
    "rara-angkasa": dict(skin="tan", bulk=1.1, top="#8c86b8", skirt="#5b5590", accent="#e8e4ff", head="band", fins="#c9c4f0", cape="#6a5fb0", fists=True, armour=True),
    # AI allies.
    "diponegoro": dict(skin="brown", top="#ecebe4", skirt="#e0ddd2", long=True, accent="#c9a24a", head="turban", right="keris", beard="#1a1210"),
    "pattimura": dict(skin="dark", top=None, skirt="#a32a24", accent="#d23a2e", head="iket_red", right="parang", left="salawaku"),
    "si-pitung": dict(skin="tan", top="#1c1c1f", skirt="#1c1c1f", accent="#b23a33", head="peci", right="golok", legs="#1c1c1f"),
    "barong": dict(skin="stone", bulk=1.25, top="#c9a24a", skirt="#a8323a", accent="#f0d070", head="barong", claws="#f4ecd0", fur=True),
    # AI enemies (Kalatidha).
    "rangda": dict(skin="pale", top="#f0ece4", skirt="#2b2b2b", long=True, accent="#c23a3a", head="rangda", hair="#f2f2f2", claws="#2b2b2b"),
    "leak": dict(skin="ghost", top="#2b1f33", skirt="#2b1f33", long=True, accent="#ff7a5a", head="hood", claws="#ffd0b0"),
    "buto-ijo": dict(skin="green", bulk=1.4, top=None, skirt="#5a3a22", accent="#d4b478", head="horns", right="club"),
    "kuntilanak": dict(skin="pale", top="#f4f2ec", skirt="#f4f2ec", long=True, accent="#e8e2d8", head="hair_face", hair="#121212"),
    "pocong": dict(skin="pale", top="#f2efe6", skirt="#f2efe6", long=True, accent="#f2efe6", head="shroud", legs="#f2efe6", wrapped=True),
    # Troops and Adventure enemies.
    "prajurit": dict(skin="brown", top="#7a5a3a", skirt="#5a4630", accent="#d4b478", head="cap", right="spear", left="shield"),
    "prajurit-kalatidha": dict(skin="dark", top="#4a1f2a", skirt="#2e1a20", accent="#d17791", head="mask_horn", right="sword"),
    "guardian": dict(skin="stone", top="#5d5550", skirt="#4a443f", accent="#c8789a", head="mask_stone", right="spear", armour=True),
    "warden": dict(skin="dark", bulk=1.1, top="#3a2a4a", skirt="#2a1f36", accent="#b06a9c", head="horns_small", right="sword", left="shield", armour=True),
    "kalatidha": dict(skin="dark", bulk=1.3, top="#2a1a2e", skirt="#1f1424", long=True, accent="#e57c86", head="crown_spike", right="staff", cape="#3a1a30"),
    "spirit": dict(skin="ghost", top="#7fd6d0", skirt="#5fb8b2", long=True, accent="#c8fff8", head="hood_pale"),
    # Workers and NPCs.
    "worker": dict(skin="tan", top="#8a6a44", skirt="#6a5236", accent="#e9c368", head="caping", right="hoe", basket=True),
    "keeper": dict(skin="brown", top="#6a5a44", skirt="#4a3e30", long=True, accent="#83c9ba", head="blangkon", right="cane", beard="#d8d4cc"),
    "captain": dict(skin="tan", top="#24324a", skirt="#3a3026", accent="#e0b07a", head="wide_hat", right="sword", beard="#8a8076"),
    # Neutral creeps of the jungle camps, weakest first. Siluman are were-creatures of Indonesian folklore.
    "tuyul": dict(skin="tuyul", bulk=.85, top=None, skirt="#4a3a2a", accent="#c9a24a", head="tuyul", hair_style="none"),
    "siluman-kera": dict(skin="fur_brown", top=None, skirt="#5a3a22", accent="#d4b478", head="kera", claws="#e8dcc0", tail="#6a4a2e", legs="#6a4a2e"),
    "siluman-macan": dict(skin="tiger", bulk=1.15, top=None, skirt="#3a2a1f", accent="#d4b478", head="macan", claws="#f4f0e0", tail="#d8892f", stripes="#2a1a12", legs="#d8892f"),
    "siluman-buaya": dict(skin="croc", bulk=1.2, top=None, skirt="#3a3020", accent="#c9a24a", head="buaya", right="spear", tail="#4d6a32", legs="#4d6a32"),
    "genderuwo": dict(skin="fur_dark", bulk=1.5, top=None, skirt="#2a1f1a", accent="#5a2a22", head="genderuwo", fur=True, claws="#d8cfc0", legs="#3a2e2a"),
    "banaspati": dict(skin="flame", bulk=1.2, top="#ff6a1a", skirt="#c83a10", long=True, accent="#ffd060", head="flame", claws="#ffe080"),
    # Villagers: ambient life drawn by the client only. They never fight.
    "petani": dict(skin="tan", top="#5a6a3a", skirt="#4a3a26", accent="#c9a86a", head="caping", right="hoe"),
    "nelayan": dict(skin="brown", top="#3a5a6a", skirt="#2a3a44", accent="#c8b490", head="iket_red", right="spear", legs="#2a3a44"),
    "ibu": dict(skin="tan", top="#e8d8c0", skirt="#7a3a2a", long=True, accent="#c9a24a", head="bakul", hair="#1a1210"),
    "anak": dict(skin="tan", bulk=.8, top="#d8a040", skirt="#4a6a8a", accent="#e8e4ff"),
}
# Head gear that covers the hair; everyone else gets a short haircut.
COVERED = {"veil", "helmet", "turban", "shroud", "hood", "hood_pale", "barong", "rangda", "hair_face", "mask_stone", "caping", "blangkon", "wide_hat", "peci", "cap", "crown_spike",
           "tuyul", "kera", "macan", "buaya", "genderuwo", "flame", "bakul"}


def darker(color, factor=0.6):
    value = color.lstrip("#")
    return "#" + "".join(f"{int(int(value[i:i + 2], 16) * factor):02x}" for i in (0, 2, 4))


def limb(x, rings, y=0.0):
    """Rings for a vertical limb at x: (z, rx, ry) -> loft rings centred on (x, y)."""
    return [(z, rx, ry, x, y) for z, rx, ry in rings]


def build_character(b, name, spec):
    B = spec.get("bulk", 1.0)
    skin = SKIN[spec["skin"]]
    accent = spec["accent"]
    top = spec["top"] or skin
    sleeve = top if spec["top"] else skin
    arm_x = 0.355 + (B - 1) * 0.22  # Arm meshes hang just inside the arm bones (x 0.37), close to the body.
    leg_x = 0.13 * B
    armour = spec.get("armour", False)

    # Torso: waist, chest, shoulders and the base of the neck.
    b.add(kit.bm_loft([(1.05, .17 * B, .12 * B), (1.16, .185 * B, .125 * B), (1.28, .225 * B, .14 * B), (1.40, .27 * B, .15 * B),
                       (1.47, .3 * B, .145 * B), (1.53, .29 * B, .125 * B), (1.58, .2 * B, .1 * B), (1.62, .08, .068)], segments=16, power=2.4),
          top, "spine", angle=50)
    if armour:
        b.add(kit.bm_loft([(1.3, .225 * B, .148 * B, 0, -.01), (1.42, .26 * B, .158 * B, 0, -.01), (1.5, .265 * B, .145 * B, 0, -.01)], segments=18, power=3.2),
              darker(top, 1.25) if top != skin else top, "spine", angle=35)
    elif spec["top"] is not None and not spec.get("wrapped"):
        b.add(kit.bm_loft([(1.575, .2 * B, .1 * B), (1.61, .12 * B, .085 * B), (1.625, .085, .07)], segments=16, power=2.4), darker(top, .72), "spine", angle=35)
        if spec.get("long"):
            b.add(kit.bm_box((.06 * B, .008, .42), taper=1.15), darker(top, .8), "spine", at=(0, -.14 * B, 1.32), angle=30)
    # Pelvis and belt.
    b.add(kit.bm_loft([(0.88, .17 * B, .12 * B), (0.96, .19 * B, .13 * B), (1.06, .18 * B, .126 * B)], segments=18, power=2.4), spec["skirt"], "hips")
    b.add(kit.bm_loft([(1.0, .2 * B, .139 * B), (1.085, .196 * B, .136 * B)], segments=20, power=2.4), accent, "hips", tint=True, angle=30)
    b.add(kit.bm_ico(.035, 1), accent, "hips", at=(-.06 * B, -.14 * B, 1.04), tint=True)
    b.add(kit.bm_box((.05, .015, .18), taper=1.4), accent, "hips", at=(-.07 * B, -.145 * B, .92), tint=True, subdivide=1)
    # Sarong or skirt with a hem band.
    if spec.get("long"):
        rings = [(1.02, .19 * B, .135 * B), (.8, .25 * B, .18 * B), (.5, .3 * B, .22 * B), (.1, .36 * B, .28 * B)]
        hem = .1
    else:
        rings = [(1.02, .19 * B, .135 * B), (.88, .23 * B, .17 * B), (.7, .27 * B, .2 * B)]
        hem = .7
    b.add(kit.bm_loft(rings, segments=18, power=2.2), spec["skirt"], "hips", angle=35)
    b.add(kit.bm_loft([(hem - .005, rings[-1][1] + .008, rings[-1][2] + .008), (hem + .05, rings[-1][1] * .985 + .006, rings[-1][2] * .985 + .006)], segments=18, power=2.2),
          accent, "hips", tint=True, angle=30)
    # Neck and head.
    b.add(kit.bm_tube((0, 0, 1.57), (0, 0, 1.69), .058, .054, 12), skin, "spine")
    head_rings = [(1.64, .045, .05, 0, -.01), (1.67, .078, .088, 0, -.02), (1.71, .102, .115, 0, -.016), (1.76, .118, .128, 0, -.006),
                  (1.82, .122, .13, 0, 0), (1.88, .112, .12, 0, .01), (1.93, .085, .095, 0, .015), (1.965, .04, .05, 0, .015)]
    b.add(kit.bm_loft(head_rings, segments=18, power=2.1), skin, "head", angle=60)
    face(b, spec, skin)
    if spec.get("head") not in COVERED and spec.get("hair_style", "short") != "none" and not spec.get("hair"):
        b.add(kit.bm_sphere(.127, 18, 10, scale=(1, 1.06, .82)), HAIR, "head", at=(0, .02, 1.86))
    # Shoulders, arms and hands.
    for side, sign in (("L", 1), ("R", -1)):
        x = arm_x * sign
        pad = spec.get("pads")
        b.add(kit.bm_sphere(.083 * B * (1.45 if pad else 1), 12, 7, scale=(1.05, 1, .85)), pad or sleeve, f"upper_arm.{side}", at=(x, 0, 1.47), angle=40 if pad else 60)
        b.add(kit.bm_loft(limb(x, [(1.47, .07 * B, .07 * B), (1.34, .068 * B, .064 * B), (1.2, .056 * B, .054 * B)]), segments=10, power=2), sleeve, f"upper_arm.{side}")
        b.add(kit.bm_ico(.057 * B, 1), sleeve if spec.get("long") else skin, f"forearm.{side}", at=(x, 0, 1.185))
        b.add(kit.bm_loft(limb(x, [(1.19, .055 * B, .053 * B), (1.05, .056 * B, .054 * B), (.9, .044 * B, .042 * B)]), segments=10, power=2), skin, f"forearm.{side}")
        bracer = spec.get("bracer")
        b.add(kit.bm_loft(limb(x, [(1.04, .064 * B, .062 * B), (.93, .052 * B, .05 * B)]), segments=12, power=2.6), bracer or accent, f"forearm.{side}", tint=not bracer, angle=30)
        hand(b, x, sign, B, skin, spec, side)
        # Legs and shoes.
        lx = leg_x * sign
        legs = spec.get("legs", spec["skirt"] if spec.get("long") else (skin if spec["top"] is None else darker(spec["skirt"], .8)))
        b.add(kit.bm_loft(limb(lx, [(.97, .095 * B, .1 * B), (.75, .085 * B, .09 * B), (.53, .064 * B, .068 * B)]), segments=10, power=2), legs, f"thigh.{side}")
        b.add(kit.bm_ico(.066 * B, 1), legs, f"shin.{side}", at=(lx, -.005, .5))
        b.add(kit.bm_loft(limb(lx, [(.5, .064 * B, .068 * B), (.3, .058 * B, .062 * B), (.09, .042 * B, .045 * B)]), segments=10, power=2), legs, f"shin.{side}")
        shoe = spec.get("legs", "#3a2a1f")
        b.add(kit.bm_loft([(.008, .05 * B, .13, 0, -.055), (.05, .052 * B, .125, 0, -.05), (.085, .04 * B, .08, 0, -.02)], segments=12, power=2.6), shoe, f"shin.{side}", at=(lx, 0, 0), angle=50)
        b.add(kit.bm_loft(limb(lx, [(.07, .05 * B, .052 * B), (.13, .046 * B, .048 * B)]), segments=12, power=2.4), darker(shoe, .8) if spec.get("legs") else "#5a4430", f"shin.{side}", angle=30)
    if spec.get("wrapped"):
        # A pocong is bound in a shroud: a single wrapped column over the legs, tied at the ankles.
        b.add(kit.bm_loft([(.95, .2, .15), (.6, .22, .17), (.25, .19, .15), (.06, .16, .13)], segments=20, power=2.2), spec["top"], "hips")
        b.add(kit.bm_loft([(.16, .175, .14), (.21, .175, .14)], segments=20, power=2.2), "#cfc8b8", "hips")
    # Extras.
    if spec.get("cape"):
        b.add(kit.bm_box((.5 * B, .025, .95), taper=1.35), spec["cape"], "spine", at=(0, .17 * B, 1.06), rotation=(-.1, 0, 0), subdivide=1, angle=30)
        b.add(kit.bm_loft([(1.55, .2 * B, .14 * B), (1.6, .18 * B, .12 * B)], segments=18, power=2.6), accent, "spine", tint=True)
    if spec.get("fins"):
        for sign in (1, -1):
            b.add(kit.bm_blade(.6, .16, .03, sections=6, tip=.6), spec["fins"], "spine", at=(.2 * sign * B, .14, 1.55), rotation=(-2.1, 0, -.75 * sign), angle=30)
    if spec.get("basket"):
        b.add(kit.bm_loft([(1.08, .13, .1), (1.38, .17, .13), (1.42, .175, .135)], segments=14, power=2), "#a07a44", "spine", at=(0, .27, 0), angle=40)
    if spec.get("fur"):
        for k in range(10):
            a = k / 10 * kit.TAU
            b.add(kit.bm_cone(.07, 0, .22, 6), spec["accent"], "spine", at=(.2 * math.cos(a) * B, .14 * math.sin(a) * B, 1.62), rotation=(math.sin(a) * .9, -math.cos(a) * .9, 0))
    if spec.get("hair"):
        hair = spec["hair"]
        b.add(kit.bm_sphere(.135, 18, 10, scale=(1, 1.08, .9)), hair, "head", at=(0, .025, 1.85))
        b.add(kit.bm_loft([(1.8, .12, .07, 0, .1), (1.5, .15, .07, 0, .13), (1.15, .13, .05, 0, .14)], segments=14, power=2.4), hair, "spine")
    if spec.get("tail"):
        tail = [(0, .14 * B, 1.0), (0, .3 * B, .85), (.05, .45 * B, .62), (.12, .55 * B, .45), (.18, .58 * B, .3)]
        for k in range(4):
            b.add(kit.bm_tube(tail[k], tail[k + 1], .07 * B * (1 - k / 5), .07 * B * (1 - (k + 1) / 5), 8), spec["tail"], "hips")
    if spec.get("stripes"):
        for k in range(5):
            z = 1.12 + k * .09
            b.add(kit.bm_loft([(z, .24 * B * (1 + k * .05), .155 * B), (z + .025, .24 * B * (1 + k * .05), .155 * B)], segments=16, power=2.4), spec["stripes"], "spine", angle=30)
        for side, sign in (("L", 1), ("R", -1)):
            for z in (1.38, 1.28, 1.05):
                bone = f"upper_arm.{side}" if z > 1.2 else f"forearm.{side}"
                b.add(kit.bm_loft([(z, .072 * B, .07 * B, arm_x * sign, 0), (z + .022, .072 * B, .07 * B, arm_x * sign, 0)], segments=10, power=2), spec["stripes"], bone)
    if spec.get("beard"):
        b.add(kit.bm_loft([(1.62, .03, .03, 0, -.1), (1.67, .07, .05, 0, -.105), (1.72, .095, .06, 0, -.09)], segments=14, power=2), spec["beard"], "head")
    head_gear(b, spec.get("head"), accent, spec)
    weapon(b, spec.get("right"), arm_x, accent)
    off_hand(b, spec.get("left"), arm_x, accent)
    return b.join(name)


def face(b, spec, skin):
    if spec.get("head") in ("barong", "rangda", "shroud", "mask_stone", "mask_horn", "hair_face", "buaya", "genderuwo", "flame"):
        return
    eye_white = "#f1ece2" if spec["skin"] not in ("ghost",) else "#ffffff"
    iris = "#221812" if spec["skin"] != "ghost" else "#ff7a5a"
    for sign in (1, -1):
        b.add(kit.bm_sphere(.021, 8, 5, scale=(1, .55, .75)), eye_white, "head", at=(.044 * sign, -.113, 1.796))
        b.add(kit.bm_sphere(.012, 6, 4, scale=(1, .6, 1)), iris, "head", at=(.044 * sign, -.124, 1.796))
        b.add(kit.bm_box((.052, .014, .013)), darker(skin, .45) if spec["skin"] != "pale" else "#5a5048", "head", at=(.046 * sign, -.118, 1.826), rotation=(0, .15 * sign, 0), subdivide=1)
        b.add(kit.bm_sphere(.03, 8, 5, scale=(.45, .8, 1.15)), skin, "head", at=(.121 * sign, .0, 1.78))
    b.add(kit.bm_cone(.024, .008, .055, 8), darker(skin, .95), "head", at=(0, -.128, 1.768), rotation=(PI / 2, 0, 0))
    b.add(kit.bm_box((.045, .008, .007)), darker(skin, .55), "head", at=(0, -.117, 1.722), subdivide=1)


def hand(b, x, sign, B, skin, spec, side):
    bone = f"forearm.{side}"
    if spec.get("fists"):
        b.add(kit.bm_loft(limb(x, [(.92, .07 * B, .07 * B), (.86, .085 * B, .085 * B), (.78, .08 * B, .08 * B), (.74, .05 * B, .05 * B)]), segments=12, power=3), spec.get("fins", spec["accent"]), bone, angle=35)
        return
    b.add(kit.bm_loft(limb(x, [(.9, .043, .032), (.85, .05, .036), (.79, .047, .034), (.765, .03, .024)], y=-.008), segments=12, power=2.4), skin, bone)
    b.add(kit.bm_tube((x - .035 * sign, -.025, .86), (x - .04 * sign, -.05, .81), .014, .011, 8), skin, bone)
    if spec.get("claws"):
        for k in (-1, 0, 1):
            b.add(kit.bm_cone(.012, 0, .09, 6), spec["claws"], bone, at=(x + .028 * k, -.02, .73), rotation=(PI, 0, 0))


def spec_skin(spec):
    return SKIN[spec["skin"]]


def horns(b, color, size, curl=0.5):
    for sign in (1, -1):
        points = [(.1 * sign, 0, 1.9)]
        for k in range(1, 5):
            t = k / 4
            points.append((.1 * sign + .11 * size * t * sign, .03 * t, 1.9 + .18 * size * t - curl * .06 * t * t))
        for k in range(4):
            b.add(kit.bm_tube(points[k], points[k + 1], .042 * size * (1 - k / 4.5), .042 * size * (1 - (k + 1) / 4.5), 10), color, "head", angle=50)


def head_gear(b, kind, accent, spec):
    gold, H = "#d4b478", "head"
    band = lambda z, rx, ry, h, color, tint=False, power=2.2: b.add(kit.bm_loft([(z, rx, ry), (z + h, rx * .99, ry * .99)], segments=22, power=power), color, H, tint=tint, angle=30)
    if kind == "crown":
        band(1.84, .132, .14, .055, gold)
        for k in range(7):
            a = -PI / 2 + (k - 3) * .38
            b.add(kit.bm_blade(.11, .07, .02, sections=4, tip=.7), gold, H, at=(.133 * math.cos(a), .14 * math.sin(a), 1.89), rotation=(-PI / 2, 0, a + PI / 2), angle=30)
        b.add(kit.bm_ico(.026, 1), accent, H, at=(0, -.15, 1.87), tint=True)
    elif kind == "crown_tall":
        band(1.84, .13, .138, .05, gold)
        b.add(kit.bm_loft([(1.885, .12, .125), (1.98, .095, .1), (2.06, .06, .065), (2.16, .02, .02)], segments=16, power=3), gold, H, angle=35)
        for k in range(5):
            a = -PI / 2 + (k - 2) * .55
            b.add(kit.bm_blade(.16, .06, .02, sections=4, tip=.6), gold, H, at=(.13 * math.cos(a), .138 * math.sin(a), 1.89), rotation=(-PI / 2, 0, a + PI / 2))
        b.add(kit.bm_ico(.03, 1), accent, H, at=(0, -.12, 1.95), tint=True)
    elif kind == "crown_spike":
        band(1.86, .14, .148, .07, "#3a2030")
        for k in range(8):
            a = k / 8 * kit.TAU
            b.add(kit.bm_cone(.035, 0, .3, 6), accent, H, at=(.14 * math.cos(a), .148 * math.sin(a), 2.06), tint=True)
        horns(b, "#e8dcc0", 1.4)
    elif kind == "veil":
        b.add(kit.bm_loft([(1.67, .13, .1, 0, .02), (1.76, .145, .15, 0, .01), (1.86, .14, .145), (1.94, .1, .11), (1.99, .03, .03)], segments=20, power=2.2), "#26262b", H)
        b.add(kit.bm_loft([(1.75, .17, .12, 0, .07), (1.5, .2, .12, 0, .1), (1.3, .2, .1, 0, .12)], segments=18, power=2.4), "#26262b", "spine")
    elif kind == "helmet":
        b.add(kit.bm_loft([(1.74, .14, .15, 0, .01), (1.84, .145, .152), (1.93, .12, .13), (1.99, .06, .07)], segments=22, power=2.4), "#4a6080", H, angle=35)
        b.add(kit.bm_loft([(1.77, .12, .025, 0, -.135), (1.82, .12, .025, 0, -.14)], segments=16, power=4), accent, H, tint=True, angle=25)
        b.add(kit.bm_box((.02, .2, .03)), "#6a809e", H, at=(0, .02, 1.97), subdivide=1)
    elif kind in ("iket", "iket_red"):
        color = accent if kind == "iket" else "#d23a2e"
        tint = kind == "iket"
        band(1.82, .127, .135, .045, color, tint)
        b.add(kit.bm_ico(.03, 1), color, H, at=(0, .14, 1.845), tint=tint)
        for sign in (1, -1):
            b.add(kit.bm_box((.05, .015, .17), taper=1.3), color, H, at=(.035 * sign, .155, 1.77), rotation=(.35, 0, .3 * sign), tint=tint, subdivide=1)
    elif kind == "band":
        band(1.85, .128, .136, .03, accent, True)
        b.add(kit.bm_blade(.08, .05, .015, sections=3, tip=.6), accent, H, at=(0, -.14, 1.87), rotation=(-PI / 2, 0, 0), tint=True)
        b.add(kit.bm_sphere(.128, 18, 10, scale=(1, 1.06, .8)), "#2a2235", H, at=(0, .02, 1.86))
    elif kind == "turban":
        b.add(kit.bm_torus(.11, .045, 20, 8), "#f0efe8", H, at=(0, 0, 1.87), rotation=(0, 0, 0))
        b.add(kit.bm_torus(.085, .04, 20, 8), "#e6e4dc", H, at=(0, .01, 1.93), rotation=(.15, 0, 0))
        b.add(kit.bm_sphere(.09, 14, 8, scale=(1, 1, .6)), "#f0efe8", H, at=(0, .01, 1.94))
        b.add(kit.bm_ico(.025, 1), accent, H, at=(0, -.15, 1.88), tint=True)
    elif kind == "peci":
        b.add(kit.bm_loft([(1.84, .128, .135), (1.95, .125, .132), (1.96, .118, .125)], segments=20, power=2.6), "#141414", H, angle=30)
    elif kind == "cap":
        b.add(kit.bm_loft([(1.8, .135, .145), (1.88, .14, .148), (1.95, .1, .11), (1.99, .03, .03)], segments=20, power=2.2), "#8a6a3a", H, angle=40)
        band(1.8, .152, .16, .025, accent, True)
    elif kind == "caping":
        b.add(kit.bm_loft([(1.86, .42, .42), (1.9, .4, .4), (2.06, .06, .06), (2.08, .01, .01)], segments=28, power=2), "#c9a86a", H, angle=25)
        band(1.86, .13, .135, .04, "#8a6a3a")
    elif kind == "blangkon":
        b.add(kit.bm_loft([(1.8, .13, .138), (1.88, .132, .14), (1.93, .1, .11), (1.955, .03, .03)], segments=20, power=2.4), "#5a3a22", H, angle=35)
        b.add(kit.bm_sphere(.06, 12, 8, scale=(1, .8, 1)), "#5a3a22", H, at=(0, .15, 1.81))
        band(1.84, .134, .142, .02, accent, True)
    elif kind == "wide_hat":
        b.add(kit.bm_loft([(1.88, .38, .36), (1.9, .37, .35)], segments=28, power=2), "#2a2420", H, angle=25)
        b.add(kit.bm_loft([(1.88, .14, .14), (2.02, .13, .13), (2.04, .1, .1)], segments=20, power=2.4), "#2a2420", H, angle=35)
        band(1.9, .142, .142, .025, accent, True)
    elif kind == "horns":
        horns(b, "#e8dcc0", 1.6)
        for sign in (1, -1):
            b.add(kit.bm_cone(.016, 0, .06, 6), "#f4f0e0", H, at=(.04 * sign, -.115, 1.7), rotation=(PI, 0, 0))
    elif kind == "horns_small":
        horns(b, "#cfc6b0", 1.0)
    elif kind == "barong":
        b.add(kit.bm_loft([(1.62, .12, .1, 0, -.06), (1.7, .2, .2, 0, -.06), (1.82, .22, .22, 0, -.05), (1.95, .18, .19, 0, -.03), (2.0, .08, .1, 0, -.02)], segments=20, power=2.4), "#c23a2e", H)
        b.add(kit.bm_box((.26, .1, .1), taper=.9), "#f4ecd0", H, at=(0, -.27, 1.74), subdivide=1, angle=30)
        for k in range(6):
            b.add(kit.bm_cone(.016, 0, .05, 6), "#ffffff", H, at=(-.1 + k * .04, -.3, 1.7), rotation=(PI, 0, 0))
        for sign in (1, -1):
            b.add(kit.bm_sphere(.05, 12, 8), "#ffffff", H, at=(.09 * sign, -.23, 1.88))
            b.add(kit.bm_sphere(.025, 10, 6), "#141414", H, at=(.09 * sign, -.27, 1.88))
        for k in range(7):
            a = (k - 3) * .4
            b.add(kit.bm_blade(.3, .1, .02, sections=4, tip=.5), "#f0d070", H, at=(.17 * math.sin(a), .02 + .05 * math.cos(a), 1.96), rotation=(-PI / 2 - .5, 0, a))
    elif kind == "rangda":
        b.add(kit.bm_loft([(1.62, .1, .1, 0, -.04), (1.72, .17, .17, 0, -.04), (1.84, .18, .18, 0, -.03), (1.95, .14, .15), (2.0, .05, .05)], segments=20, power=2.2), "#f4f0e8", H)
        b.add(kit.bm_box((.16, .05, .05)), "#c23a3a", H, at=(0, -.21, 1.68), subdivide=1)
        for sign in (1, -1):
            b.add(kit.bm_cone(.02, 0, .12, 6), "#ffffff", H, at=(.05 * sign, -.21, 1.62), rotation=(PI, 0, 0))
            b.add(kit.bm_sphere(.045, 12, 8), "#ffffff", H, at=(.08 * sign, -.18, 1.84))
            b.add(kit.bm_sphere(.022, 10, 6), "#c23a3a", H, at=(.08 * sign, -.215, 1.84))
        for k in range(14):
            a = k / 14 * kit.TAU
            b.add(kit.bm_cone(.05, 0, .55, 5), spec.get("hair", "#f2f2f2"), H, at=(.16 * math.cos(a), .16 * math.sin(a) + .03, 1.8), rotation=(math.sin(a) * 1.9, -math.cos(a) * 1.9, 0))
    elif kind in ("hood", "hood_pale"):
        color = "#2b1f33" if kind == "hood" else "#5fb8b2"
        b.add(kit.bm_loft([(1.62, .17, .15, 0, .03), (1.75, .16, .16, 0, .01), (1.9, .13, .14, 0, .02), (2.02, .07, .08, 0, .05), (2.1, .01, .01, 0, .09)], segments=20, power=2.2), color, H)
        if kind == "hood":
            b.add(kit.bm_ico(.03, 1), accent, H, at=(0, -.13, 1.8), tint=True)
    elif kind == "hair_face":
        b.add(kit.bm_sphere(.135, 18, 10, scale=(1, 1.08, 1)), "#121212", H, at=(0, .01, 1.82))
        b.add(kit.bm_loft([(1.86, .12, .03, 0, -.125), (1.65, .13, .03, 0, -.135), (1.45, .12, .03, 0, -.13)], segments=14, power=2.6), "#121212", H)
        b.add(kit.bm_loft([(1.85, .14, .08, 0, .1), (1.3, .17, .07, 0, .13), (.95, .14, .05, 0, .13)], segments=14, power=2.4), "#121212", "spine")
    elif kind == "shroud":
        b.add(kit.bm_loft([(1.62, .1, .1), (1.75, .135, .138), (1.88, .13, .133), (1.98, .07, .07), (2.06, .02, .02), (2.12, .04, .04)], segments=18, power=2.2), "#f2efe6", H)
        band(1.99, .045, .045, .025, "#cfc8b8")
        for sign in (1, -1):
            b.add(kit.bm_sphere(.02, 10, 6), "#2a2a2a", H, at=(.045 * sign, -.13, 1.8))
    elif kind == "mask_horn":
        b.add(kit.bm_loft([(1.67, .1, .03, 0, -.12), (1.78, .13, .035, 0, -.13), (1.88, .11, .03, 0, -.12)], segments=14, power=3), "#4a1f2a", H)
        for sign in (1, -1):
            b.add(kit.bm_sphere(.018, 8, 6), "#ffcc66", H, at=(.045 * sign, -.155, 1.8))
        horns(b, "#d8cfc0", .8)
        b.add(kit.bm_sphere(.127, 18, 10, scale=(1, 1.06, .82)), HAIR, H, at=(0, .02, 1.86))
    elif kind == "tuyul":
        b.add(kit.bm_sphere(.15, 16, 10, scale=(1, 1.05, 1.08)), spec_skin(spec), H, at=(0, .01, 1.84))
        for sign in (1, -1):
            b.add(kit.bm_sphere(.06, 10, 6, scale=(.35, 1, 1.3)), spec_skin(spec), H, at=(.16 * sign, .01, 1.8), rotation=(0, 0, .5 * sign))
    elif kind == "kera":
        b.add(kit.bm_sphere(.135, 16, 9, scale=(1, 1.05, .95)), "#5a3e26", H, at=(0, .02, 1.83))
        b.add(kit.bm_sphere(.085, 12, 8, scale=(1.1, .9, .8)), "#c8a07a", H, at=(0, -.105, 1.74))
        b.add(kit.bm_box((.17, .03, .03)), "#3a2a1a", H, at=(0, -.12, 1.83), subdivide=1)
        for sign in (1, -1):
            b.add(kit.bm_sphere(.045, 10, 6, scale=(.4, .9, 1)), "#c8a07a", H, at=(.13 * sign, 0, 1.8))
    elif kind == "macan":
        b.add(kit.bm_sphere(.14, 16, 10, scale=(1.05, 1.05, .95)), "#d8892f", H, at=(0, 0, 1.82))
        b.add(kit.bm_loft([(1.66, .07, .05, 0, -.13), (1.72, .1, .07, 0, -.15), (1.8, .09, .06, 0, -.15)], segments=12, power=2.2), "#f2e6d0", H)
        b.add(kit.bm_sphere(.025, 8, 5, scale=(1.3, .8, .8)), "#2a1a12", H, at=(0, -.21, 1.79))
        for sign in (1, -1):
            b.add(kit.bm_cone(.05, .01, .1, 6), "#d8892f", H, at=(.09 * sign, .01, 1.96), rotation=(0, .3 * sign, 0))
            b.add(kit.bm_sphere(.02, 8, 5), "#f2d040", H, at=(.05 * sign, -.125, 1.84))
            for k in range(2):
                b.add(kit.bm_tube((.06 * sign, -.19, 1.75 + k * .02), (.2 * sign, -.21, 1.73 + k * .04), .004, .002, 4), "#f4f0e0", H)
        for k in range(3):
            b.add(kit.bm_box((.05, .02, .012)), "#2a1a12", H, at=(0, -.11 + k * .05, 1.95 - k * .005), rotation=(.2, 0, 0))
    elif kind == "buaya":
        b.add(kit.bm_sphere(.13, 14, 9, scale=(1, 1.1, .85)), "#4d6a32", H, at=(0, .02, 1.8))
        b.add(kit.bm_loft([(1.72, .1, .06, 0, -.1), (1.74, .09, .055, 0, -.25), (1.76, .07, .045, 0, -.4)], segments=10, power=2.6), "#5d7a3a", H)
        b.add(kit.bm_loft([(1.68, .095, .04, 0, -.1), (1.68, .085, .035, 0, -.25), (1.69, .06, .03, 0, -.38)], segments=10, power=2.6), "#c8c08a", H)
        for k in range(6):
            for sign in (1, -1):
                b.add(kit.bm_cone(.009, 0, .03, 4), "#f4f0e0", H, at=(.07 * sign * (1 - k * .07), -.14 - k * .045, 1.705), rotation=(PI, 0, 0))
        for sign in (1, -1):
            b.add(kit.bm_sphere(.03, 8, 6), "#e8d040", H, at=(.07 * sign, -.08, 1.87))
            b.add(kit.bm_sphere(.014, 6, 4, scale=(.4, 1, 1)), "#141414", H, at=(.07 * sign, -.105, 1.875))
    elif kind == "genderuwo":
        b.add(kit.bm_sphere(.15, 14, 9, scale=(1.05, 1.05, 1)), "#2e2420", H, at=(0, 0, 1.8))
        for sign in (1, -1):
            b.add(kit.bm_sphere(.025, 8, 5), "#ff3a2a", H, at=(.05 * sign, -.13, 1.81))
            b.add(kit.bm_cone(.016, 0, .07, 5), "#f0e8d8", H, at=(.035 * sign, -.13, 1.69), rotation=(PI, 0, 0))
        for k in range(16):
            a = k / 16 * kit.TAU
            b.add(kit.bm_cone(.05, 0, .32, 5), "#3a2e2a", H, at=(.14 * math.cos(a), .14 * math.sin(a) + .03, 1.78 + (k % 3) * .04), rotation=(math.sin(a) * 1.6, -math.cos(a) * 1.6, 0))
    elif kind == "flame":
        b.add(kit.bm_sphere(.14, 14, 9), "#ffb040", H, at=(0, 0, 1.8))
        for sign in (1, -1):
            b.add(kit.bm_sphere(.03, 8, 5), "#fff4c0", H, at=(.05 * sign, -.13, 1.82))
        for k in range(12):
            a = k / 12 * kit.TAU
            b.add(kit.bm_cone(.06 - (k % 3) * .01, 0, .3 + (k % 4) * .09, 6), ["#ff6a1a", "#ffb040", "#ffd060"][k % 3], H, at=(.08 * math.cos(a), .08 * math.sin(a) + .02, 1.9 + (k % 2) * .03), rotation=(math.sin(a) * .22, -math.cos(a) * .22, 0))
    elif kind == "bakul":
        # A woven basket carried on the head, on a folded cloth.
        b.add(kit.bm_loft([(1.94, .1, .1), (1.98, .12, .12)], segments=14, power=2), "#c8a050", H)
        b.add(kit.bm_loft([(1.98, .14, .14), (2.1, .2, .2), (2.13, .21, .21)], segments=16, power=2), "#a07a44", H, angle=40)
        for k in range(5):
            b.add(kit.bm_sphere(.05, 8, 5), ["#e8c040", "#7aa83a", "#e05a3a"][k % 3], H, at=(.08 * math.cos(k * 1.3), .08 * math.sin(k * 1.3), 2.13))
    elif kind == "mask_stone":
        b.add(kit.bm_loft([(1.66, .12, .04, 0, -.115), (1.8, .14, .045, 0, -.125), (1.92, .12, .04, 0, -.115)], segments=14, power=4), "#8a908c", H, angle=30)
        b.add(kit.bm_loft([(1.84, .145, .15), (1.96, .14, .145), (2.0, .1, .1)], segments=18, power=3.5), "#6d736f", H, angle=30)
        for sign in (1, -1):
            b.add(kit.bm_box((.04, .02, .012)), accent, H, at=(.045 * sign, -.17, 1.8), tint=True)


def weapon(b, kind, arm_x, accent):
    x, z, bone = -arm_x, .82, "forearm.R"
    steel, wood, brass = "#c8d0d4", "#6a4a2a", "#c9a24a"
    vertical = (0, PI / 2, 0)
    hilt = lambda color=brass, length=.18: b.add(kit.bm_loft([(-length / 2, .022, .022), (length / 2, .02, .02)], segments=10, power=2), color, bone, at=(x, -.02, z), rotation=(PI / 2, 0, 0))
    if kind in ("sword", "keris", "golok", "parang", "rencong"):
        length = {"sword": .8, "keris": .52, "golok": .56, "parang": .62, "rencong": .38}[kind]
        width = {"sword": .065, "keris": .06, "golok": .08, "parang": .11, "rencong": .045}[kind]
        hilt(wood if kind in ("golok", "parang") else brass)
        b.add(kit.bm_box((.03, .03, .12)), brass, bone, at=(x, -.12, z), bevel=.006, bevel_segments=1)
        wave, waves, curve = (.018, 7, 0) if kind == "keris" else (0, 0, .07 if kind in ("parang", "golok") else (.05 if kind == "rencong" else 0))
        b.add(kit.bm_blade(length, width, .018, sections=18, wave=wave, waves=waves, curve=curve, tip=.22), steel, bone, at=(x, -.13, z), rotation=vertical, angle=25)
        if kind == "rencong":
            b.add(kit.bm_ico(.03, 1), brass, bone, at=(x, .08, z + .03))
    elif kind == "spear":
        b.add(kit.bm_tube((x, .5, z), (x, -1.0, z), .02, .018, 10), wood, bone)
        b.add(kit.bm_blade(.26, .07, .025, sections=8, tip=.5), steel, bone, at=(x, -1.0, z), rotation=vertical, angle=25)
        b.add(kit.bm_loft([(.96, .03, .03), (1.0, .028, .028)], segments=10, power=2), brass, bone, at=(x, 0, z), rotation=(PI / 2, 0, 0))
    elif kind == "staff":
        b.add(kit.bm_tube((x, -.02, z - .55), (x, -.02, z + .75), .022, .02, 10), wood, bone)
        b.add(kit.bm_torus(.07, .015, 16, 6), brass, bone, at=(x, -.02, z + .8), rotation=(PI / 2, 0, 0))
        b.add(kit.bm_ico(.075, 2), accent, bone, at=(x, -.02, z + .8), tint=True)
    elif kind == "cane":
        b.add(kit.bm_tube((x, -.03, z + .05), (x, -.03, .02), .018, .016, 10), wood, bone)
        b.add(kit.bm_torus(.04, .014, 12, 6), wood, bone, at=(x, -.06, z + .06), rotation=(0, PI / 2, 0))
    elif kind == "rifle":
        b.add(kit.bm_box((.075, .62, .11)), "#2a3440", bone, at=(x, -.24, z + .02), bevel=.012, angle=30)
        b.add(kit.bm_box((.06, .2, .08)), "#3a4656", bone, at=(x, .08, z - .02), bevel=.01, angle=30)
        b.add(kit.bm_tube((x, -.55, z + .04), (x, -.72, z + .04), .025, .022, 12), "#1c2430", bone)
        b.add(kit.bm_box((.08, .3, .02)), accent, bone, at=(x, -.24, z + .08), tint=True, bevel=.005)
    elif kind == "club":
        b.add(kit.bm_loft([(0, .035, .035), (.25, .06, .06), (.6, .13, .12), (.72, .11, .1)], segments=12, power=2), wood, bone, at=(x, -.02, z), rotation=(PI / 2, 0, 0))
        for k in range(6):
            b.add(kit.bm_cone(.025, 0, .08, 6), "#c8c0b0", bone, at=(x + .1 * math.cos(k), -.5 - (k % 3) * .06, z + .1 * math.sin(k)), rotation=(0, math.cos(k) * 1.6, 0))
    elif kind == "hoe":
        b.add(kit.bm_tube((x, -.02, z + .45), (x, -.02, z - .65), .018, .018, 10), wood, bone)
        b.add(kit.bm_box((.14, .24, .02)), steel, bone, at=(x, -.13, z - .66), bevel=.006)
    elif kind == "lightning":
        b.add(kit.bm_blade(.75, .09, .02, sections=7, wave=.06, waves=6, tip=.25), "#d6f2ff", bone, at=(x, -.12, z), rotation=vertical, smooth=False)
        b.add(kit.bm_blade(.6, .05, .025, sections=7, wave=.05, waves=5, tip=.3), "#7fd0ff", bone, at=(x + .01, -.14, z + .02), rotation=vertical, smooth=False)
        hilt("#e8eef2", .14)


def off_hand(b, kind, arm_x, accent):
    x, bone = arm_x + .11, "forearm.L"
    sideways = (0, PI / 2, 0)
    if kind == "shield":
        b.add(kit.bm_loft([(-.025, .3, .3), (0, .31, .31), (.03, .28, .28), (.045, .12, .12)], segments=20, power=2), "#8a6234", bone, at=(x, -.02, 1.0), rotation=sideways, angle=40)
        b.add(kit.bm_torus(.3, .018, 24, 4), "#c9a24a", bone, at=(x + .005, -.02, 1.0), rotation=sideways)
        b.add(kit.bm_sphere(.075, 10, 6, scale=(1, 1, .7)), accent, bone, at=(x + .045, -.02, 1.0), rotation=sideways, tint=True)
        for k in range(8):
            a = k / 8 * kit.TAU
            b.add(kit.bm_box((.012, .02, .15)), "#c9a24a", bone, at=(x + .036, -.02 + .19 * math.cos(a), 1.0 + .19 * math.sin(a)), rotation=(a + PI / 2, 0, 0))
    elif kind == "salawaku":
        b.add(kit.bm_box((.04, .17, .85), taper=.8), "#5a3a22", bone, at=(x, -.02, 1.0), bevel=.012, angle=35)
        for k in range(5):
            b.add(kit.bm_sphere(.022, 10, 6, scale=(.4, 1, 1)), "#f0e8d0", bone, at=(x + .022, -.02, .72 + k * .14))


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


def build_characters():
    scene = kit.fresh_scene("Nusantara_Characters")
    tint, fixed = kit.materials()
    rig = make_rig(scene, "CharacterRig", BONES)
    meshes = []
    for name, spec in CHARACTERS.items():
        b = kit.Builder(tint, fixed, scene.collection)
        body = build_character(b, name, spec)
        meshes.append(body)
    # Ambient occlusion before parenting: each variant is baked alone, standing on a floor.
    kit.bake_ao(meshes, scene, strength=.7, samples=128, distance=.45)
    for body in meshes:
        body.parent = rig
        modifier = body.modifiers.new("Armature", "ARMATURE")
        modifier.object = rig
    animate(rig, HUMAN_CLIPS, "hips")
    path = OUTPUT / "characters.glb"
    kit.export_glb(scene, path, animations=True, skins=True)
    return scene, rig, meshes, path


# ------------------------------------------------------------------------------------------------ naga
NAGA_BONES = {"base": ((0, 0, 0), (0, 0, 0.4), None)}
_previous = "base"
for _i in range(7):
    NAGA_BONES[f"seg{_i}"] = ((0, 0, 0.4 + _i * 0.45), (0, 0, 0.85 + _i * 0.45), _previous)
    _previous = f"seg{_i}"


def build_naga():
    scene = kit.fresh_scene("Nusantara_Naga")
    tint, fixed = kit.materials()
    rig = make_rig(scene, "NagaRig", NAGA_BONES)
    b = kit.Builder(tint, fixed, scene.collection)
    scale_a, scale_b, belly, gold = "#2f6a40", "#3f8450", "#e8d58a", "#d4b478"
    # The coiled tail: a tapering spiral of overlapping rounded segments.
    for k in range(16):
        a = k * .5
        radius = 1.05 - k * .045
        b.add(kit.bm_sphere(.3 - k * .012, 14, 8, scale=(1, 1, .7)), scale_a if k % 2 else scale_b, "base", at=(radius * math.cos(a), radius * math.sin(a), .2))
    # The rising body, one lofted segment per bone so it bends cleanly.
    for i in range(7):
        z0 = .4 + i * .45
        r0, r1 = .32 - i * .024, .32 - (i + 1) * .024
        b.add(kit.bm_loft([(z0 - .04, r0, r0 * .92), (z0 + .23, (r0 + r1) / 2 * 1.04, (r0 + r1) / 2 * .95), (z0 + .5, r1, r1 * .92)], segments=16, power=2.1), scale_a if i % 2 else scale_b, f"seg{i}")
        b.add(kit.bm_loft([(z0, r0 * .8, .05, 0, -r0 * .86), (z0 + .45, r1 * .8, .05, 0, -r1 * .86)], segments=12, power=3), belly, f"seg{i}", angle=35)
        for k in (0, 1):
            b.add(kit.bm_blade(.2, .1, .03, sections=4, tip=.7), gold, f"seg{i}", at=(0, r0 * .9, z0 + .1 + k * .22), rotation=(-2.4, 0, PI / 2), angle=30)
    # Head with jaw, teeth, eyes, whiskers, horns and Naga Basuki's crown.
    hz = .4 + 7 * .45 + .1
    b.add(kit.bm_loft([(hz - .05, .2, .2), (hz + .1, .24, .26, 0, -.08), (hz + .2, .2, .28, 0, -.18), (hz + .27, .12, .2, 0, -.3), (hz + .3, .05, .08, 0, -.38)], segments=18, power=2.4), scale_b, "seg6")
    b.add(kit.bm_loft([(hz - .02, .16, .2, 0, -.12), (hz + .05, .14, .2, 0, -.26), (hz + .08, .07, .1, 0, -.4)], segments=14, power=2.4), scale_a, "seg6", at=(0, 0, -.03))
    for k in range(5):
        for sign in (1, -1):
            b.add(kit.bm_cone(.014, 0, .05, 6), "#f4f0e0", "seg6", at=(.1 * sign - .02 * k * sign, -.2 - k * .045, hz + .07), rotation=(PI, 0, 0))
    for sign in (1, -1):
        b.add(kit.bm_sphere(.04, 12, 8), "#ffe27a", "seg6", at=(.13 * sign, -.22, hz + .2))
        b.add(kit.bm_sphere(.02, 8, 6, scale=(.5, 1, 1.2)), "#141414", "seg6", at=(.15 * sign, -.25, hz + .2))
        b.add(kit.bm_tube((.1 * sign, .05, hz + .25), (.18 * sign, .3, hz + .55), .04, .01, 8), "#e8dcc0", "seg6")
        b.add(kit.bm_tube((.12 * sign, -.35, hz + .1), (.4 * sign, -.5, hz - .05), .012, .004, 6), gold, "seg6")
    b.add(kit.bm_loft([(hz + .27, .13, .13, 0, .02), (hz + .33, .135, .135, 0, .02)], segments=20, power=2), gold, "seg6", tint=True)
    for k in range(7):
        a = k / 7 * kit.TAU
        b.add(kit.bm_blade(.14, .06, .02, sections=3, tip=.7), gold, "seg6", at=(.13 * math.cos(a), .02 + .13 * math.sin(a), hz + .33), rotation=(-PI / 2, 0, a + PI / 2), tint=True)
    body = b.join("naga")
    kit.bake_ao([body], scene, strength=.7, samples=128, distance=.6)
    body.parent = rig
    modifier = body.modifiers.new("Armature", "ARMATURE")
    modifier.object = rig

    def sway(phase, amount=10):
        return {f"seg{i}": [(X, amount * .6 * math.sin(phase + i * .7)), (Y, amount * math.sin(phase * .5 + i * .5))] for i in range(7)}

    clips = [
        ("Idle", [(1 + k * 6, sway(k * PI / 4), 0.0) for k in range(9)]),
        ("Walk", [(1 + k * 3, sway(k * PI / 4, 16), 0.0) for k in range(9)]),
        ("Attack", [(1, sway(0), 0.0), (8, {f"seg{i}": [(X, 12)] for i in range(7)}, 0.0), (13, {f"seg{i}": [(X, -16 if i > 2 else -6)] for i in range(7)}, 0.0), (22, sway(0), 0.0)]),
        ("Cast", [(1, sway(0), 0.0), (12, {f"seg{i}": [(X, 8)] for i in range(7)}, 0.0), (24, sway(0), 0.0)]),
        ("Hit", [(1, sway(0), 0.0), (4, {f"seg{i}": [(X, 10)] for i in range(7)}, 0.0), (11, sway(0), 0.0)]),
        ("Death", [(1, sway(0), 0.0), (12, {"seg0": [(X, -40)], **{f"seg{i}": [(X, -12)] for i in range(1, 7)}}, 0.0), (30, {"seg0": [(X, -75)], **{f"seg{i}": [(X, -8)] for i in range(1, 7)}}, 0.0)]),
    ]
    animate(rig, clips, "base")
    path = OUTPUT / "naga.glb"
    kit.export_glb(scene, path, animations=True, skins=True)
    return scene, body, path


previous = bpy.context.window.scene if bpy.context.window else None
character_scene, rig, meshes, characters_path = build_characters()
naga_scene, naga_body, naga_path = build_naga()
bpy.data.libraries.write(str(ROOT / "assets/blender/characters.blend"), {character_scene, naga_scene}, fake_user=True)
try:
    if previous and bpy.context.window:
        bpy.context.window.scene = previous
except ReferenceError:
    pass  # The previous scene was the one this script rebuilt.
result = {
    "variants": len(meshes),
    "triangles": {m.name: kit.triangles(m) for m in meshes},
    "naga_triangles": kit.triangles(naga_body),
    "characters_bytes": characters_path.stat().st_size,
    "naga_bytes": naga_path.stat().st_size,
}
print(result)
