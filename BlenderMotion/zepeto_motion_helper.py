bl_info = {
    "name": "ZEPETO 모션 헬퍼",
    "author": "easy",
    "version": (1, 5, 0),
    "blender": (4, 2, 0),
    "location": "3D View > 사이드바(N) > ZEPETO",
    "description": "ZEPETO 의상 미리보기용 모션을 버튼 몇 개로 만들고 Unity로 내보냅니다.",
    "category": "Animation",
}

import os
import time
from collections import namedtuple
import bpy
from bpy.props import BoolProperty, StringProperty
from mathutils import Euler, Vector

FPS = 24
DEFAULT_END = 48

# get_rig의 2순위 후보 이름. ZEPETO FBX는 최상위 뼈가 Blender에서 아마추어 오브젝트가 되므로 리그가
# "hips"라는 이름으로 들어온다 (headless_check.py:138과 make_live_fixtures.py:97이 실제로 그 이름으로
# 리그를 집는다). 예전 값 "HumanoidRig"는 Mixamo 쪽 작명이라 ZEPETO 씬에서는 한 번도 걸린 적이 없고,
# Mixamo 리그와 ZEPETO 몸이 한 씬에 같이 있으면 오히려 Mixamo 쪽을 골랐다.
RIG_NAME = "hips"

# Unity의 Humanoid 아바타가 실제로 매핑하는 뼈 55개. ZepetoBaseModel.fbx.meta의 humanDescription에서
# 그대로 읽어 왔다. Humanoid AnimationClip은 이 뼈들만 저장하고, 나머지 뼈에 넣은 회전은 임포트할 때
# 조용히 버려진다. 그 나머지가 리그 103개 중 49개다 - 모든 *Twist*, 모든 *_scale*, pelvis, heel,
# 그리고 얼굴 대부분. 그래서 초보자가 엉뚱한 뼈를 고르면 작업이 Unity에서 에러 하나 없이 사라진다.
#
# "hips"에 대한 주의: FBX 안에서 이것은 아마추어의 루트이고, Blender는 루트를 뼈가 아니라 아마추어
# 오브젝트로 만든다. 그래서 Humanoid의 Hips는 여기서 뼈로는 아예 포즈를 잡을 수 없고, 대신 오브젝트를
# 움직이면 아바타가 화면 밖으로 걸어나간다. 목록에는 셈을 맞추려고 넣어 뒀고 is_mapped_bone은 절대
# 이 이름을 보지 못한다.
MAPPED_BONES = frozenset((
    "hips", "spine", "chest", "chestUpper", "neck", "head",
    "shoulder_L", "upperArm_L", "lowerArm_L", "hand_L",
    "shoulder_R", "upperArm_R", "lowerArm_R", "hand_R",
    "upperLeg_L", "lowerLeg_L", "foot_L", "toes_L",
    "upperReg_R", "lowerReg_R", "foot_R", "toes_R",
    "eye_L", "eye_R", "mouth",
    "thumbPro_L", "thumbInt_L", "thumbDis_L",
    "indexPro_L", "indexInt_L", "indexDis_L",
    "middlePro_L", "middleInt_L", "middleDis_L",
    "ringPro_L", "ringInt_L", "ringDis_L",
    "littlePro_L", "littleInt_L", "littleDis_L",
    "thumbPro_R", "thumbInt_R", "thumbDis_R",
    "indexPro_R", "indexInt_R", "indexDis_R",
    "middlePro_R", "middleInt_R", "middleDis_R",
    "ringPro_R", "ringInt_R", "ringDis_R",
    "littlePro_R", "littleInt_R", "littleDis_R",
))


# ---------------------------------------------------------------- Unity 프로젝트 위치 찾기
#
# 배포된 폴더 모양. 이 그림 하나가 아래 탐색 규칙 전부의 이유다.
#
#   Desktop/zepeto/
#     ├─ BlenderMotion/                              ← .blend과 이 파일이 있는 곳 (= 탐색 시작점)
#     │    ├─ zepeto_motion.blend
#     │    └─ zepeto_motion_helper.py
#     └─ ZEPETO Studio Unity Project File 3.2.16/    ← 찾으려는 프로젝트 (= 시작점의 형제)
#          └─ Assets/
#               ├─ CustomMotions/                    ← 내보내기 목적지, Unity가 감시하는 폴더
#               └─ ZepetoHelper/Rig/ZepetoBaseModel.fbx
#
# 프로젝트는 시작점의 위가 아니라 '옆'에 있다. 그래서 위로 올라가면서 각 층의 바로 아래 자식들까지
# 같이 본다 (_walk_up_for_project).
#
# 이 애드온이 쓰는 경로 두 개는 모두 Unity 프로젝트 안에 있는데, 그 프로젝트 위치는 컴퓨터마다 다르다.
# 예전에는 이 파일 최상단에 상수로 박혀 있었고, 그래서 개발자 한 명의 C:\Users\<이름>\... 경로가
# 그대로 배포됐다. Blender는 프로퍼티의 default= 를 등록할 때 딱 한 번 평가하므로, 새 .blend나 새 씬은
# 존재하지도 않는 폴더를 물려받았고 "몸 불러오기"와 "Unity로 보내기"가 멀쩡히 설치된 컴퓨터에서 실패했다.
# 살아 있던 그 하나의 .blend만 동작했는데, 저장된 씬 프로퍼티에 우연히 진짜 값이 들어 있어서였다.
#
# 그래서 실행 시점에 찾는다. Unity 쪽이 이미 하는 것과 같은 방식이다 -
# Packages/com.easy.zepeto-helper/Editor/ZepetoStudioHelperWindow.GoToBlender.cs의 GuessBlendFilePath.
# 순수 표준 라이브러리만 쓰므로 Blender 버전에 전혀 기대지 않는다.
UNITY_PROJECT_ENV = "ZEPETO_UNITY_PROJECT"
UNITY_PROJECT_PREFIX = "zepeto studio unity project file"     # 소문자로 바꿔서 비교한다
PROJECT_WALK_LIMIT = 6
# 두 후보가 다른 근거로는 우열을 못 가릴 때, '더 최근에 만진 쪽'이라는 이유만으로 이기려면 Assets 폴더가
# 이만큼은 더 새로워야 한다(초). 실제로 작업 중인 프로젝트는 그날 안에 만져지고, 옆에 놓인 예전 다운로드는
# 그렇지 않다. 이 차이보다 가까우면 동점으로 보고, 동점은 사용자에게 알릴 뿐 절대 찍지 않는다 - _pick_project.
PROJECT_AGE_MARGIN = 24 * 60 * 60

# refresh_paths가 '무엇을 찾았나'가 아니라 '무엇을 썼나'를 담는 값.
#   project     해석된 프로젝트 폴더, 없으면 "" - 뭔가 고쳐졌다는 뜻이 절대 아니다
#   export_dir  scene.zepeto_export_dir에 실제로 써 넣은 값, 안 썼으면 ""
#   rig_fbx     scene.zepeto_rig_fbx에 실제로 써 넣은 값, 안 썼으면 ""
#   ambiguous   찾기는 했지만 고르기를 거부한 프로젝트들 (튜플, 보통 비어 있음)
#
# 네 자리가 전부 비어 보이는 결과가 서로 다른 두 가지 뜻을 가진다. 호출부는 반환값만 보고 판단하면 안 되고,
# ZEPETO_OT_locate_paths가 실제로 그렇게 한다 - 씬에서 '아직 비어 있는 것'을 직접 다시 계산한 다음에야
# fix.ambiguous / fix.project를 본다.
#
#   무엇을 찾았나            무엇을 썼나        사용자에게 뭐라고 하나
#   ---------------------  ----------------  -----------------------------------------------
#   씬이 None               아무것도 안 씀     (호출부 없음 - 방어용)
#   두 칸 다 이미 정상       아무것도 안 씀     "두 경로가 이미 올바릅니다. 바꾸지 않았습니다"
#   프로젝트 못 찾음         아무것도 안 씀     직접 지정하라고, 또는 환경 변수를 쓰라고
#   동점이라 고르기 거부      아무것도 안 씀     동점인 폴더들을 나열하고 사용자가 고르게
#   프로젝트는 찾았는데      아무것도 안 씀     "프로젝트는 찾았지만 그 안에 필요한 폴더가 없습니다"
#     하위 폴더가 없음
#   정상                    한 칸 또는 두 칸    무엇을 어디로 바꿨는지
PathFix = namedtuple("PathFix", "project export_dir rig_fbx ambiguous")


def _looks_like_unity_project(path):
    """ZEPETO Studio 다운로드다운 이름이면서 Assets 폴더까지 실제로 들고 있는 폴더인가."""
    if not path or not os.path.isdir(path):
        return False
    name = os.path.basename(os.path.normpath(path)).lower()
    return name.startswith(UNITY_PROJECT_PREFIX) and os.path.isdir(os.path.join(path, "Assets"))


def _anchor_dirs():
    """
    사용자가 어느 프로젝트 안에 있는지 증명해 주는 폴더들: 열려 있는 .blend, 그리고 이 애드온 파일.

    resolve_unity_project의 '시작점' 목록과 일부러 분리해 둔다. 시작점은 거기서부터 찾아 나가는 자리이고,
    앵커는 어떤 후보가 맞는지에 대한 증거다. 둘을 합치면 근거 없는 후보가 근거로 승격된다.
    """
    out = []
    if bpy.data.filepath:
        out.append(os.path.dirname(os.path.abspath(bpy.data.filepath)))
    # globals().get을 쓰는 이유: 설치된 애드온이 아니라 Blender의 Text 편집기에서 그냥 실행하면 __file__이
    # 없을 수 있고, 여기서 NameError가 나면 이 파일이 통째로 피하려는 그 날것의 traceback이 그대로 뜬다.
    own_file = globals().get("__file__", "")
    if own_file:
        out.append(os.path.dirname(os.path.abspath(own_file)))
    return out


def _is_inside(parent, child):
    """child가 parent 자신이거나 그 아래인가. 순수 경로 계산이라 사라진 폴더는 그냥 False가 된다."""
    try:
        parent = os.path.normcase(os.path.abspath(parent))
        child = os.path.normcase(os.path.abspath(child))
    except (OSError, ValueError):
        return False
    return child == parent or child.startswith(parent + os.sep)


def _rank_project(path):
    """
    path가 사용자가 말하는 그 프로젝트일 확신의 정도. 높은 쪽이 이기고, 같으면 동점이다.

      2  열려 있는 .blend나 이 애드온 파일을 품고 있다 - 지금 실행 중인 파일을 들고 있는 것보다
         강한 근거는 없다
      1  이 파이프라인이 쓰는 폴더 둘을 모두 갖고 있다 (Assets/CustomMotions와 Assets/ZepetoHelper/Rig).
         즉 헬퍼를 실제로 써 본 프로젝트라는 뜻이고, 그냥 한 번 더 받아 둔 사본과 구별된다
      0  Unity 프로젝트처럼 생겼을 뿐이다
    """
    for anchor in _anchor_dirs():
        if _is_inside(path, anchor):
            return 2
    # '둘 중 하나'가 아니라 '둘 다'인 이유: Assets/CustomMotions는 라이브 미리보기 감시 루트이고
    # Assets/ZepetoHelper/Rig는 Unity 쪽이 리그를 써 넣는 곳이다. 일부러 다른 폴더다.
    if (os.path.isdir(os.path.join(path, "Assets", "CustomMotions"))
            and os.path.isdir(os.path.join(path, "Assets", "ZepetoHelper", "Rig"))):
        return 1
    return 0


def _assets_mtime(path):
    """이 프로젝트의 Assets 폴더를 마지막으로 만진 시각. 못 읽으면 0.0이라 동점 판정에서 절대 못 이긴다."""
    try:
        return os.path.getmtime(os.path.join(path, "Assets"))
    except OSError:
        return 0.0


def _pick_project(candidates):
    """
    나란히 놓인 프로젝트들 중에서 고르거나, 고르기를 거부한다. (project, ambiguous)를 돌려준다.

    둘 중 정확히 한쪽만 채워진다: 폴더 하나와 (), 아니면 ""과 동점인 후보들.

    예전에는 "os.listdir가 먼저 정렬해 준 것"이었고, 그래서 오래된 "... 3.2.12" 다운로드나 "(1)"이 붙은
    사본이 진짜 프로젝트를 조용히 이길 수 있었다. 그러면 내보내기가 Unity가 감시하지 않는 프로젝트에
    떨어지고, 사용자는 '성공했다는데 아무 데도 안 나타나는' 상태를 본다 - 여기서 가장 진단하기 어려운 실패다.
    그래서 근거로 등수를 매기고, 근거가 동점이면 찍는 대신 동점이라고 말한다.
    """
    unique = []
    for path in candidates:
        norm = os.path.normpath(path)
        if norm not in unique:
            unique.append(norm)
    if not unique:
        return "", ()
    if len(unique) == 1:
        return unique[0], ()

    ranked = [(_rank_project(p), p) for p in unique]
    best = max(rank for rank, _ in ranked)
    top = [p for rank, p in ranked if rank == best]
    if len(top) == 1:
        return top[0], ()

    # 마지막 수단이고, 아슬아슬한 차이가 아닐 때만 쓴다 (PROJECT_AGE_MARGIN).
    top.sort(key=_assets_mtime, reverse=True)
    if _assets_mtime(top[0]) - _assets_mtime(top[1]) >= PROJECT_AGE_MARGIN:
        return top[0], ()
    return "", tuple(sorted(top))


def _walk_up_for_project(start_dir):
    """
    start_dir를 보고, 그 다음 모든 조상을 보고, 각 조상의 '바로 아래 자식'들까지 같이 본다.

    자식까지 보는 것이 배포된 폴더 모양(이 파일 위쪽 그림)을 성립시킨다. .blend은 프로젝트 '안'이 아니라
    프로젝트 '옆'의 BlenderMotion 폴더에 있다. 올라가는 횟수에 상한을 둔 이유는 버튼 한 번이 드라이브
    전체를 훑는 일로 변하지 않게 하기 위해서다.

    _pick_project와 같은 (project, ambiguous)를 돌려준다. 후보가 하나라도 있는 가장 가까운 층에서 결론이
    난다: 거리가 다른 모든 신호를 이기고, 거기서 멈추는 덕분에 동점 집합이 '실제로 서로 옆에 놓인 폴더'들로만
    좁혀진다.
    """
    current = start_dir
    for _ in range(PROJECT_WALK_LIMIT + 1):
        if not current or not os.path.isdir(current):
            return "", ()
        if _looks_like_unity_project(current):
            # start_dir가 이 폴더이거나 그 아래다. 즉 우리를 품고 있으므로 정의상 모호할 수 없다.
            return os.path.normpath(current), ()
        try:
            entries = sorted(os.listdir(current))
        except OSError:
            entries = []
        matches = [os.path.join(current, entry) for entry in entries
                   if _looks_like_unity_project(os.path.join(current, entry))]
        if matches:
            return _pick_project(matches)
        parent = os.path.dirname(current)
        if parent == current:                                  # 드라이브 루트까지 왔다
            return "", ()
        current = parent
    return "", ()


def resolve_unity_project():
    """
    이 컴퓨터의 Unity 프로젝트 폴더, 그리고 우리가 해결하기를 거부한 모호함.

    (project, ambiguous)를 돌려준다: 폴더 하나와 (), 아니면 ""과 동점이던 후보 프로젝트들.

    순서: 환경 변수가 먼저다(사용자가 언제든 답을 강제할 수 있어야 한다). 그 다음 열려 있는 .blend의 폴더,
    마지막으로 이 애드온 파일 자신의 폴더 - .blend을 한 번도 저장하지 않았어도 이건 해석된다.
    """
    forced = os.environ.get(UNITY_PROJECT_ENV, "").strip().strip('"')
    if forced and os.path.isdir(os.path.join(forced, "Assets")):
        return os.path.normpath(forced), ()

    starts = []
    if bpy.data.filepath:
        starts.append(os.path.dirname(bpy.data.filepath))
    own_file = globals().get("__file__", "")
    if own_file:
        starts.append(os.path.dirname(os.path.abspath(own_file)))
    ambiguous = ()
    for start in starts:
        found, tied = _walk_up_for_project(start)
        if found:
            return found, ()
        # 첫 동점은 기억해 두되 멈추지는 않는다. 애드온 자신의 폴더에서 깨끗한 답이 나올 수도 있다.
        if tied and not ambiguous:
            ambiguous = tied
    return "", ambiguous


def refresh_paths(scene, force=False):
    """
    resolve_unity_project()로 씬의 경로 프로퍼티 두 개를 채운다. PathFix를 돌려준다.

    반환값은 '무엇을 찾았나'가 아니라 '무엇을 썼나'다(위 PathFix의 표 참고). 호출부는 반드시 그쪽으로
    보고해야 한다. 예전에는 하위 폴더가 둘 다 없는 프로젝트 폴더를 찾은 것이 '수리 성공'으로 사용자에게
    발표됐고, UI에 하나뿐인 수리 버튼이 여전히 망가진 상태를 고쳤다고 주장했다.

    오퍼레이터에서만 부르고 draw()에서는 절대 부르지 않는다: 패널을 그리는 중에 ID 데이터에 쓰는 것은
    금지돼 있고, 디렉터리 탐색이 매 리드로마다 돌 이유도 없다. 덕분에 .blend을 다른 위치에 저장해도
    다음 버튼 한 번이면 경로가 고쳐지고, 코드를 손댈 일이 없다.

    이미 존재하는 값은 force가 아니면 건드리지 않는다. 사용자가 손으로 고른 폴더를 덮어쓰지 않기 위해서다.
    아무것도 해석되지 않으면 프로퍼티를 일부러 '빈 채로' 둔다: 빈 칸은 사용자에게 폴더를 물어보지만,
    틀린 경로는 나중에 실패해서 도구 자체가 고장 난 것처럼 보이게 만든다.
    """
    if scene is None:
        return PathFix("", "", "", ())
    need_dir = force or bool(export_dir_problem(scene))
    need_rig = force or bool(rig_fbx_problem(scene))
    if not need_dir and not need_rig:
        return PathFix("", "", "", ())

    project, ambiguous = resolve_unity_project()
    if not project:
        return PathFix("", "", "", ambiguous)
    set_dir = ""
    set_rig = ""
    if need_dir:
        # Assets/CustomMotions는 Unity가 라이브 미리보기용으로 감시하는 폴더다. Unity 쪽이 소유한
        # ZepetoHelper 폴더 두 개와 절대 섞지 않는다.
        candidate = os.path.join(project, "Assets", "CustomMotions")
        if os.path.isdir(candidate):
            scene.zepeto_export_dir = candidate
            set_dir = candidate
    if need_rig:
        candidate = os.path.join(project, "Assets", "ZepetoHelper", "Rig", "ZepetoBaseModel.fbx")
        if os.path.isfile(candidate):
            scene.zepeto_rig_fbx = candidate
            set_rig = candidate
    return PathFix(project, set_dir, set_rig, ambiguous)


def export_dir_problem(scene):
    """패널과 내보내기 오퍼레이터가 '쓸 수 있는 저장 폴더'의 정의에서 어긋날 수 없도록, 둘 다 여기에 묻는다."""
    folder = bpy.path.abspath(scene.zepeto_export_dir)
    if not folder:
        return "저장 폴더가 비어 있습니다. 패널의 '저장 폴더'를 직접 지정하세요 (Unity 프로젝트의 Assets/CustomMotions)"
    if not os.path.isdir(folder):
        return "저장 폴더가 없습니다: %s - 패널의 '저장 폴더'를 직접 지정하세요" % folder
    return None


def rig_fbx_problem(scene):
    """
    리그 FBX 쪽의 export_dir_problem. 같은 판정이 네 군데에서 필요하다:
    refresh_paths(수리할지 결정), import_rig(거절), locate_paths(무엇이 아직 비었는지), 패널(안내 문구).

    저장 폴더 쪽은 진작에 공유 술어를 갖고 있었는데 리그 쪽은 같은 조건이 네 번 따로 적혀 있었다.
    한 군데만 고치면 나머지 세 곳이 조용히 다른 말을 하게 된다.
    """
    path = bpy.path.abspath(scene.zepeto_rig_fbx)
    if not path or not os.path.isfile(path):
        return ("리그 FBX를 찾을 수 없습니다. Unity 헬퍼에서 'ZEPETO 리그 내보내기'를 먼저 누르세요. "
                "이미 내보냈다면 아래 'ZEPETO FBX' 칸에 그 파일을 직접 지정하세요")
    return None


# ---------------------------------------------------------------- 보조 함수
def get_rig(context):
    """아마추어면 무엇이든 된다: 연습용 리그도, Unity에서 내보낸 진짜 ZEPETO 모델도."""
    obj = getattr(context, "object", None)
    if obj and obj.type == "ARMATURE":
        return obj
    named = bpy.data.objects.get(RIG_NAME)
    # 이름이 같아도 아마추어가 아니면 안 된다. 예전에는 타입 검사가 없어서 같은 이름의 메시가 리그 자리를
    # 차지할 수 있었다. 여기서 못 찾으면 아래 '아무 아마추어나' 규칙이 받아 준다.
    if named is not None and named.type == "ARMATURE":
        return named
    for candidate in bpy.data.objects:
        if candidate.type == "ARMATURE":
            return candidate
    return None


def is_mapped_bone(name):
    return name in MAPPED_BONES


def rig_action(rig):
    """이 리그가 지금 들고 있는 액션. 애니메이션 데이터 자체가 없으면 None."""
    return rig.animation_data.action if rig and rig.animation_data else None


def apply_bone_visibility(rig, show_all):
    """
    Unity가 버리는 뼈를 숨겨서, 클릭이 되는 뼈에만 닿게 만든다.

    몇 개를 숨겼는지 돌려준다. 나중에 경고하는 대신 아예 숨기는 것은 의도한 설계다. 이 실패는 눈에 보이지
    않기 때문이다 - 에러도 경고도 없이 Unity에서 그 관절만 안 움직인다. 그러니 실수를 '닿을 수 없게'
    만드는 것이 유일하게 믿을 수 있는 대책이다.
    """
    if rig is None:
        return 0
    hidden = 0
    for bone in rig.data.bones:
        dead = not is_mapped_bone(bone.name)
        bone.hide = dead and not show_all
        if bone.hide:
            hidden += 1
    return hidden


def _on_show_all_bones_changed(self, context):
    apply_bone_visibility(get_rig(context), self.zepeto_show_all_bones)


def _has_quat_curves(rig, name):
    prefix = 'pose.bones["%s"].rotation_quaternion' % name
    return any((fc.data_path or "").startswith(prefix) for fc in iter_fcurves(rig_action(rig)))


def ensure_euler(rig):
    """
    이 도구가 키를 찍는 뼈는 XYZ 오일러여야 한다. 여기 있는 모든 오퍼레이터가 rotation_euler에 쓰기 때문이다.

    예전에는 불러오기 버튼만 모드를 바꿨다. append하거나 link해 온 리그는 QUATERNION인 채로 남고, Blender는
    오일러 값을 저장하고 오일러 F-커브까지 만들어 주지만 평가할 때는 그 값을 완전히 무시한다 - 그런데
    패널의 체크리스트는 그 죽은 커브를 세고 "보낼 준비 완료"라고 말한다. 결과는 Unity에서 아무 데도 메시지가
    없는, 움직이지 않는 클립이다.

    이미 쿼터니언 F-커브를 갖고 있는 뼈는 건드리지 않고 이름만 보고한다. 그 뼈의 모드를 바꾸면 그 커브들이
    고아가 되면서 진짜 애니메이션이 소리 없이 파괴되는데, 그건 원래 버그보다 나쁘다.
    """
    blocked = []
    if rig is None:
        return blocked
    for pb in rig.pose.bones:
        if pb.rotation_mode == "XYZ":
            continue
        if _has_quat_curves(rig, pb.name):
            blocked.append(pb.name)
            continue
        pb.rotation_mode = "XYZ"
    return blocked


def mapped_coverage(rig):
    """이 리그가 실제로 가진 Humanoid 매핑 이름의 개수. 천장은 54다 - 'hips'는 뼈가 아니라 아마추어 오브젝트."""
    if rig is None:
        return 0
    return len(MAPPED_BONES & {b.name for b in rig.data.bones})


# Windows가 대놓고 거부하는 글자들이고, bpy.ops.export_scene.fbx는 그걸 읽을 수 있는 메시지가 아니라 날것의
# traceback으로 뱉는다. 그래서 버튼이 살아나기 전에 이름부터 검사한다.
BAD_NAME_CHARS = r'\/:*?"<>|'

# Windows의 예약 장치 이름. 확장자를 붙이든 안 붙이든(CON, CON.fbx) 파일 이름으로 쓸 수 없고, 위 글자
# 검사만으로는 전부 통과해서 exporter까지 도달한다 - BAD_NAME_CHARS가 막으려던 바로 그 traceback으로.
RESERVED_WINDOWS_NAMES = frozenset(
    ["CON", "PRN", "AUX", "NUL"]
    + ["COM%d" % i for i in range(1, 10)]
    + ["LPT%d" % i for i in range(1, 10)])


def motion_name_problem(raw):
    name = (raw or "").strip()
    if not name:
        return "이름 칸이 비어 있습니다"
    bad = sorted({c for c in name if c in BAD_NAME_CHARS})
    if bad:
        return "이름에 쓸 수 없는 문자가 있습니다: %s" % " ".join(bad)
    if name.rstrip(". ") != name:
        return "이름이 마침표나 공백으로 끝날 수 없습니다"
    if name.split(".")[0].strip().upper() in RESERVED_WINDOWS_NAMES:
        return "이 이름은 Windows에서 파일 이름으로 쓸 수 없습니다: %s" % name
    return None


def keyed_dead_bones(rig):
    """사용자가 숨긴 뼈를 다시 꺼내 놓았을 경우를 대비해, 이미 찍힌 키프레임에 대해 매핑 여부를 검사한다."""
    out = set()
    for fc in iter_fcurves(rig_action(rig)):
        path = fc.data_path or ""
        if not path.startswith('pose.bones["'):
            continue
        name = path.split('"')[1]
        if not is_mapped_bone(name):
            out.add(name)
    return sorted(out)


def iter_fcurves(action):
    """
    액션의 F-커브 전부. Blender 4.4+가 커브를 액션 레이어/슬롯 아래로 옮긴 것을 함께 지원한다.

    갈림길의 조건이 '존재'가 아니라 '비었는지'인 것이 핵심이다. Action.fcurves는 지원 범위 전체(4.2 ~
    실측 5.2.0 LTS)에 계속 존재하므로 hasattr로 재면 아래 레이어 탐색은 영원히 실행되지 않는다 - 실제로
    5.2.0 LTS 헤드리스 실행에서 이 위쪽 경로로만 fcurve 162개를 봤다. 그런데 4.4+에서 Action.fcurves는
    '첫 번째 슬롯만' 돌려주는 하위호환 접근자라, 커브가 첫 슬롯에 없는 액션에서는 빈 목록이 나온다.
    비었을 때만 아래로 내려가면 5.2는 실측된 빠른 경로에 그대로 있고, 이 분기는 자기가 존재하는 이유가 되는
    경우에만 도달한다. (슬롯이 여러 개고 첫 슬롯에도 커브가 있는 액션은 여전히 짧은 목록이 나온다.
    지금 파이프라인은 슬롯을 하나만 만들므로 실측되지 않은 경우이고, 매 리드로마다 레이어를 걷는 비용을
    치를 근거가 아직 없다.)
    """
    if action is None:
        return []
    curves = list(getattr(action, "fcurves", None) or [])
    if curves:
        return curves
    for layer in getattr(action, "layers", []):
        for strip in layer.strips:
            for bag in getattr(strip, "channelbags", []):
                curves.extend(bag.fcurves)
    return curves


def curve_varies(fc):
    """
    이 F-커브가 실제로 값을 바꾸는가. 키가 두 개라도 값이 같으면 정지 화면이다.

    make_live_fixtures.py가 '이 클립이 정말 움직이는가'를 증명하려고 쓰던 판정을 애드온으로 올린 것이다.
    거기서는 리포트에 찍기만 했지만, 내보내기 게이트가 진짜로 필요로 하던 술어가 이것이다.
    """
    values = [kp.co[1] for kp in fc.keyframe_points]
    return bool(values) and (max(values) - min(values)) > 1e-4


def has_moving_rotation(rig):
    """회전 커브 중에 값이 실제로 변하는 것이 하나라도 있는가."""
    for fc in iter_fcurves(rig_action(rig)):
        if "rotation" in (fc.data_path or "") and curve_varies(fc):
            return True
    return False


def frame_character(context, rig=None):
    """
    뷰포트를 캐릭터에 맞춘다: 정면, 직교, 화면에 꽉 차게. 성공하면 True.

    view3d.view_all / view_selected를 부르지 않고 region_3d에 직접 쓴다. 그 오퍼레이터들은 모드와 선택에
    따라 다르게 동작한다 - Pose 모드의 view_selected는 선택된 뼈만 잡아서, 아무것도 선택 안 된 상태면
    10cm짜리 확대가 되고, view_all은 기본 큐브·카메라·라이트까지 끌어들인다. 범위를 직접 계산하는 이 방식만이
    매번 같은 자리에 떨어진다.
    """
    try:
        # 캐릭터 자신의 메시만 본다. 씬의 모든 메시를 넣으면 원점에 있는 Blender 기본 큐브가 범위에 끌려
        # 들어오고, 불러온 리그는 약 53m 떨어져 있어서 시야가 그 둘 사이의 빈 공간에 맞춰진다.
        rig = rig or get_rig(context)
        meshes = [o for o in bpy.data.objects
                  if o.type == "MESH" and (rig is None or o.parent == rig or o == rig)]
        if not meshes:
            meshes = [o for o in bpy.data.objects if o.type == "MESH"]

        lo = Vector((1e9, 1e9, 1e9))
        hi = Vector((-1e9, -1e9, -1e9))
        found = False
        for obj in meshes:
            for corner in obj.bound_box:
                p = obj.matrix_world @ Vector(corner)
                lo = Vector((min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z)))
                hi = Vector((max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z)))
                found = True
        if not found:
            return False

        center = (lo + hi) * 0.5
        height = max((hi - lo).z, (hi - lo).x, 0.1)

        for window in context.window_manager.windows:
            for area in window.screen.areas:
                if area.type != "VIEW_3D":
                    continue
                space = area.spaces.active
                space.show_region_ui = True
                space.shading.type = "SOLID"
                space.clip_start = 0.01
                space.clip_end = max(1000.0, center.length * 4.0)

                r3d = space.region_3d
                r3d.view_perspective = "ORTHO"
                # 정면 뷰: 기본 탑뷰를 X축으로 90도 돌린 것.
                r3d.view_rotation = Euler((1.5707963, 0.0, 0.0), "XYZ").to_quaternion()
                r3d.view_location = center
                r3d.view_distance = height * 2.6
                area.tag_redraw()

        frame_timeline(context)
        return True
    except Exception:
        # 뷰포트 조정 실패가 성공한 불러오기를 취소시켜서는 안 된다. 대신 False가 호출부까지 올라가고,
        # import_rig가 리포트 끝에 Home 키 안내를 붙인다.
        return False


def frame_timeline(context):
    """
    타임라인을 모션이 실제로 차지하는 2초에 맞춘다.

    Blender 기본 뷰는 250프레임 너머까지 보여 주므로, 초보자는 자기 48프레임이 스트립 왼쪽 5분의 1에
    뭉개진 것을 보고 키프레임을 하나하나 구별하지 못한다.
    """
    for window in context.window_manager.windows:
        for area in window.screen.areas:
            if area.type not in {"DOPESHEET_EDITOR", "TIMELINE"}:
                continue
            region = next((r for r in area.regions if r.type == "WINDOW"), None)
            if region is None:
                continue
            try:
                with context.temp_override(window=window, area=area, region=region):
                    bpy.ops.action.view_all()
            except Exception:
                # DOPESHEET/TIMELINE 영역이 없으면 temp_override가 정당하게 실패한다. headless_check.py와
                # make_live_fixtures.py가 도는 --background가 정확히 그 상황이라, 이 가드를 빼면 둘 다 깨진다.
                pass
            area.tag_redraw()


def odd_bones(rig):
    """포즈 변환이 항등이 아닌 뼈들. 회전은 뺀다(회전이 이 도구의 목적이니까)."""
    out = set()
    if rig is None:
        return out
    for pb in rig.pose.bones:
        if pb.location.length > 0.01 or (pb.scale - Vector((1, 1, 1))).length > 0.05:
            out.add(pb.name)
    return out


def baseline_odd(scene):
    """
    import_rig가 찍어 둔 '불러온 직후 이미 어긋나 있던 뼈' 스냅샷.

    clear_pose와 패널이 같은 뼈 집합을 놓고 이야기하게 만드는 것이 이 스냅샷의 존재 이유인데, 정작 그것을
    읽어 내는 코드가 두 군데에 글자 그대로 복사돼 있었다. 데이터는 공유하면서 파싱은 공유하지 않으면
    한쪽만 고쳤을 때 둘이 조용히 어긋난다.
    """
    return set(filter(None, (scene.zepeto_baseline_odd or "").split(",")))


def object_baseline(scene):
    """import_rig가 찍어 둔 리그 '오브젝트'의 위치/크기 스냅샷. 없거나 깨졌으면 None."""
    parts = (scene.zepeto_baseline_object or "").split(",")
    if len(parts) != 6:
        return None
    try:
        nums = [float(v) for v in parts]
    except ValueError:
        return None
    return Vector(nums[:3]), Vector(nums[3:])


def object_moved(scene, rig):
    """
    아마추어 '오브젝트' 자체가 움직였는가. odd_bones는 pose bone만 보므로 이건 못 본다.

    그런데 이 파이프라인에서 눈에 보이는 실패 1위가 바로 이것이다 - 리그 오브젝트를 G로 끌면 아바타가
    화면 밖으로 걸어나간다(README_모션만들기.md의 '막히면' 표). 임계값은 odd_bones와 같은 0.01 / 0.05다.

    스냅샷이 없으면(이 기능 이전에 저장된 씬, 1단계를 안 거친 리그) 판단하지 않고 False를 돌려준다.
    원래 위치를 모르는 채로 경고하면 아무 잘못도 안 한 사용자를 잡게 된다.
    """
    base = object_baseline(scene)
    if base is None or rig is None:
        return False
    loc, scale = base
    return (Vector(rig.location) - loc).length > 0.01 or (Vector(rig.scale) - scale).length > 0.05


def keyframe_times(rig):
    times = set()
    for fc in iter_fcurves(rig_action(rig)):
        for kp in fc.keyframe_points:
            times.add(round(kp.co[0]))
    return sorted(times)


def export_problems(scene, rig):
    """
    내보내기를 막는 이유 전부를, 사용자가 읽을 순서 그대로. 비어 있으면 내보낼 수 있다.

    패널은 이 목록을 그대로 찍고 버튼을 회색으로 만들며, ZEPETO_OT_export는 같은 목록으로 거절한다.
    1.4.0까지는 다섯 개 중 세 개(fps, 키프레임 2개 이상, 이상한 뼈)가 패널 draw 코드에만 있었고, 그래서
    패널을 거치지 않는 호출은 전부 무검사로 나갔다. 그런 경로는 가정이 아니라 실재한다 - 이 오퍼레이터에는
    poll()이 없어서 F3 검색으로 바로 실행되고, make_live_fixtures.py는 bpy.ops.zepeto.export()를 직접 부른다.

    export_dir_problem과 motion_name_problem은 여기 안에서 다시 불린다. 그 중복은 의도된 것이다 -
    패널은 버튼을 잠그고 오퍼레이터는 그래도 한 번 더 거절한다.
    """
    problems = []
    if rig is None:
        problems.append("리그가 없습니다")
        return problems

    # 실효 프레임레이트는 fps가 아니라 fps / fps_base다. Blender 기본 프리셋 '24 fps NTSC'는
    # 24 / 1.001 = 23.976이라, fps만 보면 게이트를 통과하고 미세하게 어긋난 길이의 클립이 나간다.
    effective = scene.render.fps / max(scene.render.fps_base, 1e-6)
    if abs(effective - FPS) > 1e-3:
        shown = ("%.3f" % effective).rstrip("0").rstrip(".")
        problems.append("fps가 %s입니다. 24로 바꾸세요 (Output 속성의 Frame Rate)" % shown)

    # 키가 두 개라도 '값이 같은 두 개'면 Unity에서는 그냥 정지 포즈다. keyframe_times는 서로 다른 시각을
    # 셀 뿐 서로 다른 포즈를 세지 않으므로, 실제로 값이 변하는 회전 커브가 있는지 따로 본다.
    times = keyframe_times(rig)
    if len(times) < 2:
        problems.append("키프레임이 %d개입니다. 2개 이상 필요합니다" % len(times))
    elif not has_moving_rotation(rig):
        problems.append("키프레임은 있지만 포즈가 전부 같습니다. 다른 프레임에서 뼈를 돌리고 다시 저장하세요")

    if object_moved(scene, rig):
        problems.append("리그 오브젝트가 움직였습니다 - Ctrl+Z 하고 뼈만 돌리세요 "
                        "('포즈 전부 되돌리기'로도 되돌아갑니다)")

    # Humanoid 리타게팅은 회전만 옮기므로 뼈를 옮기거나 키우는 것은 헛수고다. 사용자가 불러온 뒤에 바꾼
    # 것만 지적한다(ZEPETO 몸은 단위가 아닌 포즈 스케일 몇 개를 원래 갖고 있다).
    changed = sorted(odd_bones(rig) - baseline_odd(scene))
    if changed:
        problems.append("이동/크기가 바뀐 뼈: %s (회전만 쓰세요)" % ", ".join(changed[:3]))

    # 사용자가 죽은 뼈를 다시 꺼내 놓고 키까지 찍었을 때만 도달한다. Unity는 이 커브들을 한마디 말도 없이
    # 버리므로, 여기서 잡는 것이 무언가를 말할 수 있는 마지막 기회다.
    dead = keyed_dead_bones(rig)
    if dead:
        problems.append("Unity가 무시하는 뼈에 키가 있습니다: %s (지우거나 매핑된 뼈로 다시 만드세요)"
                        % ", ".join(dead[:3]))

    name_problem = motion_name_problem(scene.zepeto_motion_name)
    if name_problem:
        problems.append(name_problem)
    folder_problem = export_dir_problem(scene)
    if folder_problem:
        problems.append(folder_problem)
    return problems


# ---------------------------------------------------------------- 오퍼레이터
class ZEPETO_OT_import_rig(bpy.types.Operator):
    bl_idname = "zepeto.import_rig"
    bl_label = "ZEPETO 리그 불러오기"
    bl_description = "Unity에서 내보낸 ZEPETO 실제 모델 FBX를 불러옵니다. 체형이 정확히 맞습니다"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        # 판단하기 전에 먼저 해석한다. 저장된 경로가 비어 있을 수도 있고(새 씬), .blend과 함께 다른
        # 컴퓨터에서 건너왔을 수도 있다. refresh_paths는 비어 있거나 깨진 것만 덮어쓴다.
        refresh_paths(context.scene)
        problem = rig_fbx_problem(context.scene)
        if problem:
            self.report({"ERROR"}, problem)
            return {"CANCELLED"}
        path = bpy.path.abspath(context.scene.zepeto_rig_fbx)

        active = getattr(context, "object", None)
        if active and active.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")

        before = set(bpy.data.objects.keys())
        bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False)
        added = [bpy.data.objects[n] for n in bpy.data.objects.keys() if n not in before]

        rig = next((o for o in added if o.type == "ARMATURE"), None)
        if rig is None:
            self.report({"ERROR"}, "FBX 안에서 뼈대를 찾지 못했습니다")
            return {"CANCELLED"}

        scene = context.scene
        # fps_base까지 맞추는 이유는 실효 프레임레이트가 fps / fps_base이기 때문이다. 여기서 1.0으로
        # 못 박아 두면 '24 fps NTSC'(23.976) 프리셋을 물려받은 씬이 내보내기 게이트에서 막히지 않는다.
        scene.render.fps = FPS
        scene.render.fps_base = 1.0
        scene.frame_start = 1
        scene.frame_current = 1

        # 리그 fbx에는 애니메이션이 없어서 Blender는 기본 250프레임 타임라인을 그대로 둔다. 그냥 두면
        # "반복되게 만들기"가 250프레임에 루프 키를 찍어 10초짜리 클립을 만든다. 파일이 실제로 더 긴 액션을
        # 갖고 온 것이 아니라면 2초 작업 길이로 되돌린다.
        imported_end = 0
        for obj in added:
            if obj.animation_data and obj.animation_data.action:
                try:
                    imported_end = max(imported_end, int(obj.animation_data.action.frame_range[1]))
                except (AttributeError, TypeError):
                    pass
        scene.frame_end = imported_end if imported_end > 1 else DEFAULT_END

        rig.show_in_front = True                 # 뼈가 몸 위에 겹쳐 그려지게
        rig.data.display_type = "OCTAHEDRAL"
        for obj in added:
            if obj.type == "MESH":
                obj.hide_select = True           # 클릭이 몸이 아니라 항상 뼈에 떨어지게

        context.view_layer.objects.active = rig
        rig.select_set(True)
        bpy.ops.object.mode_set(mode="POSE")
        for pb in rig.pose.bones:
            pb.rotation_mode = "XYZ"

        # 불러온 직후에 이미 rest에서 벗어나 있는 뼈를 기록해 둔다. ZEPETO 리그는 단위가 아닌 포즈 스케일을
        # 몇 개 갖고 출고되는데, 기준선이 없으면 체크리스트가 사용자에게 그걸 망가뜨렸다고 뒤집어씌운다.
        scene.zepeto_baseline_odd = ",".join(sorted(odd_bones(rig)))
        # 오브젝트 자체의 위치/크기도 같이 찍는다. odd_bones는 pose bone만 보기 때문에, 이 스냅샷이
        # 없으면 '리그를 통째로 끌고 갔다'는 이 파이프라인 최대 실패를 아무도 못 본다.
        scene.zepeto_baseline_object = ",".join(
            "%.6f" % v for v in tuple(rig.location) + tuple(rig.scale))

        hidden = apply_bone_visibility(rig, scene.zepeto_show_all_bones)
        framed = frame_character(context, rig)

        # len(bones) - hidden이 아니라 매핑에서 센다. '무시하는 뼈도 보기'가 켜져 있으면 hidden이 0이라
        # 그 뺄셈은 103개 전부 쓸 수 있다고 말했다 - 사용자가 죽은 뼈를 보겠다고 선택한 바로 그 순간에,
        # 진실과 정반대로.
        usable = mapped_coverage(rig)
        dead = len(rig.data.bones) - usable
        tail = (("Unity가 무시하는 %d개는 숨김" % dead) if hidden
                else ("Unity가 무시하는 %d개도 보이는 중 - 돌려도 Unity에서 사라집니다" % dead))
        message = ("ZEPETO 몸을 불러왔습니다. 쓸 수 있는 뼈 %d개 (%s). "
                   "뼈를 클릭하고 R을 누르세요" % (usable, tail))
        # frame_character는 VIEW_3D 영역이 없으면 실패한다. 예전에는 반환값을 버려서, 화면이 안 맞은 채로
        # 성공 메시지만 뜨고 이유를 알려 주는 곳이 아무 데도 없었다.
        if not framed:
            message += ". 화면 맞추기는 실패했습니다 - 3D 화면에 마우스를 두고 Home 키를 누르세요"
        self.report({"INFO"}, message)
        return {"FINISHED"}


class ZEPETO_OT_locate_paths(bpy.types.Operator):
    """
    두 경로 칸을 이 컴퓨터의 Unity 프로젝트로 다시 맞춘다.

    저장 폴더가 없으면 내보내기 버튼이 비활성이고, 그래서 내보내기 오퍼레이터 자신의 수리 코드는 실행될
    기회조차 없다. 이 버튼이 없던 시절의 유일한 해결책은 파일 브라우저나 Python 콘솔이었다.
    """
    bl_idname = "zepeto.locate_paths"
    bl_label = "Unity 프로젝트 경로 자동 찾기"
    bl_description = "이 컴퓨터의 Unity 프로젝트를 찾아 저장 폴더와 리그 FBX 경로를 다시 채웁니다"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        scene = context.scene
        # force=True: 이 버튼을 누르는 목적 자체가 지금 칸에 들어 있는 값을 갈아 끼우는 것이다.
        fix = refresh_paths(scene, force=True)

        # '프로젝트를 찾았다'가 아니라 '무엇을 써 넣었다'로 보고한다. 폴더를 찾은 것만으로 보고하던 시절에는
        # 두 칸이 여전히 빈 채로 버튼이 성공을 선언했고, 사용자는 "찾았습니다"를 읽었는데 내보내기 버튼은
        # 계속 회색이었으며 UI 어디에도 이유가 없었다.
        done = []
        if fix.export_dir:
            done.append("저장 폴더 → %s" % fix.export_dir)
        if fix.rig_fbx:
            done.append("리그 FBX → %s" % fix.rig_fbx)
        if done:
            self.report({"INFO"}, "경로를 다시 채웠습니다. " + " / ".join(done))
            return {"FINISHED"}

        # 아무것도 안 썼다. 아직 무엇이 비었는지, 사용자가 무엇을 해야 하는지 정확히 말한다.
        missing = []
        if export_dir_problem(scene):
            missing.append("저장 폴더")
        if rig_fbx_problem(scene):
            missing.append("리그 FBX")
        if not missing:
            # 진짜 no-op: force로 다시 썼는데도 바뀐 게 없다면 두 칸 모두 이미 실재하는 것을 가리키고 있다.
            self.report({"INFO"}, "두 경로가 이미 올바릅니다. 바꾸지 않았습니다")
            return {"FINISHED"}

        if fix.ambiguous:
            # 다운로드가 두 개 이상 나란히 있다. 여기서 찍는 것이 바로 '내보내기가 Unity가 안 보는
            # 프로젝트에 떨어지는' 사고라서, 고르는 것은 사용자 몫이다.
            self.report({"ERROR"},
                        "Unity 프로젝트로 보이는 폴더가 %d개라서 고르지 못했습니다 (%s). "
                        "패널의 '저장 폴더'에서 쓰려는 프로젝트의 Assets/CustomMotions 폴더를 직접 고르세요 "
                        "(또는 환경 변수 %s에 그 프로젝트 폴더를 넣으세요). 아직 비어 있는 것: %s"
                        % (len(fix.ambiguous), " / ".join(fix.ambiguous),
                           UNITY_PROJECT_ENV, ", ".join(missing)))
        elif fix.project:
            self.report({"ERROR"},
                        "Unity 프로젝트는 찾았지만(%s) 그 안에 필요한 폴더가 없어서 %s 경로를 채우지 못했습니다. "
                        "Unity 헬퍼에서 'ZEPETO 리그 내보내기'를 먼저 누르거나, 패널에서 직접 고르세요"
                        % (fix.project, ", ".join(missing)))
        else:
            self.report({"ERROR"},
                        "Unity 프로젝트를 찾지 못해 %s 경로가 그대로 비어 있습니다. 패널에서 직접 지정하세요 "
                        "(또는 환경 변수 %s에 프로젝트 폴더를 넣으세요)" % (", ".join(missing), UNITY_PROJECT_ENV))
        return {"CANCELLED"}


class ZEPETO_OT_clear_pose(bpy.types.Operator):
    bl_idname = "zepeto.clear_pose"
    bl_label = "포즈 초기화"
    # 문구는 execute()가 실제로 하는 일을 따라간다: 회전은 전부, 이동·크기는 패널이 지적한 곳만,
    # 그리고 리그 오브젝트는 불러올 때 찍어 둔 자리로. 몸이 원래 갖고 나온 오프셋은 건드리지 않는다.
    bl_description = ("모든 뼈의 회전을 되돌리고, 이동·크기가 바뀐 뼈와 움직인 리그 오브젝트만 "
                      "원래대로 돌립니다 (키프레임은 지우지 않습니다)")
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        rig = get_rig(context)
        if not rig:
            self.report({"ERROR"}, "리그가 없습니다")
            return {"CANCELLED"}
        ensure_euler(rig)

        # 회전만으로는 부족했다. G로 끌거나 S로 키운 뼈는 그대로 남았고, 패널은 계속 "이동/크기가 바뀐 뼈"에
        # 그 이름을 올렸으며, 애드온의 어떤 버튼도 그걸 치울 수 없었다.
        #
        # 회전은 '모든' 뼈에서 지운다 - 그게 "포즈 초기화"의 뜻이다. 이동과 크기는 패널이 실제로 지적한
        # 뼈에서만 건드린다(cleared: 지금 rest에서 벗어난 것에서 zepeto_baseline_odd 스냅샷을 뺀 것.
        # ZEPETO 몸이 단위가 아닌 포즈 스케일을 몇 개 갖고 출고되기 때문이다).
        #
        # 반대로 스냅샷 밖 전부를 되돌리게 했더니, 스냅샷이 없을 때마다(이 기능 이전에 저장된 씬, 1단계를
        # 안 거친 리그) 그 출고 오프셋과 스케일이 납작해졌고, 스냅샷이 있어도 odd_bones의 임계값보다 작은
        # 변형은 파괴됐다. 그 손상은 Unity에서 결과가 이상해 보일 때까지 아무 소리도 내지 않는다.
        #
        # 스냅샷 하나를 패널과 공유하는 것이, 이 버튼이 세는 개수와 경고가 나열하는 목록이 같은 뼈 집합을
        # 가리키게 만드는 유일한 장치다(baseline_odd).
        cleared = odd_bones(rig) - baseline_odd(context.scene)
        for pb in rig.pose.bones:
            pb.rotation_euler = (0, 0, 0)
            if pb.name not in cleared:
                continue
            pb.location = (0.0, 0.0, 0.0)
            pb.scale = (1.0, 1.0, 1.0)

        # 오브젝트를 통째로 끌고 간 경우. 여기서 되돌리지 않으면 되돌릴 버튼이 아예 없다.
        base = object_baseline(context.scene)
        moved = object_moved(context.scene, rig)
        if moved and base is not None:
            rig.location, rig.scale = base
        tail = ", 리그 오브젝트 위치도 되돌림" if moved else ""
        self.report({"INFO"},
                    "포즈를 되돌렸습니다 (회전 전체 + 이동·크기가 바뀐 뼈 %d개%s)" % (len(cleared), tail))
        return {"FINISHED"}


class ZEPETO_OT_key_pose(bpy.types.Operator):
    bl_idname = "zepeto.key_pose"
    # 이 버튼이 사는 패널 칸("3단계 · 이 순간 기록")과 번호를 맞춘 라벨이다. 라벨은 F3 검색과 Info 로그에
    # 그대로 나오므로, 여기 번호가 낡으면 사용자를 엉뚱한 칸으로 보내게 된다.
    bl_label = "3. 현재 포즈 저장"
    bl_description = "지금 프레임에 현재 포즈를 키프레임으로 기록합니다"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        rig = get_rig(context)
        if not rig:
            self.report({"ERROR"}, "리그가 없습니다")
            return {"CANCELLED"}
        blocked = ensure_euler(rig)
        if blocked:
            self.report({"WARNING"},
                        "쿼터니언 애니메이션이 있는 뼈는 건너뜁니다(%d개): %s"
                        % (len(blocked), ", ".join(blocked[:3])))

        frame = context.scene.frame_current
        # Unity의 Humanoid 아바타가 매핑하는 뼈만 찍는다. 나머지 49개에 키를 넣어 봐야 임포트에서 버려질
        # 커브가 생길 뿐이고, "죽은 뼈에 키가 있다" 경고가 모든 모션에서 울리게 된다.
        kept = 0
        for pb in rig.pose.bones:
            if not is_mapped_bone(pb.name):
                continue
            pb.keyframe_insert(data_path="rotation_euler", frame=frame)
            kept += 1
        self.report({"INFO"}, "%d 프레임에 포즈를 저장했습니다 (뼈 %d개)" % (frame, kept))
        return {"FINISHED"}


class ZEPETO_OT_delete_key(bpy.types.Operator):
    bl_idname = "zepeto.delete_key"
    bl_label = "이 프레임 키 삭제"
    bl_description = "지금 프레임의 키프레임을 지웁니다"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        rig = get_rig(context)
        if not rig:
            self.report({"ERROR"}, "리그가 없습니다")
            return {"CANCELLED"}
        frame = context.scene.frame_current

        # 회전 표현 두 가지를 모두 지우고, 실제로 사라진 개수를 센다.
        #
        # rotation_euler만 지우던 것은 조용한 거짓말이었다. ensure_euler는 이미 쿼터니언 F-커브를 가진 뼈를
        # 일부러 QUATERNION인 채로 남긴다(모드를 바꾸면 진짜 애니메이션이 고아가 되니까). 그런 뼈는 설계상
        # 존재하는데, 그 키는 타임라인에 남아 있는 채로 이 오퍼레이터만 "지웠습니다"라고 보고했다.
        removed = 0
        for pb in rig.pose.bones:
            hit = False
            for data_path in ("rotation_euler", "rotation_quaternion"):
                try:
                    if pb.keyframe_delete(data_path=data_path, frame=frame):
                        hit = True
                except RuntimeError:
                    pass
            if hit:
                removed += 1
        if not removed:
            self.report({"WARNING"}, "%d 프레임에는 지울 키가 없습니다" % frame)
            return {"CANCELLED"}
        self.report({"INFO"}, "%d 프레임 키를 지웠습니다 (뼈 %d개)" % (frame, removed))
        return {"FINISHED"}


class ZEPETO_OT_make_loop(bpy.types.Operator):
    bl_idname = "zepeto.make_loop"
    # 패널 칸 "4단계 · 부드럽게 반복"과 맞춘다 (key_pose 라벨의 주석 참고).
    bl_label = "4. 반복되게 만들기"
    bl_description = "첫 프레임 포즈를 마지막 프레임에 복사해 자연스럽게 반복되게 합니다"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        rig = get_rig(context)
        if not rig:
            self.report({"ERROR"}, "리그가 없습니다")
            return {"CANCELLED"}
        scene = context.scene
        start, end = scene.frame_start, scene.frame_end

        # frame_set/스냅샷보다 반드시 먼저다. QUATERNION 뼈에서 rotation_euler를 읽으면 0이 나오고,
        # 그러면 루프가 마지막 프레임에 rest 포즈를 복사해 넣는다.
        ensure_euler(rig)

        scene.frame_set(start)
        snapshot = {pb.name: tuple(pb.rotation_euler) for pb in rig.pose.bones}

        scene.frame_set(end)
        # key_pose와 같은 '매핑된 뼈만' 규칙이다. 그래야 루프를 만드는 것이 버려질 뼈의 커브를 다시
        # 끌어들이지 못한다.
        for pb in rig.pose.bones:
            if not is_mapped_bone(pb.name):
                continue
            pb.rotation_euler = snapshot[pb.name]
            pb.keyframe_insert(data_path="rotation_euler", frame=end)

        self.report({"INFO"}, "%d 프레임을 시작 포즈와 같게 맞췄습니다" % end)
        return {"FINISHED"}


def _snapshot_selection(context, wanted):
    """
    내보내기 직전의 선택 상태를 통째로 기억한다. _restore_selection에 그대로 돌려주면 원상복구된다.

    exporter를 부르기 전에(=try 블록 밖에서) 찍어야 한다. 그래야 선택을 바꾸다가 실패해도 finally가
    되돌릴 값을 이미 갖고 있다.
    """
    return {
        "selected": [o for o in context.view_layer.objects if o.select_get()],
        "active": context.view_layer.objects.active,
        "hide_select": {o: o.hide_select for o in wanted},
    }


def _select_only(context, rig, wanted):
    """
    내보낼 오브젝트만 선택 상태로 만든다.

    hide_select를 먼저 꺼야 한다. import_rig가 클릭이 뼈에 떨어지도록 몸에 hide_select를 걸어 두는데,
    Blender의 select_set은 선택 불가 오브젝트에서 아무 말 없이 아무 일도 하지 않는다. 이걸 놓치면
    스킨드 메시가 빠진 채로 나가고, 아래 [AUDIT] 블록이 경고하는 isHuman=false 내보내기가 된다.
    """
    for o in wanted:
        o.hide_select = False
    for o in context.view_layer.objects:
        o.select_set(False)
    for o in wanted:
        o.select_set(True)
    context.view_layer.objects.active = rig


def _restore_selection(context, snapshot):
    """사용자가 두고 갔던 선택 상태로 되돌린다. 내보내기 성공 여부와 무관하게 항상 실행된다."""
    for o, hidden in snapshot["hide_select"].items():
        o.hide_select = hidden
    for o in context.view_layer.objects:
        o.select_set(False)
    for o in snapshot["selected"]:
        try:
            o.select_set(True)
        except ReferenceError:
            # 내보내는 동안 지워진 오브젝트다. 참조만 죽었을 뿐이니 복구는 나머지를 계속 진행한다.
            pass
    context.view_layer.objects.active = snapshot["active"]


def _atomic_swap(temp_path, path):
    """
    .part를 제자리로 바꿔 넣는다. 성공하면 None, 끝까지 실패하면 마지막 PermissionError를 돌려준다.

    Unity가 목적지 파일을 리임포트하면서 붙잡고 있을 수 있고, 그것이 여기서 PermissionError로 나타난다.

    예전의 평평한 0.2초 x 5회는 약 0.8초 만에 포기했다. 그런데 기다리는 대상은 ~1.6MB짜리 fbx에 대한
    AssetDatabase.ImportAsset(ForceUpdate)이고 그건 그보다 오래 걸리는 일이 잦아서, 저절로 나을 일에
    "잠시 뒤 다시 누르세요"가 떴다. 그래서 지수 백오프로 바꿨다.

    delays는 '대기 스케줄'이지 '시도 횟수'가 아니다. 시도는 len(delays) + 1 = 6번이고 마지막 시도 뒤에는
    자지 않으므로, 최대 대기 예산은 0.1+0.2+0.4+0.8+1.6 = 3.1초다. 예전에는 튜플 끝에 2.0이 하나 더
    있었는데 마지막 원소는 한 번도 sleep되지 않아서, 코드가 5.1초처럼 읽히면서 실제로는 3.1초였다.
    """
    delays = (0.1, 0.2, 0.4, 0.8, 1.6)
    last_error = None
    for attempt in range(len(delays) + 1):
        try:
            os.replace(temp_path, path)
            return None
        except PermissionError as exc:
            last_error = exc
            if attempt < len(delays):
                time.sleep(delays[attempt])
    return last_error


class ZEPETO_OT_export(bpy.types.Operator):
    bl_idname = "zepeto.export"
    # 패널 칸 "5단계 · Unity로 보내기"와 맞춘다 (key_pose 라벨의 주석 참고).
    bl_label = "5. Unity로 내보내기"
    bl_description = "ZEPETO용 설정으로 FBX를 Unity 프로젝트에 바로 저장합니다"

    def execute(self, context):
        # 다섯 단계로 읽으면 된다: 경로 수리 → 검사 → 죽은 .part 청소 → 선택 상태를 바꿔가며 내보내기
        # → 원자적 교체.
        rig = get_rig(context)
        if not rig:
            self.report({"ERROR"}, "리그가 없습니다")
            return {"CANCELLED"}

        scene = context.scene
        # (1) 판단하기 전에 먼저 고친다. 폴더가 비었거나 다른 컴퓨터에서 온 .blend일 수 있다.
        refresh_paths(scene)

        # (2) 패널이 버튼을 회색으로 만들 때 쓰는 것과 같은 목록으로 거절한다. 패널은 버튼을 잠그고
        #     오퍼레이터는 그래도 한 번 더 거절하는데, 패널을 거치지 않는 호출 경로가 실재하기 때문이다
        #     (poll()이 없어 F3 검색으로 직접 실행되고, make_live_fixtures.py도 직접 호출한다).
        problems = export_problems(scene, rig)
        if problems:
            first = problems[0]
            if len(problems) > 1:
                first = "%s (그 밖에 %d개 - 패널 5단계의 목록을 보세요)" % (first, len(problems) - 1)
            self.report({"ERROR"}, first)
            return {"CANCELLED"}

        folder = bpy.path.abspath(scene.zepeto_export_dir)
        name = scene.zepeto_motion_name.strip()
        path = os.path.join(folder, name + ".fbx")

        # (3) 임시 이름으로 쓰고 원자적으로 갈아 끼운다.
        #
        # Unity는 이 폴더를 감시하다가 파일의 타임스탬프가 움직이는 순간 리임포트한다. fbx를 제자리에 쓰면
        # Unity가 반쯤 쓰인 파일을 읽어 깨진 클립을 구울 수 있고, Windows에서는 Unity가 이전 fbx를 잡고 있어서
        # 쓰기 자체가 PermissionError로 실패한다. os.replace는 같은 볼륨 안에서 원자적이므로, Unity는 언제나
        # 완성된 파일만 본다.
        temp_path = path + ".part"
        # 쓰다가 죽은 예전 내보내기의 .part가 남아 있으면 이번 것을 막는다.
        if os.path.exists(temp_path):
            try:
                os.remove(temp_path)
            except OSError:
                pass

        # [AUDIT] 아마추어만이 아니라 노드 그래프 전체를 내보낸다.
        #
        # MESH: ZEPETO의 뼈 이름(hips, upperArm_R, 오른쪽 다리는 심지어 upperReg_R로 오타가 나 있다)은
        # Unity의 humanoid 자동 매퍼가 아는 이름이 아니다. 스킨드 메시가 없으면 Unity는 generic 아바타를
        # 만들고(isHuman=false) humanoid AnimationClip을 하나도 생성하지 않는다 - 임포트 결과가 텅 빈
        # 것처럼 보인다.
        #
        # EMPTY는 일부러 뺐다. 리그 fbx는 래퍼 empty를 하나 갖고 있는데(ZepetoBaseModel > hips > body),
        # Unity는 임포트된 모델의 루트를 항상 '파일 이름'으로 짓는다. 래퍼까지 내보내면 Unity 자신의 루트
        # 아래에 한 겹 더 끼어들어 쓸데없는 층이 생긴다(transform 106개 대신 107개).
        object_types = {"ARMATURE", "MESH"}

        # (4) 리그와 그 자신의 메시'만' 내보낸다.
        #
        # use_selection=False였을 때는 .blend 안에 있던 것이 그대로 실려 나갔다 - 새로 설치한 Blender의 기본
        # 큐브라든가, 두 번째 아마추어. 그러면 Unity의 humanoid 매퍼가 실패하고, 사용자에게는 임포트 설정에
        # 대한 엉뚱한 안내로 보인다.
        wanted = [rig] + [o for o in rig.children_recursive if o.type in object_types]
        selection = _snapshot_selection(context, wanted)

        # exporter가 실패해도 여기의 다른 실패들처럼 한국어 리포트로 나와야 한다. 감싸지 않았을 때는
        # export_scene.fbx 안에서 난 에러(잠긴 경로, 쓸 수 없는 폴더, exporter가 못 삼키는 인코딩)가 시스템
        # 콘솔에 날것의 Python traceback으로 빠져나갔다 - 초보자는 그 콘솔을 볼 일이 없으므로, 버튼은 그냥
        # 아무 일도 안 한 것처럼 보였다.
        export_error = None
        try:
            _select_only(context, rig, wanted)
            bpy.ops.export_scene.fbx(
                filepath=temp_path,
                use_selection=True,
                object_types=object_types,
                add_leaf_bones=False,          # leaf bone이 있으면 Unity humanoid 매핑이 깨진다
                bake_anim=True,
                bake_anim_use_all_bones=True,
                bake_anim_use_nla_strips=False,
                bake_anim_use_all_actions=False,
                bake_anim_force_startend_keying=True,
                bake_anim_step=1.0,
                bake_anim_simplify_factor=0.0,
                apply_scale_options="FBX_SCALE_ALL",
                axis_forward="-Z",
                axis_up="Y",
                global_scale=1.0,
            )
        except Exception as exc:
            export_error = exc
        finally:
            _restore_selection(context, selection)

        if export_error is not None or not os.path.exists(temp_path):
            # 아래 os.replace 실패와 같은 정리 약속이다. 반쯤 쓰인 .part를 Assets/ 안에 절대 남기지 않는다.
            # 남기면 사용자가 손으로 AssetDatabase.Refresh를 했을 때 그게 임포트되면서 고아 .meta가 생긴다.
            try:
                os.remove(temp_path)
            except OSError:
                pass
            if export_error is None:
                self.report({"ERROR"}, "FBX를 만들지 못했습니다")
            else:
                self.report({"ERROR"}, "FBX를 만들지 못했습니다: %s" % export_error)
            return {"CANCELLED"}

        # (5) 원자적 교체.
        last_error = _atomic_swap(temp_path, path)
        if last_error is not None:
            # 임시 파일을 Assets/ 안에 절대 남기지 않는다. Unity의 감시자는 ".part"를 건너뛰지만, 손으로
            # AssetDatabase.Refresh를 하면 DefaultAsset으로 임포트되면서 고아 .meta가 생긴다.
            try:
                os.remove(temp_path)
            except OSError:
                pass
            self.report({"ERROR"},
                        "Unity가 파일을 잡고 있어서 저장하지 못했습니다. 잠시 뒤 다시 누르세요 (%s)" % path)
            return {"CANCELLED"}

        self.report({"INFO"}, "내보냈습니다: %s" % path)
        return {"FINISHED"}


# ---------------------------------------------------------------- 패널
class ZEPETO_PT_panel(bpy.types.Panel):
    bl_label = "ZEPETO 모션"
    bl_idname = "ZEPETO_PT_panel"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "ZEPETO"

    def draw(self, context):
        draw_zepeto_panel(self.layout, context)


class ZEPETO_PT_panel_item(bpy.types.Panel):
    """
    Blender 기본 "Item" 탭에 같은 패널을 한 벌 더 띄운다.

    사이드바는 Item 탭이 열린 채로 나타나는데 Python으로는 활성 탭을 바꿀 수 없다
    (region.active_panel_category가 읽기 전용이다). 그래서 처음 쓰는 사람은 텅 빈 Transform 패널을 보고
    세로 띠에서 ZEPETO 탭을 스스로 찾아내야 했다. Item에도 같은 컨트롤을 그려서 그 단계를 아예 없앤다.
    """
    bl_label = "ZEPETO 모션"
    bl_idname = "ZEPETO_PT_panel_item"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Item"

    def draw(self, context):
        draw_zepeto_panel(self.layout, context)


def draw_zepeto_panel(layout, context):
    scene = context.scene
    rig = get_rig(context)

    if not rig:
        box = layout.box()
        box.label(text="1단계 · 몸 불러오기", icon="ARMATURE_DATA")
        box.operator("zepeto.import_rig", text="ZEPETO 몸 불러오기", icon="IMPORT")
        box.prop(scene, "zepeto_rig_fbx", text="")
        # 이 칸은 일부러 빈 채로 시작한다(refresh_paths 참고). 그렇다고 말해 주지 않으면 빈 칸이
        # 고장으로 읽힌다.
        if rig_fbx_problem(scene):
            box.label(text="비워 두면 불러올 때 자동으로 찾습니다", icon="INFO")
            box.operator("zepeto.locate_paths", text="경로 자동 찾기", icon="FILE_REFRESH")
        return

    usable = mapped_coverage(rig)
    box = layout.box()
    box.label(text="쓸 수 있는 뼈 %d개 / 전체 %d개" % (usable, len(rig.data.bones)), icon="CHECKMARK")
    # 천장은 54다: "hips"는 뼈가 아니라 아마추어 오브젝트다. 이보다 한참 낮으면 리그가 다른 뼈 이름으로
    # 다시 내보내졌고 MAPPED_BONES가 낡았다는 뜻이다 - 안 잡으면 뼈대가 통째로 숨겨진 채 안심시키는
    # 체크마크만 뜨는 상태가 된다.
    if usable < len(MAPPED_BONES) - 1:
        box.label(text="이 리그는 ZEPETO 기본 몸과 뼈 이름이 다릅니다. 3번에서 다시 내보내세요",
                  icon="ERROR")
        # 씬에 아마추어가 하나라도 있으면 1단계 칸이 통째로 사라진다. 그래서 엉뚱한 아마추어(append한 리그,
        # Mixamo 임포트)가 들어온 사용자에게는 zepeto.import_rig로 돌아갈 길이 아예 없었다 - 이 경고만 읽고
        # 아무것도 누를 수 없는 상태. 그 경고가 뜨는 자리에 되돌아갈 버튼을 같이 둔다.
        row = box.row()
        row.operator("zepeto.import_rig", text="다시 불러오기", icon="IMPORT")
        row.prop(scene, "zepeto_rig_fbx", text="")

    box = layout.box()
    box.label(text="2단계 · 포즈 만들기", icon="POSE_HLT")
    box.label(text="뼈를 클릭 → R → Z", icon="INFO")
    box.label(text="→ 마우스 이동 → 좌클릭", icon="BLANK1")
    box.operator("zepeto.clear_pose", text="포즈 전부 되돌리기")
    box.prop(scene, "zepeto_show_all_bones")
    if scene.zepeto_show_all_bones:
        box.label(text="숨겨둔 뼈는 돌려도 Unity에서 사라집니다", icon="ERROR")

    box = layout.box()
    box.label(text="3단계 · 이 순간 기록", icon="KEYFRAME_HLT")
    row = box.row()
    row.scale_y = 1.2
    row.prop(scene, "frame_current", text="프레임")
    col = box.column()
    col.scale_y = 1.4
    col.operator("zepeto.key_pose", text="현재 포즈 저장", icon="KEY_HLT")
    box.operator("zepeto.delete_key", text="이 프레임 지우기", icon="KEY_DEHLT")

    times = keyframe_times(rig)
    box.label(text="저장된 프레임: %s" % (", ".join(str(t) for t in times) if times else "아직 없음"))

    box = layout.box()
    box.label(text="4단계 · 부드럽게 반복", icon="LOOP_BACK")
    box.operator("zepeto.make_loop", text="처음과 끝 맞추기")

    # 아바타를 가만히 서 있게 만들거나 부스 화면 밖으로 밀어내는 문제들. 오퍼레이터도 같은 목록으로
    # 거절하므로, 여기 보이는 이유와 내보내기가 실패하는 이유는 언제나 같다.
    problems = export_problems(scene, rig)

    box = layout.box()
    box.label(text="5단계 · Unity로 보내기", icon="EXPORT")
    box.prop(scene, "zepeto_motion_name", text="이름")
    # 저장 폴더는 예전에 UI에 아예 없었다. 다른 컴퓨터의 경로를 물고 온 .blend은 내보내기 버튼에서 막히는데
    # Python 콘솔 말고는 고칠 방법이 없었다.
    box.prop(scene, "zepeto_export_dir", text="저장 폴더")

    if export_dir_problem(scene):
        box.label(text="이 폴더를 찾을 수 없습니다. 아래 버튼을 누르거나 폴더를 직접 고르세요", icon="ERROR")
        box.operator("zepeto.locate_paths", text="경로 자동 찾기", icon="FILE_REFRESH")

    if problems:
        box.label(text="아직 안 됩니다", icon="ERROR")
        for p in problems:
            box.label(text="· " + p)
    else:
        box.label(text="보낼 준비 완료", icon="CHECKMARK")

    col = box.column()
    col.scale_y = 1.4
    col.enabled = not problems
    col.operator("zepeto.export", text="Unity로 보내기", icon="EXPORT")


CLASSES = (
    ZEPETO_OT_import_rig,
    ZEPETO_OT_locate_paths,
    ZEPETO_OT_clear_pose,
    ZEPETO_OT_key_pose,
    ZEPETO_OT_delete_key,
    ZEPETO_OT_make_loop,
    ZEPETO_OT_export,
    ZEPETO_PT_panel,
    ZEPETO_PT_panel_item,
)


def register():
    for c in CLASSES:
        bpy.utils.register_class(c)
    bpy.types.Scene.zepeto_motion_name = StringProperty(
        name="이름", default="MyMotion",
        description="내보낼 FBX 파일 이름")
    # 경로 프로퍼티 둘은 빈 값으로 시작해서 첫 사용 때 refresh_paths가 채운다. default= 는 지금 이
    # 등록 시점에 딱 한 번 평가되므로, 여기에 박아 넣은 값은 한 컴퓨터의 폴더를 모든 새 씬에 얼려 넣는다.
    bpy.types.Scene.zepeto_export_dir = StringProperty(
        name="저장 폴더", subtype="DIR_PATH", default="",
        description="Unity 프로젝트의 Assets/CustomMotions 폴더. 비워 두면 자동으로 찾습니다")
    # 아래 두 baseline 프로퍼티는 어느 패널에도 그리지 않는다. 일부러 숨긴 씬 단위 스냅샷이고,
    # import_rig가 한 번 쓰면 clear_pose와 export_problems가 읽는다.
    bpy.types.Scene.zepeto_baseline_odd = StringProperty(
        name="baseline", default="",
        description="Bones already off-rest when the rig was imported")
    bpy.types.Scene.zepeto_baseline_object = StringProperty(
        name="baseline object", default="",
        description="Rig object location/scale when the rig was imported")
    bpy.types.Scene.zepeto_rig_fbx = StringProperty(
        name="ZEPETO FBX", subtype="FILE_PATH", default="",
        description="Unity 헬퍼의 'ZEPETO 리그 내보내기'가 만든 FBX. 비워 두면 자동으로 찾습니다")
    bpy.types.Scene.zepeto_show_all_bones = BoolProperty(
        name="Unity가 무시하는 뼈도 보기", default=False,
        description="켜면 Twist·_scale·얼굴 뼈까지 전부 보입니다. 그 뼈들을 돌려도 Unity에서는 사라집니다",
        update=_on_show_all_bones_changed)


def unregister():
    for c in reversed(CLASSES):
        bpy.utils.unregister_class(c)
    del bpy.types.Scene.zepeto_motion_name
    del bpy.types.Scene.zepeto_export_dir
    del bpy.types.Scene.zepeto_baseline_odd
    del bpy.types.Scene.zepeto_baseline_object
    del bpy.types.Scene.zepeto_rig_fbx
    del bpy.types.Scene.zepeto_show_all_bones


if __name__ == "__main__":
    register()
