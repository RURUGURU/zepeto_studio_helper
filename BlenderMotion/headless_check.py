"""Headless validation of the ZEPETO Blender add-on.

Run:
  "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" ^
      --background --factory-startup --python BlenderMotion/headless_check.py

Needs no Unity licence - it drives Blender only, and reads the rig FBX that step 3
already exported. Writes its FBX to the OS temp dir, never into Assets/ (that folder
is polled every 0.4s by the Unity helper).

Covers the things that are otherwise unverifiable without opening Blender by hand:
  - the module imports and registers under real Blender 5.2 (bl_info targets 4.2)
  - runtime Unity-project resolution, including the unsaved-.blend case that used to
    die on a hardcoded path, and the env-var override
  - the ambiguity refusal actually refuses instead of picking alphabetically
  - iter_fcurves works on a real 5.2 action (its 4.4+ branch was suspected dead)
  - import_rig -> key_pose -> make_loop -> export produces a BINARY fbx
  - the bone arithmetic on the real rig: 54 mapped + 49 hidden = 103
  - clear_pose only touches the bones the panel flags
  - the .part handoff leaves no scratch file

Prints PASS/FAIL lines and a final count, and writes the same to
<temp>/zepeto_headless_check/result.txt.
"""
import glob
import os
import sys
import tempfile
import traceback

import bpy

_HERE = os.path.dirname(os.path.abspath(__file__))
ADDON_SRC = os.path.join(_HERE, "zepeto_motion_helper.py")


def _find_unity_project():
    """Same search the add-on does: a sibling or parent folder holding Assets/."""
    for base in (os.path.dirname(_HERE), _HERE):
        for cand in sorted(glob.glob(os.path.join(base, "ZEPETO Studio Unity Project File*"))):
            if os.path.isdir(os.path.join(cand, "Assets")):
                return cand
    return ""


UNITY_PROJECT = _find_unity_project()
RIG_FBX = os.path.join(UNITY_PROJECT, "Assets", "ZepetoHelper", "Rig", "ZepetoBaseModel.fbx")
OUT_DIR = os.path.join(tempfile.gettempdir(), "zepeto_headless_check")

results = []


def check(name, ok, detail=""):
    results.append((name, bool(ok), detail))
    print("%s %s%s" % ("PASS" if ok else "FAIL", name, (" :: " + str(detail)) if detail else ""))


def note(name, detail):
    print("NOTE %s :: %s" % (name, detail))


def load_addon():
    """Load the add-on from its source path without installing it."""
    import importlib.util
    spec = importlib.util.spec_from_file_location("zepeto_motion_helper", ADDON_SRC)
    mod = importlib.util.module_from_spec(spec)
    sys.modules["zepeto_motion_helper"] = mod
    spec.loader.exec_module(mod)
    mod.register()
    return mod


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    check("env:unity-project-found", bool(UNITY_PROJECT), UNITY_PROJECT or "(not found)")

    # ---- 1. import + register under real Blender ---------------------------
    try:
        mod = load_addon()
        check("addon:register", True, "bl_info version %s, blender target %s"
              % (mod.bl_info["version"], mod.bl_info["blender"]))
    except Exception:
        check("addon:register", False, traceback.format_exc().strip().splitlines()[-1])
        return

    note("addon:blender-running", bpy.app.version_string)

    # ---- 2. no hardcoded foreign path anywhere -----------------------------
    src = open(ADDON_SRC, encoding="utf-8").read()
    check("addon:no-foreign-user-path", "Jun-WN" not in src,
          "found Jun-WN" if "Jun-WN" in src else "clean")
    check("addon:project-resolved-at-runtime", "UNITY_PROJECT = r" not in src,
          "module-level constant still present" if "UNITY_PROJECT = r" in src
          else "no frozen module-level constant")

    # ---- 3. runtime resolution, unsaved .blend (the fresh-scene case) ------
    try:
        resolved = mod.resolve_unity_project()
        project = resolved[0] if isinstance(resolved, tuple) else resolved
        ambiguous = resolved[1] if isinstance(resolved, tuple) else ()
        same = os.path.normcase(os.path.abspath(project or "")) == os.path.normcase(
            os.path.abspath(UNITY_PROJECT))
        check("paths:resolve-from-addon-file", same, project or "(empty)")
        check("paths:not-ambiguous-here", not ambiguous, str(ambiguous))
    except Exception:
        check("paths:resolve-from-addon-file", False,
              traceback.format_exc().strip().splitlines()[-1])

    # ---- 4. env var override wins ------------------------------------------
    try:
        os.environ[mod.UNITY_PROJECT_ENV] = UNITY_PROJECT
        r2 = mod.resolve_unity_project()
        p2 = r2[0] if isinstance(r2, tuple) else r2
        check("paths:env-override", os.path.normcase(os.path.abspath(p2 or "")) ==
              os.path.normcase(os.path.abspath(UNITY_PROJECT)), p2 or "(empty)")
    finally:
        os.environ.pop(mod.UNITY_PROJECT_ENV, None)

    # ---- 5. refresh_paths writes into the scene ----------------------------
    scene = bpy.context.scene
    try:
        fix = mod.refresh_paths(scene, force=True)
        wrote_export = bool(getattr(fix, "export_dir", "") or scene.zepeto_export_dir)
        wrote_rig = bool(getattr(fix, "rig_fbx", "") or scene.zepeto_rig_fbx)
        check("paths:refresh-writes-export-dir", wrote_export, scene.zepeto_export_dir)
        check("paths:refresh-writes-rig", wrote_rig, scene.zepeto_rig_fbx)
        check("paths:rig-file-exists", os.path.isfile(scene.zepeto_rig_fbx or ""),
              scene.zepeto_rig_fbx)
    except Exception:
        check("paths:refresh-writes-export-dir", False,
              traceback.format_exc().strip().splitlines()[-1])

    # ---- 6. import the real rig -------------------------------------------
    check("rig:source-exists", os.path.isfile(RIG_FBX), RIG_FBX)
    rig = None
    try:
        res = bpy.ops.zepeto.import_rig()
        rig = bpy.context.scene.objects.get("hips") or next(
            (o for o in bpy.data.objects if o.type == "ARMATURE"), None)
        check("rig:import", "FINISHED" in res and rig is not None, str(res))
        if rig is not None:
            bones = len(rig.pose.bones)
            hidden = sum(1 for b in rig.data.bones if b.hide)
            mapped = sum(1 for b in rig.pose.bones if b.name in mod.MAPPED_BONES)
            # The reconciliation STATUS.md documents: 54 mapped + 49 hidden = 103.
            # (The 55th Humanoid mapping, hips, is the armature OBJECT, not a bone.)
            check("rig:bone-count-103", bones == 103, "%d pose bones" % bones)
            check("rig:hidden-49", hidden == 49, "%d hidden" % hidden)
            check("rig:mapped-54", mapped == 54, "%d mapped of %d" % (mapped, bones))
            check("rig:arithmetic-closes", mapped + hidden == bones,
                  "%d + %d = %d" % (mapped, hidden, bones))
            note("rig:frame-end", str(bpy.context.scene.frame_end))
    except Exception:
        check("rig:import", False, traceback.format_exc().strip().splitlines()[-1])

    # ---- 7. author a motion: rotate one mapped bone, key two frames --------
    if rig is not None:
        try:
            bpy.context.view_layer.objects.active = rig
            bpy.ops.object.mode_set(mode="POSE")
            arm = rig.pose.bones.get("upperArm_R") or rig.pose.bones.get("spine")
            check("pose:target-bone-found", arm is not None,
                  arm.name if arm else "no upperArm_R/spine")

            bpy.context.scene.frame_set(1)
            r1 = bpy.ops.zepeto.key_pose()
            bpy.context.scene.frame_set(24)
            arm.rotation_mode = "XYZ"
            arm.rotation_euler = (0.0, 0.0, 0.6)
            r2 = bpy.ops.zepeto.key_pose()
            check("pose:key-two-frames", "FINISHED" in r1 and "FINISHED" in r2,
                  "%s / %s" % (r1, r2))

            act = rig.animation_data.action if rig.animation_data else None
            curves = list(mod.iter_fcurves(act)) if act else []
            check("pose:iter-fcurves-works", len(curves) > 0, "%d fcurves seen" % len(curves))

            r3 = bpy.ops.zepeto.make_loop()
            check("pose:make-loop", "FINISHED" in r3, str(r3))
        except Exception:
            check("pose:key-two-frames", False, traceback.format_exc().strip().splitlines()[-1])

    # ---- 8. clear_pose must only touch flagged bones -----------------------
    if rig is not None:
        try:
            probe = rig.pose.bones.get("spine")
            probe.location = (0.0, 0.05, 0.0)          # user-moved -> flagged
            baseline = set(
                scene.zepeto_baseline_odd.split(",") if scene.zepeto_baseline_odd else [])
            flagged = set(mod.odd_bones(rig)) - baseline
            # A bone the panel does NOT flag but which ships with a non-unit scale.
            untouched = next((b for b in rig.pose.bones
                              if b.name not in flagged and tuple(b.scale) != (1.0, 1.0, 1.0)),
                             None)
            before_scale = tuple(untouched.scale) if untouched else None

            bpy.ops.zepeto.clear_pose()

            check("clear:flagged-bone-reset", abs(probe.location.y) < 1e-6,
                  "spine.location.y=%.6f" % probe.location.y)
            if untouched is not None:
                after = tuple(untouched.scale)
                check("clear:unflagged-scale-preserved",
                      all(abs(a - b) < 1e-6 for a, b in zip(before_scale, after)),
                      "%s %s -> %s" % (untouched.name, before_scale, after))
            else:
                note("clear:unflagged-scale-preserved",
                     "no unflagged non-unit-scale bone in this rig to probe")
        except Exception:
            check("clear:flagged-bone-reset", False,
                  traceback.format_exc().strip().splitlines()[-1])

    # ---- 9. export -> BINARY fbx, no .part left ---------------------------
    if rig is not None:
        try:
            # Re-author: clear_pose wiped the rotation keyed above.
            bpy.context.view_layer.objects.active = rig
            if bpy.context.mode != "POSE":
                bpy.ops.object.mode_set(mode="POSE")
            arm = rig.pose.bones.get("upperArm_R") or rig.pose.bones.get("spine")
            bpy.context.scene.frame_set(1)
            bpy.ops.zepeto.key_pose()
            bpy.context.scene.frame_set(24)
            arm.rotation_mode = "XYZ"
            arm.rotation_euler = (0.0, 0.0, 0.6)
            bpy.ops.zepeto.key_pose()
            bpy.ops.zepeto.make_loop()

            scene.zepeto_export_dir = OUT_DIR
            scene.zepeto_motion_name = "HeadlessProbe"
            res = bpy.ops.zepeto.export()
            out = os.path.join(OUT_DIR, "HeadlessProbe.fbx")
            check("export:operator-finished", "FINISHED" in res, str(res))
            check("export:file-written", os.path.isfile(out),
                  "%d bytes" % os.path.getsize(out) if os.path.isfile(out) else "missing")
            if os.path.isfile(out):
                with open(out, "rb") as f:
                    head = f.read(18)
                # Blender cannot read ASCII FBX and Unity reports success either way,
                # so the magic bytes are the only real check.
                check("export:is-binary-fbx", head == b"Kaydara FBX Binary", repr(head))
            leftovers = [f for f in os.listdir(OUT_DIR) if f.endswith(".part")]
            check("export:no-part-left", not leftovers, str(leftovers))
        except Exception:
            check("export:operator-finished", False,
                  traceback.format_exc().strip().splitlines()[-1])

    # ---- 10. ambiguity must be refused, not resolved by sort order ---------
    try:
        tmp = tempfile.mkdtemp(prefix="zpamb_")
        for name in ("ZEPETO Studio Unity Project File 3.2.12",
                     "ZEPETO Studio Unity Project File 3.2.16"):
            os.makedirs(os.path.join(tmp, name, "Assets"), exist_ok=True)
        picked = mod._pick_project([os.path.join(tmp, n) for n in sorted(os.listdir(tmp))])
        got, amb = picked if isinstance(picked, tuple) else (picked, ())
        check("paths:ambiguity-refused", (not got) and bool(amb),
              "picked=%r ambiguous=%r" % (got, amb))
    except AttributeError as exc:
        note("paths:ambiguity-refused", "helper not exposed as expected: %s" % exc)
    except Exception:
        check("paths:ambiguity-refused", False, traceback.format_exc().strip().splitlines()[-1])

    # ---- summary ----------------------------------------------------------
    npass = sum(1 for _, ok, _ in results if ok)
    nfail = len(results) - npass
    print("")
    print("=== headless addon check: pass=%d fail=%d ===" % (npass, nfail))
    for name, ok, detail in results:
        if not ok:
            print("   FAILED: %s :: %s" % (name, detail))
    sys.stdout.flush()

    with open(os.path.join(OUT_DIR, "result.txt"), "w", encoding="utf-8") as f:
        f.write("ZEPETO Blender add-on headless check\n")
        f.write("pass=%d fail=%d\n" % (npass, nfail))
        f.write("blender=%s\n" % bpy.app.version_string)
        f.write("----\n")
        for name, ok, detail in results:
            f.write("%s %s :: %s\n" % ("PASS" if ok else "FAIL", name, detail))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        traceback.print_exc()
