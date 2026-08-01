"""
문서가 시키는 순서 그대로 눌러 본다 - 초보자가 실제로 걷는 길.

실행:
    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" ^
        --background BlenderMotion\\zepeto_motion.blend --python BlenderMotion\\beginner_check.py

--factory-startup을 붙이면 안 된다. 이 검사가 묻는 것 중 하나가 "설치된 애드온이 켜진 채로 열리는가"다.

headless_check.py와 무엇이 다른가:
  headless_check는 부품을 검사한다. 깨끗한 씬을 새로 만들고, 리그를 직접 임포트하고, 함수 하나하나가
  약속대로 동작하는지 본다. 그래서 '부품은 다 멀쩡한데 배포된 .blend으로는 시작조차 못 하는' 상태를
  구조적으로 볼 수 없다. 실제로 그런 상태였다 - 배포되는 zepeto_motion.blend의 리그 경로에 다른
  사람의 계정 경로(C:\\Users\\Jun-WN\\...)가 저장돼 있었고, 애드온은 Blender에 설치조차 되어 있지
  않았다. 검사 28개가 전부 통과하는 동안에도 그랬다.

  이 파일은 반대로 간다. 배포되는 그 .blend을 그대로 열고, README_모션만들기.md가 적어 둔 순서대로만
  누른다. 내부 함수를 직접 부르지 않고 오퍼레이터만 부르는 것이 규칙이다 - 사용자가 버튼을 누르는
  것과 같은 경로여야 이 검사가 무언가를 증명한다.

이 검사는 Assets/CustomMotions에 fbx를 하나 만들었다가 지운다. 그 폴더는 Unity 헬퍼가 0.4초마다
폴링하므로, 남기면 사용자의 '내 모션' 목록에 정체불명의 항목이 생긴다.
"""

import os
import sys

import bpy

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

# 설치된 애드온이 이미 같은 이름으로 import돼 있으면 그것이 돌아온다. 그래야 맞다 - 이 검사가 보려는
# 것은 저장소의 파일이 아니라 사용자의 Blender가 실제로 돌리는 코드다.
import zepeto_motion_helper as addon

results = []


def check(name, ok, detail=""):
    results.append((name, bool(ok), detail))
    print("%s %s :: %s" % ("PASS" if ok else "FAIL", name, detail))


def note(name, detail):
    print("NOTE %s :: %s" % (name, detail))


def main():
    scene = bpy.context.scene

    # ---- 0. 문서: "Blender를 열고 N 키를 누르면 패널이 있습니다" ------------
    # 등록 여부만 본다. 여기서 register()를 부르면 '설치돼 있지 않아도 통과하는' 검사가 된다.
    check("beginner:addon-enabled-on-open", hasattr(bpy.types, "ZEPETO_PT_panel"),
          "설치본이 켜진 채로 열렸다" if hasattr(bpy.types, "ZEPETO_PT_panel")
          else "패널이 등록되지 않았다 - install_addon.py를 실행하세요")

    # ---- 배포되는 .blend은 어느 컴퓨터에서 열어도 되어야 한다 ---------------
    # 저장된 절대 경로는 정의상 그 컴퓨터에서만 맞다. 빈 값이어야 refresh_paths가 여는 쪽 컴퓨터의
    # 프로젝트로 채운다. 예전에는 여기에 C:\Users\Jun-WN\... 가 들어 있었다 - 남의 계정 이름이
    # 저장소에 커밋된 상태였고, 이 검사가 없어서 아무도 몰랐다.
    for prop in ("zepeto_rig_fbx", "zepeto_export_dir"):
        stored = getattr(scene, prop)
        check("beginner:blend-has-no-absolute-%s" % prop.replace("zepeto_", "").replace("_", "-"),
              not os.path.isabs(bpy.path.abspath(stored)) if stored else True,
              "저장된 값: %r" % stored)

    # ---- 1단계: 문서 "쓸 수 있는 뼈 54개 / 전체 103개 라고 떠 있으면 넘어가세요"
    rig = addon.get_rig(bpy.context)
    check("beginner:1-body-already-loaded", rig is not None,
          rig.name if rig else "아마추어가 없다")
    if rig is None:
        return finish()

    usable = addon.mapped_coverage(rig)
    check("beginner:1-bone-counts-match-doc", usable == 54 and len(rig.data.bones) == 103,
          "쓸 수 있는 뼈 %d개 / 전체 %d개" % (usable, len(rig.data.bones)))
    # 문서: "화면에는 54개만 보입니다". 숨김은 import_rig가 걸어 두는 것이라, 저장된 .blend이
    # 그 상태로 저장돼 있지 않으면 문서가 거짓말이 된다.
    check("beginner:1-dead-bones-hidden", sum(1 for b in rig.data.bones if b.hide) == 49,
          "숨겨진 뼈 %d개" % sum(1 for b in rig.data.bones if b.hide))
    # 문서: "몸통은 클릭해도 안 잡히게 막아뒀습니다"
    meshes = [o for o in rig.children_recursive if o.type == "MESH"]
    check("beginner:2-body-not-clickable", bool(meshes) and all(o.hide_select for o in meshes),
          str([(o.name, o.hide_select) for o in meshes]))

    # ---- 5단계 저장 폴더: 문서 "비어 있으면 경로 자동 찾기를 한 번 누르면 됩니다"
    note("beginner:export-dir-on-open", repr(scene.zepeto_export_dir))
    bpy.ops.zepeto.locate_paths()
    check("beginner:5-locate-paths-fixes-folder", not addon.export_dir_problem(scene),
          repr(scene.zepeto_export_dir))

    # ---- 2단계: 문서 "파란 뼈를 클릭 -> R" -------------------------------
    bpy.context.view_layer.objects.active = rig
    if bpy.context.object.mode != "POSE":
        bpy.ops.object.mode_set(mode="POSE")
    arm = rig.pose.bones.get("upperArm_R")
    check("beginner:2-target-bone-visible",
          arm is not None and not rig.data.bones["upperArm_R"].hide,
          "upperArm_R")
    if arm is None:
        return finish()

    # ---- 3단계: 문서 "1 -> 24 -> 48 순서를 추천" --------------------------
    # frame_current에 대입하는 대신 frame_set을 쓴다. 대입은 평가를 뒤로 미루기 때문에, 그 다음에
    # 넣은 회전을 오퍼레이터 호출 시점의 depsgraph 갱신이 애니메이션 값으로 덮어쓴다. 패널의 프레임
    # 칸은 UI에서 진짜 프레임 변경을 일으키므로 frame_set 쪽이 사용자의 조작과 같다.
    scene.frame_set(scene.frame_start)
    bpy.ops.zepeto.key_pose()
    scene.frame_set(24)
    arm.rotation_euler = (0.0, 0.0, 1.0)          # R 키로 돌린 것과 같은 결과
    bpy.ops.zepeto.key_pose()
    times = addon.keyframe_times(rig)
    check("beginner:3-two-keyframes", len(times) >= 2, "저장된 프레임: %s" % times)

    # ---- 4단계: 문서 "처음과 끝 맞추기 버튼 한 번" ------------------------
    bpy.ops.zepeto.make_loop()
    times = addon.keyframe_times(rig)
    check("beginner:4-loop-keys-last-frame", scene.frame_end in times,
          "저장된 프레임: %s" % times)

    # ---- 5단계: 문서 "이름 칸에 영어로 이름을 씁니다" ---------------------
    scene.zepeto_motion_name = "BeginnerCheck"
    problems = addon.export_problems(scene, rig)
    check("beginner:5-export-button-enabled", not problems, "막는 이유: %s" % problems)
    if problems:
        return finish()

    folder = bpy.path.abspath(scene.zepeto_export_dir)
    out = os.path.join(folder, "BeginnerCheck.fbx")
    try:
        result = bpy.ops.zepeto.export()
        check("beginner:5-fbx-written", os.path.isfile(out), "%s -> %s" % (result, out))
        if os.path.isfile(out):
            with open(out, "rb") as handle:
                head = handle.read(18)
            # Blender는 ASCII fbx를 열지 못하고 Unity는 둘 다 성공으로 보고한다. 매직 바이트가
            # 유일하게 믿을 수 있는 판정이다.
            check("beginner:5-binary-fbx", head.startswith(b"Kaydara FBX Binary"),
                  "%r, %d bytes" % (head, os.path.getsize(out)))
            check("beginner:5-no-part-left", not os.path.exists(out + ".part"), "")

            # [QC] 내보낸 fbx에 절대 경로가 들어가지 않는가.
            #
            # 예전에는 모션 fbx마다 C:\Users\<계정>\...\ZepetoBaseModel.prefab 이 여덟 군데씩
            # 박혀 나갔다. 리그 재질의 텍스처가 prefab 안 서브애셋이라 그 '경로'가 .prefab 자신이었고,
            # FBX exporter의 기본값(path_mode="AUTO")이 그것을 절대 경로로 적었기 때문이다.
            # 커밋되는 파일에 계정 이름이 들어가는데 눈으로는 절대 보이지 않는다.
            # 애드온이 path_mode="STRIP"으로 내보내므로 이제 경로가 남지 않아야 한다.
            with open(out, "rb") as handle:
                raw = handle.read()
            user_paths = raw.lower().count(rb":\users" + b"\\")
            prefabs = raw.lower().count(b".prefab")
            check("beginner:5-no-absolute-path-in-fbx", user_paths == 0,
                  "fbx 안의 사용자 경로 %d건" % user_paths)
            check("beginner:5-no-prefab-texture-in-fbx", prefabs == 0,
                  "fbx 안의 .prefab 참조 %d건" % prefabs)
    finally:
        # Assets/CustomMotions는 Unity 헬퍼가 0.4초마다 폴링한다. 남기면 사용자의 목록이 오염된다.
        for leftover in (out, out + ".part", out + ".meta"):
            if os.path.exists(leftover):
                try:
                    os.remove(leftover)
                except OSError as exc:
                    note("beginner:cleanup-failed", "%s (%s)" % (leftover, exc))
        note("beginner:cleanup", "이 검사가 만든 fbx를 지웠습니다")

    return finish()


def finish():
    passed = sum(1 for _, ok, _ in results if ok)
    failed = len(results) - passed
    for name, ok, detail in results:
        if not ok:
            print("   FAILED: %s :: %s" % (name, detail))
    print("\n=== beginner walkthrough: pass=%d fail=%d ===" % (passed, failed))
    return failed


if __name__ == "__main__":
    main()
