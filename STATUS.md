# ZEPETO 모션 파이프라인 — 진행 상황

마지막 갱신: 2026-07-30

---

## 한 줄 요약

**Blender에서 모션을 만들어 내 ZEPETO 아바타로 확인하는 파이프라인**입니다.
이번 회차에 **전 항목을 코드 기준으로 재검증**하고, 이전 문서가 틀렸던 10곳과
검증에서 새로 드러난 회귀 18건을 고쳤습니다.

**커밋 지점이 생겼습니다.** 루트에도 git 저장소를 만들어, 이전에 어떤 저장소에도 없던
애드온·`.blend`·테스트·씬·리그 meta가 이제 추적됩니다.

> **⚠️ 이 회차의 검증은 전부 정적입니다.** Unity와 Blender를 실행하지 않았습니다
> (`csc.exe` 사전 컴파일과 파일 바이트 분석만). 화면 확인과 테스트 재실행이 남아 있습니다 — [남은 일](#남은-일) 참고.

---

## ⚠️ 배포 전 차단 항목 — 개인 아이디가 캡처에 남아 있습니다

이전 회차가 "개인 아이디를 placeholder로 교체했다"고 기록했지만, **그것은 텍스트 전용이었습니다.**
`grep`으로는 패키지 어디에도 없지만 **캡처 PNG에는 픽셀로 남아 있습니다.** 직접 이미지를 열어 확인했습니다.

| 파일 | 무엇이 보이는가 |
| --- | --- |
| `docs/images/helper-window.png` | `현재 아이디` 줄과 입력칸에 실제 아이디 |
| `docs/images/step-1-avatar-outfit.png` | 같은 두 칸 |
| `docs/images/workflow-overview.png` | 위 캡처를 썸네일로 포함 |
| `docs/images/play-preview.png` | 본인 아바타의 얼굴·머리·의상 |

`.npmignore`는 `Documentation~/`만 제외하고 `docs/`는 일부러 포함하므로 **tarball에 그대로 들어갑니다.**
자체 테스트의 `no-personal-id-in-source`는 `.cs`와 `.md`만 읽으므로 **PNG를 구조적으로 볼 수 없습니다** —
초록불이 이것을 보증하지 않습니다.

**재촬영이나 마스킹은 사람이 해야 합니다. 그 전에는 발행하지 마세요.**

---

## ⚠️ 모션은 아이템으로 못 올립니다 (이전 결론 유지)

조사 결과(공식 문서 · 스튜디오 제품 목록 · 크리에이터 프로그램 · World SDK 제스처 API ·
`naverz/zepeto-studio-global` GitHub Discussions의 제페토 직원 답변 #44/#67):

- 제페토 스튜디오의 업로드 가능 카테고리는 **전부 착용 아이템**입니다. 모션·제스처·포즈·댄스 항목이 없습니다.
- 앱 내 포즈/제스처는 `requestOfficialContentList()`로 **제페토 서버 공식 라이브러리에서만** 옵니다.
- 아이템 SDK가 노출하는 애니메이션 슬롯은 `dynamic` 하나이고 Unity 미리보기 전용입니다.

**자작 모션이 갈 수 있는 유일한 공식 목적지는 ZEPETO World입니다.**
7단계가 만드는 `.zepeto`에는 **의상만** 들어갑니다(`Publish.cs`, `Export.cs`). 즉 1~6단계는
"내 옷이 움직일 때 어떻게 보이나"를 확인하는 프리뷰 하네스입니다.

> 확인 못 한 것: 비공개 파트너 모션 파이프라인의 존재 여부. 공개 문서에는 없습니다.

---

## 지금 상태

| 항목 | 값 |
| --- | --- |
| Unity | 2020.3.9f1 (`108be757e447`) |
| ZEPETO SDK | `zepeto.studio@3.2.16`, `zepeto.character@3.1.32` |
| 헬퍼 패키지 | **0.9.1** — `Packages/com.easy.zepeto-helper` (Editor 20파일 8,169줄) |
| Blender 애드온 | **1.4.0** — `BlenderMotion/zepeto_motion_helper.py` (1,154줄) |
| 테스트 | 6파일 3,198줄 (`Assets/ZepetoHelperTests`) |
| Blender 설치본 | 5.2.0 LTS — 단 애드온의 `bl_info["blender"]`는 `(4, 2, 0)` (= 최소 4.2) |
| 컴파일 검증 | `csc.exe` — 헬퍼 **에러 0 · 경고 0**, 테스트 **에러 0 · 경고 0** |
| 자체 테스트 | 기록은 **60 / 60** 이지만 **재실행 필요** (아래) |
| 마지막 커밋 | 패키지 `6b77404` · 루트 `3ede9b2` |
| 미커밋 | 패키지 25파일 (+1,934/−276) · 루트 7파일 (+1,681/−2,312) + untracked 4 |

### 두 개의 git 저장소

| 저장소 | 무엇을 추적 | 원격 |
| --- | --- | --- |
| `zepeto/.git` (신규) | 애드온 · `.blend` · STATUS.md · 테스트 · 씬 · 리그 meta · ProjectSettings · manifest | 없음 |
| `.../Packages/com.easy.zepeto-helper/.git` | 헬퍼 패키지만 | `github.com/RURUGURU/zepeto_studio_helper` — **`origin/main`은 0.2.4** |

루트 저장소는 `.gitignore`로 헬퍼 패키지 폴더를 제외합니다. 중첩 저장소를 추적하면 gitlink(내용 없는
참조)만 남아 오히려 백업이 안 되기 때문입니다. **패키지 변경은 그 폴더 안에서 커밋하세요.**

> `origin/main`이 0.2.4라서 README의 `Add package from git URL`을 따르면 Blender 파이프라인이 없는
> 4단계 헬퍼가 설치됩니다. README·ENVIRONMENT에 그 경고를 넣어뒀습니다.

### 활성 계정 / 애셋

- ZEPETO 아이디: 씬의 `LOADER`에 저장 (EditorPrefs 저장 기능은 제거됨)
- 의상: `Assets/Contents/TRANSPARENT_1` (SDK 샘플 — 예시일 뿐 필수 아님)
- 리그: `Assets/ZepetoHelper/Rig/ZepetoBaseModel.fbx` (뼈 103개, 손으로 만든 55-bone Humanoid meta)

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

**기본 동작만 쓸 때** 1 → 2 → (6) → 7
**직접 만들 때** 1 → **3 → 4 → 5** → (6) → 7

### ⚠️ 카드 번호(1~7) ≠ 내부 preview stage 번호(1~4)

```
stage 1 = 카드 1     stage 2 = 카드 2 프리뷰 AND 카드 5 라이브
stage 3 = 카드 6     stage 4 = 카드 7
```

이번 회차에 bare literal을 `PreviewStageAvatarOutfit`/`Motion`/`ClipAdjust`/`Export` 상수로 바꿨습니다
(`Workflow.cs`). **값은 그대로 유지**했습니다 — SessionState에 이미 int가 들어있을 수 있고 `Motion.cs`가 2를
넘깁니다. 카드 번호만 고치면 클립 조정 프리뷰가 조용히 죽습니다. 이 프로젝트 최대의 함정입니다.

---

## 중요한 기술적 사실

### 재생을 결정하는 것은 오버라이드 슬롯입니다

`PlaygroundController.AnimationClip`을 써도 **아바타는 움직이지 않습니다.** SDK 기본 컨트롤러의 교체
가능 슬롯은 `dynamic` 하나뿐이고 배포 기본값이 `A_pose.anim`(0.0417초)입니다. 그래서
`AssignAnimationClip`은 필드를 쓴 뒤 반드시 `ApplyClipToOverrideController`로 모든 슬롯을 덮어씁니다.
패키지 원본은 절대 안 쓰고 `EnsureLocalAnimatorController`가 프로젝트 로컬로 먼저 복사합니다.

### 뼈 103개 중 49개는 죽어 있습니다 — 산수 정정

이전 문서는 "103개 중 49개가 죽었고 Humanoid가 매핑하는 뼈는 55개"라고 적었는데 **103 − 55 = 48입니다.**
올바른 화해는 이렇습니다:

```
매핑 뼈 54 + 죽은 뼈 49 = 103
```

55번째 매핑인 `hips`는 **뼈가 아니라 아마추어 오브젝트**입니다. 애드온 코드는 이 주의를 정확히 담고
있었고(`len(MAPPED_BONES) - 1`) 산문만 빠뜨렸습니다.

숨김 대상 49개 = 모든 `*Twist*`(8) + 모든 `*_scale`(24) + `pelvis`(1) + `heel_L/R`(2) +
`eye_L`·`eye_R`·`mouth`를 뺀 얼굴 전체(14). 매핑 밖 뼈는 **에러·경고·로그 없이 사라집니다.**

### Hips는 Blender에서 못 돌립니다 (그리고 강제되지 않습니다)

FBX의 `hips`는 아마추어 **오브젝트**입니다(Blender가 최상위 뼈를 오브젝트로 변환). 몸통은 `spine`을 쓰세요.
**이 제약은 코드로 강제되지 않습니다** — `odd_bones`는 pose bone만 검사하므로 오브젝트를 움직여도
체크리스트가 발화하지 않고 `clear_pose`도 되돌리지 못합니다.

### 리타게팅은 깔끔하지 않습니다

muscle 커브는 관절 **각도**라 비율 차이를 보정하지 못하고 순운동학으로 전파됩니다.
`hasTranslationDoF: 0`, `armStretch`/`legStretch`는 `0.05`, `armTwist: 0.5`, `limit.modified: 0` ×55.
발 접지 오차는 왼다리 세그먼트 합 0.456401m의 10% ≈ **4.6cm**로 유도됩니다.

### `ZepetoRig_Wave.fbx`의 스케일 경고는 단일 전역 인자입니다

meta에 뼈별 위치 오차 경고가 2.3m~21.5m로 기록돼 있지만, 64개 경고 전부가 **하나의 상수배**
(≈174,752×, 상대 편차 6.6e-06)입니다. 실제 골격 손상이라면 모든 뼈 길이의 정확한 상수배가 될 수 없습니다.

원인: 애드온이 기본 `global_scale`로 리그를 임포트해 Blender가 cm→m 변환(0.01)을 **아마추어 오브젝트
스케일**에 얹고, export가 `apply_scale_options="FBX_SCALE_ALL"`이라 그 0.01이 `hips` Null에 그대로 남습니다.

**오늘 동작하는 이유:** Humanoid `.anim`은 `m_RotationCurves`/`m_PositionCurves`/`m_ScaleCurves`/
`m_EulerCurves`가 **전부 비어 있고** 정규화된 muscle 커브 130개만 담습니다 — 뼈 이름도 뼈 길이도
운반하지 않으므로 100배가 재생에 도달하지 않습니다.
**깨질 것:** generic/bone-space 클립(가장 가까운 위험), Translation DoF, root motion, IK goal, 발 접지.

### 내 아바타 메시는 추출하지 않습니다

헬퍼 Editor 전체에서 `SkinnedMeshRenderer|sharedMesh|BakeMesh|boneWeights` **0 hit**로 확인했습니다.
`zepeto.asset.protector`의 목적이 명시적으로 그 차단이고 라이선스·약관이 금지합니다.

> 이전 문서의 "프로텍터 게이트가 `!Application.isPlaying`이라 Edit 모드에선 무력"은 **문장 자체가
> 자기모순**입니다(`!isPlaying`이면 Edit 모드에서 오히려 *활성*). DLL이 클로즈드 소스라 극성은 확인
> 불가입니다. 어느 쪽이든 결론은 무관합니다 — Edit 모드에는 추출할 아바타 메시가 애초에 없습니다.

---

## Assets/CustomMotions 실태

| 파일 | 상태 |
| --- | --- |
| `ZepetoRig_Wave.fbx` | ✅ **유일하게 정상.** 스킨 메시 있음(Deformer 107 / Skin 1), 55/55 ZEPETO 뼈 이름, `rigImportErrors` 비어 있음 |
| `Wave_Hello.fbx` | ⚠️ 스킨 메시 **없음**(Deformer 0). generic 뼈 이름. `.meta`가 오염돼 있었음 |
| `AddonSmokeTest.fbx` | ⚠️ 같음. 초기 부트스트랩 리그의 스모크 테스트 잔재 |

### 오염 원인과 이번 수정

`importer.sourceAvatar` 대입은 **소스의 humanDescription을 대상의 `.meta`에 씁니다.** 대상 스켈레톤에
그 뼈들이 없으면 이후 모든 재임포트가 `Transform 'hips' for human bone 'Hips' not found`로 영구 실패합니다.
두 파일이 정확히 그 상태였습니다 — 원래는 잘 임포트되던 파일들입니다.

이번에 한 일:
- 오염된 `.meta` 2개 삭제 (외부 참조 0개 확인 후, git에 커밋되어 복구 가능)
- `CanCopyRigAvatarTo` 가드 추가 — 대상에 필요한 뼈 이름이 없으면 복사를 **건너뜁니다**
- 이미 오염된 자산 **복구** 경로 추가 — Unity는 거절된 복사에서 `avatarSetup`/`sourceAvatar`를 되돌리면서
  **복사된 뼈 매핑은 남기므로**, `humanDescription.human`을 실제 transform 이름과 대조해 필요 시 비웁니다

> **Mixamo 임포트는 지원 불가가 아닙니다.** 오히려 generic 뼈 이름(Hips/Spine/LeftArm)이 Unity 오토매퍼의
> 모국어입니다. `Wave_Hello.anim`이 증거입니다 — ZEPETO 뼈 이름과 0/55 겹침인데도 유효한 130커브 Humanoid
> 클립입니다. 자동 매핑이 **안 되는** 쪽이 ZEPETO 이름이고(`upperReg_R`에 leg 토큰이 없음), 그래서 리그에
> 손으로 만든 humanDescription이 필요합니다.

`Capoeira.fbx`(루트, Assets 밖)는 진짜 Mixamo 파일(Maya 2020, `mixamorig:*` 65뼈, 완전 스킨)입니다.
Mixamo 경로 테스트용 픽스처로 쓸 만하지만 `Assets/CustomMotions`(폴링됨)에는 넣지 마세요.

---

## 이번 회차에 고친 것

### 동작이 틀렸던 것

| 항목 | 증상 |
| --- | --- |
| **L1** 2단계 프리뷰 복원 | 복원 상태가 비직렬화 필드라 Play 도메인 리로드로 소멸 → **프리뷰 클립이 LOADER와 오버라이드 컨트롤러에 그대로 남고 작업 모션이 복원되지 않으며 메시지도 없음.** SessionState 백업 프로퍼티로 전환(호출부 무변경) |
| **라이브 확인이 조용히 죽음** | `Maximize On Play`(Game 뷰 기본 토글)가 켜져 있으면 Play 진입 시 레이아웃 리로드로 `OnDestroy`가 발화 → 무장 직후 disarm. `isPlaying` 조기 반환으로 수정 |
| **미드플레이 리바인드** | 위 복원이 Play 중 동기로 `ApplyOverrides`+`SaveAssets`+`ImportAsset`을 실행 — ZEPETO 컨텍스트를 끊는 동작. 같은 수정으로 해결 |
| **스탠드인 청소 무력화** | `DontSave` 게이트가 청소 대상을 정확히 전부 제외 (Unity는 `DontSave`를 직렬화하지 않으므로 생존자는 그 플래그를 잃은 상태). 게이트 제거 |
| **리그 export 실패가 초록** | 거절 삭제가 바이너리 검증 분기에만 연결. 모든 실패 분기를 한 출구로 모음 |
| **6단계 도달 불가** | 라이브·임포트 모션은 `Motions/`에 떨어지는데 편집 자격은 `Animations/`만 요구 → 영구 경고. `IsClipEditEligiblePath`로 두 루트 허용(저장 대상은 `ClipEdits/` 유지) |
| **Loop 저장 불가** | `clipLoop` 기본값이 `true`인데 `loopChanged = !clipLoop`이라 Loop를 켜면 변경 없음으로 판정 → **클립을 안 쓰고 단계만 완료 표시.** 원본 클립의 실제 `loopTime`을 기준으로 seed |
| **애드온이 rest pose를 뭉갬** | `clear_pose`가 baseline에 없는 **모든** 뼈의 location/scale 초기화 → 스냅샷 없는 씬에서 전체 평탄화. 패널이 지목한 집합만 되돌리도록 수정 |
| **애드온 경로 오선택** | 후보 여럿일 때 알파벳 첫 번째를 조용히 선택 → 프로젝트 사본이 둘이면 Unity가 안 보는 쪽으로 export. 근거 기반 순위 + 동점이면 사용자에게 보고 |
| **테스트 거절이 기준선 파괴** | dirty 씬 거절이 결과 파일을 `pass=0 fail=0`으로 덮어써서 거절과 통과가 구별 불가. `.skipped.txt`로 분리 |
| **러너 2차 실행** | `StartKey` 미리셋으로 2회차 즉시 타임아웃, min/max 키 미리셋으로 **이전 실행 극값을 물려받아 거짓 PASS** 가능 |

### 하드코딩 제거

애드온의 `UNITY_PROJECT = r"C:\Users\Jun-WN\..."` — **다른 사용자 계정 경로**였습니다. 이 머신은 `darba`이고
그 경로는 존재하지 않습니다. 새 `.blend`나 새 씬에서는 라운드트립이 즉사했습니다. 런타임 유도로 교체:
환경변수 `ZEPETO_UNITY_PROJECT` → `.blend` 위치에서 상향 탐색 → 애드온 파일 위치. `경로 자동 찾기`
오퍼레이터와 `저장 폴더` 패널 노출도 추가(이전에는 UI로 고칠 방법이 없었음).

### 문서

개인 아이디 텍스트 제거, 버전 동기화(0.3.0/0.3.2 → 0.9.1), 자체 테스트 수치 정정(51 → 60),
파일 구조표 20파일 재작성, 단계 번호 1~4 → 1~7, 스니펫 SDK 버전 3.2.12 → 3.2.16(다운그레이드 유발),
설치 확인 절차를 임베디드 패키지 실태에 맞게 수정(`manifest.json`에 항목이 **없는 것이 정상**),
캡처 staleness 정직하게 공개(7장 전부 4단계 시대).

---

## 남은 일

### 1. 화면·실행 검증 (가장 급함)

**이번 회차는 Unity를 한 번도 띄우지 않았습니다.** 컴파일이 깨끗한 것은 정확한 것과 다릅니다 —
실제로 이번에 잡은 치명 2건이 둘 다 경고 없이 통과하던 것들입니다.

- [ ] 헬퍼 창 7단계 육안 확인 (헤더 `현재 작업` 문구, 카드 번호, Stop 버튼)
- [ ] `zepeto-helper-selftest.trigger`로 자체 테스트 재실행 → 결과 파일 재기록
      (검사 60개·이름은 유지했으나 NOTE 5줄이 새로 추가될 예정이라 **기록이 내용상 낡았습니다**)
- [ ] `zepeto-rig-export.trigger` 재실행 (이번에 assertion 4개가 새로 생겼습니다)
- [ ] Blender에서 애드온 1.4.0 로드 → 경로 자동 유도, `경로 자동 찾기`, `clear_pose` 확인
- [ ] 라운드트립 1회 완주 (3 → 4 → 5)

### 2. 캡처 4장 재촬영 또는 마스킹

[위](#️-배포-전-차단-항목--개인-아이디가-캡처에-남아-있습니다) 참고. **발행 전 필수.**

### 3. 라이브 왕복 픽스처 복원

`zepeto-live-a.fbx`(48프레임, 오른팔만) / `zepeto-live-b.fbx`(96프레임, 왼다리만)가 **존재하지 않아**
`ZepetoLiveReloadRun`이 즉시 중단됩니다. 두 픽스처가 서로 **다른 뼈**를 움직여야 하는 이유는
"핫리로드 실패"와 "아무것도 안 움직임"을 구별하기 위한 것입니다. 사양은 러너 파일 상단 주석에 적어뒀습니다.

> 이전 문서가 실측으로 인용한 `arm=0.308m / leg=0.249m / 1.4초 반영`은 **산문에만 존재합니다.**
> 결과 파일이 없고 픽스처가 없어 현재 재현 불가입니다. (소수점 자릿수와 48/96프레임이 코드 포맷과
> 정확히 일치해 실제 실행의 흔적으로는 일관됩니다.)
> 같은 이유로 `F_CUBE_IN_FBX` / `F_BODY_IN_FBX` 값도 출처가 없습니다 — 그 토큰의 유일한 등장 위치가
> 이전 STATUS.md 자신이었고, 헤드리스 Blender 테스트 스크립트가 존재하지 않습니다.

### 4. 커밋

미커밋: 패키지 25파일(+1,934/−276), 루트 7파일(+1,681/−2,312) + untracked 4.

### 5. 그 외

- `Videobooth_282_editable.anim` 26.3MB + 편집본 14.8MB — `ClipEdits/`가 쌓이면 편집마다 수십 MB
- `zepeto-studio-unity-3.2.12/` 빈 디렉터리 (git이 빈 폴더를 추적하지 않아 커밋에서는 이미 사라짐)
- 애드온 `iter_fcurves`의 Blender 4.4+ 슬롯 액션 분기는 아마 죽은 코드 — 실제 5.2에서 미검증
- `.zepeto` export 실제 실행 + Studio 업로드 확인 (계정 로그인 필요)
- Humanoid muscle 클램핑이 재생 시점에 일어나는지 (클램프 경고 감지기는 제거된 상태)

---

## 작업 시 주의 (반복해서 부딪힌 것들)

**Unity에 포커스를 주기 전에 편집을 끝낼 것.** Unity는 창이 활성화될 때 자동 리프레시하는데,
파일 여러 개를 순서대로 고치는 중이면 반쯤 고쳐진 상태를 컴파일합니다. 이것 때문에 유령 컴파일 에러가
세 번 났습니다. `csc.exe`로 먼저 검증하고 넘기면 안전합니다.

```
csc: C:\Program Files\Unity\Hub\Editor\2020.3.9f1\Editor\Data\Tools\Roslyn\csc.exe
     -langversion:7.3 -nostdlib+, MonoBleedingEdge/lib/mono/4.7.1-api + Managed/UnityEngine/*
     테스트를 컴파일할 때는 helper.dll을 -r: 로 넘겨야 합니다 (네임스페이스를 using 하므로)
```

**Play 중에는 재컴파일이 안 됩니다.** 헬퍼가 `ScriptCompilationDuringPlay`를
`Recompile After Finished Playing`으로 바꿔놓기 때문입니다(SDK가 깨지는 걸 막으려고).
새 코드를 반영하려면 Stop이 먼저입니다. **이 설정은 Unity 전역이고 복원되지 않습니다** — 사용자가 여는
모든 다른 Unity 프로젝트에 영향합니다.

**떠도는 트리거 파일은 위험합니다.** 무장된 트리거는 아무 재컴파일에서 자동 발화합니다. 이번에 dirty 씬
거절을 4개 러너 전부에 넣었지만, 그래도 트리거를 방치하지 마세요. `.gitignore`에 `*.trigger`를 넣었습니다.

**Unity가 뒤에 있으면 Play가 멈춥니다** (`runInBackground: 0`). 라이브 확인은 이 설정을 켜고 Stop에서
되돌립니다. 그래도 Blender에서 보낸 뒤 **Unity 창을 다시 클릭**해야 확실합니다.

**재컴파일이 곧 테스트의 시계입니다.** 재컴파일이 안 일어나면 트리거도 안 돕니다.
러너의 `Serial` 상수를 올리면 강제됩니다.

---

## 만질 때 반드시 알아야 할 함정

1. **`AnimationClip` 필드만 써서는 아무 일도 안 일어납니다.** 오버라이드 슬롯 재작성만이 재생을 바꿉니다.
2. **카드 번호(1~7) ≠ preview stage(1~4).** stage 3 = 카드 6, stage 4 = 카드 7. 카드 번호만 고치면
   클립 조정 프리뷰가 조용히 죽습니다.
3. **컨트롤을 조건부로 그리지 마세요.** 고정 순서로 무조건 그리고 `enabled`/라벨/`MessageType`만 바꿉니다.
   스냅샷은 그리는 중에 2초 타이머로 자기 갱신되고 `sessionErrorCount`는 로그 콜백에서 변합니다. 어기면
   하필 Stop 버튼이 사라집니다. 비활성 컨트롤에는 **반드시** 사유 문자열을 붙이세요.
4. **OnGUI 안에서 씬을 변경하지 마세요.** `EditorApplication.delayCall`로 미룹니다.
5. **Play 진입은 도메인 리로드입니다.** 교차 Play 상태는 SessionState, 애셋 참조는 `[SerializeField]`.
   EditorPrefs는 세션 상태에 부적합합니다(이전 세션의 값이 엉뚱한 애셋을 씬에 씁니다).
6. **`OnEnable`에서는 `isPlayingOrWillChangePlaymode`를 보세요**, `isPlaying`이 아닙니다.
   OnEnable은 Play에 *진입하는* 리로드 중에도 돕니다.
7. **`OnDestroy`는 "사용자가 창을 닫았다"가 아닙니다.** 창 레이아웃 리로드(`Maximize On Play`,
   `Window > Layouts`)에서도 발화합니다. 이번 치명 버그의 정체입니다.
8. **경로 상수 3개를 절대 합치지 마세요.** `Assets/CustomMotions`(Blender 드롭존, 폴링) ≠
   `ZepetoHelper/Motions`(추출) ≠ `ZepetoHelper/Animations`(편집 자격).
9. **`.part` 접미는 양쪽 계약입니다.** Windows 8.3 단축명 때문에 `*.fbx` 패턴이 `X.fbx.part`도 잡습니다.
   Unity 쪽 두 열거 지점의 스킵을 제거하면 부분 파일을 임포트합니다.
10. **`importer.clipAnimations` 쓰기가 take를 핀 고정합니다.** 48→96프레임 변경이 반영되지 않아
    `RefreshClipRangeIfStale`가 존재합니다 — 한쪽을 리팩터하면 반드시 다른 쪽을 보세요.
11. **Avatar "Copy From Other"는 절대 성립하지 않습니다.** Unity가 루트를 파일명으로 명명해 항상 1개
    차이가 납니다. 그래서 `ConfigureMotionFolderForLivePreview`의 sourceAvatar 분기는 수렴하지 않고,
    arm할 때마다 폴더 전체를 2~3회 `SaveAndReimport` 합니다(프로그레스 바가 있는 이유).
12. **오른다리 뼈 이름은 오타입니다** — `upperReg_R`/`lowerReg_R`(왼쪽은 `upperLeg_L`).
    **`_L`→`_R` 치환 미러링은 반드시 깨집니다.** 고치면 Unity 매핑이 깨지니 고치지 마세요.
13. **Humanoid `Jaw`가 `mouth` 뼈에 매핑**되어 있습니다(`jaw` 뼈가 따로 있는데도). SDK 클립이 `mouth`를
    애니메이션하므로 "고치면" 회귀입니다.
14. **매핑 밖 뼈는 조용히 사라집니다** — 에러·경고·로그 없음. 103개 중 49개.
15. **자체 테스트는 리플렉션으로만 헬퍼에 접근합니다** — 제거된 멤버의 *부재*를 단정할 수 있어야 하기
    때문입니다. 테스트가 참조하는 멤버는 이름·시그니처를 그대로 유지하세요.
16. **모든 쓰기가 undo를 두 번 등록합니다**(`Undo.RecordObject` + `ApplyModifiedProperties`).
    그리고 `ApplyClipToOverrideController`는 모션 선택의 부수효과로 `AssetDatabase.SaveAssets()`를 호출합니다.

---

## 파일 위치

| 무엇 | 어디 |
| --- | --- |
| Unity 프로젝트 | `Desktop/zepeto/ZEPETO Studio Unity Project File 3.2.16` |
| 헬퍼 패키지 (자체 git) | `.../Packages/com.easy.zepeto-helper` |
| 테스트 러너 | `.../Assets/ZepetoHelperTests/Editor` (+ 공용 `ZepetoSelfTestSceneGuard.cs`) |
| Blender 작업 파일 | `Desktop/zepeto/BlenderMotion/zepeto_motion.blend` |
| 애드온 원본 | `Desktop/zepeto/BlenderMotion/zepeto_motion_helper.py` |
| Blender 리그 | `.../Assets/ZepetoHelper/Rig/ZepetoBaseModel.fbx` |
| Blender→Unity 드롭존 | `.../Assets/CustomMotions` |
| 라이브 확인 클립 | `.../Assets/ZepetoHelper/Motions/LiveFromBlender.anim` |

> **애드온 설치본은 없습니다.** `%APPDATA%/Blender Foundation/Blender/5.2` 아래에 `config`만 있고
> `scripts/addons`가 없습니다. 전 파일시스템에 `zepeto_motion_helper.py`는 `BlenderMotion/`의 1개뿐이라,
> 소스 폴더에서 직접 로드하는 방식으로 쓰이고 있습니다. (이전 문서는 설치본 경로가 있다고 적었습니다.)

### 테스트 실행 방법

프로젝트 루트에 트리거 파일을 두고 Unity를 활성화하면 재컴파일 시 실행됩니다.

| 트리거 | 하는 일 |
| --- | --- |
| `zepeto-helper-selftest.trigger` | 자체 테스트 60개 → `.result.txt` (거절 시 `.skipped.txt`) |
| `zepeto-livereload.trigger` | Play 왕복 실측 (**픽스처 2개 필요 — 현재 없음**) |
| `zepeto-rig-export.trigger` | 리그 내보내기 + assertion 4개 |
| `zepeto-custom-motion.trigger` | 커스텀 모션 end-to-end |

---

## 사용자 결정 사항 (기록)

- 목표: 제페토 앱의 포즈/제스처 — **불가능함을 확인**, World가 유일한 대안
- "내 캐릭터를 그대로": 작업 중 얼굴·옷이 보이는 것 **과** 체형이 맞는 것, 둘 다 중요
- 저장된 아이디 목록: 제거하고 직접 입력으로
- 정지 상태 Scene에 몸 표시: 필요함
- A/B/C 하위 단계: 번호(3·4·5)로 승격
- 커밋 전략: 버전별 분할은 **불가능**(19개 신규 partial을 git이 본 적이 없어 중간 상태를 재구성할 수 없음)
  → 0.9.0 단일 커밋 + 메시지에 버전 시리즈 기록, 루트는 신규 저장소
- 6단계 도달 불가 해결: 편집 자격을 `Motions/`까지 **넓힘**(저장 대상은 `ClipEdits/` 유지)
- 실패 FBX 처리: FBX는 보존하고 **오염된 `.meta`만 삭제** + 재오염 방지 가드
