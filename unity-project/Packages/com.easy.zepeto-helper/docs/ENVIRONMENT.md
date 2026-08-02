# 환경 설정

`ZEPETO Studio Helper`는 공식 ZEPETO Studio SDK 프로젝트 안에서 여는 Unity Editor 창입니다.

## 검증한 환경

| 항목 | 값 |
| --- | --- |
| 운영체제 | Windows 11 |
| Unity | `2020.3.9f1` |
| ZEPETO Studio | `3.2.12` 이상 (`3.2.16`에서 확인) |
| helper 패키지 | `com.easy.zepeto-helper@0.11.0` |
| ZEPETO registry | `https://upm.zepeto.run` |

## 필요한 준비물

| 준비물 | 확인 방법 |
| --- | --- |
| 공식 ZEPETO Studio SDK | `Packages/manifest.json`에 `zepeto.studio`가 있음 |
| helper 패키지 | 아래 `helper 설치 확인`을 보세요. **설치 형태에 따라 `manifest.json`에 안 적혀 있는 것이 정상입니다** |
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

`dependencies`에 아래 줄이 필요합니다. 값은 **검증한 프로젝트에 실제로 들어 있는 버전**이라 그대로
붙여넣어도 안전합니다.

```json
{
  "dependencies": {
    "zepeto.studio": "3.2.16"
  }
}
```

최소 요구 버전은 따로입니다.

- helper가 요구하는 **최소** 버전은 `3.2.12`이고, `3.2.12` 이상이면 모두 통과합니다.
- 이미 `zepeto.studio`가 있고 버전이 `3.2.16` 이상이면 **그 줄은 그대로 두세요.** 위 값으로 덮어쓰면
  SDK가 다운그레이드될 수 있습니다.

## helper 패키지 추가

설치 형태가 두 가지 있고, 확인 방법이 서로 다릅니다.

### 형태 A. 임베디드 — 폴더를 `Packages/` 아래에 두기 (이 문서가 검증한 형태)

패키지 폴더를 프로젝트의 `Packages/` 아래로 옮기면 끝입니다.

```text
<Unity 프로젝트 폴더>/Packages/com.easy.zepeto-helper/
```

- **`manifest.json`에 아무것도 적지 않습니다.** Unity는 `Packages/` 아래에 있는 폴더를 임베디드
  패키지로 자동 인식합니다. 검증한 프로젝트의 `Packages/manifest.json`에도 `com.easy.zepeto-helper`
  항목이 **없습니다** — 없는 것이 정상입니다.
- Unity가 인식하면 `Packages/packages-lock.json`에 아래처럼 기록됩니다. 이 파일은 Unity가 쓰는 것이므로
  손으로 편집하지 마세요.

  ```json
  "com.easy.zepeto-helper": {
    "version": "file:com.easy.zepeto-helper",
    "depth": 0,
    "source": "embedded",
    "dependencies": {}
  }
  ```

- **이 형태가 확실합니다.** 폴더를 직접 두는 것이라 버전이 어긋날 여지가 없습니다. 이 저장소를
  클론하면 이미 이 형태로 들어 있습니다.

### 형태 B. `manifest.json`의 dependency로 적기

`dependencies`에 줄을 추가하는 방식입니다. 이 경우에만 `manifest.json`에 항목이 보입니다.

git 주소 (**⚠️ 검증하지 않았습니다.** 저장소를 하나로 합치면서 패키지가 저장소 루트가 아니라
하위 폴더로 들어갔습니다. 그래서 UPM에 `?path=`로 위치를 알려줘야 하는데 **그 경로에 공백이
있습니다.** 아래는 공백을 `%20`으로 인코딩한 형태이고, 실제로 실행해 보지 않았습니다.
되지 않으면 위 **형태 A**(폴더째 두기)를 쓰세요 — 그쪽은 확실합니다):

```json
{
  "dependencies": {
    "com.easy.zepeto-helper": "https://github.com/RURUGURU/zepeto_studio_helper.git?path=/unity-project/Packages/com.easy.zepeto-helper"
  }
}
```

로컬 경로 (`file:`은 `Packages/` 폴더 기준 상대 경로입니다):

```json
{
  "dependencies": {
    "com.easy.zepeto-helper": "file:com.easy.zepeto-helper"
  }
}
```

폴더가 이미 `Packages/` 아래에 있으면 이 줄은 필요하지 않습니다. 형태 A로 이미 인식되기 때문입니다.

## Unity에서 새로고침

`manifest.json`을 수정했거나 패키지 폴더를 새로 넣었다면 Unity에서 아래 메뉴를 실행합니다.

```text
Assets > Refresh
```

또는 Unity를 종료한 뒤 다시 열어도 됩니다.

## helper 설치 확인

설치 형태와 무관하게 아래 두 가지로 확인합니다.

1. `Window > Package Manager`의 목록에 `com.easy.zepeto-helper`가 보입니다.
   (임베디드는 `In Project` / `Custom` 그룹에 나옵니다.)
2. 상단 메뉴에 아래 항목이 생깁니다. 이게 보이면 설치된 것입니다.

   ```text
   Window > Easy > ZEPETO Studio Helper
   ```

임베디드 형태라면 여기에 하나 더:

3. `Packages/com.easy.zepeto-helper/package.json` 파일이 실제로 있는지 확인합니다.

`Packages/manifest.json`에서 `com.easy.zepeto-helper`를 찾는 방법은 **형태 B에서만** 통합니다.
임베디드 형태에서는 거기에 없는 것이 정상이므로, 그것만 보고 "설치가 안 됐다"고 판단하면 안 됩니다.

실제 창 모습은 README의 캡처로 볼 수 있습니다. 어느 캡처가 무엇을 담고 있는지는 README의
`캡처 이미지에 대하여` 표가 파일 단위로 관리합니다 — 이 문서는 그 내용을 옮겨 적지 않습니다.

## 초보자 확인 순서

1. Unity에서 ZEPETO 프로젝트를 엽니다.
2. Project 창에서 의상 prefab이 `Assets/Contents` 아래에 있는지 확인합니다.
3. Hierarchy에서 `LOADER`가 보이는 scene을 엽니다.
4. `Window > Easy > ZEPETO Studio Helper`를 엽니다.
5. README의 `처음 사용하는 순서` 표대로 1번부터 7번까지 진행합니다.

## 프로젝트 구조 예시

`TRANSPARENT_1`은 공식 SDK 의상 템플릿에 들어 있는 **예시 의상 폴더**입니다. 정해진 경로가 아니라
아래처럼 생겼다는 예시일 뿐이고, 폴더 이름과 prefab 이름은 내 의상에 맞게 달라도 됩니다.
헬퍼는 `Assets/Contents` 아래에 있는 prefab을 직접 찾습니다.

```text
Assets/
  Contents/
    TRANSPARENT_1/            # 예시 의상 폴더. 이름은 달라도 됩니다
      TRANSPARENT_1.prefab
  CustomMotions/              # Blender 애드온이 FBX를 떨어뜨리는 곳. 5번이 이 폴더를 감시합니다
  ZepetoHelper/               # 아래 4개는 helper가 직접 만들고 씁니다
    Rig/                      # 3번이 내보낸 ZepetoBaseModel.fbx (Blender 작업용 몸)
    Motions/                  # FBX에서 뽑아낸 내 모션 .anim + 라이브 확인용 LiveFromBlender.anim
    Animations/               # 2번이 만드는 편집용 복사본
      ClipEdits/              # 6번이 저장한 조정 결과
      Preview/                # Play 확인 중에만 쓰는 임시 clip
    Controllers/              # 재생 슬롯을 바꿔 쓰는 override controller 사본
Packages/
  manifest.json               # zepeto.studio 와 scopedRegistries 가 여기 있습니다
  packages-lock.json          # Unity가 관리합니다. 손으로 고치지 않습니다
  com.easy.zepeto-helper/     # 임베디드 설치일 때. manifest.json에는 적지 않습니다
    package.json
```

`CustomMotions`와 `ZepetoHelper/Motions`와 `ZepetoHelper/Animations`는 **서로 다른 세 폴더**입니다.
이름이 비슷해서 하나로 합치고 싶어지지만, 첫 번째는 애드온과의 약속(애드온이 여기로만 내보내고 helper가
여기만 감시합니다), 두 번째는 내 모션 보관함, 세 번째는 편집 자격이 있는 복사본 자리입니다.
