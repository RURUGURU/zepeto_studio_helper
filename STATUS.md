# ZEPETO 모션 파이프라인 — 진행 상황

마지막 갱신: 2026-07-28

---

## 한 줄 요약

**Blender에서 모션을 만들어 내 ZEPETO 아바타로 확인하는 파이프라인**이 동작합니다.
왕복(Unity → Blender → Unity)은 실측으로 검증됐고, Unity 헬퍼 UI는 7단계로 재작성했습니다.

**커밋은 아직 하지 않았습니다.** 되돌릴 지점이 없는 상태입니다.

---

## ⚠️ 먼저 알아야 할 것 — 모션은 아이템으로 못 올립니다

조사 결과(공식 문서 · 스튜디오 제품 목록 · 크리에이터 프로그램 · World SDK 제스처 API ·
`naverz/zepeto-studio-global` GitHub Discussions의 제페토 직원 답변 #44/#67):

- 제페토 스튜디오의 업로드 가능 카테고리는 **전부 착용 아이템**입니다. 모션·제스처·포즈·댄스 항목이 없습니다.
- 앱 내 포즈/제스처는 `requestOfficialContentList()`로 **제페토 서버 공식 라이브러리에서만** 옵니다.
- 아이템 SDK가 노출하는 애니메이션 슬롯은 `dynamic` 하나이고 Unity 미리보기 전용입니다.

**자작 모션이 갈 수 있는 유일한 공식 목적지는 ZEPETO World입니다.**
헬퍼 7번의 `이 모션을 제페토에 넣기` 패널이 그 방법(World SDK 4단계)을 안내합니다.

> 확인 못 한 것: 비공개 파트너 모션 파이프라인의 존재 여부. 공개 문서에는 없습니다.

---

## 지금 상태

| 항목 | 값 |
| --- | --- |
| Unity | 2020.3.9f1 (`108be757e447`) |
| ZEPETO SDK | `zepeto.studio@3.2.16`, `zepeto.character@3.1.32` |
| 헬퍼 패키지 | **0.9.0** — `Packages/com.easy.zepeto-helper` |
| Blender 애드온 | **1.3.0** — `BlenderMotion/zepeto_motion_helper.py` |
| Blender | 5.2.0 LTS |
| 자체 테스트 | **60 / 60 통과** |
| 컴파일 | 에러 0, **경고 0** |
| 미커밋 변경 | **44건** (`+805 / −4276`) |
| 마지막 커밋 | `a574a42 Show play preview prominently in README` |

### 활성 계정 / 애셋

- ZEPETO 아이디: `darbams77` (씬의 `LOADER`에 저장 — EditorPrefs 저장 기능은 제거됨)
- 의상: `Assets/Contents/TRANSPARENT_1`
- 리그: `Assets/ZepetoHelper/Rig/ZepetoBaseModel.fbx` (뼈 103개, Humanoid Avatar 생성됨)

---

## 헬퍼 창 구조 (7단계)

```
1. 아바타 준비              아이디 · 의상 · 정지 중 몸 보이기
2. 동작 고르기              ZEPETO 기본 동작 목록
3. Blender용 몸 내보내기     ← 처음 한 번만
4. Blender에서 모션 만들기   ← Blender 열기 버튼
5. 내 캐릭터로 확인          ← 라이브 (Play 유지)
      └ 직접 등록하기 (Mixamo 등)
6. 클립 조정
7. 제페토로 내보내기         .zepeto + World 안내
```

**단계 잠금이 없습니다.** 조건이 안 맞아도 카드가 접히지 않고, 그 자리에 이유를 씁니다.
이전 구조에서 잠금이 여러 종류의 막다른 길을 만들었기 때문입니다.

**기본 동작만 쓸 때** 1 → 2 → (6) → 7
**직접 만들 때** 1 → **3 → 4 → 5** → (6) → 7

---

## 검증된 것 (실측)

### 라이브 왕복 — Play 중 핫리로드

`ZepetoLiveReloadRun.cs`로 실제 Play를 띄우고 도중에 FBX를 교체:

```
구간 A (팔 모션 48프레임):  arm=0.308m  leg=0.000m
구간 B (다리 모션 96프레임): arm=0.000m  leg=0.249m
클립 길이 1.96s → 3.96s, 파일 교체 후 1.4초 만에 반영
```

두 모션이 서로 다른 뼈를 움직이므로 "옛 클립이 계속 재생 중"과 구분됩니다.
**리바인드 없이 재생 중인 Animator에 반영됩니다.**

### Blender 애드온 (헤드리스 실행)

```
큐브를 씬에 남긴 채 export → F_CUBE_IN_FBX: False, F_BODY_IN_FBX: True
매핑 커버리지 54 / 103
이름 검증: '' · '   ' · 'Wave?' · 'Wave.' 전부 거부, 'Wave' 통과
쿼터니언 뼈: 변환 거부하고 보고만 (기존 커브 보존)
원자적 저장: 죽은 .part 정리 → 1.66MB binary → .part 없음
```

### 그 외

- `CopySerialized`가 `.anim`의 GUID·instanceID 유지 (자체 테스트)
- 내보낸 FBX가 binary (`Kaydara FBX Binary`) — Blender는 ASCII를 못 읽음
- 미리보기 몸이 Edit 모드에서 렌더링 (렌더러 2개 활성)

---

## 중요한 기술적 사실

### 뼈 103개 중 49개는 죽어 있습니다

`ZepetoBaseModel.fbx.meta`의 humanDescription 기준, Humanoid가 매핑하는 뼈는 **55개**.
나머지는 아무리 돌려도 **에러 없이 사라집니다.** 애드온이 기본으로 숨깁니다.

숨김 대상: 모든 `*Twist*`, 모든 `*_scale`, `pelvis`, `heel_L/R`,
그리고 `eye_L`·`eye_R`·`mouth`를 뺀 얼굴 전체.

### Hips는 Blender에서 못 돌립니다

FBX의 `hips`는 뼈가 아니라 **아마추어 오브젝트**입니다(Blender가 최상위 뼈를 오브젝트로 바꿈).
Humanoid Hips에 매핑되는 게 그 루트라, 골반 회전은 Blender에서 만들 수 없습니다.
오브젝트를 움직이면 아바타가 화면 밖으로 나갑니다. 몸통은 `spine`을 쓰세요.

### 리타게팅은 깔끔하지 않습니다

muscle 커브는 관절 **각도**라 비율 차이를 보정하지 못하고 순운동학으로 전파됩니다.
`hasTranslationDoF: 0`, `armStretch`/`legStretch`는 `0.05`뿐.

- 트위스트 뼈가 매핑에 없어 authored twist가 버려지고 `armTwist: 0.5`가 롤을 몰아줌
- muscle 클램핑 (`limit.modified: 0`)
- 발 접지 오차 (다리 길이 10% 차이에 ~4.6cm)

**이건 내 캐릭터를 내보낸다고 해결되지 않습니다.** 실제 아바타 위에서 재생해봐야 잡힙니다.

### 내 아바타 메시는 추출하지 않습니다

기술적으로는 가능합니다(프로텍터 게이트가 `!Application.isPlaying`이라 Edit 모드에선 무력).
하지만 `zepeto.asset.protector`의 목적이 명시적으로 그 차단이고 라이선스·약관이 금지합니다.
**만들지 않았습니다.** 대신 체형은 Transform 값으로 읽을 수 있고, 얼굴·옷은 Play 중 실제 아바타로 봅니다.

---

## 남은 일

### 커밋 (가장 급함)

미커밋 44건. 여러 차례 여쭤봤지만 아직 정하지 못했습니다.

- 한 번에 묶기, 또는
- 버전별 분할: `0.5.0` 리팩토링 / `0.5.1` 월드 안내 / `0.6.0` 라이브 확인 /
  `0.7.0` 아이디·Scene 몸 / `0.8.x` Stop 수정 / `0.9.0` 7단계 재작성

### 코드 리뷰 잔여 (약 20건)

전면 리뷰 98건 중 88건 확정, 배치 1(bug + dead-code)과 README를 처리했습니다.

| 남은 등급 | 대략 | 성격 |
| --- | --- | --- |
| lifecycle | 6 | 구독/해제, SessionState 누수, previewBody 소유권 |
| tests | 4 | 자체 테스트가 사라진 동작을 검사하는지 |
| inconsistency · polish | ~10 | 문구, 다듬기 |

**동작이 틀린 것은 남아 있지 않습니다.**

### 화면 미확인

번호 정정(18곳)과 배치 1 수정 이후의 7단계 화면을 아직 눈으로 보지 못했습니다.
코드만 보고 판단해서 두 번 틀린 적이 있으니(중복 렌더링, Stop 색상) 반드시 확인이 필요합니다.

---

## 작업 시 주의 (반복해서 부딪힌 것들)

**Unity에 포커스를 주기 전에 편집을 끝낼 것.** Unity는 창이 활성화될 때 자동 리프레시하는데,
파일 여러 개를 순서대로 고치는 중이면 반쯤 고쳐진 상태를 컴파일합니다. 이것 때문에 유령
컴파일 에러가 세 번 났습니다. `csc.exe`로 먼저 검증하고 넘기면 안전합니다.

```
csc: C:\Program Files\Unity\Hub\Editor\2020.3.9f1\Editor\Data\Tools\Roslyn\csc.exe
     -langversion:7.3 -nostdlib+, MonoBleedingEdge/lib/mono/4.7.1-api + Managed/UnityEngine/*
```

**Play 중에는 재컴파일이 안 됩니다.** 헬퍼가 `ScriptCompilationDuringPlay`를
`Recompile After Finished Playing`으로 바꿔놓기 때문입니다(SDK가 깨지는 걸 막으려고).
새 코드를 반영하려면 Stop이 먼저입니다.

**트리거 파일은 Play 중에 터질 수 있었습니다.** 자체 테스트가 씬을 열어서 Play 중에는 금지입니다.
지금은 4개 러너 전부 Play 가드가 있어 대기했다가 Stop 후 실행됩니다.

**Unity가 뒤에 있으면 Play가 멈춥니다** (`runInBackground: 0`). 라이브 확인은 이 설정을 켜고
Stop에서 되돌립니다. 그래도 Blender에서 보낸 뒤 **Unity 창을 다시 클릭**해야 확실합니다.

---

## 파일 위치

| 무엇 | 어디 |
| --- | --- |
| Unity 프로젝트 | `Desktop/zepeto/ZEPETO Studio Unity Project File 3.2.16` |
| 헬퍼 패키지 (git) | `.../Packages/com.easy.zepeto-helper` |
| 테스트 러너 | `.../Assets/ZepetoHelperTests/Editor` |
| Blender 작업 파일 | `Desktop/zepeto/BlenderMotion/zepeto_motion.blend` |
| 애드온 원본 | `Desktop/zepeto/BlenderMotion/zepeto_motion_helper.py` |
| 애드온 설치본 | `%APPDATA%/Blender Foundation/Blender/5.2/scripts/addons/` |
| Blender 리그 | `.../Assets/ZepetoHelper/Rig/ZepetoBaseModel.fbx` |
| Blender→Unity 모션 | `.../Assets/CustomMotions` |
| 라이브 확인 클립 | `.../Assets/ZepetoHelper/Motions/LiveFromBlender.anim` |

### 테스트 실행 방법

프로젝트 루트에 트리거 파일을 두고 Unity를 활성화하면 재컴파일 시 실행됩니다.

| 트리거 | 하는 일 |
| --- | --- |
| `zepeto-helper-selftest.trigger` | 자체 테스트 60개 → `.result.txt` |
| `zepeto-livereload.trigger` | Play 왕복 실측 (픽스처 2개 필요) |
| `zepeto-rig-export.trigger` | 리그 내보내기 |

> 재컴파일이 안 일어나면 테스트도 안 돕니다. `ZepetoCustomMotionRun.cs`의 `Serial` 상수를
> 올리면 강제로 재컴파일됩니다.

---

## 사용자 결정 사항 (기록)

- 목표: 제페토 앱의 포즈/제스처 — **불가능함을 확인**, World가 유일한 대안
- "내 캐릭터를 그대로": 작업 중 얼굴·옷이 보이는 것 **과** 체형이 맞는 것, 둘 다 중요
- 저장된 아이디 목록: 제거하고 직접 입력으로
- 정지 상태 Scene에 몸 표시: 필요함
- A/B/C 하위 단계: 번호(3·4·5)로 승격
