"""Assets/ZepetoHelperTests/Editor/ZepetoLiveReloadRun.cs용 라이브 왕복 픽스처를 하나 만든다.

픽스처마다 한 번씩, 매번 새 Blender로 실행한다. 그래야 키프레임이 서로 새지 않는다:

  set ZEPETO_FIXTURE=a
  "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" ^
      --background --factory-startup --python BlenderMotion/make_live_fixtures.py
  set ZEPETO_FIXTURE=b
  ... 같은 명령을 한 번 더

ZepetoLiveReloadRun.cs 맨 위의 FIXTURE SPECIFICATION 블록을 그대로 구현한 것이다:

  a : 48프레임, upperArm_R'만' 회전, 키는 1 / 24 / 48, 그 다음 루프
  b : 96프레임, upperLeg_L'만' 회전, 키는 1 / 48 / 96, 그 다음 루프

둘 다 Assets/CustomMotions가 아니라 프로젝트 루트에 떨어진다. 그 폴더는 라이브 감시자의 폴링 루트라,
픽스처가 거기 앉아 있으면 감시자가 러너가 Assets/CustomMotions/LiveRoundTrip.fbx에 놓는 사본이 아니라
픽스처 쪽에 반응해 버린다.

두 픽스처는 반드시 서로 '다른' 뼈를 움직여야 한다. 그 분리만이 러너가 "핫 리로드가 아예 일어나지 않았다"와
"애초에 아무것도 애니메이션되지 않았다"를 구별할 수 있게 해 준다 - 같은 뼈면 두 경우가 똑같이 '가만히 있는
뼈'로 읽힌다.
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
    """
    픽스처를 떨어뜨릴 Unity 프로젝트 루트. 고를 수 없으면 ""를 돌려주고, 절대 찍지 않는다.

    예전에는 glob 결과를 정렬해서 첫 번째를 집었다. 그건 애드온의 _pick_project가 없애려고 존재하는 바로 그
    알고리즘이다 - "... 3.2.12"와 "... 3.2.16"이 나란히 있으면 이름순 첫 번째인 3.2.12가 이기고, 픽스처가
    Unity가 열어 두지도 않은 프로젝트에 조용히 떨어진다. 여기서는 그게 특히 나쁜데, 픽스처는 git에서 제외돼
    있어서 잘못된 위치에 생겨도 아무 흔적이 남지 않기 때문이다.

    그래서 순서는 애드온과 같다: 환경 변수 ZEPETO_UNITY_PROJECT가 먼저고, 그 다음 glob 결과가 '정확히 하나'일
    때만 그것을 쓴다. 둘 이상이면 사람이 고를 문제라서 ""를 돌려준다.
    """
    forced = (os.environ.get("ZEPETO_UNITY_PROJECT") or "").strip().strip('"')
    if forced and os.path.isdir(os.path.join(forced, "Assets")):
        return os.path.normpath(forced)

    # 폴더 이름이 아니라 구조로 찾는다. ZEPETO Studio가 붙여 주는 기본 이름에 기대면 폴더를
    # 정리하는 순간 못 찾는다. ProjectSettings/ProjectVersion.txt는 Unity가 만드는 파일이라
    # 이름과 무관하다 (headless_check.py의 같은 함수와 판정을 맞춰 둔다).
    for base in (os.path.dirname(_HERE), _HERE):
        found = [cand for cand in sorted(glob.glob(os.path.join(base, "*")))
                 if os.path.isdir(os.path.join(cand, "Assets"))
                 and os.path.isfile(os.path.join(cand, "ProjectSettings", "ProjectVersion.txt"))]
        if len(found) == 1:
            return os.path.normpath(found[0])
        if len(found) > 1:
            print("FAIL: Unity 프로젝트 후보가 %d개입니다. 환경 변수 ZEPETO_UNITY_PROJECT로 하나를 지정하세요:"
                  % len(found))
            for cand in found:
                print("        %s" % cand)
            return ""
    return ""


def load_addon():
    """설치하지 않고 소스 경로에서 애드온을 그대로 불러온다 (headless_check.py와 같은 방식)."""
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

    # 애드온은 실효 프레임레이트가 24가 아니면 내보내기를 거부한다(export_problems의 fps 게이트). 그러니
    # 여기서 직접 맞춰 줘야 한다. fps_base까지 1.0으로 못 박는 이유는 실효 프레임레이트가 fps / fps_base라서,
    # Blender 기본 프리셋 '24 fps NTSC'(24 / 1.001 = 23.976)가 그대로면 게이트에 걸리기 때문이다.
    # 이 두 줄이 ZepetoLiveReloadRun.cs가 재는 'clip length: 1.96s -> 3.96s'를 성립시킨다.
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

    # 픽스처마다 Blender를 새로 띄우지만 그래도 명시적으로 비운다. 액션이 없어야 임포트가 들고 왔을지도 모를
    # 커브가 하나도 살아남지 못한다.
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
    # 양 끝은 rest, 가운데 키에만 회전. 그래야 뼈가 실제로 '이동'한다 - 값이 같은 키 두 개는 애드온의
    # curve_varies 게이트에도 걸리고, Unity에서도 그냥 정지 포즈다.
    for frame, angle in ((first, 0.0), (middle, spec["angle"]), (last, 0.0)):
        scene.frame_set(frame)
        bone.rotation_euler = (0.0, 0.0, angle)
        if "FINISHED" not in bpy.ops.zepeto.key_pose():
            print("FAIL: key_pose refused at frame %d" % frame)
            return 2

    # ZEPETO는 모션을 반복 재생하므로 frame_end가 1프레임과 같아야 한다.
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
    # Blender는 ASCII FBX를 읽지 못하는데 Unity는 어느 쪽이든 성공했다고 보고한다. 그래서 매직 바이트만이
    # 진짜 검사다. '.part'가 남았는지도 같이 본다 - 남아 있으면 원자적 교체가 끝까지 가지 못한 것이다.
    leftovers = [p for p in os.listdir(project) if p.endswith(".part")]

    # 애드온의 curve_varies를 그대로 쓴다. 예전에는 이 판정이 여기에만 따로 있었고, 정작 애드온의 내보내기
    # 게이트는 '서로 다른 시각의 키가 2개 이상'만 봐서 값이 같은 두 키를 통과시켰다. 이제 게이트가 같은
    # 술어를 쓰므로, 여기서 다시 정의하면 두 벌이 어긋날 수 있다.
    moved = [fc.data_path for fc in mod.iter_fcurves(rig.animation_data.action)
             if "rotation" in fc.data_path and mod.curve_varies(fc)]

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


if __name__ == "__main__":
    try:
        sys.exit(main())
    except SystemExit:
        raise
    except Exception:
        import traceback
        traceback.print_exc()
        sys.exit(2)
