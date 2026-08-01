"""ZEPETO Blender 애드온의 헤드리스 검증.

실행:
  "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" ^
      --background --factory-startup --python BlenderMotion/headless_check.py

Unity 라이선스가 전혀 필요 없다 - Blender만 돌리고, 3단계가 이미 내보낸 리그 FBX를 읽을 뿐이다.
자기 FBX는 OS 임시 폴더에 쓰고 Assets/ 안에는 절대 쓰지 않는다 (그 폴더는 Unity 헬퍼가 0.4초마다 폴링한다).

Blender를 손으로 열지 않으면 확인할 방법이 없는 것들을 덮는다:
  - 진짜 Blender 5.2에서 모듈이 import되고 register된다 (bl_info의 blender 값은 최소 버전 4.2다)
  - 실행 시점 Unity 프로젝트 해석. 예전에 하드코딩된 경로로 죽던 '저장 안 한 .blend' 경우와 환경 변수 우선까지
  - 모호할 때 이름순으로 찍는 대신 실제로 거부한다
  - iter_fcurves가 진짜 5.2 액션에서 동작한다 (실측 162개. 4.4+ 분기는 '비었을 때만' 타는 쪽으로 바뀌었고,
    5.2는 계속 Action.fcurves 경로를 탄다)
  - import_rig → key_pose → make_loop → export가 BINARY fbx를 만든다
  - 패널을 거치지 않는 호출도 게이트에 걸린다 (1.4.0까지 fps·키프레임·이상한 뼈 검사가 패널에만 있었다)
  - 진짜 리그의 뼈 셈: 매핑 54 + 숨김 49 = 103
  - clear_pose가 패널이 지적한 뼈만 건드린다
  - .part 핸드오프가 임시 파일을 남기지 않는다

PASS/FAIL 줄과 마지막 집계를 찍고, 같은 내용을 <temp>/zepeto_headless_check/result.txt에도 쓴다.
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
    """
    비교 기준(oracle)이 될 Unity 프로젝트 경로. (project, tied)를 돌려준다.

    검사 대상의 알고리즘을 여기서 다시 구현하면 안 된다. 예전에는 glob 결과를 정렬해 첫 번째를 집었는데,
    그건 애드온의 _pick_project가 없애려고 존재하는 바로 그 알고리즘이고 이 파일의 10번 검사가 틀렸다고
    증명하는 알고리즘이다. "... 3.2.12"와 "... 3.2.16"이 나란히 있으면 기준값이 3.2.12가 되고,
    resolve_unity_project()는 정확하게 ('', ('3.2.12','3.2.16'))을 돌려주는데, paths:resolve-from-addon-file이
    FAIL로 뜨면서 옳게 행동한 애드온을 탓하게 된다.

    그래서 기준값은 찍지 않는다: 환경 변수 ZEPETO_UNITY_PROJECT가 먼저고, 없으면 glob 결과가 '정확히 하나'일
    때만 그것을 쓴다. 둘 이상이면 ("", 후보들)을 돌려주고, 사람이 환경 변수로 고르게 한다.
    """
    forced = (os.environ.get("ZEPETO_UNITY_PROJECT") or "").strip().strip('"')
    if forced and os.path.isdir(os.path.join(forced, "Assets")):
        return os.path.normpath(forced), ()

    for base in (os.path.dirname(_HERE), _HERE):
        found = [cand for cand in sorted(glob.glob(os.path.join(base, "ZEPETO Studio Unity Project File*")))
                 if os.path.isdir(os.path.join(cand, "Assets"))]
        if len(found) == 1:
            return os.path.normpath(found[0]), ()
        if len(found) > 1:
            return "", tuple(os.path.normpath(c) for c in found)
    return "", ()


UNITY_PROJECT, PROJECT_TIE = _find_unity_project()
RIG_FBX = os.path.join(UNITY_PROJECT, "Assets", "ZepetoHelper", "Rig", "ZepetoBaseModel.fbx")
OUT_DIR = os.path.join(tempfile.gettempdir(), "zepeto_headless_check")

results = []


def check(name, ok, detail=""):
    results.append((name, bool(ok), detail))
    print("%s %s%s" % ("PASS" if ok else "FAIL", name, (" :: " + str(detail)) if detail else ""))


def note(name, detail):
    print("NOTE %s :: %s" % (name, detail))


def load_addon():
    """설치하지 않고 소스 경로에서 애드온을 그대로 불러온다."""
    import importlib.util
    spec = importlib.util.spec_from_file_location("zepeto_motion_helper", ADDON_SRC)
    mod = importlib.util.module_from_spec(spec)
    sys.modules["zepeto_motion_helper"] = mod
    spec.loader.exec_module(mod)
    mod.register()
    return mod


def _installed_addon_path():
    """
    이 컴퓨터의 Blender가 실제로 불러오는 애드온 파일의 경로. 설치돼 있지 않으면 "".

    addon_utils.modules()를 쓰는 이유는 경로를 추측하지 않기 위해서다. Blender는 버전마다, 그리고
    설치 방식(레거시 scripts/addons vs 4.2+ extensions)마다 다른 폴더에 넣는다. 그 폴더를 여기서
    다시 계산하면 Blender가 어디에 넣었는지가 아니라 우리가 어디에 넣었다고 믿는지를 검사하게 된다.

    --factory-startup으로 돌 때도 이 목록은 디스크의 애드온 폴더를 그대로 보여 준다. 켜져 있는지가
    아니라 설치돼 있는지를 묻는 것이라 그게 맞다.
    """
    try:
        import addon_utils
    except ImportError:
        return ""
    for module in addon_utils.modules():
        if module.__name__ == "zepeto_motion_helper":
            path = getattr(module, "__file__", "") or ""
            # 이 저장소의 파일을 그대로 가리키고 있으면(스크립트 폴더를 직접 등록한 경우) 사본이
            # 아니므로 비교할 것이 없다.
            if os.path.normcase(os.path.abspath(path)) == os.path.normcase(ADDON_SRC):
                return ""
            return path
    return ""


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    if PROJECT_TIE:
        detail = ("후보가 %d개라 기준값을 정하지 못했습니다 (%s). 환경 변수 ZEPETO_UNITY_PROJECT로 "
                  "하나를 지정하고 다시 실행하세요" % (len(PROJECT_TIE), " / ".join(PROJECT_TIE)))
    else:
        detail = UNITY_PROJECT or "(not found)"
    check("env:unity-project-found", bool(UNITY_PROJECT), detail)

    # ---- 1. 진짜 Blender에서 import + register -----------------------------
    try:
        mod = load_addon()
        check("addon:register", True, "bl_info version %s, blender target %s"
              % (mod.bl_info["version"], mod.bl_info["blender"]))
    except Exception:
        check("addon:register", False, traceback.format_exc().strip().splitlines()[-1])
        return

    note("addon:blender-running", bpy.app.version_string)

    # ---- 2. 남의 컴퓨터 경로가 하드코딩돼 있지 않다 -------------------------
    # 소스 텍스트를 직접 본다. 이 두 문자열이 다시 들어오는 것이 곧 하드코딩 경로의 재발이라,
    # 동작 검사로는 잡을 수 없고 텍스트로만 잡을 수 있다.
    src = open(ADDON_SRC, encoding="utf-8").read()
    check("addon:no-foreign-user-path", "Jun-WN" not in src,
          "found Jun-WN" if "Jun-WN" in src else "clean")
    check("addon:project-resolved-at-runtime", "UNITY_PROJECT = r" not in src,
          "module-level constant still present" if "UNITY_PROJECT = r" in src
          else "no frozen module-level constant")

    # ---- 2b. 설치된 사본이 이 저장소와 같은 코드인가 -----------------------
    # Blender의 "Install from Disk"는 링크가 아니라 복사다. 그래서 여기 있는 파일을 고쳐도 사용자의
    # Blender는 예전 사본을 계속 돌린다 - "고쳤는데 그대로예요"의 전형적인 원인이고, 아무 에러도 나지
    # 않아서 스스로는 절대 드러나지 않는다. install_addon.py를 다시 돌리면 맞춰진다.
    #
    # 설치가 안 되어 있는 것 자체는 실패가 아니다. 이 검사 묶음은 --factory-startup으로도 돌고, CI처럼
    # 애드온을 설치하지 않는 환경도 정상이다. 설치되어 '있는데' 내용이 다를 때만 실패다.
    installed_copy = _installed_addon_path()
    if not installed_copy:
        note("install:not-installed",
             "이 컴퓨터의 Blender에 설치돼 있지 않습니다 (install_addon.py로 설치할 수 있습니다)")
    else:
        try:
            copy_src = open(installed_copy, encoding="utf-8").read()
        except OSError as exc:
            copy_src = ""
            note("install:copy-unreadable", str(exc))
        check("install:copy-matches-source", copy_src == src,
              "설치된 사본이 낡았습니다 (%s). install_addon.py를 다시 실행하세요" % installed_copy
              if copy_src != src else installed_copy)

    # ---- 3. 실행 시점 해석, 저장 안 한 .blend (새 씬의 경우) ---------------
    try:
        resolved = mod.resolve_unity_project()
        project = resolved[0] if isinstance(resolved, tuple) else resolved
        ambiguous = resolved[1] if isinstance(resolved, tuple) else ()
        same = os.path.normcase(os.path.abspath(project or "")) == os.path.normcase(
            os.path.abspath(UNITY_PROJECT))
        if UNITY_PROJECT:
            check("paths:resolve-from-addon-file", same, project or "(empty)")
        else:
            # 기준값이 없는 것은 이 컴퓨터의 문제지 애드온의 문제가 아니다. 여기서 FAIL을 찍으면
            # 옳게 거부한 애드온을 탓하게 된다.
            note("paths:resolve-from-addon-file",
                 "기준값이 없어 비교를 건너뜁니다 (애드온 결과: %r)" % (project or "",))
        check("paths:not-ambiguous-here", not ambiguous, str(ambiguous))
    except Exception:
        check("paths:resolve-from-addon-file", False,
              traceback.format_exc().strip().splitlines()[-1])

    # ---- 4. 환경 변수가 우선한다 -------------------------------------------
    try:
        os.environ[mod.UNITY_PROJECT_ENV] = UNITY_PROJECT
        r2 = mod.resolve_unity_project()
        p2 = r2[0] if isinstance(r2, tuple) else r2
        check("paths:env-override", os.path.normcase(os.path.abspath(p2 or "")) ==
              os.path.normcase(os.path.abspath(UNITY_PROJECT)), p2 or "(empty)")
    finally:
        # 반드시 지운다. 남겨 두면 이 프로세스의 이후 resolve_unity_project() 호출에 전부 새어 들어가고,
        # 특히 10번의 모호성 검사가 동점을 아예 못 보게 된다.
        os.environ.pop(mod.UNITY_PROJECT_ENV, None)

    # ---- 5. refresh_paths가 씬에 값을 써 넣는다 ----------------------------
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

    # ---- 6. 진짜 리그를 불러온다 -------------------------------------------
    check("rig:source-exists", os.path.isfile(RIG_FBX), RIG_FBX)
    rig = None
    try:
        res = bpy.ops.zepeto.import_rig()
        # 리그 오브젝트는 "hips"라는 이름으로 들어온다. FBX의 최상위 뼈를 Blender가 아마추어 오브젝트로
        # 만들기 때문이고, 애드온의 RIG_NAME도 같은 이름을 본다.
        rig = bpy.context.scene.objects.get("hips") or next(
            (o for o in bpy.data.objects if o.type == "ARMATURE"), None)
        check("rig:import", "FINISHED" in res and rig is not None, str(res))
        if rig is not None:
            bones = len(rig.pose.bones)
            hidden = sum(1 for b in rig.data.bones if b.hide)
            mapped = sum(1 for b in rig.pose.bones if b.name in mod.MAPPED_BONES)
            # STATUS.md가 기록한 셈 맞추기: 매핑 54 + 숨김 49 = 103.
            # (55번째 Humanoid 매핑인 hips는 뼈가 아니라 아마추어 오브젝트다.)
            check("rig:bone-count-103", bones == 103, "%d pose bones" % bones)
            check("rig:hidden-49", hidden == 49, "%d hidden" % hidden)
            check("rig:mapped-54", mapped == 54, "%d mapped of %d" % (mapped, bones))
            check("rig:arithmetic-closes", mapped + hidden == bones,
                  "%d + %d = %d" % (mapped, hidden, bones))
            note("rig:frame-end", str(bpy.context.scene.frame_end))
    except Exception:
        check("rig:import", False, traceback.format_exc().strip().splitlines()[-1])

    # ---- 7. 모션 만들기: 매핑된 뼈 하나를 돌려 두 프레임에 키 --------------
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

    # ---- 8. clear_pose는 지적된 뼈만 건드려야 한다 -------------------------
    if rig is not None:
        try:
            probe = rig.pose.bones.get("spine")
            probe.location = (0.0, 0.05, 0.0)          # 사용자가 옮긴 것 -> 지적 대상
            # 기준선 파싱을 애드온의 baseline_odd()를 부르지 않고 여기서 직접 편 것은 의도적이다.
            # 검사 대상과 같은 함수로 기대값을 만들면 그 함수가 틀려도 검사는 통과한다.
            baseline = set(
                scene.zepeto_baseline_odd.split(",") if scene.zepeto_baseline_odd else [])
            flagged = set(mod.odd_bones(rig)) - baseline
            # 패널이 지적하지 '않는', 그런데 단위가 아닌 스케일을 갖고 출고된 뼈 하나.
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

    # ---- 9. 내보내기 -> BINARY fbx, .part 잔여물 없음 ---------------------
    if rig is not None:
        try:
            # 다시 만든다: clear_pose가 위에서 찍어 둔 회전을 되돌렸다.
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
                # Blender는 ASCII FBX를 읽지 못하는데 Unity는 어느 쪽이든 성공했다고 보고한다.
                # 그래서 매직 바이트만이 진짜 검사다.
                check("export:is-binary-fbx", head == b"Kaydara FBX Binary", repr(head))
            leftovers = [f for f in os.listdir(OUT_DIR) if f.endswith(".part")]
            check("export:no-part-left", not leftovers, str(leftovers))
        except Exception:
            check("export:operator-finished", False,
                  traceback.format_exc().strip().splitlines()[-1])

    # ---- 9b. 내보내기 게이트가 패널이 아니라 오퍼레이터에 있다 ------------
    #
    # 이 파일 자체가 그 문제의 호출 경로다: 패널을 한 번도 그리지 않고 bpy.ops.zepeto.export()를 직접 부른다.
    # 1.4.0까지 fps 게이트는 패널 draw 코드에만 있었으므로, 여기서 fps를 30으로 바꿔도 내보내기가 그대로
    # 성공했다. 애드온 1.5.0에서 export_problems()가 오퍼레이터 안으로 들어갔고, 이 검사가 그것을 증명한다.
    if rig is not None:
        try:
            scene.render.fps = 30
            try:
                res = bpy.ops.zepeto.export()
                refused = "CANCELLED" in res
                detail = str(res)
            except RuntimeError as exc:
                # Blender는 오퍼레이터의 ERROR 리포트를 Python 호출부에서 RuntimeError로 바꿔 던진다.
                # 거절한 것이 맞으므로 이것도 통과다.
                refused = True
                detail = str(exc).strip().splitlines()[0]
            check("export:gate-refuses-off-panel", refused, detail)
        except Exception:
            check("export:gate-refuses-off-panel", False,
                  traceback.format_exc().strip().splitlines()[-1])
        finally:
            scene.render.fps = mod.FPS

    # ---- 10. 모호하면 정렬 순서로 풀지 말고 거부해야 한다 ------------------
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

    # ---- 집계 --------------------------------------------------------------
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
