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
