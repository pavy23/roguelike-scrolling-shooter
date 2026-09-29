# 개편 변경 기록

## 2026-09-29 — 1단계 첫 변경: 레이저 위험 구간 표시

첫 예고는 발사원의 이전 최대 폭을 관측해야 정확해지는 구조였다. 처음 보는
굵은 빔에는 0.75유닛 대체 폭을 사용했고, 같은 발사원의 공격이 바뀌면 이전 폭을
재사용할 수 있었다. 또한 Firing 진입 시 피해 판정은 즉시 전장에 생기는데,
그림은 0.18초 동안 짧은 길이에서 자라 보이지 않는 구간에서도 피격될 수 있었다.

변경:

- Core가 공격 정의의 `FullHalfWidth`를 첫 Telegraph 상태부터 전달한다.
  현재 충돌 폭 `HalfWidth`와 피해 단계는 그대로 유지한다.
- Presentation의 관측 폭 캐시를 제거하고, 현재 공격의 명시적인 폭을 사용한다.
- 본체와 코어는 첫 발사 프레임부터 전체 선분을 표시한다. 착탄 표시는 끝점에
  고정하고, 뻗어 나가는 연출은 바깥 광채에 유지한다.
- 플레이어 빔은 현재 성장한 폭을 계속 표시한다.
- Unity 전용 표현 테스트 어셈블리를 추가했다. CoreStandalone의 순수 C# 테스트와
  분리해 SpriteRenderer의 실제 길이·방향·폭을 검사한다.

검증:

| 항목 | 결과 |
| --- | --- |
| 수정 전 회귀 재현 | Core 예고 폭 2건 실패. Unity 표현 6건 실패, 플레이어 폭 1건 통과 |
| 수정 후 CoreStandalone 전체 | **597 passed, 0 failed** |
| 수정 후 Unity EditMode 전체 | **598 passed, 0 failed** (표현 테스트 8건 포함) |
| 시드 12345, 3스테이지·9개 방 | 20,513틱. 변경 전후 해시 모두 `1D3B51FCF6AF2D54` |
| 최신 CLI와 기존 Pipeline | `unity run --command editor_status` 성공, `ready` |
| 설정 diff | CLI 테스트가 변경한 `runInBackground`를 기존 값으로 복원 |

재현 명령 (저장소 루트 기준):

```powershell
dotnet test Tools/CoreStandalone --no-restore --nologo
unity test . --mode EditMode --output out/revamp/laser-unity.xml --timeout 300 --format json --non-interactive -- -nographics -logFile out/revamp/laser-unity.log
dotnet run --project Tools/DeterminismAudit --no-restore -- 12345 3 30000
```

결정론 해시는 새 표현용 파생 값을 중복해서 넣지 않는다. 공격 정의의 원래 폭과
현재 충돌 폭은 기존 감사에 포함되므로, 이번 수정 전후의 게임플레이 추적 결과를
직접 비교할 수 있다. 저장·리플레이 형식과 GameData 수치는 변경하지 않았다.

범위와 남은 검증:

- 이번 결과는 컴파일·시뮬레이션·렌더러 기하 검증이다. 실제 배경 위에서의 명암,
  눈부심, 위험 띠 가장자리 인지와 발사 체감은 화면 캡처·실플레이로 확인해야 한다.
- 1단계 다음 항목은 재생성 벽의 표시 크기와 최종 보스 사망/결과 화면 순서다.
  그래픽·도트 애니메이션·스테이지 구성 개편은 `REVAMP-PLAN.md` 순서를 따른다.
- CLI는 `1.0.0-beta.11`, Pipeline은 연결 검증을 통과한 `0.4.0-exp.1`이다.
  연결 문제가 재현되면 사용자 지시에 따라 최신 호환 Pipeline으로 업데이트한다.

## 2026-09-29 — UI·UX / 오디오 검토와 다음 표현 수정

전체 화면 흐름, 설정·입력 안내, 오디오 이벤트, BGM 전환, 감소 설정의 적용 범위를
추가 조사했다. 근거·우선순위·후속 검증을 `UIUX-AUDIO-REVIEW.md`에 정리하고
UI·UX와 사운드 개선을 개편 단계에 명시했다. WAV 원본 22개를 조사했으며,
실제 청취나 런타임 믹스 품질을 검증한 것으로 취급하지 않는다.

이번 구현:

- 개별 크기가 있는 벽이 재생 중과 재생 종료에도 원래 배율을 유지한다.
- 최종 완주 결과 화면은 보스 격파 연출이 끝난 뒤 표시한다.
- 격파·함체 붕괴 연출 중에는 기존 음악을 유지하고 이후 클리어 징글/트랙 전환을
  처리한다. 플레이어 사망은 기존처럼 결과와 실패 음악을 즉시 처리한다.
- 성장/완료 및 풀 재사용, 기본·1.5배·2배 크기, 결과 지연과 사망 예외,
  보상/완주 음악 전환을 Unity 전용 테스트 12개로 검증한다.

검증 결과:

| 항목 | 결과 |
| --- | --- |
| 수정 전 회귀 재현 | 10개 중 7개 실패, 기본 크기·일반 사망 3개 통과 |
| 수정 후 CoreStandalone 전체 | **597 passed, 0 failed** |
| 수정 후 Unity EditMode 전체 | **610 passed, 0 failed** |
| 검증 도구 | Unity CLI `1.0.0-beta.11`, Editor `6000.5.3f1`, headless |
| 검증 산출물 | `out/revamp/transitions-unity.xml`, `transitions-unity.log`, `audio-inventory.json` |

새 테스트는 Core의 종료 상태를 주입한 뒤 실제 Presentation 메서드를 호출한다.
게임 전체의 승리 경로·오디오 출력·화면 합성까지 검증한 것은 아니다. 변경은
Presentation에 한정했고 Core/GameData와 씬·오디오 임포트 설정은 유지했다.
CLI 테스트가 남긴 `runInBackground` 변경은 원복했다.

다음 UI 작업은 항상 사격 정책과 옵션의 모순 정리, 실제 바인딩을 반영하는 안내,
입력 장치별 메뉴 흐름이다. 음악/효과음 분리와 UI 사운드, 플래시 감소의 일관성은
그다음 순서이며, 새 아트·사운드 채택 전에는 비교 시안과 청취가 필요하다.

## 2026-09-29 — 중간 화면 공유와 Pipeline 호환성 수정

사용자의 중간 스크린샷 공유 요청을 실행 계획에 반영했다. 첫 타이틀 캡처 중
Pipeline `0.4.0-exp.1`이 명령 인자 파싱에 `0.6.0-exp.1` 이상을 요구하며
`eval_file` 실행을 거절했다. 앞선 조건부 업데이트 지시에 따라 Unity CLI의
`pipeline upgrade`로 **`0.8.0-exp.1`**을 설치했다. Editor는 `6000.5.3f1`로 유지했다.

- 업데이트 후 Unity EditMode: **610 passed / 0 failed / 0 skipped**.
  증거: `out/revamp/pipeline08-unity.xml`, `pipeline08-unity.log`.
- Unity CLI와 GPU batch Editor로 창을 띄우지 않고 타이틀 PNG 생성 성공.
  재현 스크립트: `Tools/QaHarness/capture_title.eval.cs`, 사용법은 같은 폴더 README.
  저장소의 최종 스크립트로 재실행도 통과했다 (`capture-title-workflow.log`).
- `out/revamp/title-ui-review.png`: 1280×720, 현재 씬·UI 코드의 에디터 개발 모드
  오프스크린 렌더. 실플레이 캡처가 아니며 개발 패널이 포함된다. 씬은 저장하지 않았다.
- 제목/난이도, 시드/격납고 정보, 개발 패널/제목의 겹침을 확인했다.
  다음 UI 작업의 첫 비교 기준으로 `UIUX-AUDIO-REVIEW.md`에 반영했다.

최근 벽 재생·보스 결과/음악 순서 수정은 회귀 테스트까지 검증했다. 이번 타이틀
이미지는 그 전투 연출의 시각 검증을 대신하지 않는다. 실플레이와 실제 청취는 남아 있다.
CLI가 남긴 `runInBackground` 설정 변경은 원복했다.

## 2026-09-29 — 타이틀 배치와 항상 사격 정책 정리

첫 기준 화면의 제목/난이도, 시드/격납고, 개발 패널 겹침을 수정했다.
기존 uGUI·픽셀 폰트·팔레트·640×360 기준을 유지하고 화면 요소의 위치와 동작을 정리했다.

- 제목은 상단, 출격 설정/랭킹은 왼쪽, 기체 선택/설명은 중앙, 저장된 런/리플레이는
  오른쪽, 주 출격 버튼은 중앙 하단에 둔다. PC에도 클릭 버튼을 제공하고 기존 단축키를 유지한다.
- 기체 정보와 컨티뉴 재고에는 각각 배경 패널을 주어 배경 이미지 위에서 읽히게 한다.
  해금·구매 가격은 해당 버튼에 표시한다. 버튼 사이 간격은 실제 최소 높이 40px를 고려한다.
- 개발 도구는 기본적으로 접고 버튼/F2/패드 Select로 연다. 개발 모달과 랭킹은
  포인터와 뒤쪽 메뉴/격납고 동작을 차단한다. 개발 도구에는 최초 선택 대상을 지정한다.
  수동 시드의 점수 제출 불가 안내는 모달을 닫아도 유지하며, 데일리 모드에는 적용하지 않는다.
- 자동 사격 OFF와 발사 리바인딩 항목을 제거하고 옵션/온보딩에 자동 사격을 명시한다.
  입력 생성기는 구 `rss.autofire` 저장값과 Attack 액션 유무에 관계없이 발사한다.
  비활성 입력의 무입력 동작은 유지한다. Core와 GameData, 아트·음원은 변경하지 않았다.

검증:

| 항목 | 결과 |
| --- | --- |
| CoreStandalone | **597 passed / 0 failed** |
| Unity EditMode | **617 passed / 0 failed / 0 skipped** |
| 추가 회귀 테스트 | 구 자동 사격 저장값 2종, 옵션/닫기, PC·터치 모달 차단/초기 선택, 랭킹 뒤쪽 차단, 수동 시드 안내 — 7개 |
| PC 타이틀 | `out/revamp/title-ui-review.png`, 1280×720 |
| 터치 타이틀 | `out/revamp/title-ui-review-touch-640.png`, 640×360 |
| 개발 도구 | `out/revamp/title-ui-review-dev.png`, 1280×720 |
| 옵션 화면 | `out/revamp/options-ui-review.png`, 1280×720 |
| 변경 전 타이틀 | `out/revamp/title-ui-before.png` |
| 테스트 증거 | `out/revamp/menu-unity.xml`, `menu-unity.log` |

캡처는 GPU batch Editor에서 현재 UI 코드를 초기화한 오프스크린 렌더다.
옵션은 타이틀 배경 위에 UI를 올려 배치를 확인했으며 전투 일시정지 플레이 화면은 아니다.
실제 플레이, 모바일 안전 영역/작은 창, 장치별 전체 조작, 모든 저장 상태와 청취 검증은
아직 완료하지 않았다. 타이틀 캡처 도구는 터치·개발 모달·원본 배율을 환경변수로 지원한다.

다음 단위는 실제 바인딩과 안내의 일치, 메뉴 포커스 표시 및 장치별 입력 흐름이다.
