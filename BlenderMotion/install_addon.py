"""
이 컴퓨터의 Blender에 애드온을 설치하고 켠다.

실행:
    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" --background --python install_addon.py

Blender의 Preferences > Add-ons > Install from Disk 를 손으로 하는 것과 같은 일이다. 그 화면을 찾아
들어가는 것 자체가 처음 쓰는 사람에게는 벽이라서 한 줄로 만들어 둔다.

주의: Blender의 설치는 '복사'다. 이 폴더의 zepeto_motion_helper.py를 고쳐도 이미 설치된 사본은 그대로다.
고친 뒤에는 이 스크립트를 다시 돌려야 한다. 잊었을 때 조용히 낡은 코드가 도는 것을 막으려고
headless_check.py에 install:copy-matches-source 검사를 넣어 두었다 - 두 파일이 다르면 거기서 실패한다.
"""

import os
import sys
import traceback

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(HERE, "zepeto_motion_helper.py")
MODULE = "zepeto_motion_helper"


def main():
    if not os.path.isfile(SOURCE):
        print("FAIL :: 애드온 파일을 찾지 못했습니다: %s" % SOURCE)
        return 1

    # 이미 켜져 있으면 먼저 끈다. 켜진 채로 덮어쓰면 Blender가 예전 클래스를 등록해 둔 상태로 남아
    # register()가 "already registered"로 죽는다.
    try:
        bpy.ops.preferences.addon_disable(module=MODULE)
    except Exception:
        # 처음 설치라 켜져 있지 않은 것이 정상이다.
        pass

    try:
        result = bpy.ops.preferences.addon_install(filepath=SOURCE, overwrite=True)
        print("addon_install ->", result)
        result = bpy.ops.preferences.addon_enable(module=MODULE)
        print("addon_enable  ->", result)
    except Exception:
        traceback.print_exc()
        print("FAIL :: 설치에 실패했습니다")
        return 1

    if not hasattr(bpy.types, "ZEPETO_PT_panel"):
        print("FAIL :: 설치는 됐지만 패널이 등록되지 않았습니다")
        return 1

    # 이 한 줄이 없으면 Blender를 껐다 켰을 때 애드온이 다시 꺼진다. addon_enable은 이번 세션에만
    # 적용되고, 켜진 목록은 사용자 설정 파일에 들어 있기 때문이다.
    bpy.ops.wm.save_userpref()

    import addon_utils
    installed = ""
    for module in addon_utils.modules():
        if module.__name__ == MODULE:
            installed = module.__file__
    print("PASS :: 설치 완료 -> %s" % installed)
    print("PASS :: 설정 저장 완료 (다음에 Blender를 켜도 켜진 상태로 남습니다)")
    print("      3D 화면에서 N 키를 누르면 사이드바에 'ZEPETO' 탭이 있습니다.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
