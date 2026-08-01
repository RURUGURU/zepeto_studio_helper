"""
패널 draw() 검사 - headless_check.py가 닿지 못하는 유일한 구간.

headless_check.py는 --background로 도는데, 그 모드에는 창도 영역도 없다. Panel.draw는 UILayout을
받아야 하고 UILayout은 실제로 그려지는 영역 안에서만 만들어지므로, --background에서는 draw를 한 줄도
실행할 수 없다. 그래서 지금까지 오퍼레이터와 순수 함수는 28개 검사가 지키고 있었지만, 사용자가 실제로
보는 패널은 아무도 실행해 본 적이 없었다.

draw가 던지는 예외는 Blender를 죽이지 않는다. 콘솔에 traceback만 찍고 그 패널은 반쯤 그리다 만
상태로 남는다. 즉 조용히 망가진다 - "버튼이 안 보여요"로만 드러나고, 로그를 안 보면 원인을 알 수 없다.
이 스크립트는 그 traceback을 실패로 승격시킨다.

그리고 draw는 분기가 많다. 리그가 없을 때 / 있을 때, 뼈 이름이 안 맞을 때, 경로가 깨졌을 때,
'무시하는 뼈도 보기'가 켜졌을 때, 내보낼 준비가 됐을 때와 아닐 때. 각 분기는 서로 다른 UILayout
호출을 타므로 한 번 그려 보는 것으로는 부족하다. 아래 STATES가 그 분기들을 하나씩 강제한다.

실행:
    blender.exe --factory-startup --python ui_check.py
--background를 붙이면 안 된다. 창이 필요하다. 검사가 끝나면 스스로 종료한다.
종료 코드가 0이 아니거나 출력에 FAIL이 있으면 실패다.
"""

import os
import sys
import traceback

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import zepeto_motion_helper as addon


results = []
# draw 안에서 터진 예외는 여기 모인다. sys.excepthook으로는 못 잡는다. Blender가 draw 호출을
# 자체 try로 감싸서 traceback을 직접 찍고 삼키기 때문이다. 그래서 draw_zepeto_panel을 우리가 감싼다.
draw_errors = []


def check(name, ok, detail=""):
    results.append((name, bool(ok), detail))
    print("%s %s :: %s" % ("PASS" if ok else "FAIL", name, detail))


def note(name, detail):
    print("NOTE %s :: %s" % (name, detail))


# ---------------------------------------------------------------- draw 감싸기
_real_draw = addon.draw_zepeto_panel
# 그려진 UI 요소를 상태별로 기록한다. 라벨/버튼 텍스트를 눈으로 확인할 수 없으니, 대신 어떤 오퍼레이터
# 버튼이 실제로 layout에 들어갔는지를 센다. "버튼이 조용히 사라졌다"가 이 파이프라인의 대표 회귀라서
# 존재 자체를 검사값으로 삼는다.
drawn_ops = set()
draw_count = [0]


class _LayoutSpy:
    """
    UILayout을 그대로 감싸서 operator()에 넘어온 bl_idname만 훔쳐본다.

    UILayout은 Python에서 상속할 수 없는 C 타입이라 __getattr__ 위임으로 감싼다. box()/row()/column()
    처럼 다시 UILayout을 돌려주는 것들은 다시 감싸서 중첩된 자식 안의 버튼도 놓치지 않는다.
    """

    def __init__(self, inner):
        object.__setattr__(self, "_inner", inner)

    def operator(self, idname, *args, **kwargs):
        drawn_ops.add(idname)
        return object.__getattribute__(self, "_inner").operator(idname, *args, **kwargs)

    def box(self, *a, **k):
        return _LayoutSpy(object.__getattribute__(self, "_inner").box(*a, **k))

    def row(self, *a, **k):
        return _LayoutSpy(object.__getattribute__(self, "_inner").row(*a, **k))

    def column(self, *a, **k):
        return _LayoutSpy(object.__getattribute__(self, "_inner").column(*a, **k))

    def __getattr__(self, item):
        return getattr(object.__getattribute__(self, "_inner"), item)

    def __setattr__(self, item, value):
        setattr(object.__getattribute__(self, "_inner"), item, value)


def _spy_draw(layout, context):
    draw_count[0] += 1
    try:
        _real_draw(_LayoutSpy(layout), context)
    except Exception:
        # 여기서 다시 던지면 Blender가 traceback을 찍고 삼킨다. 우리가 먼저 붙잡아 두고 조용히
        # 넘어가면, 한 상태에서 터져도 나머지 상태를 계속 검사할 수 있다.
        draw_errors.append((_state_label[0], traceback.format_exc()))


addon.draw_zepeto_panel = _spy_draw

_state_label = ["<none>"]


# ---------------------------------------------------------------- 화면 준비
def find_view3d():
    for window in bpy.context.window_manager.windows:
        for area in window.screen.areas:
            if area.type == "VIEW_3D":
                return window, area
    return None, None


def open_sidebar():
    """
    사이드바(N 패널)를 연다. 닫혀 있으면 어떤 패널의 draw도 호출되지 않으므로 검사가 통째로
    무의미해진다 - 0번 그리고 통과했다고 말하는 최악의 결과가 된다. draw_count로 그걸 막는다.

    factory-startup은 사이드바가 닫힌 채로 시작하고, 열자마자의 UI 영역 폭은 1픽셀이다. 실제 폭은
    한 번 그려 봐야 정해지므로 여기서 바로 폭을 검사하면 안 된다 - main()의 첫 draw_state가
    draw_count로 확인한다.
    """
    window, area = find_view3d()
    if area is None:
        return False
    for space in area.spaces:
        if space.type == "VIEW_3D":
            space.show_region_ui = True
    area.tag_redraw()
    return True


def ui_region_width():
    """사이드바가 실제로 자리를 차지하고 있는지. 실패했을 때 원인을 좁히려고 찍는다."""
    _, area = find_view3d()
    if area is None:
        return -1
    for region in area.regions:
        if region.type == "UI":
            return region.width
    return -1


def force_redraw():
    """
    실제로 한 프레임을 그리게 만든다. tag_redraw만으로는 즉시 그려지지 않는다 - 이벤트 루프가
    돌아야 한다. INVOKE 없이 그 루프를 한 바퀴 돌리는 방법이 wm.redraw_timer다.
    """
    window, area = find_view3d()
    if area is None:
        return False
    with bpy.context.temp_override(window=window, area=area):
        bpy.ops.wm.redraw_timer(type="DRAW_WIN_SWAP", iterations=1)
    return True


def draw_state(label):
    """한 상태를 그려 보고, 그 상태에서 나온 draw 예외와 버튼 목록을 돌려준다."""
    _state_label[0] = label
    drawn_ops.clear()
    before_errors = len(draw_errors)
    before_draws = draw_count[0]
    force_redraw()
    return {
        "ops": set(drawn_ops),
        "threw": len(draw_errors) > before_errors,
        "drew": draw_count[0] > before_draws,
    }


# ---------------------------------------------------------------- 검사
def main():
    addon.register()

    if not open_sidebar():
        check("ui:view3d-present", False, "VIEW_3D 영역이 없다 - --background로 실행했나?")
        return finish()
    check("ui:view3d-present", True, "사이드바를 열었다")

    scene = bpy.context.scene

    # --- 상태 1: 리그 없음. 새로 설치한 사람이 처음 보는 화면이다.
    # factory-startup 씬에는 큐브·카메라·라이트만 있고 아마추어가 없으므로 get_rig가 None을 돌려준다.
    # 예전에는 여기서 bpy.data.objects를 전부 지웠는데, 오브젝트가 하나도 없는 화면은 사이드바 영역이
    # 자리를 잡지 못해서 draw가 아예 호출되지 않았다 - 검사 6개가 "버튼이 없다"고 거짓 실패했다.
    check("ui:no-rig-state", addon.get_rig(bpy.context) is None,
          "%s" % [o.name for o in bpy.data.objects])
    state = draw_state("리그 없음")
    check("ui:draws-at-all", state["drew"],
          "draw가 %d번 호출됐다 (UI 영역 폭 %d)" % (draw_count[0], ui_region_width()))
    check("ui:no-rig-no-exception", not state["threw"], "예외 없음")
    # 리그가 없을 때 유일하게 눌러야 할 버튼. 이게 빠지면 시작 자체가 불가능하다.
    check("ui:no-rig-shows-import", "zepeto.import_rig" in state["ops"],
          sorted(state["ops"]))

    # --- 상태 2: 경로가 깨진 채 리그 없음. 다른 컴퓨터에서 건너온 .blend이 이 상태다.
    # "//" 접두사(.blend 상대 경로)는 이 프로퍼티가 받지 않는다 - Blender가 RuntimeWarning을 찍고
    # 값을 그대로 둔다. 그러면 경로가 안 깨져서 검사가 무의미해진다. 절대 경로로 깨뜨린다.
    scene.zepeto_rig_fbx = os.path.join(HERE, "없는파일", "does_not_exist.fbx")
    state = draw_state("리그 없음 + 경로 깨짐")
    check("ui:broken-path-no-exception", not state["threw"], "예외 없음")
    check("ui:broken-path-offers-locate", "zepeto.locate_paths" in state["ops"],
          sorted(state["ops"]))

    # --- 상태 3: 진짜 ZEPETO 리그를 불러온 뒤. 정상 경로 전체.
    scene.zepeto_rig_fbx = ""
    addon.refresh_paths(scene, force=True)
    if addon.rig_fbx_problem(scene):
        check("ui:rig-import", False, "리그 FBX를 찾지 못했다: %s" % addon.rig_fbx_problem(scene))
        return finish()
    window, area = find_view3d()
    with bpy.context.temp_override(window=window, area=area):
        rig_result = bpy.ops.zepeto.import_rig()
    check("ui:rig-import", rig_result == {"FINISHED"}, str(rig_result))

    state = draw_state("리그 있음")
    check("ui:with-rig-no-exception", not state["threw"], "예외 없음")
    # 5단계 전체가 그려졌는지. 이 네 개 중 하나라도 빠지면 작업 흐름이 끊긴다.
    expected = {"zepeto.clear_pose", "zepeto.key_pose", "zepeto.delete_key",
                "zepeto.make_loop", "zepeto.export"}
    missing = expected - state["ops"]
    check("ui:with-rig-full-workflow", not missing, "빠진 버튼: %s" % sorted(missing))

    # --- 상태 4: '무시하는 뼈도 보기'를 켠 상태. update 콜백이 draw 도중이 아니라 여기서 돈다.
    scene.zepeto_show_all_bones = True
    state = draw_state("숨긴 뼈 보기 켬")
    check("ui:show-all-bones-no-exception", not state["threw"], "예외 없음")
    scene.zepeto_show_all_bones = False

    # --- 상태 5: 저장 폴더가 깨진 상태. 5단계가 경고 + 복구 버튼을 그려야 한다.
    good_dir = scene.zepeto_export_dir
    scene.zepeto_export_dir = os.path.join(HERE, "없는폴더", "CustomMotions")
    state = draw_state("저장 폴더 깨짐")
    check("ui:broken-export-dir-no-exception", not state["threw"], "예외 없음")
    check("ui:broken-export-dir-offers-locate", "zepeto.locate_paths" in state["ops"],
          sorted(state["ops"]))
    # 내보내기 버튼은 사라지면 안 된다. 비활성이어야 한다. 사라지면 사용자는 고칠 대상을 못 찾는다.
    check("ui:broken-export-dir-keeps-export", "zepeto.export" in state["ops"],
          sorted(state["ops"]))
    scene.zepeto_export_dir = good_dir

    # --- 상태 6: 이름이 잘못된 상태. 5단계가 이유를 말하고 내보내기를 막아야 한다.
    scene.zepeto_motion_name = "bad/name"
    state = draw_state("이름 잘못됨")
    check("ui:bad-name-no-exception", not state["threw"], "예외 없음")
    problems = addon.export_problems(scene, addon.get_rig(bpy.context))
    check("ui:bad-name-blocks-export", any("이름" in p for p in problems),
          str(problems))
    scene.zepeto_motion_name = "MyMotion"

    # --- 상태 7: Item 탭 사본. 같은 함수를 쓰지만 등록이 따로라, 한쪽만 죽는 회귀가 가능하다.
    check("ui:item-tab-registered", hasattr(bpy.types, "ZEPETO_PT_panel_item"),
          "Item 탭 사본이 등록됐다")

    return finish()


def finish():
    for label, tb in draw_errors:
        print("---- draw 예외 (%s) ----\n%s" % (label, tb))
    passed = sum(1 for _, ok, _ in results if ok)
    failed = len(results) - passed
    print("\n=== ui check: pass=%d fail=%d ===" % (passed, failed))
    # Blender는 --python 스크립트의 예외로 종료 코드를 바꾸지 않는다. 직접 끝낸다.
    bpy.ops.wm.quit_blender()
    return failed


if __name__ == "__main__":
    try:
        main()
    except Exception:
        traceback.print_exc()
        print("\n=== ui check: pass=0 fail=1 (하네스 자체가 죽었다) ===")
        bpy.ops.wm.quit_blender()
