# ZEPETO 모션 파이프라인 — 진행 상황

마지막 갱신: 2026-08-01

## 이 문서 읽는 순서

| 알고 싶은 것 | 볼 곳 |
| --- | --- |
| 이게 무슨 프로젝트인가 | `한 줄 요약` → `헬퍼 창 구조 (7단계)` |
| 지금 무엇이 돌아가는가 | `양쪽을 실제로 실행해서 검증했습니다` (실행 결과) → `지금 상태` (버전) |
| **코드를 만지기 전에** | **`만질 때 반드시 알아야 할 함정` 16개 — 여기만은 건너뛰지 마세요** |
| 왜 이렇게 되어 있는가 | `중요한 기술적 사실` (전부 실측 기록입니다) |
| 무엇이 남았는가 | `남은 일` — 그 아래 `다시 하지 않아도 되는 것`도 같이 보세요 |
| 테스트를 어떻게 돌리나 | `테스트 실행 방법` |

이 문서는 **저장소 전체**(Blender 애드온 · 테스트 · 씬 · 리그 · 헬퍼 패키지)를 다룹니다.
헬퍼 패키지 자체의 사용법 · 설치 · 캡처는 `Packages/com.easy.zepeto-helper/README.md`,
검증 기록은 같은 폴더의 `Documentation~/QA_AUDIT.md`가 원본이고 여기서 옮겨 적지 않습니다.
헬퍼 코드가 왜 그렇게 되어 있는지는 각 파일 머리와 `[AUDIT]`/`[QC]` 주석이 원본입니다.

---

## 한 줄 요약

**Blender에서 모션을 만들어 내 ZEPETO 아바타로 확인하는 파이프라인**입니다.
0.9.1 회차에 **전 항목을 코드 기준으로 재검증**하고, 이전 문서가 틀렸던 10곳과
검증에서 새로 드러난 회귀 18건을 고쳤습니다. 이 문서에서 "이번 회차"는 그 0.9.1을 가리킵니다.
그 뒤의 0.10.0 정리 회차(죽은 코드 제거 · 확정 결함 수정 · 주석 한국어화 · **애드온 1.5.0**)는
`Packages/com.easy.zepeto-helper/CHANGELOG.md`가 기록합니다. 애드온 쪽은 정리만이 아니라 동작이
바뀌었고, 게이트 셋이 1.4.0에서 나가던 내보내기를 거절합니다 — 목록은 그 CHANGELOG의 0.10.0 항목입니다.

**커밋 지점이 생겼습니다.** 루트에도 git 저장소를 만들어, 이전에 어떤 저장소에도 없던
애드온·`.blend`·테스트·씬·리그 meta가 이제 추적됩니다.

**양쪽을 실제로 실행해서 검증했습니다.**

| 무엇 | 결과 |
| --- | --- |
| Blender 애드온 (헤드리스 5.2.0 LTS) | **29 / 29 통과** — `BlenderMotion/headless_check.py` (`%TEMP%\zepeto_headless_check\result.txt`의 `pass=29 fail=0`) |
| Blender 패널 draw (창 있는 5.2.0 LTS) | **17 / 17 통과** — `BlenderMotion/ui_check.py`. 헤드리스가 닿을 수 없는 유일한 구간입니다 (아래 상자) |
| **초보자 왕복** (배포되는 `.blend` 그대로) | **15 / 15 통과** — `BlenderMotion/beginner_check.py`. `README.md`가 적어 둔 순서대로만 눌러서 FBX까지 갑니다 (아래 상자) |
| **10초 안무 실제 제작** | **15 / 15 통과** — `BlenderMotion/make_dance.py`. 240프레임 20비트(120 BPM), 루프 각도차 `0.00e+00`, 손 이동 7.8m, **발 이동 3.6m / 3.2m**. Unity에서 9.96초 Humanoid 클립으로 임포트되고 **Play 중 실제 아바타가 그대로 춤춥니다** (`docs/images/dance-on-avatar.gif`가 그 화면입니다). 검사 2개(`legs-move-every-beat` · `knees-actually-bend`)는 첫 판이 20비트 중 13비트에서 다리를 rest로 두고도 통과했기 때문에 추가한 것입니다 |
| Unity 자체 테스트 | **전 항목 통과** — `zepeto-helper-selftest.result.txt`의 `pass=`/`fail=` 집계. 개수와 그룹별 명세는 `Documentation~/QA_AUDIT.md`의 `최근 결과`가 원본이고 여기서 옮겨 적지 않습니다 |
| 리그 export 러너 | **4 / 4 통과** — 바이너리 검증 포함, 씬 오염 없음 |
| **라이브 왕복 실측** | **통과** — 팔 0.272m / 다리 0.195m, 1.96s→3.96s, **1.3초** 반영 |
| 커스텀 모션 end-to-end | **통과** — 손 이동 0.373m, 씬 완전 복원 |
| 컴파일 (`csc.exe` + Unity 양쪽) | 에러 0 · 경고 0 |
| 헬퍼 창 7단계 육안 확인 | 완료 (캡처 8장, 그중 7장이 신규·재촬영) |

> ### 부품이 다 멀쩡해도 초보자가 못 시작할 수 있습니다
>
> `headless_check.py`는 깨끗한 씬을 새로 만들고 리그를 직접 임포트해서 **부품**을 검사합니다. 그래서
> "배포되는 `zepeto_motion.blend`으로 시작조차 못 하는" 상태를 구조적으로 볼 수 없습니다. 실제로 그
> 상태였습니다 — 그 `.blend`의 `zepeto_rig_fbx`에 **`C:\Users\Jun-WN\...`** 가 저장돼 있었습니다.
> 다른 사람의 계정 이름이고 이 컴퓨터에 없는 경로인데, 추적되는 파일입니다. 검사 28개가 전부 통과하는
> 동안 내내 그랬습니다.
>
> 그 값은 `scene.keys()`나 `scene.get()`으로는 **보이지 않습니다.** Blender는 `bpy.types.Scene`에 등록한
> RNA 프로퍼티를 시스템 IDProperty로 저장하고, `keys()`는 사용자가 손으로 넣은 커스텀 프로퍼티만
> 돌려줍니다. 속성으로 직접 읽고 써야 하며, 그러려면 애드온이 켜져 있어야 합니다.
> 그리고 `.blend`은 zstd 압축본이라 평문 검색으로도 안 걸립니다.
> 저장할 때 `compress=True`를 빠뜨리면 876KB가 2.9MB로 부풀면서 diff가 파일 전체가 됩니다.
>
> ```
> "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" \
>     --background BlenderMotion\zepeto_motion.blend --python BlenderMotion\beginner_check.py
> ```
>
> `--factory-startup`을 붙이면 안 됩니다. 이 검사가 묻는 것 중 하나가 "설치된 애드온이 켜진 채로
> 열리는가"입니다. 내부 함수를 부르지 않고 **오퍼레이터만** 부르는 것이 규칙입니다 — 사용자가 버튼을
> 누르는 것과 같은 경로여야 이 검사가 무언가를 증명합니다. 두 경로 프로퍼티가 비어 있는지도 함께
> 단언하므로, 절대 경로가 다시 저장되면 여기서 실패합니다.
>
> ### 애드온은 이제 이 컴퓨터의 Blender에 실제로 설치돼 있습니다
>
> 이번 회차 전까지 애드온은 **한 번도 설치된 적이 없었습니다.**
> `%APPDATA%\Blender Foundation\Blender\5.2\scripts\addons\`가 비어 있었고 설정 폴더도 비어 있었습니다
> (= 저장된 환경설정 자체가 없었습니다). 그래서 Blender를 열어도 사이드바에 `ZEPETO` 탭이 없었고,
> 헤드리스 검사 28개는 전부 `sys.path`에 폴더를 끼워 넣고 모듈을 직접 import해서 통과한 것이었습니다.
> **검사가 통과한다는 것과 사용자가 쓸 수 있다는 것은 다른 문장이었습니다.**
>
> ```
> "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background \
>     --python BlenderMotion\install_addon.py
> ```
>
> Blender의 설치는 링크가 아니라 **복사**입니다. `BlenderMotion\zepeto_motion_helper.py`를 고친 뒤
> 다시 설치하지 않으면 Blender는 낡은 사본을 계속 돌립니다 — 에러가 나지 않으므로 스스로 드러나지
> 않습니다. `headless_check.py`의 `install:copy-matches-source`가 두 파일을 비교해서 그 상태를 실패로
> 만듭니다(설치돼 있지 않은 것 자체는 실패가 아니라 NOTE입니다).
>
> ### 패널 `draw()`는 창이 있어야만 실행됩니다
>
> `--background`에는 창도 영역도 없고, `UILayout`은 실제로 그려지는 영역 안에서만 만들어집니다.
> 그래서 헤드리스 검사는 오퍼레이터와 순수 함수만 덮었고, **사용자가 실제로 보는 패널은 아무도 실행해
> 본 적이 없었습니다.** `draw` 안에서 난 예외는 Blender를 죽이지 않고 콘솔에 traceback만 찍은 뒤 그
> 패널을 반쯤 그리다 말기 때문에, 증상은 "버튼이 안 보여요"로만 나타납니다.
>
> ```
> "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" \
>     --factory-startup --python BlenderMotion\ui_check.py
> ```
>
> `--background`를 붙이면 안 됩니다. 리그 없음 / 경로 깨짐 / 리그 있음 / 숨긴 뼈 보기 / 저장 폴더 깨짐 /
> 이름 잘못됨 6가지 상태를 강제로 그려 보고, 각 상태에서 어떤 버튼이 실제로 layout에 들어갔는지까지
> 셉니다. 검사가 스스로 죽어 0번 그리고 통과하는 것을 막으려고 `ui:draws-at-all`이 draw 호출 횟수를
> 따로 단언합니다 — 실제로 첫 판이 그렇게 거짓 실패했습니다.
>
> ### Unity Personal은 `-batchmode`를 쓸 수 없습니다
>
> 라이선스는 유효합니다(Personal, `%LOCALAPPDATA%\Unity\licenses\UnityEntitlementLicense.xml`).
> 그런데 `-batchmode`는 엔타이틀먼트 라이선싱이 성공한 뒤에도 거부합니다:
> `BatchMode: Unity has not been activated with a valid License`.
> **GUI 모드 + `-executeMethod`는 정상 동작합니다** — 이 회차의 모든 Unity 검증을 그 방식으로 했습니다.
>
> ```
> Unity.exe -projectPath "<프로젝트>" -logFile <로그> -quit \
>     -executeMethod Easy.ZepetoHelper.SelfTestEditor.ZepetoHelperSelfTest.Run
> ```
>
> 자체 테스트는 `[MenuItem]`이 붙은 `public static Run()`이라 트리거 파일이나 재컴파일 없이 바로
> 호출됩니다. Unity Hub가 떠 있어야 라이선싱 클라이언트 IPC가 5초 타임아웃에 걸리지 않습니다.

---

## 캡처 이미지 — 정리 완료

이전 회차가 "개인 아이디를 placeholder로 교체했다"고 기록했지만 **그것은 텍스트 전용이었고**, 캡처 PNG에는
픽셀로 남아 있었습니다. 이번에 **Unity를 띄워 8장 중 7장을 새로 만들었습니다** — 씬 `LOADER`의 `zepetoId`를
`my_zepeto_id`로 바꾼 상태에서 촬영하고 원래 값으로 되돌렸습니다. 전부 현재 7단계 UI이고,
**배포를 막는 항목은 남아 있지 않습니다.**

파일별 내역은 **패키지 README의 `캡처 이미지에 대하여` 표 한 곳에서만** 관리합니다. 예전에는 같은 표를
README·QA_AUDIT·이 문서가 각자 들고 있다가 장수와 상태가 서로 어긋났습니다. 캡처를 바꿨다면 갱신할 곳은
그 표입니다.

> 자체 테스트의 `no-personal-id-in-source`는 `.cs`와 `.md`만 읽으므로 **PNG를 구조적으로 볼 수 없습니다.**
> 왜 그런지는 `Documentation~/QA_AUDIT.md`의 `자체 테스트가 이것을 잡을 수 없는 이유`에 적혀 있습니다.
> `play-preview.png`에 제작자 아바타가 보이는 것은 공개 허용 판단을 받은 **의도된 상태**입니다.

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
| 헬퍼 패키지 | **0.10.1** — `Packages/com.easy.zepeto-helper` (Editor 20파일) |
| Blender 애드온 | **1.5.1** — `BlenderMotion/zepeto_motion_helper.py` (`bl_info`의 `version = (1, 5, 1)`) |
| 테스트 | `Assets/ZepetoHelperTests` — `.cs` 6파일 + **어셈블리 정의 2개** (아래) |
| Blender 설치본 | 5.2.0 LTS — 단 애드온의 `bl_info["blender"]`는 `(4, 2, 0)` (= 최소 4.2) |
| 컴파일 검증 | `csc.exe` — 헬퍼 **에러 0 · 경고 0**, 테스트 **에러 0 · 경고 0** |
| 자체 테스트 | 위 `양쪽을 실제로 실행해서 검증했습니다` 표 참고 (개수는 `Documentation~/QA_AUDIT.md`) |
| git 상태 | 아래 참고 — 해시는 이 문서에 적지 않습니다 |

> **커밋 해시 · 미커밋 통계 · 줄 수를 여기 적지 않는 이유:** 다음 커밋이 들어오는 순간 거짓이 되기
> 때문입니다. 실제로 두 저장소의 해시가 각각 4개·여러 개 뒤처진 채로, 존재하지 않는 미커밋 diff까지
> 적혀 있었습니다. 줄 수도 같은 방식으로 썩습니다 — 정리 회차 **하나**가 여기 있던 `8,169 / 3,198 / 1,154`을
> 전부 수백 줄씩 틀리게 만들었고, 같은 표에서 헬퍼 버전 행만 갱신된 탓에 옆 행은 일부러 그대로 둔 것처럼 보였습니다.
> `git log --oneline -3`과 `git status --porcelain`을, 줄 수가 필요하면 `wc -l`을 보세요.
> **파일 개수(20 / 6 / 2)는 남깁니다** — 어셈블리 구성과 `QA_AUDIT.md`의 파일 구조 표가 그 값에 걸려 있습니다.

### 테스트 폴더는 어셈블리 **2개**입니다 (한 개로 만들면 깨집니다)

| 어셈블리 정의 | `includePlatforms` | 들어 있는 것 |
| --- | --- | --- |
| `ZepetoHelperTests/Easy.ZepetoHelper.Tests.asmdef` | `[]` (= 런타임 포함) | `ZepetoHelperTestLoader.cs` — 씬에 붙는 MonoBehaviour |
| `ZepetoHelperTests/Editor/Easy.ZepetoHelper.Tests.Editor.asmdef` | `["Editor"]` | 러너 4개 (`ZepetoHelperSelfTest` · `ZepetoRigExportRun` · `ZepetoLiveReloadRun` · `ZepetoCustomMotionRun`) + 공용 가드 `ZepetoSelfTestSceneGuard` (러너가 아니라 넷이 함께 부르는 씬 가드입니다) |

폴더 루트에 `["Editor"]` 하나만 두면 **런타임 MonoBehaviour까지 Editor 전용이 되어 `AddComponent`가
null을 반환하고** 자체 테스트가 NRE로 중단됩니다. 실제로 그렇게 만들어 24번째 검사에서 멈춘 적이
있습니다. 테스트에 파일을 추가할 때는 그 파일이 씬에 붙는지부터 보고 어느 폴더에 넣을지 정하세요.

### 두 개의 git 저장소

| 저장소 | 무엇을 추적 | 원격 |
| --- | --- | --- |
| `zepeto/.git` | **전부** — 애드온 · `.blend` · STATUS.md · 테스트 · 씬 · 리그 meta · ProjectSettings · manifest · **헬퍼 패키지** | `github.com/RURUGURU/zepeto_studio_helper` |

**저장소는 하나입니다.** 예전에는 둘이었습니다 — 헬퍼 패키지가 자체 `.git`을 갖고 있었고 루트가
그 폴더를 `.gitignore`로 제외했습니다. 중첩 저장소를 추적하면 gitlink(내용 없는 참조)만 남기 때문에
그 자체는 타당한 결정이었지만, 대가가 있었습니다: **루트만 클론하면 그 폴더가 비어서
`Assets/ZepetoHelperTests`가 참조하는 `Easy.ZepetoHelper.Editor` 어셈블리가 없고 Unity가 컴파일
에러를 냈습니다.** 클론 두 번을 정확히 기억해야만 열리는 프로젝트였습니다.

`git subtree add --prefix=...`로 패키지 커밋 전부를 제자리 경로에 붙였습니다. 패키지 저장소의 마지막
커밋이 지금 히스토리의 **조상**이라 force push 없이 올라갔고, 양쪽 커밋이 한 DAG 안에 다 살아 있습니다.

> **주의:** `git log -- 'ZEPETO*/Packages/com.easy.zepeto-helper'`로는 합치기 이전 커밋이 안 보입니다.
> 그 커밋들은 파일 경로가 저장소 루트 기준(`CHANGELOG.md` 등)이라 경로 필터에 걸리지 않습니다.
> 필터 없이 `git log`를 보거나 합치기 커밋의 두 번째 부모를 따라가세요.

앞으로 패키지 변경은 **다른 파일과 똑같이** 이 저장소에서 커밋하면 됩니다.

> ~~`origin/main`이 0.2.4~~ → **해소됐습니다.** `origin/main`은 이제 이 저장소 자신이고 패키지는
> `0.10.1`입니다. 대신 새 제약이 생겼습니다: 저장소 루트가 더 이상 패키지가 아니라서 **git URL
> 설치는 `?path=`가 필요하고, 그 경로에 공백이 있어 검증하지 못했습니다.** 패키지 README가 그
> 사실을 명시하고 확실한 대안(폴더째 복사)을 안내합니다.

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

**값은 절대 다시 매기지 마세요.** SessionState에 이미 int가 들어 있을 수 있고 `Motion.cs`가 2를 넘깁니다.
카드 번호에 맞춰 값을 고치면 클립 조정 프리뷰가 조용히 죽습니다 — 이 프로젝트 최대의 함정입니다.
상수(`PreviewStageAvatarOutfit`/`Motion`/`ClipAdjust`/`Export`)와 그 이유는 `Workflow.cs` 머리 주석에 있습니다.

---

## 중요한 기술적 사실

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

### Hips는 Blender에서 못 돌립니다 (오브젝트를 끌고 가면 1.5.0부터 막힙니다)

FBX의 `hips`는 아마추어 **오브젝트**입니다(Blender가 최상위 뼈를 오브젝트로 변환). 뼈로는 포즈를 잡을
방법이 아예 없으니 몸통은 `spine`을 쓰세요.

**대신 오브젝트를 끌고 가는 쪽은 이제 강제됩니다.** `odd_bones`가 pose bone만 보는 것은 그대로지만,
애드온 1.5.0의 `object_moved`가 리그 오브젝트의 위치·크기를 `zepeto_baseline_object` 스냅샷과 대조합니다
(임계값 0.01 / 0.05로 `odd_bones`와 같습니다). `export_problems`가 패널과 `ZEPETO_OT_export` **양쪽에서**
불리므로 체크리스트에 `리그 오브젝트가 움직였습니다`가 뜨고 내보내기가 거절되며, `포즈 전부 되돌리기`가
그 스냅샷으로 되돌립니다(`clear_pose`).

> **남은 구멍 하나는 정직하게 적어 둡니다.** 스냅샷은 `1단계 · 몸 불러오기`가 찍습니다. `object_baseline()`이
> None이면 — 이 기능 이전에 저장된 `.blend`, append/link로 들여온 리그, 1단계를 건너뛴 씬 — 원래 자리를
> 모르므로 **경고도 복구도 없습니다.** 모르면서 경고하면 아무 잘못도 안 한 사용자를 잡게 되므로 의도된
> 선택입니다. 1단계를 한 번 누르면 스냅샷이 생깁니다.

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

### 라이브 왕복에는 실측치가 있습니다

`BlenderMotion/make_live_fixtures.py`가 만드는 픽스처 2개로, Play를 켜둔 채 FBX를 갈아끼운 결과입니다.

```
clip length: 1.96s -> 3.96s                       PASS clip-swapped
phase A (픽스처 A, 팔 모션):  arm=0.272m  leg=0.000m
phase B (픽스처 B, 다리 모션): arm=0.000m  leg=0.195m   PASS avatar-animating
reload fired after 1.3s, count = 2
animator is playing: LiveFromBlender (3.96s)
```

**2×2 진리표라서 의미가 있습니다** — A에서는 팔만, B에서는 다리만 움직이므로 "핫리로드가 안 일어났다"와
"아무것도 안 움직인다"가 서로 구별됩니다. 스왑 뒤 **재생 중인** Animator가 다리를 흔들었다는 것은
리바인드 없이 반영됐다는 뜻입니다. 픽스처(3.4MB)는 언제든 다시 만들 수 있으므로 `.gitignore`로 빼고
생성 스크립트만 추적합니다.

> 이전 문서가 인용하던 `arm=0.308m / leg=0.249m / 1.4초`는 산문에만 있었습니다. 지금 값이 다른 것은
> 회전 각도가 다르기 때문이고(현재 픽스처는 0.95 / 0.70 rad), **구조와 클립 길이(1.96s→3.96s), 반영
> 지연(1.4초 vs 1.5초)은 일치**합니다. 즉 옛 수치도 실제 실행의 흔적이었을 가능성이 높고, 이제 그 자리에
> 재현 가능한 측정이 있습니다.

---

## Assets/CustomMotions 실태

| 파일 | 상태 |
| --- | --- |
| `ZepetoRig_Wave.fbx` | ✅ 스킨 메시 있음(Deformer 107 / Skin 1), 55/55 ZEPETO 뼈 이름 |
| `Wave_Hello.fbx` | ✅ **복구됨.** generic 뼈 20개로 자기 Avatar 생성 (`Wave_HelloAvatar` isHuman=True) |
| `AddonSmokeTest.fbx` | ✅ 같은 방식으로 복구됨 |

**셋 다 정상입니다.** `.meta`를 지운 뒤 Unity가 재생성했고, 두 generic 파일은 `boneName` 20개(자기 뼈)에
`avatarSetup: 1`(CreateFromThisModel), `rigImportErrors` 비어 있음 — 오염 시의 55개 ZEPETO 맵이 아닙니다.

### 오염 원인과 이번 수정

`importer.sourceAvatar` 대입은 **소스의 humanDescription을 대상의 `.meta`에 씁니다.** 대상 스켈레톤에
그 뼈들이 없으면 이후 모든 재임포트가 `Transform 'hips' for human bone 'Hips' not found`로 영구 실패합니다.
두 파일이 정확히 그 상태였습니다 — 원래는 잘 임포트되던 파일들입니다.

이번에 한 일:
- 오염된 `.meta` 2개 삭제 (외부 참조 0개 확인 후, git에 커밋되어 복구 가능)
- `CanCopyRigAvatarTo` 가드 추가 — 대상에 필요한 뼈 이름이 없으면 복사를 **건너뜁니다**
- 이미 오염된 자산 **복구** 경로 추가 — Unity는 거절된 복사에서 `avatarSetup`/`sourceAvatar`를 되돌리면서
  **복사된 뼈 매핑은 남기므로**, `humanDescription.human`을 실제 transform 이름과 대조해 필요 시 비웁니다

### 실측으로 확인한 재오염 방지

실제 1번 버튼 코드 경로(`TryConfigureMotionFbx`)를 `Wave_Hello.fbx`에 돌린 결과:

```
Avatar는 이 FBX에서 생성 - 이 FBX의 뼈 이름이 ZEPETO 리그와 달라
  Avatar를 복사하지 않았습니다 (55개 중 55개 없음, 예: hips)
avatarSetup = CreateFromThisModel   sourceAvatar = NULL
avatar 'Wave_HelloAvatar' isValid=True isHuman=True
extract -> Wave_Hello.anim (1.96초) humanoid=True
Play: 오른손 이동 0.373m → the avatar IS performing the custom motion
```

라이브 프리뷰가 arm할 때 폴더 내 FBX **전부**를 Humanoid로 재설정하는데도(그게 진행바가 있는 이유),
그 뒤에도 두 파일의 `boneName`은 20개로 유지됐습니다 — 재오염되지 않습니다.

> **Mixamo 임포트는 지원 불가가 아닙니다 — 실측으로 확정됐습니다.** generic 뼈 이름(Hips/Spine/LeftArm)이
> Unity 오토매퍼의 모국어입니다. ZEPETO 뼈 이름과 0/55 겹침인 20뼈 FBX가 들어가서 유효한 Humanoid
> 클립이 나왔고 아바타 위에서 재생됐습니다. 자동 매핑이 **안 되는** 쪽이 ZEPETO 이름이고
> (`upperReg_R`에 leg 토큰이 없음), 그래서 리그에 손으로 만든 humanDescription이 필요합니다.

`Capoeira.fbx`(루트, Assets 밖)는 진짜 Mixamo 파일(Maya 2020, `mixamorig:*` 65뼈, 완전 스킨)입니다.
Mixamo 경로 테스트용 픽스처로 쓸 만하지만 `Assets/CustomMotions`(폴링됨)에는 넣지 마세요.

---

## 이번 회차에 고친 것

패키지·애드온 쪽 변경의 원본 기록은 `Packages/com.easy.zepeto-helper/CHANGELOG.md`입니다.
아래는 저장소 전체를 한 화면에서 보기 위한 요약이고, 새 사실을 여기서 만들지는 않습니다.

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

버전 동기화(0.3.0/0.3.2 → 0.9.1), 자체 테스트 수치 정정(51 → 60 — 그 회차의 값입니다. 지금 개수는
`Documentation~/QA_AUDIT.md`), 파일 구조표 20파일 재작성,
단계 번호 1~4 → 1~7, 스니펫 SDK 버전 3.2.12 → 3.2.16(그대로 붙여넣으면 SDK가 내려갔습니다),
설치 확인 절차를 임베디드 패키지 실태에 맞게 수정(`manifest.json`에 항목이 **없는 것이 정상**),
캡처 8장 중 7장 재작성.

#### 개인 아이디 제거는 **패키지 범위**입니다 — 루트 저장소는 아직 깨끗하지 않습니다

자체 테스트의 `no-personal-id-in-source`가 훑는 것은 `Packages/com.easy.zepeto-helper` 아래 24파일
(`Editor/`의 `.cs` 20개 + `.md` 4개)뿐이라, 루트 저장소는 **구조적으로 볼 수 없습니다.**
실제로 아이디 문자열은 루트 쪽 3개 파일에 그대로 있습니다.

| 파일 | 왜 남아 있나 |
| --- | --- |
| `Assets/Playground.unity` | 작업 씬 `LOADER`에 들어 있는 실제 값. 비우면 아바타가 로드되지 않습니다 |
| `Assets/ZepetoHelperTests/Editor/ZepetoHelperSelfTest.cs` | 재유입 검사가 찾아야 하는 토큰 자체(`PersonalIdSample`). 문자열을 쪼개 선언한 것도 같은 이유입니다 |
| `zepeto-helper-selftest.result.txt` | 그 검사가 남긴 결과 기록 |

**이 3개는 이미 공개돼 있습니다.** 예전에는 "루트 저장소에 원격이 없다"는 것이 유출이 아닌 유일한
이유였고, 이 자리에 "push하기 전에 반드시 처리하세요"라고 적혀 있었습니다. 저장소를 합쳐 push한
뒤에도 그 문장이 남아 있었습니다 — 이미 일어난 일을 막으라고 경고하고 있었던 셈입니다.

소유자의 판단으로 **그대로 둡니다.** 제페토 아이디는 앱에서 서로 검색하라고 있는 공개 식별자이고,
셋 다 지우면 기능이 깨집니다(씬은 아바타를 못 불러오고, 재유입 검사는 찾을 토큰을 잃습니다).
배포되는 **패키지 쪽은 여전히 깨끗합니다** — 자체 테스트가 그것을 지킵니다.

> `ZepetoHelperSelfTest.cs`의 값은 `"darbam" + "s77"`로 쪼개져 있습니다. 자체 테스트의 스캔에
> 걸리지 않게 하려는 의도인데 부작용이 있습니다 — **아이디 전체 문자열로 grep하면 이 파일이 안
> 잡힙니다.** 공개 전 점검에서 실제로 이 파일 하나가 누락된 적이 있습니다.

---

## 남은 일

### 1. 실행 검증 — 대부분 완료

컴파일이 깨끗한 것은 정확한 것과 다릅니다. 이번 회차가 그걸 두 번 증명했습니다: diff 리뷰가 잡은 치명 2건이
경고 없이 통과했고, **자체 테스트를 처음 실제로 돌렸을 때 24개째에서 중단**됐습니다(아래).

- [x] **Blender 애드온 1.5.0 — 헤드리스 전 항목 통과**(개수는 위 표). 경로 런타임 유도(저장 안 된
      `.blend` 기준), env 오버라이드, 모호성 거부, `clear_pose`가 표시된 뼈만 되돌리는지, export가
      바이너리인지, `.part` 잔여 없음, **패널을 거치지 않는 호출도 게이트에 걸리는지**까지 실측.
      `54 + 49 = 103` 산수도 실제 리그에서 확인
- [x] **자체 테스트 전 항목 통과**, 결과 파일 재기록. 예상했던 NOTE 5줄 등장 확인
      (`no-personal-id-in-source:scanned` 24파일, `real-template:id-restored`,
      `playback-slot:{overrides,clip,controller}-restored` — 러너가 씬을 되돌린다는 증거)
- [x] **asmdef 회귀 발견·수정.** 신규 asmdef를 테스트 폴더 루트에 `includePlatforms: ["Editor"]`로 둬서
      런타임 MonoBehaviour(`ZepetoHelperTestLoader`)까지 Editor 전용이 됐고, `AddComponent`가 null을
      반환해 NRE로 중단됐습니다. 런타임/Editor 두 어셈블리로 분리
- [x] **헬퍼 창 7단계 육안 확인.** 헤더 `현재 작업: 1. 아바타 준비` ↔ 카드 1 일치(옛 `3. 클립 조정` 버그
      해소), 4번의 Blender 안내가 `1단계~5단계`, 7번이 `이 창의 1~7단계는…`, 단계 잠금 문구 없음,
      Stop·Emergency Stop이 비활성이어도 항상 렌더링되고 사유가 붙음
- [x] **`.meta` 재생성 확인.** Unity가 `Wave_Hello.fbx` / `AddonSmokeTest.fbx`의 `.meta`를 다시 만들었고
      `rigImportErrors`가 **비어 있고** `boneName`은 **20개(자기 뼈)** — 오염이 사라졌습니다.
      오염된 상태는 `boneName` **55개**(복사돼 들어온 ZEPETO 이름)로 나타나므로, 세는 값이 0이 아니라
      20이라는 점이 판정 기준입니다
- [x] **리그 export 러너 4/4.** `Kaydara FBX Binary` 확인, `animationType: Human`, 106 transforms,
      스킨 메시 1, `NOTE scene-dirt :: none` (러너가 씬을 더럽히지 않음)
- [x] **라운드트립 3 → 4 → 5 완주 — 실측.** 수치는 위 `라이브 왕복에는 실측치가 있습니다`
- [x] **1번 버튼 재오염 없음 — 실측.** 수치는 위 `실측으로 확인한 재오염 방지`

### 2. 그 외

- `Videobooth_282_editable.anim` 26.3MB + 편집본 14.8MB — `ClipEdits/`가 쌓이면 편집마다 수십 MB
- `zepeto-studio-unity-3.2.12/` 빈 디렉터리 — 디스크에 남아 있으면 지워도 됩니다(git은 빈 폴더를
  추적하지 않으므로 커밋에는 이미 없습니다)
- **`iter_fcurves`의 4.4+ 분기에 검사가 없습니다.** 1.5.0이 게이트를 `hasattr` → "비었는지"로 바꿨는데
  (위 `다시 하지 않아도 되는 것` 참고), 헤드리스 검사는 전부 빠른 경로만 지나갑니다 —
  `pose:iter-fcurves-works`가 `Action.fcurves`로 162개를 봅니다. 즉 **동작이 커버리지 없이 나갔습니다.**
  커브가 첫 슬롯에 없는 액션(layers/strips/channelbags만 있는 스텁이면 충분)을 `iter_fcurves`에 먹이는
  검사를 `headless_check.py`에 넣어야 그 분기가 처음으로 실행됩니다
- `F_CUBE_IN_FBX` / `F_BODY_IN_FBX` 주장은 여전히 출처가 없습니다. 헤드리스 스크립트가 생겼으니
  (`BlenderMotion/headless_check.py`) 큐브 제외 검사를 추가하면 그 주장도 재현 가능해집니다
- `.zepeto` export 실제 실행 + Studio 업로드 확인 (계정 로그인 필요)
- Humanoid muscle 클램핑이 재생 시점에 일어나는지 (클램프 경고 감지기는 제거된 상태)

### 다시 하지 않아도 되는 것 (조사했고, 답이 나왔습니다)

- **중복 애셋 없음.** 트리의 `.fbx`/`.anim`/`.blend`/`.prefab`/`.unity` 18개를 이름이 아니라 내용으로
  비교했고 MD5 18개가 전부 다릅니다. 4KB 롤링 블록 비교에서 겹치는 곳은 Blender가 내보낸 리그 3개
  (`ZepetoRig_Wave.fbx` / `zepeto-live-a.fbx` / `zepeto-live-b.fbx`)뿐이고 약 137블록(≈560KB,
  각 파일의 30~34%) — **중복이 아니라 세 파일이 같은 ZEPETO 몸 메시를 싣고 있는 것**입니다.
  그 메시를 빼면 Unity가 `isHuman=false` Avatar를 만들어 Humanoid 클립이 0개가 됩니다.
  `ZepetoBaseModel.fbx`는 어느 파일과도 0블록(FBX SDK 2020.3.4 출력, 나머지는 Blender 5.2)
- **애드온 `iter_fcurves`의 Blender 4.4+ 분기는 "죽은 코드"가 아닙니다. 그리고 게이트는 이미 고쳤습니다.**
  헤드리스 5.2.0에서 정상 경로가 `162 fcurves`를 돌려줍니다(`pose:iter-fcurves-works`). **지우지 마세요** —
  4.4+에서 `Action.fcurves`는 단일 슬롯 호환 접근자라, 커브가 첫 슬롯에 없는 액션에서는 "동작하는" 쪽이
  조용히 빈 목록을 주고 이 분기가 옳은 값을 갖습니다. 조사 당시의 게이트는 `hasattr(action, 'fcurves')`라
  4.2~5.2에서 항상 참이었고, 애드온 1.5.0이 그것을 **존재가 아니라 비었는지 보도록** 바꿨습니다
  (`curves = list(getattr(action, "fcurves", None) or [])` → 비었을 때만 layers를 걷습니다). 5.2는 실측된
  빠른 경로에 그대로 있습니다. 검사가 아직 없다는 것은 조사 결과가 아니라 열린 일이라 `남은 일`에 있습니다

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
   계측 기록은 `Documentation~/QA_AUDIT.md`의 `실제 재생 경로`, 구현 쪽 설명은 `Loader.cs`의
   `ApplyClipToOverrideController` 주석입니다. 여기서 다시 설명하지 않는 이유는 같은 문장을 세 곳에
   두었다가 서로 어긋난 전례가 있어서입니다.
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
| Unity 프로젝트 | `Desktop/zepeto/unity-project` |
| 헬퍼 패키지 (자체 git) | `.../Packages/com.easy.zepeto-helper` |
| 테스트 러너 | `.../Assets/ZepetoHelperTests/Editor` (공용 `ZepetoSelfTestSceneGuard.cs` 포함 — 여기 5개 전부 Editor 전용) |
| 테스트용 런타임 컴포넌트 | `.../Assets/ZepetoHelperTests/ZepetoHelperTestLoader.cs` — **`Editor/` 밖에 있어야 합니다** (위 어셈블리 2개 참고) |
| Blender 작업 파일 | `Desktop/zepeto/BlenderMotion/zepeto_motion.blend` |
| 애드온 원본 | `Desktop/zepeto/BlenderMotion/zepeto_motion_helper.py` |
| 애드온 헤드리스 검사 | `Desktop/zepeto/BlenderMotion/headless_check.py` (Unity 불필요) |
| 라이브 픽스처 생성기 | `Desktop/zepeto/BlenderMotion/make_live_fixtures.py` (픽스처는 git 제외) |
| Blender 리그 | `.../Assets/ZepetoHelper/Rig/ZepetoBaseModel.fbx` |
| Blender→Unity 드롭존 | `.../Assets/CustomMotions` |
| 라이브 확인 클립 | `.../Assets/ZepetoHelper/Motions/LiveFromBlender.anim` |

> **애드온은 설치돼 있습니다** — 아래 문단은 2026-07-30 기준의 낡은 기록입니다.
> 지금은 `%APPDATA%/Blender Foundation/Blender/5.2/scripts/addons/zepeto_motion_helper.py`에 사본이
> 있고 켜져 있습니다. `BlenderMotion/install_addon.py`가 설치하고 `headless_check.py`의
> `install:copy-matches-source`가 그 사본이 낡았는지 감시합니다. 아래는 그 이전 상태의 기록입니다.
>
> ~~**애드온 설치본은 없습니다.**~~ `%APPDATA%/Blender Foundation/Blender/5.2` 아래에 `config`만 있고
> `scripts/addons`가 없습니다. 전 파일시스템에 `zepeto_motion_helper.py`는 `BlenderMotion/`의 1개뿐이라,
> 소스 폴더에서 직접 로드하는 방식으로 쓰이고 있습니다. (이전 문서는 설치본 경로가 있다고 적었습니다.)

### 테스트 실행 방법

**Blender 쪽 — 라이선스 불필요, 지금 바로 됩니다:**

```
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" ^
    --background --factory-startup --python BlenderMotion/headless_check.py
```

결과는 콘솔과 `%TEMP%\zepeto_headless_check\result.txt`(`pass=N fail=N` 집계 포함). FBX는 temp에만 쓰므로
`Assets/`를 오염시키지 않습니다(그 폴더는 0.4초마다 폴링됩니다).

라이브 왕복 픽스처는 git에 없으므로 그 러너를 돌리기 전에 두 번 만들어야 합니다:

```
set ZEPETO_FIXTURE=a        (그리고 b 로 한 번 더)
blender.exe --background --factory-startup --python BlenderMotion/make_live_fixtures.py
```

**Unity 쪽 — 라이선스 활성화 후.** 프로젝트 루트에 트리거 파일을 두고 Unity를 활성화하면 재컴파일 시
실행됩니다.

| 트리거 | 하는 일 |
| --- | --- |
| `zepeto-helper-selftest.trigger` | 자체 테스트 전체 → `.result.txt` (거절 시 `.skipped.txt`) |
| `zepeto-livereload.trigger` | Play 왕복 실측 (**픽스처 2개 필요** — 위 생성기로 먼저 만드세요) |
| `zepeto-rig-export.trigger` | 리그 내보내기 + assertion 4개 |
| `zepeto-custom-motion.trigger` | 커스텀 모션 end-to-end. **파일 내용에 FBX 경로를 적습니다** (예: `Assets/CustomMotions/Wave_Hello.fbx`) |

> **`-quit`을 쓰지 마세요.** 트리거는 `[InitializeOnLoadMethod]` → `delayCall`로 실행되는데 `-quit`은
> 그 전에 Unity를 닫아버려 트리거가 소비되지 않습니다. 그냥 띄워두고 결과 파일이 생길 때까지 기다린 뒤
> 닫으면 됩니다. (자체 테스트만은 `[MenuItem]`이 있어 `-executeMethod ...ZepetoHelperSelfTest.Run`으로
> 바로 호출할 수 있고, 그때는 `-quit`을 같이 써도 됩니다.)

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
