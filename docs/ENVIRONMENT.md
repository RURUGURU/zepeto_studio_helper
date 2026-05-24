# 환경 설정

`ZEPETO Studio Helper`는 공식 ZEPETO Studio SDK 프로젝트 안에서 여는 Unity Editor 창입니다.

## 검증한 환경

| 항목 | 값 |
| --- | --- |
| 운영체제 | Windows 11 |
| Unity | `2020.3.9f1` |
| ZEPETO Studio | `3.2.12` |
| helper 패키지 | `com.easy.zepeto-helper@0.2.3` |
| ZEPETO registry | `https://upm.zepeto.run` |

## 필요한 준비물

| 준비물 | 확인 방법 |
| --- | --- |
| 공식 ZEPETO Studio SDK | `Packages/manifest.json`에 `zepeto.studio`가 있음 |
| helper 패키지 | `Packages/manifest.json`에 `com.easy.zepeto-helper`가 있음 |
| ZEPETO `LOADER` scene | Unity Hierarchy에서 `LOADER` 확인 |
| 의상 prefab | `Assets/Contents` 아래에 prefab 배치 |
| 업로드 권한 | 최종 업로드 시 ZEPETO 계정에서 확인 |

## ZEPETO registry 설정

`Packages/manifest.json`에 아래 registry가 필요합니다.

```json
{
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

## ZEPETO Studio SDK 추가

`dependencies`에 아래 줄이 필요합니다.

```json
{
  "dependencies": {
    "zepeto.studio": "3.2.12"
  }
}
```

## helper 패키지 추가

GitHub에서 바로 설치:

```json
{
  "dependencies": {
    "com.easy.zepeto-helper": "https://github.com/RURUGURU/zepeto_studio_helper.git"
  }
}
```

로컬 개발 중인 프로젝트에서 설치:

```json
{
  "dependencies": {
    "com.easy.zepeto-helper": "file:com.easy.zepeto-helper"
  }
}
```

## Unity에서 새로고침

`manifest.json`을 수정했다면 Unity에서 아래 메뉴를 실행합니다.

```text
Assets > Refresh
```

또는 Unity를 종료한 뒤 다시 열어도 됩니다.

## 설치 확인

상단 메뉴에 아래 항목이 보이면 helper가 설치된 것입니다.

```text
Window > Easy > ZEPETO Studio Helper
```

실제 창은 README 상단의 `docs/images/helper-window.png` 화면처럼 표시됩니다.

## 초보자 확인 순서

1. Unity에서 ZEPETO 프로젝트를 엽니다.
2. Project 창에서 의상 prefab이 `Assets/Contents` 아래에 있는지 확인합니다.
3. Hierarchy에서 `LOADER`가 보이는 scene을 엽니다.
4. `Window > Easy > ZEPETO Studio Helper`를 엽니다.
5. README의 `처음 사용하는 순서` 표대로 1번부터 4번까지 진행합니다.

## 프로젝트 구조 예시

```text
Assets/
  Contents/
    TRANSPARENT_1/
      TRANSPARENT_1.prefab
  ZepetoHelper/
    Animations/
      ClipEdits/
      Preview/
Packages/
  manifest.json
```
