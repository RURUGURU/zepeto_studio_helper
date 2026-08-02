# ZEPETO 작업 인수인계 - 2026-08-02

## 계정 및 핵심 경로

- 제페토 ID: `sery_2750`
- 저장소: `C:\Users\pc\Desktop\zepeto_studio_helper-github`
- Unity 미리보기: `C:\Users\pc\Desktop\zepeto_studio_helper-github\unity-project`
- Blender 파일: `C:\Users\pc\Desktop\zepeto_studio_helper-github\BlenderMotion\zepeto_motion.blend`
- World 준비 폴더: `C:\Users\pc\Desktop\ZepetoWorldSetup`
- 최종 World 예정 경로: `C:\Users\pc\Desktop\ZepetoWorld`

## 현재까지 된 것

- Blender 5.2 및 Unity 2020.3.9f1 설치.
- Blender 애드온 1.6.1 안내 개선 및 테스트 통과.
- `DanceDemo10s.fbx` 생성 및 실제 런타임 아바타 애니메이션 확인.
- Unity 2022.3.34f1과 Android Build Support 설치.
- 공식 ZEPETO World 설치 패키지 및 Android 의존성 다운로드.
- 실제 아바타 ID `sery_2750`로 미리보기 확인.

## 주요 증상

- Poiyomi `Thry.ShaderEditor` 메시지는 런타임 잠금 재질의 Inspector 경고이며 회전 실패 원인이 아님.
- Play 중 Animator가 팔 뼈 회전을 계속 덮어씀.
- Animator를 끄면 아바타가 사라지므로 사용하면 안 됨.
- 아바타 로딩 완료 전에 Pause하면 Game 화면이 비어 보임.
- `F`는 선택이 아니라 화면 초점/거리 조정임.
- 숨겨진 `upperArm_L/R` 직접 선택은 Helper가 `LOADER`로 선택을 되돌려 불안정함.
- `Zepeto.SwingBoneProcessor.Update()` NullReference가 반복됐으며, 미리보기에서 해당 프로세서 하나를 비활성화한 뒤 새 오류가 멈추는 것을 확인함.

## 수동 팔 조작 프로토타입

- `ZEPETO > Manual Pose` 메뉴와 `ZEPETO MANUAL ARM HANDLE` 프록시 핸들을 추가함.
- 핸들 선택과 회전 기즈모 표시는 성공함.
- 아직 해결할 점: 아바타 렌더링이 끝나기 전에 Pause하여 Game 화면이 비었음.
- 다음 수정은 활성 `SkinnedMeshRenderer` 확인 후 1~2초 더 기다린 다음 Pause해야 함.

## World 및 업로드 상태

- 현재 Unity 프로젝트는 World 프로젝트가 아니라 아이템/아바타 미리보기 프로젝트임.
- 실제 `C:\Users\pc\Desktop\ZepetoWorld` 프로젝트 생성과 모션 연결은 미완료.
- World 업로드에는 사용자 로그인과 소유 World ID 연결이 필요함.
- Git 원격은 원본 `RURUGURU/zepeto_studio_helper`이며, GitHub CLI 2.97.0과 `RURUGURU` 계정 로그인을 확인함.
- `RURUGURU/zepeto_studio_helper`에 대한 현재 계정 권한은 `ADMIN`임.
- 해결되지 않은 Unity 수동 팔 핸들 코드는 로컬 실험으로 남기고, 완료된 Blender 안내·모션 검증·이 인수인계 문서만 먼저 별도 브랜치의 draft PR로 올리는 것이 안전함.

## 사용자 작업 방식

- Computer Use 도구 사용 금지.
- 터미널 자동화는 허용하지만 Blender/Unity는 최종적으로 GUI로 조작 가능해야 함.
- 초보자가 그대로 따라 할 수 있는 자세한 한국어 GUI 설명 필요.
