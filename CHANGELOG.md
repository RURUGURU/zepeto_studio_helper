# 변경 기록

## 0.9.1 (미출시)

**0.9.0을 전면 재검증하고, 그 과정에서 드러난 회귀 18건을 고쳤다.** 코드와 문서 양쪽이다.

절차는 이랬다. 0.9.0의 변경을 적용한 뒤 그 diff 자체를 7개 관점으로 적대적으로 다시 읽었고
(불변식 위반 · 라이프사이클 · 번호 매핑 · 임포트/익스포트 · 애드온 · 테스트 · 문서 사실성),
나온 지적을 각각 코드로 재확인한 다음 confirmed 18건만 고쳤다. `csc.exe` 사전 검증은
헬퍼 20파일 · 테스트 6파일 모두 **에러 0 · 경고 0**.

교훈은 기록해둘 만하다. **컴파일이 깨끗한 것은 정확한 것과 다르다.** 아래 치명 2건은 둘 다
컴파일 경고 하나 없이 통과하면서 기능을 죽이거나 개인정보를 유출하는 상태였다.

`package.json`의 `version`은 이 항목과 함께 `0.9.1`로 올렸다.
Blender 애드온은 `1.4.0`으로 올렸다 (경로 자동 유도와 `경로 자동 찾기` 오퍼레이터가 새 기능이다).

### 창 레이아웃이 다시 로드되면 라이브 확인이 조용히 죽었다 (치명)

`Maximize On Play`가 켜져 있으면 — Game 뷰 툴바의 기본 토글이다 — Play 진입 시 Unity가 레이아웃을
저장하고 새 레이아웃을 불러오면서 다른 EditorWindow를 전부 닫는다. 헬퍼는 `OnDisable` → `OnDestroy`를
받는다. 0.9.0이 새로 넣은 `OnDestroy` 복원은 이것을 **"사용자가 창을 닫았다"로 오인**했다.
가드 2개(`isAssemblyReloadInProgress`, `HasAnotherLiveHelperWindow`)로는 잡히지 않는다 — 레이아웃
리로드는 어셈블리 리로드가 아니고, Unity는 새 창을 만들기 전에 옛 창을 닫는다.

결과: 무장 직후 `LiveReloadArmed`가 false로, `runInBackground`가 false로 되돌려지고, 사용자의 이전
클립이 `LiveFromBlender.anim` 위에 다시 쓰였다. 재생성된 창의 `OnEnable`은
`isPlayingOrWillChangePlaymode`가 true라 재무장하지 않는다. **Play는 돌고 있는데 라이브 연결만 없다.**
`Window > Layouts` 수동 전환도 같은 경로다.

- `ReturnLivePreviewBorrowOnTeardown` 첫 줄에 `isPlaying || isPlayingOrWillChangePlaymode` 조기 반환.
  대차 기록은 SessionState에 그대로 두고, 이미 올바르게 동작하는 두 경로에 위임한다 —
  Stop 시 `EnteredEditMode`, 다음 열기 시 `OnEnable` 지연 복구. 둘 다 레이아웃 리로드를 견딘다.
- 같은 수정이 **불변식 I 위반**도 없앤다. 그 복원은 Play 중에 동기로
  `ApplyClipToOverrideController` → `ApplyOverrides` + `SaveAssets` + `ImportAsset`을 실행했는데,
  이것은 재생 중인 컨트롤러에 대한 미드플레이 리바인드이고 ZEPETO 컨텍스트를 끊는다.

### 배포하면 개인 정보가 나가는 상태인데 문서는 해결됐다고 적혀 있었다 (치명)

0.9.0의 아이디 제거는 **텍스트 전용**이었다. `grep`으로는 패키지 어디에도 아이디가 없지만, 캡처 PNG에는
**픽셀로** 남아 있다. `docs/images/helper-window.png`와 `step-1-avatar-outfit.png`가 `현재 아이디` 줄과
입력칸에 실제 아이디를 표시하고, `workflow-overview.png`는 그 캡처를 썸네일로 품고 있으며,
`play-preview.png`에는 제작자 본인 아바타의 얼굴·머리·의상이 나온다. `.npmignore`는 문서 폴더 중
`Documentation~/`만 빼고 `docs/`는 일부러 넣으므로 tarball에 그대로 들어가고, README가 이 이미지들을
직접 불러온다.

- CHANGELOG와 `QA_AUDIT.md`의 "placeholder로 교체했다"를 **실제로 한 일**로 정정했다 — 코드와 markdown은
  정리했고, **캡처는 정리하지 않았다.**
- README에 `캡처 이미지 경고` 절, `QA_AUDIT.md`에 `배포 전 차단 항목` 절을 새로 만들었다. 문제가 있는
  4개 파일을 이름으로 지목하고, **tarball 발행 또는 저장소 공개 전에 재촬영하거나 가려야 한다**고 적었다.
  README 최상단(캡처 바로 위)에도 같은 경고를 걸어 못 보고 지나칠 수 없게 했다.
- `no-personal-id-in-source`가 `.cs`와 `.md`만 수집하므로 **PNG를 구조적으로 볼 수 없다**는 사실을
  `QA_AUDIT.md`에 명시했다. 불변식이 깨진 채로 초록이 보고되는 것이 이 항목의 실제 결함이다.

**그리고 실제로 다시 찍었다 — 6장.** Unity 2020.3.9f1을 GUI로 띄우고(Personal 라이선스는 `-batchmode`를
거부하지만 GUI + `-executeMethod`는 된다), 씬 `LOADER`의 `zepetoId`를 `my_zepeto_id` placeholder로 바꾼
상태에서 헬퍼 창을 플로팅으로 열어 스크롤 위치별로 캡처한 뒤 원래 값으로 되돌렸다. 재촬영본에는 아이디가
없고, 화면도 0.2.x 4단계가 아니라 현재 7단계 UI다. 없던 구간(4·5번 카드)의 캡처도 새로 만들었다.

| 파일 | 결과 |
| --- | --- |
| `helper-window.png` | ✅ 창 전체 |
| `step-1-avatar-outfit.png` | ✅ 1번 카드 |
| `step-2-motion-select.png` | ✅ 2·3번 카드 |
| `step-4-5-blender-live.png` | ✅ 신규 — 4·5번 카드 |
| `step-3-clip-adjust.png` | ✅ 실제 6번 화면 (파일명은 옛 이름 유지) |
| `step-4-save-export.png` | ✅ 실제 7번 화면 (동일) |

그리고 마지막 2장도 해결했다.
`play-preview.png`는 제작자 아바타가 보이는 상태로 유지한다 — 공개해도 된다는 판단을 받았고,
placeholder 아이디로는 아바타가 로드되지 않아 자동 재촬영이 불가능한 유일한 이미지기도 하다.
`workflow-overview.png`는 **다시 그렸다.** 옛 도해는 `1 -> 2 -> 3 -> 4` 흐름을 전제로 설계됐고
제거된 단계 잠금을 설명하고 있었다 — 크롭으로 고칠 수 있는 종류의 문제가 아니었다.
새 도해는 위 재촬영본 4장과 Play 화면으로 7단계 흐름을 구성하고, 라이브 왕복 실측치
(클립 1.96s → 3.96s, 반영 1.5초, 팔 0.272m / 다리 0.195m)를 함께 적었다. README 상단 임베드를 되돌렸다.
