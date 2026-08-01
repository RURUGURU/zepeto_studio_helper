"""
10초짜리 춤 한 곡을 실제로 만들어서 Unity로 내보낸다.

실행:
    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" ^
        --background BlenderMotion\\zepeto_motion.blend --python BlenderMotion\\make_dance.py

이 파일이 존재하는 이유는 "만들 수 있다"와 "만들어 봤다"가 다른 문장이기 때문이다. 검사 묶음들은
버튼 하나하나가 약속대로 도는지를 증명하지만, 그것들을 엮어서 사람이 춤이라고 부를 만한 것이 실제로
나오는지는 증명하지 않는다. 이 스크립트는 그 하나를 증명한다 - 240프레임짜리 안무를 끝까지 찍고,
루프를 닫고, 내보내고, 뼈가 실제로 얼마나 움직였는지를 잰다.

## 안무에 대해

특정 곡의 실제 안무는 저작물이므로 그대로 옮기지 않는다. 여기 있는 것은 **창작 동작**이고,
120 BPM(K-pop에서 가장 흔한 템포대) 기준 20비트짜리 루프다. 한 비트가 정확히 12프레임이라
어떤 120 BPM 곡에도 박자가 맞는다.

    24 fps ÷ (120 BPM ÷ 60초) = 12 프레임/비트
    240 프레임 ÷ 12 = 20 비트 = 10초

## 뼈 회전축 (렌더와 수치 측정으로 확인함, axisnum 측정치 기준)

좌표계: +X = 캐릭터의 왼쪽, -Y = 캐릭터가 바라보는 앞, +Z = 위.
rest pose는 T자세다 - 팔이 좌우로 뻗어 있으므로, 팔을 내리려면 Z를 음수로 크게 줘야 한다.

    upperArm_L/R     X 비틀기      Y+ 뒤로        Z+ 위로 (양팔 같은 부호)
    lowerArm_L/R     X 비틀기      Y+ 뒤로        Z+ 팔꿈치 굽힘
    spine/chest/     X 몸통 회전    Y+ 뒤로 젖힘    Z+ 오른쪽으로 기울임
      chestUpper/neck
    head             X+ 숙임       Y  고개 돌리기   Z+ 오른쪽 기울임
    upperLeg_L/      X 비틀기      Y+ 뒤로        Z+ 바깥으로 벌림 (양다리 같은 부호)
      upperReg_R
    lowerLeg_L/      X 비틀기      Y+ 무릎 굽힘    -
      lowerReg_R

오른쪽 다리 이름이 upperReg_R / lowerReg_R인 것은 ZEPETO 원본 리그의 오타다. 고치면 Unity의
Humanoid 매핑이 깨지므로 그대로 쓴다.

## 얼굴에 대해

여기서 키를 찍을 수 있는 얼굴 뼈는 셋뿐이고, 되는 것은 '눈동자 방향'과 '입 벌리기' 둘이다.
리그의 humanDescription이 이렇게 매핑한다:

    eye_L -> LeftEye      eye_R -> RightEye      mouth -> Jaw

실제로 건너가는 것을 확인했다. 얼굴만 움직이는 클립을 Unity에 임포트했더니 Humanoid 클립 안에
Jaw Close(변화폭 2.60), Left/Right Eye Down-Up(각 1.64), Eye In-Out(각 0.34)이 값이 변하는 채로
들어왔고, 그 클립의 커브 130개 중 변화폭 상위 5개가 전부 이것들이었다.

함정: 턱을 움직이는 뼈는 jaw가 아니라 mouth다. jaw라는 이름의 뼈도 있지만 매핑이 없어서
애드온이 숨겨 놓았고, 돌려도 Unity에서 조용히 사라진다. nose / lip_L / lip_R도 마찬가지다.

## 눈 깜빡임은 여기서 못 만든다 (제페토가 못 해서가 아니다)

eye_L을 돌리면 움직이는 것은 눈알 385개 정점뿐이다. 눈꺼풀 뼈가 아예 없다.

그런데 진짜 아바타는 깜빡인다. Play 중 조사해 보면 body 메시에 블렌드셰이프가 250개 있고
그중 zepeto.eyeBlinkLeft / mouthSmileLeft / browInnerUp 같은 것이 79개다. 제페토 공식 클립
(zepeto.studio@3.2.16의 ANI_039_v03.anim)도 blendShape.zepeto.* 커브를 57종 담고 있다.
path는 "body", classID는 137(SkinnedMeshRenderer)이다.

빠진 곳은 우리가 내보내는 몸이다. ZepetoBaseModel.prefab 자체에 블렌드셰이프가 0개라
(m_Shapes: vertices: [] shapes: [] channels: [], 메시 4개 전부) Blender에는 섞을 모양이 없다.
얼굴 모양은 Play 중 서버에서 내려올 때 붙는다.

넣으려면 Unity 쪽에서 추출된 .anim에 커브를 직접 써야 한다. 지금 이 저장소에는 그 기능이 없다.

축은 다른 뼈와 같다: 눈은 X가 위아래, Z가 좌우. 입(턱)은 X+가 벌리기.
눈은 좌우 대칭이라 Z만 부호를 뒤집는다 - pose()가 알아서 처리한다.

## 규칙

전부 **회전**뿐이다. 이동(location)과 크기(scale)는 한 번도 건드리지 않는다 - Humanoid 리타게팅은
회전만 옮기므로 나머지는 Unity에서 조용히 사라진다. 리그 오브젝트도 건드리지 않는다. 즉 이 안무는
애드온의 내보내기 게이트를 우회하지 않고 정면으로 통과한다.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

import bpy
import zepeto_motion_helper as addon

FPS = 24
BPM = 120
BEAT = FPS * 60 // BPM           # 12 프레임
BEATS = 20
LAST = BEAT * BEATS              # 240
MOTION_NAME = "DanceDemo10s"

# 한 비트씩. 각 항목은 {뼈 이름: (X도, Y도, Z도)}이고, 적지 않은 뼈는 rest(0,0,0)로 돌아간다.
# 매 비트마다 전신을 다시 적는 이유는, 빠뜨린 뼈가 이전 비트의 각도를 그대로 들고 가면 안무가
# 아니라 '한쪽으로 계속 쌓이는 자세'가 되기 때문이다.
def pose(arm_l=(0, 0, -70), arm_r=(0, 0, -70), fore_l=(0, 0, 15), fore_r=(0, 0, 15),
         spine=(0, 0, 0), chest=(0, 0, 0), chest_upper=(0, 0, 0), head=(0, 0, 0),
         leg_l=(0, 0, 0), leg_r=(0, 0, 0), knee_l=(0, 0, 0), knee_r=(0, 0, 0),
         eyes=(0, 0, 0), mouth=(0, 0, 0)):
    """기본값은 '차렷에 가까운 준비 자세'다. 바꾸고 싶은 부위만 이름으로 넘긴다."""
    return {
        "upperArm_L": arm_l, "upperArm_R": arm_r,
        "lowerArm_L": fore_l, "lowerArm_R": fore_r,
        "spine": spine, "chest": chest, "chestUpper": chest_upper, "head": head,
        "upperLeg_L": leg_l, "upperReg_R": leg_r,
        "lowerLeg_L": knee_l, "lowerReg_R": knee_r,
        # 얼굴. 두 눈은 좌우 대칭이라 Z만 부호를 뒤집는다 (양쪽 다 안쪽/바깥쪽이 반대 방향이다).
        "eye_L": eyes, "eye_R": (eyes[0], eyes[1], -eyes[2]),
        "mouth": mouth,
    }


# 다리에 대해서 - 첫 판이 실패한 지점이다.
#
# 첫 판은 20비트 중 13비트에서 다리를 rest(0,0,0) 그대로 두었다. 팔만 크게 움직이고 하체는 선 채로
# 굳어 있어서, 보는 사람에게는 춤이 아니라 '제자리에서 팔만 젓는 것'으로 읽혔다.
#
# 골반을 못 움직인다는 제약(골반은 뼈가 아니라 아마추어 오브젝트다)이 "다리는 어차피 못 쓴다"로
# 잘못 번역된 것이 원인이었다. 실제로는 회전만으로도 다리가 충분히 읽힌다. 정면에서 가장 잘 보이는
# 순서대로:
#
#   무릎 들기   leg=(0,-45,0) + knee=(0,75,0)   허벅지는 앞으로, 종아리는 접어서 -- 가장 강하다
#   발 뒤로 차기 leg=(0,15,0)  + knee=(0,85,0)   발뒤꿈치가 엉덩이 쪽으로 올라온다
#   옆으로 벌리기 leg=(0,0,25)                    실루엣 폭이 바뀐다
#   무게 이동   한쪽만 knee=(0,25,0)             양다리가 비대칭이 되면서 몸이 기운 것처럼 보인다
#
# 그래서 아래 20비트는 **모든 비트에서 두 다리가 rest가 아니다.** 서 있는 것처럼 보여야 하는
# 비트조차 최소한 무게를 한쪽으로 실어 둔다.
CHOREOGRAPHY = [
    # 1-4비트: 좌우로 포인트하며 시작 (다리는 무게 이동 -> 무릎 들기)
    pose(spine=(0, -4, 0), knee_l=(0, 10, 0), knee_r=(0, 22, 0), leg_r=(0, -6, 0),
         eyes=(0,0,0), mouth=(4,0,0)),  # 0  준비 (오른쪽에 무게)
    pose(arm_r=(0, -15, 60), fore_r=(0, 0, 10), arm_l=(0, -20, -65), fore_l=(0, 0, 25),
         spine=(0, 0, -12), chest=(0, 0, -8), head=(0, 0, -6),
         leg_l=(0, -45, 4), knee_l=(0, 75, 0), knee_r=(0, 8, 0),
         eyes=(0,0,16), mouth=(18,0,0)),                    # 1  오른팔 위 + 왼무릎 들기
    pose(arm_l=(0, -15, 60), fore_l=(0, 0, 10), arm_r=(0, -20, -65), fore_r=(0, 0, 25),
         spine=(0, 0, 12), chest=(0, 0, 8), head=(0, 0, 6),
         leg_r=(0, -45, 4), knee_r=(0, 75, 0), knee_l=(0, 8, 0),
         eyes=(0,0,-16), mouth=(18,0,0)),                    # 2  왼팔 위 + 오른무릎 들기
    # 팔꿈치를 거의 펴는 것이 중요하다. fore를 35도까지 굽혔더니 앞으로 뻗은 팔이 얼굴 옆에서
    # 구겨진 모양이 되어 '숙였다'가 전혀 읽히지 않았다.
    pose(arm_l=(0, -85, -8), arm_r=(0, -85, -8), fore_l=(0, 0, 5), fore_r=(0, 0, 5),
         spine=(0, -16, 0), chest=(0, -10, 0), head=(12, 0, 0),
         leg_l=(0, -14, 0), leg_r=(0, -14, 0), knee_l=(0, 30, 0), knee_r=(0, 30, 0),
         eyes=(22,0,0), mouth=(6,0,0)),  # 3  앞으로 뻗기 + 무릎 굽힘
    # T자세는 이 리그의 rest이므로, 팔을 수평 근처에 두면 '포즈를 안 잡은 것'과 구별되지 않는다.
    # 정면에서 실루엣이 살려면 수평에서 확실히 벗어나야 한다.
    pose(arm_l=(0, 5, 48), arm_r=(0, 5, 48), fore_l=(0, 0, 8), fore_r=(0, 0, 8),
         spine=(0, 14, 0), chest=(0, 8, 0), head=(-14, 0, 0),
         leg_l=(0, 0, 20), leg_r=(0, 0, 20), knee_l=(0, 6, 0), knee_r=(0, 6, 0),
         eyes=(-8,0,0), mouth=(26,0,0)),    # 4  V + 젖힘 + 다리 벌림

    # 5-8비트: 좌우 스텝에서 팔 웨이브로 (스텝마다 반대 무릎이 접힌다)
    pose(arm_r=(0, -40, -30), arm_l=(0, 0, -60), fore_r=(0, 0, 20),
         leg_r=(0, 0, 30), leg_l=(0, -10, 0), knee_l=(0, 28, 0), knee_r=(0, 6, 0),
         spine=(0, 0, 10), chest=(0, 0, 6),
         eyes=(0,0,-14), mouth=(10,0,0)),                                         # 5  오른쪽 스텝
    pose(arm_l=(0, -40, -30), arm_r=(0, 0, -60), fore_l=(0, 0, 20),
         leg_l=(0, 0, 30), leg_r=(0, -10, 0), knee_r=(0, 28, 0), knee_l=(0, 6, 0),
         spine=(0, 0, -10), chest=(0, 0, -6),
         eyes=(0,0,14), mouth=(10,0,0)),                                       # 6  왼쪽 스텝
    pose(arm_l=(0, 0, 70), arm_r=(0, 0, 70), fore_l=(0, 0, 20), fore_r=(0, 0, 20),
         spine=(0, 4, 0),
         leg_l=(0, 12, 0), knee_l=(0, 80, 0), knee_r=(0, 10, 0),
         eyes=(-10,0,0), mouth=(24,0,0)),                    # 7  양팔 위 + 왼발 뒤로 차기
    pose(arm_l=(0, -20, 55), arm_r=(0, 20, 55), fore_l=(0, 0, 70), fore_r=(0, 0, 70),
         spine=(0, 0, 8), head=(0, 0, 5),
         leg_r=(0, 12, 0), knee_r=(0, 80, 0), knee_l=(0, 10, 0),
         eyes=(26,0,0), mouth=(8,0,0)),                    # 8  웨이브 + 오른발 뒤로 차기

    # 9-12비트: 몸통 회전 두 번, 다리 벌려 기울이기, 튀어오르기
    # 몸통 회전(spine X)만으로는 정면에서 거의 안 보인다 - 머리가 회전축 위에 있어서 실루엣이
    # 그대로다. 한쪽 팔을 몸 앞으로 크게 가로지르게 하고, 같은 쪽 무릎을 들어서 회전을 눈에 보이게 한다.
    pose(spine=(28, 0, 0), chest=(16, 0, 0),
         arm_l=(0, -70, -10), fore_l=(0, 0, 50), arm_r=(0, 25, -45), fore_r=(0, 0, 15),
         head=(0, 0, 8),
         leg_r=(0, -38, 10), knee_r=(0, 62, 0), knee_l=(0, 12, 0),
         eyes=(0,0,18), mouth=(14,0,0)),                  # 9  몸통 오른쪽 + 오른무릎
    pose(spine=(-28, 0, 0), chest=(-16, 0, 0),
         arm_r=(0, -70, -10), fore_r=(0, 0, 50), arm_l=(0, 25, -45), fore_l=(0, 0, 15),
         head=(0, 0, -8),
         leg_l=(0, -38, 10), knee_l=(0, 62, 0), knee_r=(0, 12, 0),
         eyes=(0,0,-18), mouth=(14,0,0)),                  # 10 몸통 왼쪽 + 왼무릎
    # 웅크리려면 골반이 내려가야 하는데 골반은 뼈가 아니라 아마추어 오브젝트이고, 그것을 움직이는
    # 것은 이 파이프라인이 금지하는 바로 그 동작이다(아바타가 화면 밖으로 걸어나간다). 대신 다리를
    # 크게 벌리고 한쪽 무릎을 깊게 접으면 정면에서 '낮은 자세'로 읽힌다.
    pose(leg_l=(0, -8, 32), leg_r=(0, 0, 26), knee_l=(0, 52, 0), knee_r=(0, 14, 0),
         spine=(0, 0, -22), chest=(0, 0, -10), head=(0, 0, -12),
         arm_l=(0, -20, -78), fore_l=(0, 0, 20), arm_r=(0, -10, -30), fore_r=(0, 0, 10),
         eyes=(0,0,-20), mouth=(20,0,0)),  # 11 런지 (왼무릎 깊게)
    # 양 무릎을 55도씩 접었더니 정강이가 카메라 축으로 단축되면서 발이 몸에서 떨어져 떠 있는 것처럼
    # 보였다. 한쪽만 깊게 접고 반대쪽은 펴서 뻗으면 같은 '솟구치는' 느낌이면서 실루엣이 무너지지 않는다.
    pose(arm_l=(0, 0, 75), arm_r=(0, 0, 75), fore_l=(0, 0, 5), fore_r=(0, 0, 5),
         spine=(0, 8, 0), head=(-15, 0, 0),
         leg_l=(0, 14, 6), knee_l=(0, 62, 0), leg_r=(0, -10, 14), knee_r=(0, 8, 0),
         eyes=(-12,0,0), mouth=(30,0,0)),  # 12 점프 (왼발만 접기)

    # 13-16비트: 대각선 포인트 두 번, 크로스, 활짝 (무릎이 팔과 반대쪽으로 올라간다)
    pose(arm_r=(0, -35, 40), fore_r=(0, 0, 15), arm_l=(0, 15, -60),
         head=(18, 0, 0), spine=(0, 0, -8),
         leg_l=(0, -50, 6), knee_l=(0, 70, 0), knee_r=(0, 8, 0),
         eyes=(20,0,10), mouth=(8,0,0)),                    # 13 오른팔 대각선 + 왼무릎
    pose(arm_l=(0, -35, 40), fore_l=(0, 0, 15), arm_r=(0, 15, -60),
         head=(-10, 0, 0), spine=(0, 0, 8),
         leg_r=(0, -50, 6), knee_r=(0, 70, 0), knee_l=(0, 8, 0),
         eyes=(-6,0,-10), mouth=(16,0,0)),                    # 14 왼팔 대각선 + 오른무릎
    pose(arm_l=(0, -75, -25), arm_r=(0, -75, -25), fore_l=(0, 0, 60), fore_r=(0, 0, 60),
         spine=(0, -8, 0), head=(8, 0, 0),
         leg_l=(0, -18, 0), leg_r=(0, -18, 0), knee_l=(0, 38, 0), knee_r=(0, 38, 0),
         eyes=(24,0,0), mouth=(6,0,0)),  # 15 크로스 + 앉는 느낌
    # 여기도 원래 수평에 가까운 '활짝'이라 T자세와 구별되지 않았다. 한 팔 위 / 한 팔 아래의
    # 대각선으로 바꾸면 실루엣이 확실히 다르고, 앞뒤 비트와도 겹치지 않는다.
    pose(arm_l=(0, -10, 62), fore_l=(0, 0, 10), arm_r=(0, 10, -72), fore_r=(0, 0, 22),
         spine=(0, 8, -10), chest=(0, 4, -6), head=(-10, 0, -6),
         leg_l=(0, 0, 24), leg_r=(0, -12, 0), knee_r=(0, 34, 0), knee_l=(0, 8, 0),
         eyes=(-10,0,-8), mouth=(22,0,0)),  # 16 대각선 + 무게 오른쪽

    # 17-19비트: 힙 스텝 두 번 뒤 준비 자세로 수렴 (20비트째는 make_loop이 채운다)
    pose(arm_r=(0, -30, -50), arm_l=(0, 20, -40), fore_r=(0, 0, 30),
         leg_r=(0, 0, 26), leg_l=(0, -16, 0), knee_l=(0, 40, 0), knee_r=(0, 8, 0),
         spine=(0, 0, 14), chest=(0, 0, 6),
         eyes=(0,0,-16), mouth=(12,0,0)),                                         # 17 오른쪽 힙
    pose(arm_l=(0, -30, -50), arm_r=(0, 20, -40), fore_l=(0, 0, 30),
         leg_l=(0, 0, 26), leg_r=(0, -16, 0), knee_r=(0, 40, 0), knee_l=(0, 8, 0),
         spine=(0, 0, -14), chest=(0, 0, -6),
         eyes=(0,0,16), mouth=(12,0,0)),                                       # 18 왼쪽 힙
    pose(arm_l=(0, -12, -68), arm_r=(0, -12, -68), fore_l=(0, 0, 18), fore_r=(0, 0, 18),
         spine=(0, -4, 0), leg_r=(0, -6, 0), knee_l=(0, 10, 0), knee_r=(0, 20, 0),
         eyes=(0,0,0), mouth=(4,0,0)),  # 19 마무리 (0번으로 수렴)
]

results = []


def check(name, ok, detail=""):
    results.append((name, bool(ok), detail))
    print("%s %s :: %s" % ("PASS" if ok else "FAIL", name, detail))


def apply_pose(rig, spec):
    """한 비트의 포즈를 적용한다. spec에 없는 매핑 뼈는 rest로 되돌린다."""
    for pose_bone in rig.pose.bones:
        if not addon.is_mapped_bone(pose_bone.name):
            continue
        degrees = spec.get(pose_bone.name, (0.0, 0.0, 0.0))
        pose_bone.rotation_euler = tuple(math.radians(d) for d in degrees)


def main():
    scene = bpy.context.scene
    rig = addon.get_rig(bpy.context)
    if rig is None:
        check("dance:rig", False, "아마추어가 없다")
        return finish()
    check("dance:rig", addon.mapped_coverage(rig) == 54,
          "쓸 수 있는 뼈 %d개" % addon.mapped_coverage(rig))

    bpy.context.view_layer.objects.active = rig
    if bpy.context.object.mode != "POSE":
        bpy.ops.object.mode_set(mode="POSE")

    # 2초짜리 기본 길이를 10초로 늘린다. frame_end는 make_loop이 루프 키를 찍는 자리이기도 하다.
    scene.frame_start = 1
    scene.frame_end = LAST
    check("dance:timeline", scene.frame_end == LAST,
          "%d ~ %d 프레임 (%.1f초 @ %dfps)" % (scene.frame_start, LAST, LAST / FPS, FPS))

    # 이전 실행이 남긴 커브를 지운다. 안 지우면 예전 안무의 키가 사이사이에 남아 섞인다.
    if rig.animation_data and rig.animation_data.action:
        rig.animation_data.action = None

    for index, spec in enumerate(CHOREOGRAPHY):
        frame = 1 + index * BEAT
        # frame_current에 대입하는 대신 frame_set을 쓴다. 대입은 평가를 뒤로 미뤄서, 그 다음에 넣은
        # 회전을 오퍼레이터 호출 시점의 depsgraph 갱신이 애니메이션 값으로 덮어쓴다.
        scene.frame_set(frame)
        apply_pose(rig, spec)
        bpy.ops.zepeto.key_pose()

    times = addon.keyframe_times(rig)
    check("dance:beats-keyed", len(times) == BEATS,
          "%d개 비트: %s" % (len(times), times))

    bpy.ops.zepeto.make_loop()
    times = addon.keyframe_times(rig)
    check("dance:loop-closed", LAST in times, "마지막 프레임 %d 키됨" % LAST)

    # 첫 프레임과 마지막 프레임의 포즈가 실제로 같은지 - '반복해도 안 튀는가'의 정의.
    scene.frame_set(scene.frame_start)
    first = [tuple(pb.rotation_euler) for pb in rig.pose.bones]
    scene.frame_set(LAST)
    last = [tuple(pb.rotation_euler) for pb in rig.pose.bones]
    worst = max((max(abs(a - b) for a, b in zip(f, l)) for f, l in zip(first, last)), default=0.0)
    check("dance:loop-seamless", worst < 1e-4, "첫/끝 최대 각도차 %.2e rad" % worst)

    # 정말 '춤'인가. 손과 발이 실제로 이동한 거리를 잰다. 키프레임 개수는 움직임을 증명하지 않는다 -
    # 값이 같은 키 20개도 정지 화면이다.
    travel = measure_travel(rig, scene)
    for label, distance in sorted(travel.items()):
        check("dance:moves-%s" % label, distance > 0.15, "이동 거리 %.3fm" % distance)

    # [QC] 아래 두 검사가 존재하는 이유는 위의 이동 거리 검사가 실패를 놓쳤기 때문이다.
    #
    # 첫 판은 20비트 중 13비트에서 다리를 rest 그대로 두었는데도 발 이동 거리가 1.20m로 통과했다.
    # 몇몇 비트에서만 다리를 벌렸고 그 왕복이 거리를 채웠기 때문이다. 총 이동 거리는 '얼마나
    # 움직였나'를 재지만 '내내 움직였나'는 재지 못한다. 결과는 팔만 젓고 하체는 굳어 있는,
    # 사람이 보면 바로 어색한 동작이었다 - 검사 13개가 전부 초록인 채로.
    idle = [i for i, spec in enumerate(CHOREOGRAPHY)
            if all(spec.get(b, (0, 0, 0)) == (0, 0, 0)
                   for b in ("upperLeg_L", "upperReg_R", "lowerLeg_L", "lowerReg_R"))]
    check("dance:legs-move-every-beat", not idle,
          "다리가 rest인 비트: %s" % (idle if idle else "없음"))

    # 무릎이 실제로 접혔다 펴지는가. 벌리기(Z)만으로는 서 있는 자세로 보인다.
    knee_angles = []
    for spec in CHOREOGRAPHY:
        for bone in ("lowerLeg_L", "lowerReg_R"):
            knee_angles.append(spec.get(bone, (0, 0, 0))[1])
    span = max(knee_angles) - min(knee_angles)
    check("dance:knees-actually-bend", span >= 40.0,
          "무릎 굽힘 범위 %.0f도 (최소 40도)" % span)

    # 얼굴. 이 셋만 Unity Humanoid로 건너간다(위 '얼굴에 대해' 참고). 실제로 값이 변하는지 본다 -
    # 리그에 뼈가 있다는 것과 클립에 커브가 들어간다는 것은 다른 이야기다.
    for bone, label in (("eye_L", "eyes"), ("mouth", "jaw")):
        vals = []
        for spec in CHOREOGRAPHY:
            vals.extend(spec.get(bone, (0, 0, 0)))
        face_span = max(vals) - min(vals)
        check("dance:face-moves-%s" % label, face_span >= 15.0,
              "%s 회전 범위 %.0f도" % (bone, face_span))

    # 내보내기. 애드온의 게이트를 그대로 통과해야 한다 - 우회 경로는 쓰지 않는다.
    bpy.ops.zepeto.locate_paths()
    scene.zepeto_motion_name = MOTION_NAME
    problems = addon.export_problems(scene, rig)
    check("dance:gates-pass", not problems, "막는 이유: %s" % problems)
    if problems:
        return finish()

    bpy.ops.zepeto.export()
    out = os.path.join(bpy.path.abspath(scene.zepeto_export_dir), MOTION_NAME + ".fbx")
    check("dance:fbx-written", os.path.isfile(out), out)
    if os.path.isfile(out):
        with open(out, "rb") as handle:
            head = handle.read(18)
        check("dance:binary-fbx", head.startswith(b"Kaydara FBX Binary"),
              "%r, %d bytes" % (head, os.path.getsize(out)))

    return finish()


def measure_travel(rig, scene):
    """한 바퀴 도는 동안 각 부위가 지나간 총 거리(월드 좌표)."""
    watched = ("hand_L", "hand_R", "foot_L", "foot_R", "head")
    previous = {}
    total = dict.fromkeys(watched, 0.0)
    for frame in range(scene.frame_start, LAST + 1):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        for name in watched:
            here = (rig.matrix_world @ rig.pose.bones[name].head).copy()
            if name in previous:
                total[name] += (here - previous[name]).length
            previous[name] = here
    return total


def finish():
    passed = sum(1 for _, ok, _ in results if ok)
    failed = len(results) - passed
    print("\n=== dance: pass=%d fail=%d ===" % (passed, failed))
    return failed


if __name__ == "__main__":
    main()
