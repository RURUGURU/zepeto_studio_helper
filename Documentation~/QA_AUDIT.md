# QA / Audit 기록

## 범위

패키지: `com.easy.zepeto-helper`

핵심 파일: `Editor/ZepetoStudioHelperWindow.cs`

목표: 공식 ZEPETO Studio SDK 프로젝트에서 다른 사람도 바로 사용할 수 있는 Unity Editor helper 패키지로 정리한다.

## 주요 Audit 결과

- Major: package/cache 안의 원본 animation clip을 직접 수정하면 SDK asset이 손상될 수 있음.
  - 현재 구현은 `Assets/ZepetoHelper/Animations` 아래 복사본만 저장 대상으로 사용한다.
- Major: 실행 확인용 임시 clip이 작업 clip을 영구 교체하면 사용자가 상태를 잃을 수 있음.
  - 현재 구현은 `clip_adjust_preview.anim`을 임시 연결하고, 실행 종료 후 원래 clip으로 복구한다.
- Major: 공식 ZEPETO 내보내기는 먼저 `<의상명>.zepeto`를 만든다.
  - 현재 구현은 공식 파일이 실제 존재할 때만 읽기 쉬운 파일명으로 이동한다.
- Major: ZEPETO export나 domain reload 이후 `SerializedObject` target이 사라질 수 있음.
  - 현재 구현은 stale reference를 감지하면 `LOADER`와 serialized field를 다시 찾는다.

## QA/QC 상태

| 항목 | 상태 |
| --- | --- |
| Unity script compile | 통과 |
| `ZepetoStudioHelperWindow.cs` console error | 통과 |
| `error CS` console error | 통과 |
| legacy motion/pose edit 문자열 검사 | 통과 |
| `.zepeto` export smoke test | 통과 |
| 다른 사용자 clean project 수동 테스트 | 미실행 |

## 검증 명령어

Unity 프로젝트 root에서 실행:

```powershell
rg -n "PoseEdit|MotionEdit|Motion Adjust|모션 조정|shallow pose" Packages\com.easy.zepeto-helper\Editor\ZepetoStudioHelperWindow.cs
```

기대 결과: 출력 없음.

Unity Console 확인:

```text
Console 검색어: ZepetoStudioHelperWindow.cs
Console 검색어: error CS
```

기대 결과: C# 오류 0건.

내보내기 smoke test 근거:

```text
ZepetoHelperExportSmoke PASS: Assets/Contents/TRANSPARENT_1/ZEPETO_TRANSPARENT_1_VideoBooth_139_v02.zepeto
```

## 남은 리스크

- ZEPETO SDK의 공식 export 파일명 규칙이 바뀌면 `MoveOfficialExportToFriendlyName` 수정이 필요할 수 있다.
- 검증 기준은 현재 확인한 `zepeto.studio 3.2.12`이다.
- 완전히 새로운 Unity 프로젝트에서의 수동 walkthrough는 별도 확인이 필요하다.
