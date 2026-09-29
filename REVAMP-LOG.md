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
