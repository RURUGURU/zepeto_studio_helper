# 변경 기록

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
