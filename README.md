<div align="center">

# ZEPETO 모션 파이프라인

Blender에서 춤을 만들어 Unity의 내 ZEPETO 아바타 위에서 바로 확인하는 작업대.
**Blender 버튼 다섯 개 → Unity 창 클릭.** 그게 한 사이클 전부입니다.

<img src="BlenderMotion/docs/dance-demo.gif" alt="이 도구로 만든 10초 안무" width="240">

*이 도구로 만든 10초 안무 — 20비트 @ 120 BPM. `BlenderMotion/make_dance.py`가 그대로 다시 만들어 냅니다.*

</div>

---

## ⚠️ 먼저 알아야 할 것 — 모션은 아이템으로 못 올립니다

제페토 스튜디오에 **모션 카테고리가 없습니다.** 업로드 가능한 항목은 전부 착용 아이템(옷·헤어·신발·
가방)입니다. 앱 안의 포즈·제스처는 제페토 서버의 공식 라이브러리에서만 오기 때문에, 내가 만든 동작을
그 목록에 넣을 방법이 없습니다.

| 하고 싶은 것 | 되나요 | 어떻게 |
| --- | --- | --- |
| 내 옷이 **움직일 때** 어떻게 보이는지 확인 | **됩니다** | 이 저장소 전체가 그것입니다 |
| 그 **옷**을 스튜디오에 올리기 | **됩니다** | 헬퍼 7번 → `.zepeto` → 스튜디오 웹 |
| 그 **모션**을 스튜디오에 올리기 | **안 됩니다** | 카테고리 자체가 없습니다 |
| 그 **모션**을 남에게 보여주기 | **됩니다** | **ZEPETO World** — 유일한 공식 목적지 |

근거와 확인 방법은 [`STATUS.md`](STATUS.md)의 `모션은 아이템으로 못 올립니다` 절에 있습니다.
헬퍼 7번 카드 안에서도 같은 내용을 안내하고 문서 링크 버튼을 제공합니다.

---

## 무엇이 들어 있나

| 폴더 | 무엇 |
| --- | --- |
| [`BlenderMotion/`](BlenderMotion/) | Blender 애드온 + 초보자 가이드 + 검사 스크립트 |
| `ZEPETO Studio Unity Project File 3.2.16/` | ZEPETO Studio 아이템 제작 템플릿 (SDK 3.2.16) |
| └ `Packages/com.easy.zepeto-helper/` | Unity 헬퍼 패키지 — **자체 git 저장소입니다** ([RURUGURU/zepeto_studio_helper](https://github.com/RURUGURU/zepeto_studio_helper)) |
| └ `Assets/ZepetoHelperTests/` | Unity 자체 테스트 + 러너 4개 |

## ⚠️ 저장소 두 개 — 이 저장소만 클론하면 Unity가 컴파일되지 않습니다

Unity 헬퍼 패키지는 **자기 git 저장소**를 갖고 있고, 이 저장소는 그 폴더를
[`.gitignore`](.gitignore)로 통째로 제외합니다. 바깥 저장소가 그 폴더를 추적하면 내용 없는
gitlink만 남아서 백업이 되지 않기 때문입니다.

그래서 이 저장소만 클론하면 `Packages/com.easy.zepeto-helper/`가 **빈 폴더**로 남고,
`Assets/ZepetoHelperTests`가 참조하는 `Easy.ZepetoHelper.Editor` 어셈블리가 없어서
**Unity가 컴파일 에러를 냅니다.** 두 개를 같이 받으세요.

```bash
git clone https://github.com/RURUGURU/zepeto-motion-pipeline.git
cd zepeto-motion-pipeline
git clone https://github.com/RURUGURU/zepeto_studio_helper.git \
    "ZEPETO Studio Unity Project File 3.2.16/Packages/com.easy.zepeto-helper"
```

두 번째 줄까지 끝나야 Unity를 여실 수 있습니다. 패키지를 고칠 때는 **그 폴더 안에서** 커밋하세요 —
바깥에서는 아예 보이지 않습니다.

## 설치 — 처음부터 순서대로

### 1. Unity 2020.3.9f1

[Unity Hub](https://unity.com/download)를 설치한 뒤, Hub의 `Installs > Install Editor >
Archive > download archive`에서 **2020.3.9f1**을 고릅니다. 최신 LTS가 아니라 이 버전이어야 합니다 —
ZEPETO SDK 3.2.16이 이 버전에 맞춰져 있습니다.

> Unity Personal 라이선스로 충분합니다. 다만 `-batchmode`는 못 씁니다(아래 `검증` 참고).

### 2. Blender 4.2 이상

[blender.org](https://www.blender.org/download/)에서 받습니다. 여기서 검증한 것은 **5.2.0 LTS**이고,
애드온이 요구하는 최소 버전은 4.2입니다.

### 3. 저장소 두 개 클론 — 위 경고 참고

```bash
git clone https://github.com/RURUGURU/zepeto-motion-pipeline.git
cd zepeto-motion-pipeline
git clone https://github.com/RURUGURU/zepeto_studio_helper.git \
    "ZEPETO Studio Unity Project File 3.2.16/Packages/com.easy.zepeto-helper"
```

### 4. Blender 애드온 설치

```
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background --python BlenderMotion\install_addon.py
```

`PASS :: 설치 완료` 두 줄이 나오면 끝입니다. 설치·활성화·설정 저장까지 한 번에 합니다.
손으로 하시려면 Blender의 `Edit > Preferences > Add-ons > Install from Disk`로
`BlenderMotion\zepeto_motion_helper.py`를 고르셔도 됩니다.

> **Blender의 설치는 복사입니다.** `zepeto_motion_helper.py`를 고친 뒤에는 위 명령을 다시 돌리세요.
> 안 그러면 낡은 사본이 계속 돕니다 — 에러가 안 나서 스스로는 드러나지 않습니다.
> `headless_check.py`의 `install:copy-matches-source`가 그 상태를 잡습니다.

### 5. Unity 프로젝트 열기

Unity Hub에서 `Add > Add project from disk`로 `ZEPETO Studio Unity Project File 3.2.16` 폴더를
고릅니다. 열리면 상단 메뉴 **`Window > Easy > ZEPETO Studio Helper`** 로 헬퍼 창을 띄웁니다.

헬퍼 패키지는 `Packages/` 안에 들어 있으므로(embedded) Unity가 알아서 인식합니다. **따로 설치할 것이
없습니다.** 다른 Unity 프로젝트에 이 패키지만 넣고 싶다면 패키지 저장소의
[`설치 방법`](https://github.com/RURUGURU/zepeto_studio_helper#설치-방법) 절에 세 가지 방법이 있습니다.

### 6. 내 제페토 아이디 넣기

헬퍼 **1번** 카드의 `아이디` 칸에 본인 아이디를 넣고 `ID 적용`. 5번의 라이브 확인이 이 아이디로
아바타를 내려받으므로, 이걸 안 하면 Play를 눌러도 아바타가 나타나지 않습니다.

---

## 사용법 — 전체 그림

```
 Unity 1·2번        Unity 3번         Blender 5단계        Unity 5번          Unity 6·7번
┌──────────┐      ┌──────────┐      ┌────────────┐      ┌──────────┐      ┌──────────┐
│ 아바타·의상 │ ──▶ │ 리그 내보내기│ ──▶ │ 포즈·키·루프 │ ──▶ │ 라이브 확인 │ ──▶ │ 조정·내보내기│
└──────────┘      └──────────┘      └────────────┘      └──────────┘      └──────────┘
   처음 한 번         평생 한 번            매번              매번            마지막
```

**한 사이클은 Blender 버튼 하나 + Unity 창 클릭입니다.** 5번의 초록 버튼으로 Play를 켜 두면,
Blender에서 `Unity로 보내기`를 누르고 Unity 창을 다시 클릭할 때마다 1~2초 안에 내 아바타에
반영됩니다. Play를 끄지 않습니다.

| 헬퍼 카드 | 무엇 | 언제 |
| --- | --- | --- |
| 1 | 아바타·의상 준비 (아이디 입력) | 처음 한 번 |
| 2 | 동작 고르기 (SDK 기본 동작 + 내 모션) | 매번 |
| 3 | Blender용 리그 FBX 내보내기 | **평생 한 번** |
| 4 | Blender 왕복 안내 | — |
| 5 | 내 캐릭터로 라이브 확인 (Play) | 매번 |
| 6 | 클립 조정 (속도·구간) | 필요할 때 |
| 7 | `.zepeto` 내보내기 + 월드 안내 | 마지막 |

Blender 쪽 5단계는 **몸 불러오기 → 포즈 → 키프레임 → 루프 → 내보내기**입니다.

### 어느 문서를 볼 것인가

| 문서 | 누구를 위한 것 |
| --- | --- |
| [`BlenderMotion/README_모션만들기.md`](BlenderMotion/README_모션만들기.md) | **처음이라면 이것 하나만.** Blender를 한 번도 안 써봤다는 전제로 쓰였고, 막히는 지점마다 화면에 뜨는 한국어 문구를 그대로 표로 옮겨 뒀습니다 |
| [패키지 README](https://github.com/RURUGURU/zepeto_studio_helper) | Unity 헬퍼 창 7개 카드의 실제 화면 캡처와 버튼별 설명 |
| [`STATUS.md`](STATUS.md) | 현재 상태·검증 기록·함정 16개. 이 프로젝트를 이어받는 사람용 |

## 검증

전부 **실제로 실행해서** 나온 수치입니다. 문서에 적힌 숫자는 그 실행 결과를 옮긴 것이지 목표치가
아닙니다.

Blender는 Windows에서 PATH에 등록되지 않으므로 아래 `$B`처럼 전체 경로로 부릅니다. 저장소 루트에서
실행하세요.

```powershell
$B = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
```

| 무엇 | 결과 | 어떻게 다시 돌리나 |
| --- | --- | --- |
| 초보자 왕복 | **15 / 15** | `& $B --background BlenderMotion\zepeto_motion.blend --python BlenderMotion\beginner_check.py` |
| 10초 안무 제작 | **13 / 13** | `& $B --background BlenderMotion\zepeto_motion.blend --python BlenderMotion\make_dance.py` |
| Blender 애드온 (헤드리스) | **29 / 29** | `& $B --background --factory-startup --python BlenderMotion\headless_check.py` |
| Blender 패널 draw | **17 / 17** | `& $B --factory-startup --python BlenderMotion\ui_check.py` — **`--background` 금지**, 창이 있어야 패널이 그려집니다 |
| Unity 자체 테스트 | **70 / 70** | Unity 메뉴 `Window > Easy > Run ZEPETO Helper Self Test` |

Unity 쪽 러너 넷(자체 테스트 · 리그 내보내기 · 커스텀 모션 · 라이브 왕복)을 트리거 파일로 돌리는
방법은 `STATUS.md`의 트리거 표에 있습니다.

> **Unity Personal은 `-batchmode`를 쓸 수 없습니다.** 라이선스가 유효해도 거부합니다.
> GUI 모드 + `-executeMethod`는 정상 동작하며, 위 Unity 검증은 전부 그 방식으로 했습니다.

## 환경

| 항목 | 버전 |
| --- | --- |
| Unity | 2020.3.9f1 |
| ZEPETO SDK | `zepeto.studio@3.2.16`, `zepeto.character@3.1.32` |
| Blender | 5.2.0 LTS (애드온 최소 요구 4.2) |
| 헬퍼 패키지 | 0.10.1 |
| Blender 애드온 | 1.5.1 |

## 라이선스와 저작권

- 이 저장소의 **코드와 문서**는 작성자의 것입니다.
- `ZEPETO Studio Unity Project File 3.2.16/` 안의 SDK·샘플 애셋은 **NAVER Z의 것**이고 여기 포함된
  것은 제페토 스튜디오가 배포하는 템플릿 원본입니다. 그쪽 이용약관을 따릅니다.
- 패키지 저장소의 `docs/images/play-preview.png`에는 **제작자 본인의 ZEPETO 아바타**가 나옵니다.
  의도된 상태입니다 (그 저장소 README의 `캡처 이미지에 대하여` 참고).
- `Assets/Playground.unity`에는 제작자의 제페토 아이디가 들어 있습니다. 포크해서 쓰실 때는 헬퍼
  1번 카드에서 본인 아이디로 바꾸세요.
- **안무는 저작물입니다.** 이 저장소의 예제 안무는 창작이고, 남의 안무를 그대로 옮겨 배포하지 마세요.
