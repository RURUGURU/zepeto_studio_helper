"""Build one live-round-trip fixture for Assets/ZepetoHelperTests/Editor/ZepetoLiveReloadRun.cs.

Run ONCE PER FIXTURE, with a fresh Blender each time so no keyframes leak between them:

  set ZEPETO_FIXTURE=a
  "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" ^
      --background --factory-startup --python BlenderMotion/make_live_fixtures.py
  set ZEPETO_FIXTURE=b
  ... same command again

Implements the FIXTURE SPECIFICATION block at the top of ZepetoLiveReloadRun.cs:

  a : 48 frames, ONLY upperArm_R rotates, keys at 1 / 24 / 48, then loop
  b : 96 frames, ONLY upperLeg_L rotates, keys at 1 / 48 / 96, then loop

Both land at the PROJECT ROOT, not Assets/CustomMotions - that folder is the live
watcher's polling root, and a fixture sitting there would make the watcher fire on the
fixture instead of on the copy the run places at Assets/CustomMotions/LiveRoundTrip.fbx.

The two fixtures must move DIFFERENT bones. That split is the only reason the run can
tell "the hot reload never happened" apart from "nothing is animating at all" - with the
same bone both produce an identical still-bone reading.
"""
import glob
import importlib.util
import os
import sys

import bpy

_HERE = os.path.dirname(os.path.abspath(__file__))
ADDON_SRC = os.path.join(_HERE, "zepeto_motion_helper.py")

FIXTURES = {
    "a": {
        "name": "zepeto-live-a",
        "frames": 48,
        "bone": "upperArm_R",
        "keys": (1, 24, 48),
        "angle": 0.95,
    },
    "b": {
        "name": "zepeto-live-b",
        "frames": 96,
        "bone": "upperLeg_L",
        "keys": (1, 48, 96),
        "angle": 0.70,
    },
}


def find_unity_project():
    for base in (os.path.dirname(_HERE), _HERE):
        for cand in sorted(glob.glob(os.path.join(base, "ZEPETO Studio Unity Project File*"))):
            if os.path.isdir(os.path.join(cand, "Assets")):
                return cand
    return ""


def load_addon():
    spec = importlib.util.spec_from_file_location("zepeto_motion_helper", ADDON_SRC)
    mod = importlib.util.module_from_spec(spec)
    sys.modules["zepeto_motion_helper"] = mod
    spec.loader.exec_module(mod)
    mod.register()
    return mod


def main():
    key = (os.environ.get("ZEPETO_FIXTURE") or "").strip().lower()
    if key not in FIXTURES:
        print("FAIL: set ZEPETO_FIXTURE to 'a' or 'b' (got %r)" % key)
        return 2

    spec = FIXTURES[key]
    project = find_unity_project()
    if not project:
        print("FAIL: Unity project folder not found next to %s" % _HERE)
        return 2

    mod = load_addon()
    scene = bpy.context.scene

    # The add-on refuses to export at any other frame rate.
    scene.render.fps = 24
    scene.render.fps_base = 1.0

    mod.refresh_paths(scene, force=True)
    if not os.path.isfile(scene.zepeto_rig_fbx or ""):
        print("FAIL: rig fbx missing (%r). Run the rig export first." % scene.zepeto_rig_fbx)
        return 2

    if "FINISHED" not in bpy.ops.zepeto.import_rig():
        print("FAIL: import_rig did not finish")
        return 2

    rig = scene.objects.get("hips") or next(
        (o for o in bpy.data.objects if o.type == "ARMATURE"), None)
    if rig is None:
        print("FAIL: no armature after import")
        return 2

    # Fresh Blender per fixture, but be explicit: no action, so no curve can survive
    # from anything the import may have brought in.
    rig.animation_data_clear()

    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")

    bone = rig.pose.bones.get(spec["bone"])
    if bone is None:
        print("FAIL: bone %s not in rig" % spec["bone"])
        return 2
    if spec["bone"] not in mod.MAPPED_BONES:
        print("FAIL: bone %s is not Humanoid-mapped, its curves would be dropped" % spec["bone"])
        return 2

    scene.frame_start = 1
    scene.frame_end = spec["frames"]
    bone.rotation_mode = "XYZ"

    first, middle, last = spec["keys"]
    # Rest at the ends, the rotation at the middle key: that is what makes the bone travel.
    for frame, angle in ((first, 0.0), (middle, spec["angle"]), (last, 0.0)):
        scene.frame_set(frame)
        bone.rotation_euler = (0.0, 0.0, angle)
        if "FINISHED" not in bpy.ops.zepeto.key_pose():
            print("FAIL: key_pose refused at frame %d" % frame)
            return 2

    # ZEPETO loops motions, so frame_end has to match frame 1.
    if "FINISHED" not in bpy.ops.zepeto.make_loop():
        print("FAIL: make_loop refused")
        return 2

    scene.zepeto_export_dir = project
    scene.zepeto_motion_name = spec["name"]
    result = bpy.ops.zepeto.export()
    out = os.path.join(project, spec["name"] + ".fbx")

    if "FINISHED" not in result or not os.path.isfile(out):
        print("FAIL: export did not produce %s (%s)" % (out, result))
        return 2

    with open(out, "rb") as f:
        head = f.read(18)
    leftovers = [p for p in os.listdir(project) if p.endswith(".part")]

    moved = [fc.data_path for fc in mod.iter_fcurves(rig.animation_data.action)
             if "rotation" in fc.data_path and _varies(fc)]

    print("PASS fixture:%s" % key)
    print("  file     : %s (%d bytes)" % (out, os.path.getsize(out)))
    print("  binary   : %s" % (head == b"Kaydara FBX Binary"))
    print("  frames   : %d..%d @ %dfps" % (scene.frame_start, scene.frame_end, scene.render.fps))
    print("  bone     : %s, keys %s, angle %.2f rad" % (spec["bone"], spec["keys"], spec["angle"]))
    print("  varying  : %d curve(s)" % len(moved))
    for path in moved:
        print("             %s" % path)
    print("  part left: %s" % leftovers)
    return 0


def _varies(fcurve):
    values = [kp.co[1] for kp in fcurve.keyframe_points]
    return bool(values) and (max(values) - min(values)) > 1e-4


if __name__ == "__main__":
    try:
        sys.exit(main())
    except SystemExit:
        raise
    except Exception:
        import traceback
        traceback.print_exc()
        sys.exit(2)
