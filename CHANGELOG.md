# 변경 기록

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
