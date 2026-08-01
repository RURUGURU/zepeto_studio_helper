<div align="center">

# ZEPETO 모션 파이프라인

Blender에서 춤을 만들어 Unity의 내 ZEPETO 아바타 위에서 바로 확인하는 작업대.
**Blender 버튼 다섯 개 → Unity 창 클릭.** 그게 한 사이클 전부입니다.

<img src="ZEPETO%20Studio%20Unity%20Project%20File%203.2.16/Packages/com.easy.zepeto-helper/docs/images/dance-demo.gif" alt="이 도구로 만든 10초 안무" width="240">

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

## 시작하기

**처음이라면 [`BlenderMotion/README_모션만들기.md`](BlenderMotion/README_모션만들기.md) 하나만 보시면 됩니다.**
Blender를 한 번도 안 써봤다는 전제로 쓰여 있고, 막히는 지점마다 화면에 뜨는 한국어 문구를 그대로
표로 옮겨 뒀습니다.

Unity 헬퍼 창 자체의 사용법은
[`Packages/com.easy.zepeto-helper/README.md`](ZEPETO%20Studio%20Unity%20Project%20File%203.2.16/Packages/com.easy.zepeto-helper/README.md)에 있습니다.

프로젝트의 현재 상태·검증 기록·함정 목록은 [`STATUS.md`](STATUS.md)가 원본입니다.

## 검증

전부 **실제로 실행해서** 나온 수치입니다. 문서에 적힌 숫자는 그 실행 결과를 옮긴 것이지 목표치가
아닙니다.

| 무엇 | 결과 | 어떻게 다시 돌리나 |
| --- | --- | --- |
| 초보자 왕복 | **15 / 15** | `blender --background BlenderMotion\zepeto_motion.blend --python BlenderMotion\beginner_check.py` |
| 10초 안무 제작 | **13 / 13** | `blender --background BlenderMotion\zepeto_motion.blend --python BlenderMotion\make_dance.py` |
| Blender 애드온 (헤드리스) | **29 / 29** | `blender --background --factory-startup --python BlenderMotion\headless_check.py` |
| Blender 패널 draw | **17 / 17** | `blender --factory-startup --python BlenderMotion\ui_check.py` (`--background` 금지) |
| Unity 자체 테스트 | **70 / 70** | `Window > Easy > Run ZEPETO Helper Self Test` |

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
- `docs/images/play-preview.png`에는 **제작자 본인의 ZEPETO 아바타**가 나옵니다. 의도된 상태입니다.
- `Assets/Playground.unity`에는 제작자의 제페토 아이디가 들어 있습니다. 포크해서 쓰실 때는 헬퍼
  1번 카드에서 본인 아이디로 바꾸세요.
- **안무는 저작물입니다.** 이 저장소의 예제 안무는 창작이고, 남의 안무를 그대로 옮겨 배포하지 마세요.
