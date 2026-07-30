# QA / Audit 기록

## 범위

패키지: `com.easy.zepeto-helper@0.9.1`

핵심 파일: `Editor/ZepetoStudioHelperWindow.cs`

목표: 공식 ZEPETO Studio SDK 프로젝트에서, 계정이 여러 개인 사람도 그대로 쓸 수 있는 Unity Editor helper 패키지로 정리한다.

## 배포 전 차단 항목 — 미해결

> **캡처 8장 중 6장은 0.9.1에서 재촬영해 해결했다. 2장이 남아 있다.**

0.9.0에서 한 것은 코드와 markdown 텍스트의 개인 아이디 제거뿐이었고 캡처는 손대지 않았다. 0.9.1에서
실제 Unity 2020.3.9f1을 띄워, 씬 `LOADER`의 `zepetoId`를 `my_zepeto_id` placeholder로 바꾼 상태로 다시
찍고 원래 값으로 되돌렸다. 그래서 재촬영본에는 아이디가 없고 화면도 현재 7단계 UI다.

`.npmignore`는 문서 폴더 중 `Documentation~/`만 빼고 **`docs/`는 일부러 포함**하므로(README가 그 이미지를
직접 불러오기 때문), 남은 2장은 `npm pack` 산출물과 GitHub 렌더링 양쪽에 그대로 실려 나간다.

| 파일 | 상태 |
| --- | --- |
| `docs/images/helper-window.png` | ✅ 재촬영 (창 전체) |
| `docs/images/step-1-avatar-outfit.png` | ✅ 재촬영 (1번 카드) |
| `docs/images/step-2-motion-select.png` | ✅ 재촬영 (2·3번 카드) |
| `docs/images/step-4-5-blender-live.png` | ✅ 신규 (4·5번 카드 — 이전에 캡처가 없던 구간) |
| `docs/images/step-3-clip-adjust.png` | ✅ 재촬영 (실제 6번 화면) |
| `docs/images/step-4-save-export.png` | ✅ 재촬영 (실제 7번 화면) |
| `docs/images/workflow-overview.png` | ⛔ **미해결** — 4단계 흐름을 전제로 설계된 합성 도해. 제거된 단계 잠금을 설명하고, `PLAY` 칸에 제작자 아바타가 들어 있다. 크롭으로 해결 불가, 도해 재작성 필요 |
| `docs/images/play-preview.png` | ⛔ **미해결** — 제작자 본인 아바타. placeholder 아이디로는 아바타가 로드되지 않아 자동 재촬영이 불가능하다. 버리거나 별도 계정으로 촬영해야 한다 |

**tarball을 발행하거나 저장소를 공개하기 전에 위 4개를 다시 찍거나 해당 영역을 가려야 한다.**
나머지 3장(`step-2-motion-select.png`, `step-3-clip-adjust.png`, `step-4-save-export.png`)은 1번 카드가
접힌 상태라 아이디가 보이지 않는다.

### 자체 테스트가 이것을 잡을 수 없는 이유 (구조적)

`no-personal-id-in-source`가 읽는 파일은 `CollectShippedPackageFiles`가 모은 것뿐이고, 그 함수가 모으는
것은 **`Editor/` 아래의 `.cs`와, 패키지 root · `docs/` · `Documentation~/`의 `.md`뿐이다.** `.png`는
수집 대상이 아니며, 검사 자체가 `File.ReadAllLines`로 텍스트 줄을 훑는 방식이라 이미지에 적용될 수도 없다.

따라서 **이 검사가 초록이어도 이미지에 대해서는 아무것도 보장하지 않는다.** 지금이 정확히
`no_personal_account_data_shipped` 불변식이 깨진 채로 초록이 보고되는 상태이고, 그것이 이 항목의 핵심
결함이다. 캡처를 고치기 전까지 이 검사 결과를 "개인 정보가 안 나간다"는 근거로 쓰면 안 된다.

## 검증한 환경

| 항목 | 값 |
| --- | --- |
| 운영체제 | Windows 11 |
| Unity | `2020.3.9f1` |
| 프로젝트 | 공식 `ZEPETO Studio Unity Project File 3.2.16` 템플릿 |
| ZEPETO Studio | `3.2.16` (최소 요구 `3.2.12`) |
| zepeto.character | `3.1.32` |
| helper 설치 형태 | embedded (`Packages/com.easy.zepeto-helper`) |
| 작업 scene | `Assets/Playground.unity` (템플릿 제공) |
| 의상 prefab | 예시로 `Assets/Contents/TRANSPARENT_1/TRANSPARENT_1.prefab`를 사용 (템플릿이 제공하는 샘플 의상 폴더 중 하나. 헬퍼가 요구하는 고정 경로가 아니다) |

### Unity 버전에 대한 참고

ZEPETO **World**(월드/게임)는 World SDK 1.22.00부터 Unity `2022.3.34f1`을 요구하지만, 이 패키지가 다루는
**아이템/의상 제작 파이프라인은 Unity `2020.3.9`**를 그대로 사용한다. 공식 아이템 템플릿
`ZEPETO Studio Unity Project File 3.2.16`의 `ProjectVersion.txt`도 `2020.3.9f1`로 고정되어 있다.
World 쪽 2022 마이그레이션 문서를 아이템 제작에 적용하면 안 된다.

## 0.3.0에서 확인한 SDK 사실

reflection으로 실제 SDK 어셈블리를 조사한 결과다. 코드가 의존하는 전제라서 기록해 둔다.

| 헬퍼가 찾는 serialized 필드 | 실제 소유 타입 | 타입 |
| --- | --- | --- |
| `zepetoId` | `Zepeto.ZepetoCharacterCustomLoader` (ZEPETO) | `String` |
| `AnimationClip` | `ZEPETO.Studio.PlaygroundController` | `AnimationClip` |
| `AnimatorController` | `ZEPETO.Studio.PlaygroundController` | `AnimatorOverrideController` |

- `ZepetoStudioLoader`에는 이 세 필드가 **없다**. 필드는 서로 다른 두 컴포넌트에 나뉘어 있다.
- 따라서 `LOADER` 오브젝트 하나만 훑는 방식은 template 구성에 따라 실패할 수 있다.
  0.3.0부터 `LOADER` → 자식 → scene 전체 순으로 탐색 범위를 넓힌다.
- UPM 패키지 4종(`zepeto.studio`, `zepeto.character`, `zepeto.asset`, `zepeto.asset.protector`) 안에는
  `.unity` scene 파일도, 의상 prefab도 **없다**. 작업 scene과 의상 prefab은 ZEPETO Studio에서 받는
  의상 템플릿 프로젝트에서 와야 한다(계정 로그인 필요).

## 주요 Audit 결과

- Major: package/cache 안의 원본 animation clip을 직접 수정하면 SDK asset이 손상될 수 있음.
  - 현재 구현은 `Assets/ZepetoHelper/Animations` 아래 복사본만 저장 대상으로 사용한다.
- Major: 실행 확인용 임시 clip이 작업 clip을 영구 교체하면 사용자가 상태를 잃을 수 있음.
  - 현재 구현은 `clip_adjust_preview.anim`을 임시 연결하고, 실행 종료 후 원래 clip으로 복구한다.
- Major: 공식 ZEPETO 내보내기는 먼저 `<의상명>.zepeto`를 만든다.
  - 현재 구현은 공식 파일이 실제 존재할 때만 읽기 쉬운 파일명으로 이동한다.
- Major: ZEPETO export나 domain reload 이후 `SerializedObject` target이 사라질 수 있음.
  - 현재 구현은 stale reference를 감지하면 `LOADER`와 serialized field를 다시 찾는다.
  - 0.3.0에서 아이디 적용, clip 연결, controller 교체, 검증 경로의 guard 누락을 보완했다.
- Major: 패키지에 개인 ZEPETO 아이디가 기본값으로 들어 있었음.
  - 0.3.0에서 코드의 기본값을 제거했다.
  - 재유입 검사 `no-personal-id-in-source`의 **현재 범위**: `CollectShippedPackageFiles`가 모으는
    `Editor/` 아래 `.cs` 20개 전부와, 패키지 root · `docs/` · `Documentation~/`의 모든 `.md`를 읽고,
    `PersonalTokens`의 토큰이 한 줄이라도 있으면 실패한다. 실패 메시지에 `파일:줄:토큰`이 찍힌다.
    수집 결과가 0개면 vacuous pass가 되지 않도록 `Fail`을 낸다.
    (범위를 넓히기 전에는 `Editor/ZepetoStudioHelperWindow.cs` **한 파일**만 읽었다. 그래서 아이디가 이
    문서의 `### 계정별 아바타 로딩` 표와 README 예시 문장에 남아 tarball에 실려 나가는데도 통과했다.)
  - **범위를 넓힌 뒤에도 `.cs`와 `.md`만 본다.** `.png`는 수집되지 않고, 텍스트 스캔이라 적용될 수도 없다.
  - 0.9.0에서 한 일은 **문서 텍스트의 아이디 2개를 placeholder로 교체한 것까지**였고, 캡처 이미지 안의
    아이디는 그대로 남아 있었다. 0.9.1에서 캡처 6장을 재촬영해 그중 아이디가 보였던 것들을 해결했다.
    **아직 2장이 남아 있다** — 위 `배포 전 차단 항목` 참고. 검사 자체는 여전히 PNG를 볼 수 없으므로,
    이 표가 검사를 대신하는 기록이다.
- Major: 배포 패키지에 개발용 Unity MCP bridge 코드가 남아 에디터 로드마다 실행되고 있었음.
  - 0.3.0에서 전부 제거했다.
- Major: `Assets/Contents`나 SDK animation 폴더가 없을 때 `AssetDatabase.FindAssets`가 매 repaint마다
  콘솔 경고를 내고, helper의 안전 패널이 그 경고를 다시 집계하고 있었음.
  - 0.3.0에서 검색 전 `IsValidFolder`로 막는다.

## 자체 테스트

`Assets/ZepetoHelperTests/Editor/ZepetoHelperSelfTest.cs`

이 환경에서는 Unity batch mode 라이선스 활성화가 되지 않아(GUI에서만 활성화됨) 실행 중인 에디터 안에서
돌린다. 프로젝트 root에 `zepeto-helper-selftest.trigger` 파일을 만들고 에디터에 포커스를 주면
다음 script reload 때 실행되며, 결과는 `zepeto-helper-selftest.result.txt`에 남는다.
`Window > Easy > Run ZEPETO Helper Self Test` 메뉴로도 실행할 수 있다.

### 최근 결과: 60 pass / 0 fail (공식 템플릿 프로젝트에서 실행)

`zepeto-helper-selftest.result.txt`의 `pass=60 fail=0`. 이 표는 그 파일의 검사 이름을 그룹으로 묶은 것이다.
`NOTE` 줄은 pass/fail이 아니라 실측값 기록이라 개수에 들어가지 않는다.

| 그룹 | 검사 내용 |
| --- | --- |
| **실제 템플릿** | `Assets/Playground.unity`의 진짜 `LOADER`에 세 필드 모두 바인딩, 계정 2개 전환, 의상 prefab 발견, SDK 동작 10개 발견 |
| 개인 아이디 제거 | `BuiltInDefaultZepetoId` 상수 부재, 배포되는 `.cs`·`.md` 전체에 개인 아이디 문자열 부재. **`.png`는 보지 않는다** — 위 `배포 전 차단 항목` 참고 |
| MCP 코드 제거 | `GetUnityMcpBridgePort` 외 4개 멤버 부재 |
| 버전 비교 | `3.2.16 > 3.2.12`, `3.2.9 < 3.2.12`, 동일 버전, prerelease suffix 처리 |
| SDK 탐지 | 설치 감지, 최소 버전 충족, 설치 형태(embedded/registry) 보고 |
| 아이디 정규화 | 앞의 `@` 제거, 앞뒤 공백 제거, 중간 공백 제거 |
| 아이디 검증 | 정상 아이디 허용, 빈 값·기호·URL 거부 |
| 여러 계정 | 계정 A 적용 → 계정 B 적용 → `@` 붙은 형태 → 계정 A 복귀 (`apply-id:*`) |
| 잘못된 아이디 | scene 값을 덮어쓰지 않고 거부 (`apply-id:reject-invalid`) |
| 저장 목록 제거 | 0.7.0에서 뺀 저장된 아이디 기능이 **다시 들어오지 않았는지** (`saved-ids:removed`), 창이 씬 LOADER에서 아이디를 읽어오는지 (`id-from-scene`) |
| scene 탐색 | `LOADER`가 든 scene을 이름과 무관하게 발견, 하드코딩 경로 미사용 |
| 필드 바인딩 | 필드가 자식 오브젝트에 있을 때, 다른 root 오브젝트에 있을 때 모두 바인딩 |
| 재생 슬롯 | local override controller 생성, 슬롯이 선택한 동작으로 갱신, 정지 포즈가 남지 않음 |
| 동작 카탈로그 | 목록이 채워짐(13개), SDK 클립이 Humanoid, 정지 포즈 감지, 기본 선택이 포즈가 아님, 포즈 적용 차단 (`catalog:*`) |
| 라이브 확인 | 클립 내용을 덮어써도 GUID·instanceID 유지, 내용 교체, 이름 복구, 반복 설정 적용 (`live:*`) |

## 코드 구조

`Editor/`는 하나의 `partial class ZepetoStudioHelperWindow`를 관심사별 파일 **20개**로 나눈 것이다.
타입이 하나이므로 파일 이동만으로 동작이 달라지지 않는다.
아래 표는 실제 디렉터리 목록과 각 파일 머리의 doc comment 기준이다.

| 파일 | 담당 |
| --- | --- |
| `ZepetoStudioHelperWindow.cs` | 창 껍데기. 공유 상태, Unity 생명주기, 최상위 렌더 진입점 |
| `.Accounts.cs` | 아이디 정규화·검증 규칙, LOADER에 아이디 적용, 옛 저장 키 일회성 삭제 |
| `.Loader.cs` | LOADER 바인딩, AnimatorOverrideController 재생 슬롯 |
| `.Scenes.cs` | `LOADER`가 든 작업 scene 탐색과 열기 |
| `.ScenePreview.cs` | 정지 중 Scene에 기본 몸 세우기. `HideFlags.DontSave`, Play 시작 시 제거 |
| `.Motion.cs` | SDK 동작 목록, 편집용 복사본 |
| `.MotionImport.cs` | 외부 애니메이션 FBX(Mixamo·Blender)를 아바타가 재생할 수 있는 클립으로 만들기 |
| `.RigExport.cs` | 기본 몸을 FBX로 내보내기, 되가져온 모션의 리타게팅 소스 Avatar 지정 |
| `.GoToBlender.cs` | Unity를 떠나는 지점. `.blend` 파일 찾기와 Blender 열기 |
| `.LivePreview.cs` | Play 중 Blender 출력 폴더 감시. 고정 클립 애셋의 내용만 덮어써 리바인드 없이 반영 |
| `.ClipEdit.cs` | 배속·구간·반복 편집, 새 `.anim` 저장 |
| `.Export.cs` | 공식 `.zepeto` export 실행과 결과 보고 |
| `.Publish.cs` | 만든 모션이 실제로 갈 수 있는 곳(ZEPETO World) 안내 |
| `.Safety.cs` | Play 차단 판정, 로그 기반 안전 스냅샷 |
| `.Validation.cs` | 진단 목록 검사 |
| `.Flow.cs` | 7개 번호 단계의 배치. 단계 잠금 없음 (0.9.0 신설) |
| `.Workflow.cs` | 7개 단계 카드를 움직이는 **내부 4단계 상태 기계**와 진행 표시. 단계 번호는 카드 번호가 아니다 (파일 머리 주석의 매핑 표 참고) |
| `.Steps.cs` | 단계 카드 UI |
| `.Ui.cs` | 재사용 그리기 요소와 단계 상태 표현 |
| `.SdkPackage.cs` | SDK 설치 감지와 버전 비교 |

### 실제 재생 경로 (0.3.1에서 계측)

Play 중 `Animator`를 직접 읽어 확인한 사실이다.

- 베이스 컨트롤러 `ZepetoBaseModel`에 교체 가능한 클립 슬롯은 **`dynamic` 하나뿐**이다.
- `PlaygroundAnimatorController`(AnimatorOverrideController)가 그 슬롯을 교체하며, SDK 기본 매핑은
  **`dynamic -> A_pose`** (길이 0.04초 정지 포즈)다.
- `PlaygroundController.AnimationClip` 필드는 이 매핑을 바꾸지 않는다.
- `ZepetoStudioLoader`가 노출하는 API는 `Awake() / InitializeRoom3DSpace() / OnGUI()` 뿐이다.

계측 결과:

| 설정 | 실제 재생된 clip |
| --- | --- |
| `AnimationClip = Videobooth_282` (23.13s)만 지정 | `A_pose` (0.04s) — 아바타 정지 |
| override slot `dynamic -> Videobooth_282` | `Videobooth_282` (23.13s) — 정상 동작 |

따라서 동작을 바꾸려면 반드시 override 슬롯을 다시 써야 한다. 0.3.1의 `AssignAnimationClip`이 이를 수행한다.

### 계정별 아바타 로딩

같은 씬·같은 동작 설정에서 아이디만 바꿔 Play한 대조 결과다.

| 아이디 | LOADER 하위 | SkinnedMeshRenderer | 재생 clip |
| --- | --- | --- | --- |
| `내_아이디_1` (아바타 있음) | `Zepeto Context` | 7 | `Videobooth_282` |
| `내_아이디_2` (아바타 없음) | 없음 | 0 | 없음 |

아바타가 로드되지 않으면 동작 설정과 무관하게 아무것도 보이지 않는다. 이 경우는 아이디 자체를 확인해야 한다.

### 실제 템플릿에서 확인된 LOADER 구성

`Assets/Playground.unity`의 `LOADER` 오브젝트 하나에 세 컴포넌트가 함께 붙어 있다.

| 컴포넌트 | helper가 쓰는 serialized 필드 |
| --- | --- |
| `Zepeto.ZepetoCharacterCustomLoader` | `zepetoId` |
| `ZepetoStudioLoader` | (없음. `playgroundController` 참조만 보유) |
| `ZEPETO.Studio.PlaygroundController` | `AnimationClip`, `AnimatorController` |

`zepetoId`는 값이 비어 있으면 scene YAML에 기록되지 않지만 `SerializedObject`에는 정상 노출되므로
바인딩과 쓰기 모두 동작한다.

## 아직 확인하지 못한 것

- 실제 `.zepeto` export 실행과 ZEPETO Studio 업로드. export 자체는 의상 prefab이 준비되어 있어
  실행 가능하지만, 업로드 결과 확인은 ZEPETO 계정 로그인이 필요하다.
- `MoveOfficialExportToFriendlyName`은 SDK의 출력 파일명 규칙에 의존한다. 규칙이 바뀌면 수정이 필요하다.
- Play mode에서의 아바타 실제 로딩. 네트워크로 ZEPETO 계정 아바타를 받아오므로 유효한 계정이 필요하다.
