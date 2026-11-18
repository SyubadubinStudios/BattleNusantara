"""Run through Blender MCP. Builds the animals on one four-legged rig and exports
src/BattleNusantara.UI/wwwroot/models/animals.glb: kancil (mouse-deer), kerbau (water buffalo), babi-hutan (wild boar),
kera-pencuri (macaque) and ayam (chicken), with the clips Idle, Walk, Attack, Cast, Hit and Death.

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios. Original designs.

Animals face -Y and stand on Z = 0. Each part follows one bone (rigid skinning). The chicken uses only the hind legs;
its front legs are folded into the body.
"""
import importlib
import math
import os
import sys
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector

ROOT = Path(os.environ["NUSANTARA_ROOT"]) if os.environ.get("NUSANTARA_ROOT") else Path(globals().get("__file__", "")).resolve().parents[2]
OUTPUT = ROOT / "src/BattleNusantara.UI/wwwroot/models"
sys.path.insert(0, str(ROOT / "assets/blender"))
import nusantara_kit as kit  # noqa: E402

importlib.reload(kit)
PI = math.pi
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))

previous = bpy.context.window.scene if bpy.context.window else None
scene = kit.fresh_scene("Nusantara_Animals")
TINT, FIXED = kit.materials()

# One rig for all: a unit-sized body that each animal fills with its own proportions.
BONES = {
    "root": ((0, 0, 0), (0, 0, .2), None),
    "spine": ((0, .4, .7), (0, -.4, .7), "root"),
    "neck": ((0, -.4, .75), (0, -.6, .95), "spine"),
    "head": ((0, -.6, .95), (0, -.85, .95), "neck"),
    "tail": ((0, .4, .75), (0, .7, .7), "spine"),
}
for side, sign in (("L", 1), ("R", -1)):
    BONES[f"leg_f.{side}"] = ((.18 * sign, -.3, .62), (.18 * sign, -.3, .02), "spine")
    BONES[f"leg_b.{side}"] = ((.18 * sign, .3, .62), (.18 * sign, .3, .02), "spine")

ANIMALS = {
    "kancil": dict(length=.62, height=.55, girth=.15, color="#a8794a", belly="#e8d8b8", head="deer", tail=.08, leg=.035),
    "kerbau": dict(length=1.0, height=.78, girth=.36, color="#4a4642", belly="#5e5a54", head="buffalo", tail=.4, leg=.09),
    "babi-hutan": dict(length=.8, height=.5, girth=.27, color="#4a3a2e", belly="#6a5a4a", head="boar", tail=.15, leg=.06),
    "kera-pencuri": dict(length=.5, height=.48, girth=.16, color="#7a6248", belly="#b8a080", head="monkey", tail=.6, leg=.045),
    "ayam": dict(length=.32, height=.3, girth=.13, color="#b04a2a", belly="#e8c070", head="chicken", tail=.2, leg=.02, biped=True),
}


def make_rig(name):
    data = bpy.data.armatures.new(name)
    rig = bpy.data.objects.new(name, data)
    scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for bone_name, (head, tail, parent) in BONES.items():
        bone = data.edit_bones.new(bone_name)
        bone.head, bone.tail = head, tail
        if parent:
            bone.parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    for pose_bone in rig.pose.bones:
        pose_bone.rotation_mode = "QUATERNION"
    return rig


def build_animal(name, spec):
    b = kit.Builder(TINT, FIXED, scene.collection)
    L, H, G = spec["length"], spec["height"], spec["girth"]
    color, belly = spec["color"], spec["belly"]
    # Body: a horizontal loft from rump (+Y) to chest (-Y), built along Z and turned onto the Y axis.
    body = [(-L / 2, G * .75, G * .8), (-L * .3, G, G), (0, G * 1.02, G * 1.0), (L * .3, G * .95, G * .98), (L / 2, G * .7, G * .75)]
    b.add(kit.bm_loft(body, segments=12, power=2.2), color, "spine", at=(0, 0, H + G * .2), rotation=(PI / 2, 0, 0), angle=50)
    b.add(kit.bm_loft([(-L * .35, G * .6, G * .3, 0, -G * .45), (L * .3, G * .6, G * .3, 0, -G * .45)], segments=10, power=2), belly, "spine", at=(0, 0, H + G * .2), rotation=(PI / 2, 0, 0))
    for side, sign in (("L", 1), ("R", -1)):
        for end, y in (("f", -L * .32), ("b", L * .32)):
            if spec.get("biped") and end == "f":
                continue
            x = G * .55 * sign
            leg = spec["leg"]
            b.add(kit.bm_loft([(H + G * .2, leg * 1.5, leg * 1.6, x, y), (H * .55, leg * 1.1, leg * 1.2, x, y), (H * .12, leg * .85, leg * .9, x, y), (0, leg, leg * 1.1, x, y - .01)], segments=8, power=2),
                  color if end == "b" or name != "kera-pencuri" else color, f"leg_{end}.{side}", angle=50)
            b.add(kit.bm_box((leg * 2.2, leg * 2.6, leg * 1.2)), "#2a2420" if name != "ayam" else "#e0a040", f"leg_{end}.{side}", at=(x, y - leg * .5, leg * .6))
    # Tail.
    tail_length = spec["tail"]
    if name == "ayam":
        for k in range(5):
            b.add(kit.bm_blade(.26, .08, .015, sections=3, tip=.5), ["#1a3a2a", "#2a5a3a", "#b04a2a"][k % 3], "tail", at=(0, L * .45, H + G * .3), rotation=(-2.2 + k * .15, 0, (k - 2) * .25))
    else:
        b.add(kit.bm_tube((0, L / 2, H + G * .3), (0, L / 2 + tail_length * .6, H + G * .1 - tail_length * .4), G * .12 + .01, G * .05 + .005, 6), color, "tail")
        if name == "kera-pencuri":
            b.add(kit.bm_tube((0, L / 2 + tail_length * .6, H + G * .1 - tail_length * .4), (0, L / 2 + tail_length * .9, H + G * .3), .02, .012, 6), color, "tail")
    head_kind = spec["head"]
    neck_base = (0, -L / 2 + G * .2, H + G * .5)
    head_at = (0, -L / 2 - G * .5, H + G * 1.15)
    b.add(kit.bm_tube(neck_base, head_at, G * .45, G * .35, 10), color, "neck")
    if head_kind == "deer":
        b.add(kit.bm_loft([(0, .07, .08), (.1, .08, .09), (.2, .05, .06), (.24, .02, .02)], segments=10, power=2), color, "head", at=head_at, rotation=(PI / 2 + .3, 0, 0))
        for sign in (1, -1):
            b.add(kit.bm_blade(.12, .06, .01, sections=3, tip=.6), color, "head", at=(.05 * sign, head_at[1] + .04, head_at[2] + .06), rotation=(-PI / 2 - .4, 0, .5 * sign))
            b.add(kit.bm_sphere(.014, 6, 4), "#141414", "head", at=(.05 * sign, head_at[1] - .09, head_at[2] + .03))
        b.add(kit.bm_sphere(.015, 6, 4), "#141414", "head", at=(0, head_at[1] - .24, head_at[2] - .06))
    elif head_kind == "buffalo":
        b.add(kit.bm_loft([(0, .16, .17), (.18, .17, .15), (.34, .12, .1), (.4, .1, .07)], segments=10, power=2.4), color, "head", at=head_at, rotation=(PI / 2 + .55, 0, 0))
        b.add(kit.bm_sphere(.07, 8, 5, scale=(1.2, .7, .8)), "#2a2622", "head", at=(0, head_at[1] - .36, head_at[2] - .22))
        for sign in (1, -1):
            points = [(.12 * sign, head_at[1] + .02, head_at[2] + .1), (.32 * sign, head_at[1] + .08, head_at[2] + .14), (.46 * sign, head_at[1] + .16, head_at[2] + .26), (.5 * sign, head_at[1] + .22, head_at[2] + .4)]
            for k in range(3):
                b.add(kit.bm_tube(points[k], points[k + 1], .045 * (1 - k * .28), .045 * (1 - (k + 1) * .28), 8), "#d8ccb0", "head")
            b.add(kit.bm_sphere(.02, 6, 4), "#141414", "head", at=(.1 * sign, head_at[1] - .12, head_at[2] + .02))
    elif head_kind == "boar":
        b.add(kit.bm_loft([(0, .13, .14), (.16, .12, .12), (.28, .08, .08), (.32, .07, .06)], segments=10, power=2.4), color, "head", at=head_at, rotation=(PI / 2 + .45, 0, 0))
        b.add(kit.bm_loft([(0, .06, .05), (.03, .065, .055)], segments=10, power=2), "#c89a8a", "head", at=(0, head_at[1] - .28, head_at[2] - .16), rotation=(PI / 2 + .45, 0, 0))
        for sign in (1, -1):
            b.add(kit.bm_cone(.016, 0, .1, 6), "#f0e8d8", "head", at=(.06 * sign, head_at[1] - .22, head_at[2] - .1), rotation=(-.6, 0, -.3 * sign))
            b.add(kit.bm_blade(.08, .05, .01, sections=2, tip=.6), color, "head", at=(.07 * sign, head_at[1] + .06, head_at[2] + .1), rotation=(-PI / 2 - .3, 0, .6 * sign))
            b.add(kit.bm_sphere(.014, 6, 4), "#141414", "head", at=(.065 * sign, head_at[1] - .1, head_at[2] + .04))
        for k in range(6):
            b.add(kit.bm_cone(.03, 0, .1, 4), "#2a1e16", "spine", at=(0, -L * .3 + k * .1, H + G * 1.15), rotation=(.3, 0, 0))
    elif head_kind == "monkey":
        b.add(kit.bm_sphere(.1, 12, 8), color, "head", at=head_at)
        b.add(kit.bm_sphere(.065, 10, 6, scale=(1.1, .8, .9)), "#d8a090", "head", at=(0, head_at[1] - .07, head_at[2] - .01))
        for sign in (1, -1):
            b.add(kit.bm_sphere(.03, 8, 5, scale=(.5, 1, 1)), "#d8a090", "head", at=(.1 * sign, head_at[1], head_at[2] + .01))
            b.add(kit.bm_sphere(.012, 6, 4), "#141414", "head", at=(.03 * sign, head_at[1] - .12, head_at[2] + .03))
    elif head_kind == "chicken":
        b.add(kit.bm_sphere(.07, 10, 7), color, "head", at=head_at)
        b.add(kit.bm_cone(.02, 0, .06, 6), "#f0b030", "head", at=(0, head_at[1] - .09, head_at[2] - .01), rotation=(PI / 2, 0, 0))
        for k in range(3):
            b.add(kit.bm_sphere(.022, 6, 4), "#e02a2a", "head", at=(0, head_at[1] - .02 + k * .03, head_at[2] + .07))
        b.add(kit.bm_sphere(.02, 6, 4, scale=(.6, .8, 1.3)), "#e02a2a", "head", at=(0, head_at[1] - .08, head_at[2] - .06))
    return b.join(name)


def animate(rig, clips):
    data = rig.data

    def turn(bone, axis, degrees):
        rest = data.bones[bone].matrix_local.to_3x3()
        return Quaternion(rest.inverted() @ axis, math.radians(degrees))

    rig.animation_data_create()
    actions = []
    for clip_name, frames in clips:
        stale = bpy.data.actions.get("A_" + clip_name)
        if stale:
            bpy.data.actions.remove(stale)
        action = bpy.data.actions.new("A_" + clip_name)
        rig.animation_data.action = action
        for frame, rotations, lift in frames:
            for pose_bone in rig.pose.bones:
                rotation = Quaternion()
                for axis, degrees in rotations.get(pose_bone.name, []):
                    rotation = turn(pose_bone.name, axis, degrees) @ rotation
                pose_bone.rotation_quaternion = rotation
                pose_bone.keyframe_insert("rotation_quaternion", frame=frame)
            root = rig.pose.bones["root"]
            root.location = (0, lift, 0)
            root.keyframe_insert("location", frame=frame)
        actions.append((clip_name, action))
    rig.animation_data.action = None
    for clip_name, action in actions:
        track = rig.animation_data.nla_tracks.new()
        track.name = clip_name
        strip = track.strips.new(clip_name, int(action.frame_range[0]), action)
        strip.name = clip_name
    for pose_bone in rig.pose.bones:
        pose_bone.rotation_quaternion = Quaternion()
        pose_bone.location = (0, 0, 0)


def gait(phase, amount=28):
    swing = math.sin(phase)
    return {
        "leg_f.L": [(X, amount * swing)], "leg_b.R": [(X, amount * swing)],
        "leg_f.R": [(X, -amount * swing)], "leg_b.L": [(X, -amount * swing)],
        "neck": [(X, 4 * math.sin(phase * 2))], "tail": [(Z, 12 * swing)], "spine": [(Y, 3 * swing)],
    }


GRAZE = {"neck": [(X, 35)], "head": [(X, 15)]}
CLIPS = [
    ("Idle", [(1, {}, 0.0), (20, {"neck": [(X, -6)], "tail": [(Z, 15)]}, 0.01), (40, GRAZE, 0.0), (64, GRAZE | {"tail": [(Z, -15)]}, 0.0), (90, {}, 0.0)]),
    ("Walk", [(1 + k * 3, gait(k * PI / 4), .02 * abs(math.sin(k * PI / 4))) for k in range(9)]),
    ("Attack", [(1, {}, 0.0), (6, {"neck": [(X, -20)], "spine": [(X, 6)], "leg_f.L": [(X, -20)], "leg_f.R": [(X, -20)]}, .03), (11, {"neck": [(X, 30)], "head": [(X, 20)], "spine": [(X, -8)], "leg_b.L": [(X, -25)], "leg_b.R": [(X, -25)]}, -.02), (18, {}, 0.0)]),
    ("Cast", [(1, {}, 0.0), (10, {"spine": [(X, -14)], "neck": [(X, -20)], "leg_f.L": [(X, -40)], "leg_f.R": [(X, -40)]}, .1), (22, {}, 0.0)]),
    ("Hit", [(1, {}, 0.0), (4, {"spine": [(Y, 8)], "neck": [(X, -18)], "tail": [(Z, 30)]}, -.02), (10, {}, 0.0)]),
    ("Death", [(1, {}, 0.0), (8, {"root": [(Y, 30)], "neck": [(X, 20)]}, -.05), (18, {"root": [(Y, 88)], "neck": [(X, 30)], "leg_f.L": [(X, 30)], "leg_b.L": [(X, -30)]}, 0.0), (30, {"root": [(Y, 90)], "neck": [(X, 35)], "leg_f.L": [(X, 35)], "leg_b.L": [(X, -35)]}, 0.0)]),
]

rig = make_rig("AnimalRig")
meshes = []
for name, spec in ANIMALS.items():
    meshes.append(build_animal(name, spec))
kit.bake_ao(meshes, scene, strength=.7, samples=96, distance=.35)
for body in meshes:
    body.parent = rig
    modifier = body.modifiers.new("Armature", "ARMATURE")
    modifier.object = rig
animate(rig, CLIPS)
kit.export_glb(scene, OUTPUT / "animals.glb", animations=True, skins=True)
bpy.data.libraries.write(str(ROOT / "assets/blender/animals.blend"), {scene}, fake_user=True)
try:
    if previous and bpy.context.window:
        bpy.context.window.scene = previous
except ReferenceError:
    pass
result = {"animals": [m.name for m in meshes], "triangles": {m.name: kit.triangles(m) for m in meshes}, "bytes": (OUTPUT / "animals.glb").stat().st_size}
print(result)
