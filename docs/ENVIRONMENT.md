# 환경 설정

이 문서는 `ZEPETO Studio Helper`를 다른 Unity 프로젝트에서 바로 쓰기 위해 필요한 환경을 정리합니다.

## 검증한 환경

| 항목 | 값 |
| --- | --- |
| 운영체제 | Windows 11 |
| Unity | `2020.3.9f1` |
| ZEPETO Studio | `3.2.12` |
| helper 패키지 | `com.easy.zepeto-helper@0.2.0` |
| ZEPETO registry | `https://upm.zepeto.run` |

## 반드시 필요한 것

- Unity `2020.3.x`
- 공식 `ZEPETO Studio SDK`
- ZEPETO scoped registry
- `LOADER`가 있는 scene
- `Assets/Contents` 아래 의상 prefab
- `.zepeto` 업로드까지 하려면 ZEPETO 계정과 업로드 권한

## ZEPETO registry 설정

Unity 프로젝트의 `Packages/manifest.json`에 아래 registry가 필요합니다.

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

## ZEPETO Studio 패키지 추가

`dependencies`에 아래 줄이 필요합니다.

```json
{
  "dependencies": {
    "zepeto.studio": "3.2.12"
  }
}
```

이미 다른 dependency가 있으면 기존 항목은 유지하고 `zepeto.studio`만 추가하세요.

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

상단 메뉴에 아래 항목이 보이면 설치가 된 것입니다.

```text
Window > Easy > ZEPETO Studio Helper
```

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

## 포함되지 않는 것

이 helper는 아래를 대신하지 않습니다.

- ZEPETO Studio SDK 설치
- ZEPETO 계정 로그인
- 업로드 심사
- Maya/Blender에서 해야 하는 의상 mesh 수정
- 자동 cloth simulation 또는 자동 rig 보정

즉, 공식 SDK 위에서 Unity Editor 작업 흐름을 정리하는 패키지입니다.
