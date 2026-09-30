# 기본 기체 뱅킹 채택 검증

사용자 “현재후보 ㄱㄱ 그리고 다음작업” 지시로 상승/하강 A/B 네 장을 확정했다.
[채택 파일·해시](../adoption.json), [큐레이션 근거](../../CURATION.md)를 함께 보관한다.
새 PNG 4개와 GUID를 추가하고 저장된 Battle 씬의 배열 및 BattleSceneBuilder를 연결했다.
기존 중립 기체/엔진과 기존 343개 PNG 바이트는 유지했다.

| 검증 | 결과 |
| --- | --- |
| Unity EditMode | 792/792 통과, 실패·건너뜀 0. 저장된 씬의 4개 참조/크기/피벗/상승·하강 엔진 A/B 검증 포함 |
| CoreStandalone | 611/611 통과, 실패·건너뜀 0 |
| 원본·임포트 검사 | 기존 PNG 343개 불변, 새 4개 원본/생산 SHA-256 일치. 48×30, PPU16, 피벗 (24,15), Point, mipmap 없음, 압축 없음 |
| 저장된 씬 표시 코드 | 테마 0·2·4 × 8상태 = 24장. 새 후보 주입 없이 저장된 배열 사용 |
| 자세/엔진 | 상승 A/B, 하강 A/B, 반전 직후 중립, 정지 후 중립, 같은 틱 반복 시 정지 |
| 기하 | 위치·회전·색·포구 로컬 위치 불변. 포구 (0.85,0,0) 유지 |

도구: Unity CLI 1.0.0-beta.11 / Editor 6000.5.3f1 / Pipeline 0.8.0-exp.1.
전체 로그/XML은 추적하지 않는 `out/revamp/sfc-banking-adoption/`에 있다.
재현은 `Tools/QaHarness/apply_sfc_banking.eval.cs` 및 환경변수
`RSS_BANK_CAPTURE=adopted`를 사용하는 `capture_sfc_banking_scene.eval.cs`로 한다.
창 없는 배치이며 CLI의 중첩 실행 결과도 성공했다.

[24상태 기록](scene-capture.json) · [원본/배경 비교 뷰어](../review/index.html)

| 배경 | 상승 A | 상승 B | 하강 A | 하강 B |
| --- | --- | --- | --- | --- |
| 0 | [보기](battle-adopted-theme-0-tick-4.png) | [보기](battle-adopted-theme-0-tick-6.png) | [보기](battle-adopted-theme-0-tick-16.png) | [보기](battle-adopted-theme-0-tick-18.png) |
| 2 | [보기](battle-adopted-theme-2-tick-4.png) | [보기](battle-adopted-theme-2-tick-6.png) | [보기](battle-adopted-theme-2-tick-16.png) | [보기](battle-adopted-theme-2-tick-18.png) |
| 4 | [보기](battle-adopted-theme-4-tick-4.png) | [보기](battle-adopted-theme-4-tick-6.png) | [보기](battle-adopted-theme-4-tick-16.png) | [보기](battle-adopted-theme-4-tick-18.png) |

캡처는 실제 Core 이동과 표시 코드를 사용한 격리된 Editor 검수다. HUD를 숨겼으며
Play Mode/브라우저 실플레이 검증이 아니다. 사용자 저장이나 PlayerPrefs에 접근하지
않았다. 연속 전투의 조작감은 사용자 플레이 피드백으로 확인한다.

이번 적용에서 새 PixelLab 요청/비용은 없었다. 다음 묶음의
[전투 아트 연구](../../combat/README.md)는 별도 검토 자료이며 게임에 채택하지 않았다.

## 배포 상태

2026-09-30 22:31 KST 공개 파일 검증 완료.

- [지금 플레이](https://pavy23.github.io/rss-play/?v=20260930-222926-7eefb4c)
- 적용 커밋 `98e4e6e`, 최종 clean 빌드 소스 `7eefb4c6d9cb6dd2ee2df238228743aceadb01eb`.
- `rss-play` 배포 커밋 `6f676fb5f7255776928a6c546df5a9b736afbbbe`.
- [Pages 작업 36722079804](https://github.com/pavy23/rss-play/actions/runs/36722079804) 성공.
- index/build-info의 버전 일치, 게임 파일 4개 HTTP 200 및 크기·SHA-256 일치.
- [빌드 provenance](webgl-provenance.json), [파일 목록](build-info.json),
  [공개 파일 확인](served-verification.json), [검사 결과](validation-results.json).

첫 빌드에서 추가된 네 원본의 Unity Atlas GUID/이름 인덱스를 커밋한 뒤 최종 빌드를
다시 만들었다. `source.dirty=false`, BuildReport Success, 종료 코드 0, 빌드 후 clean을
확인했다. CLI가 기존 LicensingClient validation 진단 1건을 표시했으나 라이선스
획득과 빌드는 성공했다. 전체 오류 0건이었다고 표현하지 않는다.

공유 PC 규칙에 따라 브라우저 자동화/실주행은 하지 않았다. 자동 테스트·Editor
캡처·공개 파일 검증과 사용자 실제 플레이 검증을 구분한다.
