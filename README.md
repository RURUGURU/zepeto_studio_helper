<div align="center">

# ZEPETO Studio Helper

공식 ZEPETO Studio SDK 프로젝트에서 의상 확인, 동작 선택, 클립 조정, `.zepeto` 생성을 한 창에서 순서대로 진행하는 Unity Editor 패키지

[![Unity](https://img.shields.io/badge/Unity-2020.3.9f1-222222?logo=unity)](https://unity.com/)
[![ZEPETO](https://img.shields.io/badge/ZEPETO%20Studio-3.2.12-2563eb)](https://studio.zepeto.me/)
[![패키지](https://img.shields.io/badge/패키지-com.easy.zepeto--helper-16a34a)](package.json)
[![검증](https://img.shields.io/badge/검증-Unity%20MCP%20확인-22c55e)](Documentation~/QA_AUDIT.md)

![ZEPETO Studio Helper 실제 Unity 화면](docs/images/helper-window.png)

</div>

## 바로 쓰는 기준

이미 ZEPETO Studio SDK로 만든 Unity 프로젝트라면 이 패키지를 추가한 뒤 아래 메뉴를 열면 됩니다.

```text
Window > Easy > ZEPETO Studio Helper
```

필요한 준비물은 단순합니다.

| 준비물 | 확인 위치 |
| --- | --- |
| 공식 `ZEPETO Studio SDK` | `Packages/manifest.json`의 `zepeto.studio` |
| 이 helper 패키지 | `Packages/manifest.json`의 `com.easy.zepeto-helper` |
| ZEPETO `LOADER`가 있는 scene | Unity Hierarchy |
| 테스트할 의상 prefab | `Assets/Contents` 아래 |

계정 로그인과 최종 업로드 심사는 ZEPETO 공식 흐름에서 진행하고, 이 helper는 Unity 안에서 헷갈리는 클릭 순서와 저장 위치를 정리합니다.

## 이 화면에서 하는 일

| 단계 | 버튼 흐름 | 결과 |
| --- | --- | --- |
| `1. 아바타와 의상 준비` | ID 적용, 의상 적용, Play 확인, `1번 적용 / 다음 단계` | 아바타와 의상이 준비되고 2번으로 이동 |
| `2. 동작 선택` | 동작 선택, 미리보기 Play/Stop, `2번 적용 / 작업 동작으로 사용` | 선택한 동작의 작업용 복사본이 `LOADER`에 연결 |
| `3. 클립 조정` | 배속, 시작/끝, 반복 조정, `3번 적용 / 저장 후 다음 단계` | 조정된 `.anim` 파일 저장 |
| `4. 저장과 내보내기` | Play로 저장 결과 확인, `4번 완료 / .zepeto 생성` | 최종 `.zepeto` 생성 경로 표시 |

완료된 단계는 잠기고, 다시 바꾸고 싶을 때만 `수정 잠금 해제`로 열어 수정합니다.

## 설치 방법

Unity Package Manager에서 설치:

1. Unity 프로젝트 열기
2. `Window > Package Manager`
3. 왼쪽 위 `+`
4. `Add package from git URL...`
5. 아래 주소 입력

```text
https://github.com/RURUGURU/zepeto_studio_helper.git
```

`Packages/manifest.json`에 직접 넣을 때:

```json
{
  "dependencies": {
    "com.easy.zepeto-helper": "https://github.com/RURUGURU/zepeto_studio_helper.git",
    "zepeto.studio": "3.2.12"
  },
  "scopedRegistries": [
    {
      "name": "ZEPETO",
      "url": "https://upm.zepeto.run",
      "scopes": [
        "zepeto"
      ]
    }
  ]
}
```

이미 `dependencies`와 `scopedRegistries`가 있다면 기존 내용을 지우지 말고 필요한 줄만 추가하세요.

로컬 tarball로 설치할 때:

```powershell
git clone https://github.com/RURUGURU/zepeto_studio_helper.git
cd zepeto_studio_helper
npm pack
```

생성되는 파일:

```text
com.easy.zepeto-helper-0.2.1.tgz
```

Unity에서는 `Window > Package Manager > + > Add package from tarball...`로 이 `.tgz`를 선택합니다.

## 실제 사용 순서

1. ZEPETO용 Unity 프로젝트를 엽니다.
2. 의상 prefab을 `Assets/Contents` 아래에 둡니다.
3. ZEPETO `LOADER`가 있는 scene을 엽니다.
4. `Window > Easy > ZEPETO Studio Helper`를 엽니다.
5. `1-1. 아이디 입력`에서 ID를 확인하고 `ID 적용`을 누릅니다.
6. `1-2. 의상 선택`에서 prefab을 고르고 `의상 적용`을 누릅니다.
7. `1-3. Play 확인`에서 Play로 확인한 뒤 Stop을 누르고 `1번 적용 / 다음 단계`를 누릅니다.
8. `2. 동작 선택`에서 동작을 고르고 미리보기 Play/Stop으로 확인한 뒤 `2번 적용 / 작업 동작으로 사용`을 누릅니다.
9. `3. 클립 조정`에서 배속, 시작 시간, 끝 시간, 반복을 조정하고 Play로 확인한 뒤 `3번 적용 / 저장 후 다음 단계`를 누릅니다.
10. `4. 저장과 내보내기`에서 저장 결과를 확인하고 `4번 완료 / .zepeto 생성`을 누릅니다.
11. 화면의 `출력 파일` 줄에서 저장된 `.zepeto` 경로를 확인합니다.

## 생성되는 파일

| 종류 | 위치 |
| --- | --- |
| 작업용 동작 복사본 | `Assets/ZepetoHelper/Animations` |
| 클립 조정 결과 | `Assets/ZepetoHelper/Animations/ClipEdits` |
| 임시 미리보기 clip | `Assets/ZepetoHelper/Animations/Preview/clip_adjust_preview.anim` |
| 최종 `.zepeto` 파일 | 의상 prefab이 있는 폴더 |

파일명 예시:

```text
ZEPETO_TRANSPARENT_1_VideoBooth_139_v02.zepeto
```

검증된 실제 출력 예시:

```text
Assets/Contents/TRANSPARENT_1/ZEPETO_TRANSPARENT_1_VideoBooth_139_v02.zepeto
```

## 검증한 환경

| 항목 | 값 |
| --- | --- |
| 운영체제 | Windows 11 |
| Unity | `2020.3.9f1` |
| ZEPETO Studio | `3.2.12` |
| 패키지 이름 | `com.easy.zepeto-helper` |
| 패키지 버전 | `0.2.1` |
| ZEPETO registry | `https://upm.zepeto.run` |

자세한 환경 설정은 [docs/ENVIRONMENT.md](docs/ENVIRONMENT.md)에 정리했습니다.

## 개발자 명령어

패키지 폴더로 이동:

```powershell
cd C:\Users\Jun-WN\Desktop\zepeto\zepeto-studio-unity-3.2.12\Packages\com.easy.zepeto-helper
```

패키지 내용 확인:

```powershell
npm pack --dry-run --json
```

실제 `.tgz` 생성:

```powershell
npm pack
```

프로젝트 산출물 폴더로 옮기기:

```powershell
New-Item -ItemType Directory -Force -Path ..\..\Build\Packages
Move-Item -Force .\com.easy.zepeto-helper-0.2.1.tgz ..\..\Build\Packages\com.easy.zepeto-helper-0.2.1.tgz
```

압축 파일 내용 확인:

```powershell
tar -tzf ..\..\Build\Packages\com.easy.zepeto-helper-0.2.1.tgz
```

## QA / Audit

상세 검증 기록은 [Documentation~/QA_AUDIT.md](Documentation~/QA_AUDIT.md)에 있습니다.
