# 변경 기록

## 0.9.0 - 2026-07-28

### 화면 구조를 다시 짰다 — 7개 번호 단계

Blender 왕복이 2번 카드 안에 A/B/C 하위 상자로 들어가 있었다. 2번 하나에 helpBox가 6개, 초록 Play가
2개였고, **가장 먼저 하는 일(몸 내보내기)이 가장 나중 일(FBX 등록)보다 아래**에 있었다. 게다가 단계가
잠기거나 완료되면 카드가 접히는데, 그때 Blender 도구 전체가 화면에서 사라졌다 — 쓰고 있는 도중에.

`Flow.cs` 신설. 1 아바타 준비 / 2 동작 고르기 / **3 Blender용 몸 내보내기 / 4 Blender에서 모션 만들기 /
5 내 캐릭터로 확인** / 6 클립 조정 / 7 제페토로 내보내기.

**단계 잠금을 없앴다.** 이전에는 조건이 안 맞으면 카드 내용이 `이전 단계를 완료하면 열립니다`로 통째로
대체됐다. 이제 항상 열려 있고, 못 하는 게 있으면 그 자리에 이유를 쓴다.

### 전면 코드 리뷰에서 나온 수정

6개 관점으로 훑고 각 지적을 반증 검증했다. 88건 확정. 주요한 것:

- **잠금을 없애면서 잠금 해제 버튼도 같이 지웠다.** 그게 `SetReadyStageUnlocked(true)`의 유일한
  호출자였다. 결과: `1번 적용`을 한 번 누르면 영원히 비활성. 재작성의 목적 자체를 뒤집은 버그였다.
  잠금 기계장치를 통째로 제거하고 두 버튼 다 다시 눌리게 했다.
- **6·7번 Play가 영구 회색이었다.** "직접 만들 거면 3번부터"라고 안내해놓고, 그 길로 간 사용자는
  `2번 적용`을 안 누르니 stage gate에 걸렸다. 이유 표시도 억제돼 아무 설명이 없었다.
- **GUILayout 붕괴 3건.** `경고 복구` 패널이 배경 로그 콜백 값으로 나타났다 사라졌다 —
  SDK 예외가 쏟아질 때, 즉 그 패널이 가장 필요할 때 정확히 터지는 구조였다. 라이브 확인 패널도
  Play 토글 시 컨트롤 개수·순서가 바뀌었다. 전부 무조건 렌더 + `enabled`만 변경으로 바꿨다.
- **헤더가 옛 4단계 레일을 계속 표시**했다. 7개 카드가 각자 번호·배지를 갖고 있어 중복이고 숫자도 틀렸다.
- 죽은 코드 제거: `BeginStep`/`DrawStepCard`/`DrawV7StagePill`/`BeginWorkflowBlock`/`CanUseStagePlay`/
  잠금 클러스터 등 약 6,700자. 컴파일 경고 0.
- 안내 문구 20여 건 정정. `3단계에서 복사하세요` 같은 것들 — 지금 3번은 Blender 몸 내보내기라
  따라가면 엉뚱한 버튼 앞에서 막혔다.

### Blender 애드온 1.3.0

실측 검증 완료:

- **기본 큐브가 모든 모션 FBX에 실려 나갔다.** `use_selection=False`였다. 선택 방식으로 바꾸되,
  `import_rig`가 걸어둔 `hide_select`를 먼저 풀어야 한다 — 안 그러면 `select_set`이 조용히 무시돼
  **스킨 메시가 빠지고** `isHuman=false`가 된다(큐브보다 훨씬 나쁜 결과).
- **뼈 개수를 거꾸로 보고했다.** `무시하는 뼈도 보기`를 켜면 `103 - 0 = 103개 사용 가능`이라고 표시됐다.
  죽은 뼈를 보기로 한 바로 그 순간 정반대를 알려준 셈. 이제 매핑에서 직접 센다.
- **쿼터니언 리그에서 모션이 통째로 사라졌다.** append/link한 리그는 QUATERNION이라 euler 값이
  저장돼도 평가에서 무시된다. 체크리스트는 `보낼 준비 완료`, Unity에는 안 움직이는 클립. `ensure_euler`
  추가 — 단, **이미 쿼터니언 키가 있는 뼈는 건드리지 않는다**(변환하면 그 커브가 소리 없이 날아간다).
- **이름 검증 없음.** 공백만 넣으면 `.fbx`라는 파일이 생겼다. Windows 금지문자는 raw 트레이스백.
- **저장 재시도가 0.8초.** 기다리는 대상이 1.6MB FBX 재임포트라 더 걸린다. 지수 백오프 ~3.1초로 늘리고
  포기 시 `.part`를 지운다(안 지우면 Unity가 애셋으로 임포트해 고아 `.meta`가 생긴다).

### 문서

README 두 개를 7단계 기준으로 다시 썼다. 이전 문서대로 따라 하면 없는 버튼(`1번 적용 / 다음 단계`,
`수정 잠금 해제`, `저장된 아이디` 드롭다운)을 찾게 됐다.


## 0.8.1 - 2026-07-27

### Play 중에 Stop을 못 누르던 문제

사용자 신고: "stop이 활성화가 안되는데 game 화면이 뜨는데". 원인이 두 겹이었습니다.

**1. Stop이 단계 소유권에 묶여 있었다.**
`isPlaying && activePreviewStage == stageToKeepOpen` 일 때만 활성화됐습니다. 라이브 확인은 어느 단계도
소유하지 않아 `-1`로 뒀고, 그 결과 **창 안의 모든 Stop이 회색**이 됐습니다. Unity 자체 ▶ 버튼으로 Play를
켜도 똑같았던, 원래부터 있던 함정입니다.

→ **Play 중이면 Stop은 항상 눌립니다.** 단계 소유 여부는 색깔만 결정합니다. Play에서 빠져나오는 길을
막는 게 옳은 상황은 없습니다. 라이브 확인은 2단계 소유로 표시합니다.

**2. 더 근본적인 것 — 의상 선택이 Play 때마다 날아갔다.**
`clothingPrefab`이 `[SerializeField]` 없는 필드라 Play 진입 도메인 리로드에서 null이 됐습니다.
`HasOutfit` false → 1단계 미완료 → **2·3·4단계가 통째로 접힘.** Stop 버튼이 든 패널까지 화면에서
사라집니다. Game 화면은 돌아가는데 창에는 나갈 방법이 없는 상태가 됩니다.

→ `clothingPrefab` / `pendingClothingPrefab`을 `[SerializeField]`로. 이건 라이브 확인뿐 아니라
**모든 Play**에서 3·4단계가 접히던 문제도 같이 고칩니다.

**3. 안전망.** 접힐 수 없는 헤더에 **Stop 버튼 하나**를 항상 둡니다. 나머지 Stop은 전부 단계 카드
안에 있고 카드는 접힐 수 있으므로, 어떤 상태에서도 확실히 하나는 남도록 했습니다. 라이브 확인 중이면
`적용된 횟수`도 헤더에 표시합니다.

### 조용히 죽어 있던 버튼

LOADER에 AnimationClip 필드가 없으면 `2번 적용`이 영구히 비활성인데 화면에 아무 설명이 없었습니다.
목록도 클립 길이도 정상으로 보여서 원인을 알 방법이 없고, 3·4단계는 영원히 잠깁니다. 유일한 단서가
기본 접힘 상태인 진단 폴드아웃 안의 영어 한 줄이었습니다. 인라인 한국어 안내를 넣었습니다.
(1단계는 같은 상황에서 이미 안내하고 있었습니다 — 빠뜨린 것이었습니다.)

### Blender 열기 실패 안내

"파일을 직접 더블클릭하세요"라고 안내하고 있었는데, 실패 원인 1순위가 **.blend 연결 프로그램이 없는
것**(=Blender 미설치)이라 더블클릭해도 똑같이 실패합니다. 설치 확인 → 연결 프로그램 지정으로 바꿨습니다.

## 0.8.0 - 2026-07-27

### 어디서 Blender로 가야 하는지가 화면에 보입니다

2단계가 거꾸로 배치돼 있었습니다. `내 모션 가져오기`(왕복의 **마지막** 단계)가 맨 위, `기본 몸
내보내기`(**첫** 단계, 평생 한 번)가 맨 아래, 그 사이에 라이브 확인이 끼어 있었습니다. Unity를 떠나는
지점을 알려주는 것이 화면에 하나도 없었습니다.

**A → B → C 순서로 다시 놓았습니다.**

- **A. Blender에서 쓸 몸 만들기 (처음 한 번만)** — 기존 리그 내보내기
- **B. 여기서 Blender로 갑니다** (신설, `GoToBlender.cs`) — **`Blender 열기` 버튼**.
  `.blend` 파일을 프로젝트 옆 `BlenderMotion/` 에서 자동으로 찾고, 못 찾으면 직접 고를 수 있습니다.
  Blender에서 할 일 4가지도 같이 적어뒀습니다.
- **C. 돌아와서 내 캐릭터로 확인 (권장)** — 라이브 확인
- **C-2. 수동으로 등록** — 기존 1번/2번 버튼. Mixamo처럼 Blender를 안 거친 파일용으로 격하.

### 고친 버그: 미리보기 몸이 안 보이던 것

SDK 프리팹으로 바꿨더니 오브젝트는 씬에 들어가는데 화면에 아무것도 안 나왔습니다.
프로브를 붙여 물어보니 원인이 나왔습니다 — **`zepeto.character` 3.1.32의 `ZepetoBaseModel.prefab`은
렌더러 2개가 전부 꺼진 채로 출하됩니다.** 런타임이 아바타를 다 조립한 뒤에 켜는 구조입니다.
인스턴스화 직후 렌더러를 켜도록 고쳤고, 이제 살구색 피부와 얼굴이 제대로 나옵니다.

## 0.7.0 - 2026-07-27

### 라이브 확인이 실제로 되는 것을 확인했습니다

0.6.0에서 만들어놓고 애셋 수준까지만 검증했던 부분입니다. Play를 실제로 띄우고 도중에 FBX를 갈아끼우는
자동 테스트(`ZepetoLiveReloadRun.cs`)를 만들어 돌렸습니다.

서로 다른 모션 두 개를 씁니다 — A는 **팔만**(48프레임), B는 **다리만**(96프레임). 두 구간에서 팔과
다리를 각각 재면 "새 클립이 재생 중"과 "옛 클립이 계속 재생 중"이 섞일 수 없습니다.

```
구간 A (팔 모션): arm=0.308m  leg=0.000m
구간 B (다리 모션): arm=0.000m  leg=0.249m
클립 길이: 1.96s -> 3.96s, 파일 교체 후 1.4초 만에 반영
```

**리바인드 없이 재생 중인 Animator에 반영됩니다.** 설계 전제가 맞았습니다.

첫 실행은 FAIL이 나왔는데 측정 쪽 버그였습니다 — 다리를 Z축으로 돌려놓고 무릎의 Z 변위만 쟀습니다.
3축 전부 재고 팔·다리를 함께 보도록 고쳤습니다.

### 고친 버그: 감시가 시작되는 순간 꺼지던 것

`OnEnable`의 stale-state 정리 조건이 `!EditorApplication.isPlaying`이었습니다. Play로 **진입하는**
도메인 리로드 중에는 이 값이 아직 true가 아니라서, 워처가 무장되어야 할 바로 그 순간에 해제됐습니다.
`isPlayingOrWillChangePlaymode`로 고쳤습니다.

### 정지 상태에서도 Scene에 몸이 보입니다

아바타는 Play 때 서버에서 받아오기 때문에, 그전까지 LOADER는 빈 GameObject였습니다. Scene 뷰에 기즈모
하나만 떠 있어서 카메라가 제대로 향하고 있는지조차 알 수 없었습니다.

- 헬퍼가 Edit 모드에서 SDK의 `ZepetoBaseModel.prefab`을 LOADER 자리에 세웁니다.
  (처음엔 Blender용으로 내보낸 FBX를 썼는데, 그 파일은 머티리얼이 없어서 시커먼 실루엣으로 나왔습니다.
  패키지 프리팹은 피부·머리·눈 머티리얼을 그대로 갖고 있고, 실제 아바타가 만들어지는 원본이라
  Play 했을 때와 색이 맞습니다. 내보내기를 먼저 해야 하는 조건도 사라졌습니다.)
- **씬 파일에 저장되지 않습니다.** `HideFlags.DontSave`라서 커밋할 것도, export에 섞일 것도 없습니다.
- Play를 누르는 순간(`ExitingEditMode`) 지워지고, 그 자리에 진짜 아바타가 들어옵니다. 겹칠 일이 없습니다.
- Hierarchy에는 일부러 보이게 뒀습니다 — 설명 없이 몸이 나타나는 게 더 나쁩니다.
  이름이 `[미리보기] ZEPETO 기본 몸 - Play 하면 사라집니다` 입니다.
- 1단계에 체크박스 `정지 중 Scene에 기본 몸 보이기` + `초점 맞추기` 버튼.
- 기본 몸 FBX를 아직 안 만들었으면 그 사실과 만드는 방법을 안내합니다.

### 저장된 아이디 기능 제거

아이디를 직접 입력하는 방식으로 바꿨습니다. 드롭다운·`목록에 추가`·`목록에서 삭제` 세 컨트롤이
짧고 거의 안 바뀌는 값 하나를 지키고 있었고, 그 값은 바로 아래 `현재` 줄에 이미 보였습니다.

- **씬의 LOADER가 유일한 기준입니다.** 창을 열면 LOADER에 들어있는 아이디를 그대로 읽어옵니다.
  EditorPrefs에 있던 값과 씬 값이 달라서 헷갈릴 여지가 사라집니다.
- 이전 버전이 저장해둔 아이디 3개 키는 **한 번 삭제**합니다. 기능을 뺀 뒤에 남의 계정이
  되살아나는 일이 없도록.
- 자체 테스트도 뒤집었습니다: 이제 저장 기능이 **없다는 것**과, 새 창이 씬에서 아이디를 읽어오는 것을
  검사합니다.

## 0.6.0 - 2026-07-26

### 라이브 확인 — Play를 켜둔 채로 Blender에서 바로 반영

`ZepetoStudioHelperWindow.LivePreview.cs` 신설. 2단계 아래 초록 버튼 하나.

`Assets/CustomMotions`를 0.4초마다 보고, FBX가 바뀌면 재임포트해서 그 클립을
`Assets/ZepetoHelper/Motions/LiveFromBlender.anim`에 `EditorUtility.CopySerialized`로 덮어씁니다.
GUID·instanceID가 유지되므로 Animator는 같은 객체를 계속 보고 있고, 리바인드 없이 새 동작이 반영됩니다.
(Play 중 컨트롤러를 다시 연결하면 ZEPETO 컨텍스트가 끊어집니다.)

한 번 고칠 때마다 9단계 → **Blender 버튼 1번 + Unity 창 다시 클릭**.

빌려간 것은 Stop 때 전부 되돌립니다: 재생 슬롯의 원래 클립, `runInBackground`.
헬퍼 창을 Play 중에 닫아서 복원이 안 돌았으면 다음에 열 때 복원합니다.

### 리뷰에서 잡힌 것들 (전부 수정)

코드를 쓴 뒤 4개 관점으로 적대적 리뷰를 돌렸고, 32건이 확정됐습니다. 주요한 것:

- **감시 폴더가 틀렸다 (블로커).** `CustomMotionRoot`(`Assets/ZepetoHelper/Motions`)를 보고 있었는데
  Blender 애드온은 `Assets/CustomMotions`로 내보냅니다. 워처가 **영원히 안 뜨는데 UI는 "연결됨"이라고
  표시하는** 최악의 실패였습니다. `LiveWatchRoot` 상수를 따로 두고, 이름이 비슷해서 통합하고 싶어지는
  함정이라는 주석을 달았습니다.
- **`runInBackground: 0` (치명).** Unity가 뒤에 있으면 Play가 멈춥니다. 이 기능의 전제가 무너지는
  설정이라, 준비 단계에서 켜고 Stop에서 되돌립니다. UI에도 "Unity 창을 다시 클릭"을 명시했습니다 —
  이전 문구는 돌아올 필요가 없다는 뜻으로 읽혔습니다.
- **override 슬롯을 복원하지 않았다 (블로커).** 라이브 확인을 한 번 하면 2·3단계에서 고른 동작이
  영구히 `LiveFromBlender.anim`으로 바뀌어 있었습니다. SessionState로 원래 경로를 기억해 되돌립니다.
  라이브 클립 자체를 기억해서 원본 포인터를 잃는 경우를 두 겹으로 막았습니다.
- **`clipAnimations`가 프레임 범위를 고정 (블로커).** Root Transform 잠금 옵션이
  `ModelImporterClipAnimation`에만 있어서 `clipAnimations`를 써야 하는데, 그러면 importer가 파일의 take를
  더 이상 따라가지 않습니다. Blender에서 48 → 96프레임으로 늘려도 48프레임만 들어오고 뒷부분이 조용히
  버려졌습니다. 범위가 어긋났을 때만 다시 유도하도록 했습니다.
- **안전 게이트가 애셋을 고친 뒤에 검사** → 순서를 뒤집었습니다.
- **local AnimatorController 전제조건 누락** → `RequestPlayMode`와 같게 맞췄습니다.
- 도메인 리로드로 감시 상태가 날아가 Play 초반에 헛돌던 것, 재진입 가드, 예외 처리,
  "왜 안 뜨는지" 진단 문구, 준비 단계 진행 표시줄.

Unity 자체 툴체인(`csc.exe`, langversion 7.3)으로 직접 컴파일해서 확인했습니다 — 17개 파일 에러 0.

## Blender 애드온 1.2.0 - 2026-07-26

### 원자적 내보내기

`.part`에 쓰고 `os.replace()`로 교체합니다. Unity가 폴더를 감시하고 있어서, 제자리에 쓰면 반쯤 쓰인 FBX를
읽어 클립이 깨지고, Windows에서는 Unity가 파일을 잡고 있어 저장 자체가 `PermissionError`로 실패합니다.
0.2초 간격 5회 재시도, 죽은 `.part` 정리 포함.

## Blender 애드온 1.1.0 - 2026-07-26

Unity 패키지는 그대로이고, `BlenderMotion/zepeto_motion_helper.py`만 바뀌었다.

### Unity가 버리는 뼈를 아예 못 만지게 했다

`ZepetoBaseModel.fbx.meta`의 humanDescription을 읽어보니 Humanoid가 매핑하는 뼈는 **55개**뿐이다.
리그의 103개 중 **49개는 매핑이 없어서, 돌려도 임포트 때 조용히 사라진다** — 에러도 경고도 없이
그 관절만 안 움직인다. 초보자가 스스로 알아낼 수 있는 실패가 아니다.

- `MAPPED_BONES` 표(meta에서 그대로 옮김)를 기준으로 매핑 없는 49개를 **불러올 때 숨긴다.**
  패널에 `쓸 수 있는 뼈 54개 / 전체 103개`로 표시한다.
- `Unity가 무시하는 뼈도 보기` 체크박스로 다시 볼 수 있다 (기본 꺼짐).
- `현재 포즈 저장`과 `처음과 끝 맞추기`가 **매핑된 뼈에만** 키를 찍는다. 이전에는 103개 전부에
  찍어서 버려질 커브를 FBX에 실어 보냈다.
- 체크박스를 켜고 Blender의 `I` 키로 죽은 뼈에 키를 찍은 경우, 5단계 체크리스트가 잡아낸다.

숨긴 것: 모든 `*Twist*`, 모든 `*_scale`, `pelvis`, `heel_L/R`, 그리고 `eye_L`·`eye_R`·`mouth`를
뺀 얼굴 전체.

### Hips는 Blender에서 돌릴 수 없다 (문서화)

FBX의 `hips`는 뼈가 아니라 아마추어 **오브젝트**다(Blender가 최상위 뼈를 오브젝트로 바꾼다).
Humanoid Hips에 매핑되는 게 바로 그 루트라서, Blender에서 골반 회전은 만들 수 없고
오브젝트를 옮기면 아바타가 화면 밖으로 나간다. README의 틀린 설명(`pelvis`를 루트로 안내)을 고쳤다.

### 검토했다가 버린 것: muscle 클램핑 경고

"HumanTrait 기본 범위를 넘긴 회전은 bake 때 잘린다"는 전제로 감지기를 만들었다가 **뺐다.**
실제 클립(`ZepetoRig_Wave.anim`)을 열어보니 muscle 값이 **1.0을 넘겨서 저장된다**(`Right Arm Down-Up`
= 1.0418). bake 때 클램프되지 않는다는 뜻이다. 게다가 멀쩡한 클립에서 `Left/Right Lower Leg Stretch`가
1.0인데, 이건 서 있는 다리가 곧다는 뜻일 뿐이다. 그대로 뒀으면 정상 모션에 오탐 4건을 띄웠을 것이다.
클램핑이 재생 시점에 일어나는지는 Unity 왕복 측정이 필요하고, 아직 확인하지 못했다.

## 0.5.1 - 2026-07-26

### 만든 모션을 어디로 보내야 하는지 알려준다

이전까지 이 도구는 4단계까지 끝내고 나면 **갈 곳이 없었다.** 아이템 Playground는 미리보기 화면이라
거기서 고른 동작은 `.zepeto`에 들어가지 않는다. 사용자는 그 사실을 알 방법이 없었다.

- **`이 모션을 제페토에 넣기` 패널 신설** (4단계 아래, `ZepetoStudioHelperWindow.Publish.cs`):
  - 제페토 스튜디오에 모션 아이템 카테고리가 **없다**는 사실을 명시한다. 업로드 가능한 항목은 전부 착용 아이템이고,
    앱 안의 포즈·제스처는 `requestOfficialContentList()`로 제페토 서버 공식 라이브러리에서만 가져온다.
  - 자작 모션의 **유일한 공식 목적지인 ZEPETO World** 레시피를 4단계로 안내한다.
  - `zepeto.character.controller`가 이 프로젝트에 있는지 실제로 확인해서 표시한다 (아이템 템플릿에는 없다).
  - 월드 문서 / 스튜디오 카테고리 목록 바로가기 버튼.

### 사실과 다른 설명 정정

`RigExport.cs`와 `MotionImport.cs`에 **"Humanoid 리타게팅이 비율 차이를 흡수하므로 손이 몸에 닿는 동작만
문제"** 라고 적혀 있었다. 틀렸다. muscle 커브는 관절 **각도**라서 비율 차이를 보정하지 못하고 순운동학으로
그대로 전파된다. `hasTranslationDoF: 0`이고 `armStretch`/`legStretch`가 `0.05`뿐이라 뼈 길이는 클립에
저장될 수조차 없다. 실제 실패는 발 접지 어긋남, 트위스트 뼈 소실, muscle 클램핑으로 나타난다.

- 두 파일의 해당 주석을 정정했다.
- `"ZEPETO 리그로 만든 모션이면 비율이 같아 문제 없음"` → `"모션 자체의 뼈대를 기준으로 읽습니다"`.
- 리그 내보내기 UI에서 `ZEPETO 실제 모델` → `ZEPETO 기본 몸`. 내 얼굴·머리·옷·체형은 Play 중에만
  서버에서 내려받으므로 이 FBX에 없다는 점, 그리고 어느 몸으로 만들든 **모션 파일 자체는 같다**는 점을 밝힌다.

## 0.5.0 - 2026-07-25

### Unity → Blender → Unity 왕복

ZEPETO 실제 모델로 모션을 만들 수 있게 됐다. 이전 0.4.0은 외부 FBX를 받기만 했고, 그 FBX를 만들 리그는
사용자가 알아서 구해야 했다.

- **`ZEPETO 리그 내보내기`** (2번 단계): `ZepetoBaseModel`을 `Assets/ZepetoHelper/Rig/ZepetoBaseModel.fbx`로
  내보내고 Humanoid로 설정한다. Blender에서 실제 체형·103본 뼈대를 보며 작업할 수 있다.
- Unity FBX Exporter(`com.unity.formats.fbx`)는 **리플렉션으로 호출**한다. 패키지가 없어도 헬퍼는 컴파일되고,
  없으면 설치 안내를 표시한다.
- 되가져온 FBX는 ZEPETO 리그 Avatar를 기준으로 리타게팅하도록 설정을 시도한다.

### 왕복하며 실제로 잡은 버그

전부 "각 단계는 성공했다고 보고하는데 결과물이 안 되는" 유형이라, 끝까지 돌려봐야 드러났다.

- **ASCII FBX**: Unity FBX Exporter의 `ExportModelOptions.ExportFormat` 기본값이 ASCII다. Blender는
  ASCII fbx를 아예 거부한다(`ASCII FBX files are not supported`). Unity 쪽은 성공으로 보고한다.
  → 바이너리 강제 + 내보낸 파일 헤더가 `Kaydara FBX Binary`인지 검증
- **`animationType`과 `sourceAvatar` 동시 적용 불가**: 한 번에 쓰면 Unity가 조용히
  `CreateFromThisModel`로 되돌린다. → Humanoid 적용 → 재임포트 → Avatar 지정 → 재임포트로 분리
- **메시 없는 애니메이션 FBX**: ZEPETO 본 이름(`hips`, `upperArm_R`, 오른다리는 오타로 `upperReg_R`)은
  Unity 자동 매핑 대상이 아니다. armature만 내보내면 Avatar가 `isHuman=false`가 되고, 그러면 Unity가
  **Humanoid 클립을 하나도 만들지 않는다**. 서브에셋 211개 중 AnimationClip 0개가 된다.
  → Blender 내보내기에 스킨 메시 포함
- 클립이 없을 때 조용히 넘어가지 않고 "이 FBX에 애니메이션 클립이 없습니다"로 표시

### Avatar 복사에 대한 결론

`Copy From Other Avatar`는 적용되지 않는다. 원인은 뼈대 불일치가 아니라 Unity의 구조적 동작이다.

```
리그 fbx        root='ZepetoBaseModel'   transforms=106
애니메이션 fbx  root='ZepetoRig_Wave'    transforms=106   뼈 이름 103개 전부 동일
```

Unity는 임포트한 모델의 루트를 **항상 파일 이름으로** 만든다. 따라서 소스 Avatar의 루트 이름과 절대
일치할 수 없고, 스켈레톤이 그 항목만큼 달라 Unity가 조용히 `CreateFromThisModel`로 되돌린다.
모든 모션 파일을 `ZepetoBaseModel.fbx`로 이름 붙이지 않는 한 회피할 수 없다.

**다만 이 워크플로우에서는 문제가 되지 않는다.** Avatar 복사가 필요한 경우는 애니메이션의 뼈 비율이
대상 모델과 다를 때다. 여기서는 ZEPETO 리그 자체로 모션을 만들었으므로 애니메이션 fbx에 ZEPETO의
실제 스켈레톤이 들어 있고, Unity가 그것으로 생성한 Avatar는 이미 올바른 비율을 갖는다. 반대로 외부
리그(Mixamo 등)로 만든 모션이라면 뼈대가 실제로 다르므로 복사 자체가 유효하지 않다.

- 내보내기에서 `EMPTY`는 제외한다. 래퍼를 함께 내보내면 Unity 루트 아래로 한 겹 더 들어가
  transforms가 107이 되어 오히려 원본과 더 멀어진다.
- 이전에 표시하던 "뼈대가 ZEPETO 리그와 다릅니다"는 **사실이 아니었다**. 정확한 설명으로 교체했다.

### 검증

실제 ZEPETO 아바타로 Play해서 손 이동을 계측했다.

```
Blender에서 upperArm_R을 Z축으로 회전 → FBX → Unity
avatar isValid=True isHuman=True / clip 1.96초 humanoid
Play 중 오른손 이동: 0.358m  → 아바타가 실제로 동작함
```

## 0.4.0 - 2026-07-25

### 내가 만든 모션 사용 (Mixamo / Blender)

0.3.x까지는 SDK가 제공하는 클립 10개 중에서만 고를 수 있었다. 이제 직접 만든 모션을 쓸 수 있다.

- **모션 카탈로그**: SDK 클립과 `Assets/ZepetoHelper/Motions`의 내 모션을 한 목록으로 합쳐 보여준다.
  목록에 길이, `[내 모션]`, `(포즈)`, `(Humanoid 아님)` 표시가 붙는다.
- **FBX 가져오기 패널** (2번 단계): Project 창에서 FBX를 고르고 두 버튼을 누르면 끝난다.
  1. `FBX를 ZEPETO용으로 설정` — `Animation Type = Humanoid`, `Import Animation` 켜기,
     Root Transform 고정을 자동 적용한다. 공식 커스텀 애니메이션 가이드가 요구하는 설정이다.
  2. `내 모션으로 추가` — FBX 안의 클립을 `Assets/ZepetoHelper/Motions` 아래 독립 `.anim`으로 복사한다.
     model asset 안의 클립은 읽기 전용이라 배속·구간·반복 편집을 하려면 복사본이 필요하다.
- **Humanoid 검증**: `AnimationClip.isHumanMotion`이 아니면 적용을 거부하고 고치는 방법을 알려준다.
  ZEPETO는 Humanoid 리타게팅으로만 동작하므로 generic 클립은 조용히 아무것도 하지 않는다.
- **포즈/동작 구분**: 0.1초 이하 클립은 정지 포즈로 표시하고, 작업 동작으로 쓰려 하면 막는다.
  SDK 클립 10개 중 5개(`A_pose`, `PhotoBooth_one_*`, `PHOTOBOOTH_ONE_631`)가 1프레임 포즈다.
- **기본 선택 보정**: 첫 선택이 절대 정지 포즈가 되지 않도록 한다.

확인한 전제: SDK 클립은 Unity Humanoid muscle 커브(`RootT`, `RootQ`, `LeftHandT` 등 432개)로 되어 있고,
`ZepetoBaseModel.prefab`은 Avatar를 가진 Humanoid 리그다. 따라서 Humanoid로 임포트한 외부 FBX는
그대로 리타게팅된다.

## 0.3.2 - 2026-07-25

### 코드 구조 정리

동작 변경 없음. 4,300줄짜리 단일 파일을 관심사별 `partial class` 파일로 나눴다.
타입은 그대로라 리플렉션 기반 자체 테스트도 그대로 통과한다 (51/51).

| 파일 | 담당 |
| --- | --- |
| `ZepetoStudioHelperWindow.cs` | 공유 상태, Unity 생명주기, 최상위 렌더 진입점 |
| `.Accounts.cs` | ZEPETO 아이디 검증 규칙, 저장 목록, LOADER 적용 |
| `.Loader.cs` | LOADER 바인딩과 SDK 재생 슬롯 제어 |
| `.Scenes.cs` | LOADER가 든 작업 scene 탐색과 열기 |
| `.Motion.cs` | SDK 동작 목록과 편집용 복사본 생성 |
| `.ClipEdit.cs` | 배속·구간·반복 편집 및 새 `.anim` 저장 |
| `.Export.cs` | 공식 `.zepeto` export 실행과 결과 보고 |
| `.Safety.cs` | Play 차단 판정과 로그 기반 안전 스냅샷 |
| `.Validation.cs` | 진단 목록에 표시되는 검사 |
| `.Workflow.cs` | 1~4단계 상태 기계와 진행 표시 |
| `.Steps.cs` | 사용자가 실제로 누르는 단계 카드 |
| `.Ui.cs` | 재사용 그리기 요소와 단계 상태 표현 |
| `.SdkPackage.cs` | `zepeto.studio` 설치 감지와 버전 비교 |

## 0.3.1 - 2026-07-25

### 동작을 골라도 아바타가 움직이지 않던 근본 원인 해결

SDK의 실제 재생 경로를 잘못 짚고 있었다. 런타임에서 확인한 구조는 다음과 같다.

- 베이스 컨트롤러 `ZepetoBaseModel`에는 교체 가능한 클립 슬롯이 **`dynamic` 하나뿐**이다
- `PlaygroundAnimatorController`(AnimatorOverrideController)가 그 슬롯을 교체한다
- SDK가 배포하는 기본 매핑은 **`dynamic -> A_pose`** 이고, `A_pose`는 길이 **0.04초짜리 정지 포즈**다
- `PlaygroundController.AnimationClip` 필드는 이 매핑에 영향을 주지 않는다

0.3.0까지 헬퍼는 `AnimationClip` 필드만 바꾸고 있었다. 그래서 2번에서 어떤 동작을 골라도 오버라이드
테이블은 `A_pose`로 남았고, Play하면 아바타가 그냥 서 있었다. 실제 Play 계측 결과:

```
AnimationClip = Videobooth_282 (23.13s)   ->   playing clip: A_pose (0.04s)   # 고치기 전
override slot dynamic -> Videobooth_282   ->   playing clip: Videobooth_282   # 고친 후
```

- `AssignAnimationClip`이 project-local AnimatorOverrideController의 모든 슬롯을 선택한 동작으로 다시 쓴다
- package cache 원본 컨트롤러에는 쓰지 않고 거부한다. SDK asset 손상 방지
- 미리보기 종료 시 원래 동작으로 되돌릴 때도 재생 슬롯을 함께 되돌린다
- 3번 단계와 검증 목록에 `실제 재생될 동작`을 표시한다. 정지 포즈가 물려 있으면 경고한다
- 자체 테스트에 재생 슬롯 검증 추가

### Play 중 재컴파일로 SDK가 깨지던 문제 해결

Play 도중 Unity가 스크립트를 다시 컴파일하면 domain reload가 일어나고, ZEPETO SDK의 UniRx 구독과
네이티브 상태가 끊어진다. 그 이후로는 매 프레임 아래 예외가 반복되며 아바타가 아무 동작도 하지 않는다.

```
NullReferenceException: ZepetoRoom3DSpace.Changed ()
NullReferenceException: Zepeto.ZepetoContext.PreUpdateContext ()   <- ZepetoInitializer.Update
NullReferenceException: Zepeto.ZepetoContext.UpdateContext ()      <- ZepetoInitializer.LateUpdate
NullReferenceException: Zepeto.SwingBoneProcessor.Update ()
```

Unity 기본 설정인 `Script Changes While Playing = Recompile And Continue Playing`이면 Play 중 스크립트를
한 글자만 고쳐도 이 상태가 된다.

- 헬퍼 상단에 이 설정을 감지해 경고와 `Play 중 재컴파일 끄기 (권장)` 버튼을 표시
- 버튼을 누르면 `Recompile After Finished Playing`으로 바꿔 Play 중 domain reload를 막는다
- 차단 메시지를 실제 복구 방법으로 교체. Console을 지워도 깨진 context는 복구되지 않으므로
  "Stop → 설정 변경 → 다시 Play"를 안내한다

## 0.3.0 - 2026-07-25

### 여러 아이디 지원

- 패키지에 박혀 있던 개인 ZEPETO 아이디 기본값(`darbams77`)을 제거. 이제 기본 아이디는 없음
- 아이디를 여러 개 저장하고 dropdown에서 골라 쓰는 기능 추가 (`목록에 추가` / `목록에서 삭제`)
- 저장 목록은 `EditorPrefs`에 남아 Unity를 다시 켜도 유지되고, 프로젝트가 달라도 같은 목록을 사용
- 0.2.x의 단일 아이디 설정(`defaultZepetoId`)은 첫 실행 시 자동으로 목록에 이전
- 아이디 형식 검증 추가: 영문/숫자/`_`/`.`/`-`만 허용하고, 앞의 `@`와 공백은 자동 제거
- 잘못된 아이디는 scene에 쓰지 않고 거부. 아이디를 바꾸면 1번 단계가 다시 확인 대상이 됨

### SDK 버전 검사

- `zepeto.studio` 검사를 정확히 `3.2.12`만 통과하던 방식에서 `3.2.12 이상`으로 변경 (3.2.13~3.2.16도 통과)
- 버전 비교를 문자열이 아닌 숫자 단위로 비교하도록 수정 (문자열 비교는 `3.2.9`를 `3.2.12`보다 높게 판정함)
- registry 설치뿐 아니라 `Packages/` 아래 embedded/local 설치도 인식
- 진단 패널에 감지된 SDK 버전과 설치 형태를 표시

### 개발용 코드 제거

- Unity MCP bridge 연동 코드 전부 제거: 에디터 로드마다 실행되던 `[InitializeOnLoadMethod]` 자동 재시작,
  ping용 TCP 소켓 코드, `MCP Recheck` / `Restart MCP Bridge` / `MCP 복구` 버튼과 상태 표시
- `System.Net.Sockets` 의존성 제거

### 작업 scene 탐색

- `Assets/Playground.unity` 하드코딩 제거. SDK가 제공하지 않는 경로였음
- 프로젝트 안에서 `LOADER`가 들어 있는 scene을 직접 찾아 목록으로 보여주고 선택해서 열도록 변경
- scene이 없을 때 "무엇을 준비해야 하는지" 알려주는 안내로 교체

### 버그 수정

- `LOADER` 탐색이 활성 오브젝트만 찾던 문제 수정. 비활성 오브젝트와 additive scene까지 탐색
- 아이디 입력 필드가 매 repaint마다 초기화되어 입력이 불가능하던 문제 수정
- `LOADER`가 없을 때 매 repaint마다 scene 전체를 탐색하던 문제를 주기 제한으로 수정
- `Assets/Contents`나 SDK animation 폴더가 없을 때 `AssetDatabase.FindAssets`가 콘솔 경고를 반복 출력하고,
  그 경고를 helper의 안전 패널이 다시 경고로 집계하던 문제 수정
- 파괴된 `SerializedObject`에 대한 guard 누락 보완 (아이디 적용, clip 연결, controller 교체, 검증 경로)
- clip 길이가 0.01초 이하일 때 시작/끝 슬라이더 clamp가 뒤집히던 문제 수정
- 재타이밍한 keyframe을 시간 순으로 정렬하도록 수정 (정렬되지 않은 curve는 잘못 평가됨)
- 존재하지 않는 `Videobooth_282` 기본 동작 지정 제거
- popup 항목의 `/`가 하위 메뉴로 해석되어 항목이 가려지던 문제 수정
- export 결과를 나중에 다시 확인할 수 있는 `결과 다시 확인` 버튼 추가
- `LOADER` 필드 탐색 범위 확대. 실제 SDK에서 `zepetoId`는 `Zepeto.ZepetoCharacterCustomLoader`에,
  `AnimationClip`과 `AnimatorController`는 `ZEPETO.Studio.PlaygroundController`에 들어 있어 서로 다른
  컴포넌트다. 이 컴포넌트들이 `LOADER` 오브젝트에 같이 붙어 있지 않으면 예전 코드는 실패했다.
  이제 `LOADER` → 자식 → scene 전체 순으로 넓혀가며 찾는다

## 0.2.4 - 2026-05-24

- README 상단에서 실제 Unity `Game View` Play 화면이 바로 보이도록 배치
- Play 캡처 설명을 `실제 Play 확인 화면`으로 명확히 정리
- tarball, 환경 문서, package metadata 버전을 `0.2.4`로 갱신

## 0.2.3 - 2026-05-24

- README 상단에 실제 화면 기반 `workflow-overview.png` 추가
- 1~4번 단계별 실제 Unity Helper 캡처 추가
- 실제 Unity Game View Play 캡처 추가
- 초보자가 화면을 보며 따라갈 수 있도록 `실제 화면으로 따라하기` 섹션 추가
- tarball, 환경 문서, package metadata 버전을 `0.2.3`으로 갱신

## 0.2.2 - 2026-05-24

- README를 초보자용 따라하기 안내서 형식으로 재구성
- 설치 전 체크리스트, 버튼 뜻, 막혔을 때 확인표 추가
- tarball, 환경 문서, package metadata 버전을 `0.2.2`로 갱신

## 0.2.1 - 2026-05-24

- README 상단의 `조건부` 설명 섹션을 제거하고 실제 Unity Helper 창 캡처로 교체
- 설치 후 보이는 메뉴와 1~4번 버튼 흐름을 실제 화면 기준으로 다시 정리
- `docs/ENVIRONMENT.md`를 준비물/설치 확인 중심으로 간소화
- imagegen 작업 흐름 이미지를 패키지에서 제거하고 실제 캡처 `docs/images/helper-window.png`만 유지
- 패키지 버전을 `0.2.1`로 갱신

## 0.2.0 - 2026-05-24

- 작업 흐름을 4단계로 정리
  - 아바타와 의상
  - 동작 선택
  - 클립 조정
  - `.zepeto` 생성
- 구형 quick control, workbench, pose edit, motion adjust 코드 제거
- 배속, 시작/끝 시간, 반복 설정을 새 `.anim` 복사본으로 저장
- 최종 `.zepeto` 파일명을 `ZEPETO_<의상명>_<동작명>.zepeto` 형식으로 정리
- UI에서 출력 파일 경로 표시
- 단계 잠금, 임시 미리보기 복구, clip 저장, export rename, stale `SerializedObject` guard에 Audit/QA/QC 주석 추가
- GitHub용 한국어 README, 환경 설정 문서, imagegen 작업 흐름 이미지 추가

## 0.1.2

- 내부 prototype build
