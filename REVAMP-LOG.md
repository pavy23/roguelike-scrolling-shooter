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

## 2026-09-29 — 실제 키 안내와 메뉴 입력 분리

사용자 요청에 따라 Git 배포본 플레이 테스트는 뒤로 두고 로컬 개편을 이어갔다.
이동·강화 키를 바꾼 뒤에도 옛 안내가 남는 문제와 설정/일시정지 사이의 입력 충돌을
수정하고, 현재 선택된 버튼과 옵션 행을 더 분명하게 표시했다.

- 저장된 바인딩은 입력 초기화 시 불러온다. `PlayerBindings`가 실제 적용 경로에서
  표시 문자열을 얻으며, 옵션과 온보딩이 같은 정보를 사용한다. 저장/초기화 시 열린
  온보딩 안내도 갱신한다.
- 키 변경은 해당 키보드 바인딩에만 적용한다. 패드 바인딩과 화살표 대체 이동은
  유지하고, 다른 이동/강화 키와 겹치거나 폭탄 B를 선택하면 이유를 표시하고 기다린다.
- Escape로 키 변경을 취소해도 저장값을 바꾸지 않으며 원래 액션 활성 상태를 복원한다.
  설정 닫기/취소 입력이 같은 프레임에 일시정지 해제까지 수행하지 않게 한다.
  일시정지/입력 비활성화 시 강화·폭탄 입력 래치를 정리한다.
- 공통 버튼에 호박색 선택 테두리를 추가하고 옵션의 선택 행도 같은 색으로 강조한다.
  랭킹 모달에 최초 선택과 Escape/패드 East 뒤로가기를 제공하며, 모달을 닫는 입력이
  같은 프레임에 타이틀 출격을 실행하지 않도록 한다.

검증:

| 항목 | 결과 |
| --- | --- |
| CoreStandalone | **597 passed / 0 failed** |
| Unity EditMode | **626 passed / 0 failed / 0 skipped** |
| 추가 회귀 테스트 | 저장/재로드/초기화, 장치 제한·중복 키 거절, 취소·일시정지 유지, 이동 대체 키·안내, 실제 강화 입력 전달, 캡처 종료, 선택 테두리 — 9개 |
| 테스트 증거 | `out/revamp/bindings-unity.xml`, `bindings-unity.log` |
| 선택 테두리 렌더 | `out/revamp/title-ui-review-dev.png`, 1280×720; `focus-capture.log` |
| J/I 변경 안내 렌더 | `out/revamp/options-ui-review.png`, 1280×720; `bindings-capture.log` |
| 비교 전 이미지 | `out/revamp/dev-focus-before.png`, `options-bindings-before.png` |

입력 테스트는 실제 InputAction 에셋의 복사본과 가상 Keyboard/Gamepad를 사용했다.
Input System 1.19의 EditMode 플레이어 업데이트 플래그는 테스트용 설정 복사본에만
켜고 원래 설정과 사용자 바인딩을 복원한다. Core/GameData·아트·음원은 변경하지 않았다.

이미지는 GPU batch Editor의 오프스크린 렌더다. 개발 모달에는 선택 이벤트를 주입했고,
옵션에는 복사한 액션의 강화 J/위 이동 I를 주입해 실제 표시 코드를 확인했다. 옵션
배경은 타이틀이며 전투 플레이 화면은 아니다. 두 화면의 텍스트와 테두리를 확인했지만,
장치별 전체 탐색·실플레이·실제 청취까지 검증한 것은 아니다. WebGL 빌드나 배포는 하지 않았다.
Unity가 바꾼 TimeManager 직렬화 형식은 원복하고 기존 시간 설정을 유지했다.

다음 UI 단위는 시간만 지나면 사라지는 온보딩을 실제 행동과 연결하는 것과
HUD/선택 카드의 정보 우선순위다. 전체 장치 탐색과 작은 화면 검증도 계속 남겨둔다.

## 2026-09-29 — 행동 기반 비행 안내와 강화 HUD

기존 첫 출격 안내는 조작하지 않아도 18초가 지나면 완료 저장값을 남겼다.
이제 Core 스텝의 전후 상태와 캡슐 획득 이벤트를 읽어 이동·획득·투자를 확인한다.

- 이동 입력과 실제 좌표 변화가 모두 있어야 이동을 배운 것으로 기록한다.
  디지털/아날로그 입력을 지원하고, 벽을 향해 누르기만 한 경우는 진행하지 않는다.
- 캡슐 획득 이벤트를 확인하고, 선택 슬롯의 투자량/레벨이 실제로 바뀐 경우만
  강화 성공으로 기록한다. 일부 비용 적립도 성공이며, 빈 선택·계약 거절은 제외한다.
- 실제 행동 3개를 확인한 뒤에만 `rss.onboarded`를 저장한다. 구 완료 기록은 존중하고,
  PC/터치 옵션의 `REPLAY FLIGHT GUIDE`로 다시 볼 수 있다. 다시 보기는 런·강화 상태나
  일시정지를 바꾸지 않는다. 리플레이·자동 조종·비활성 입력 관찰은 완료에 포함하지 않는다.
- 튜토리얼은 단계 표기와 배경을 갖춘 하단 안내로 분리했다. 보상·계약·종료·일시정지·
  리플레이 화면에서는 숨기며 기체가 뒤에 들어오면 투명도를 낮춘다. 3단계에서 선택이
  없거나 계약이 막고 있으면 같은 단계 안에서도 안내를 갱신한다.
- 강화 게이지 위에 실제 키와 투자 진행도/즉시 강화/최대/계약 잠금을 표시한다.
  선택 슬롯은 색과 `>` 표식으로 구별한다. 실드·폭탄 소진은 `EMPTY`로 적고,
  PC 폭탄에는 B/EAST 키 안내를 둔다. 새로운 실드 숫자를 다른 위치에 중복 추가하지 않았다.
- 회귀 검증에서 Core 기본 게이지의 `powerUp.shield` 키가 재고 표시 분기에 들어가지
  않던 문제를 발견했다. 슬롯 종류로 판별하고 내부 이름 접두사를 표시에서 제거했다.

검증:

| 항목 | 결과 |
| --- | --- |
| CoreStandalone | **597 passed / 0 failed** |
| Unity EditMode | **644 passed / 0 failed / 0 skipped** |
| 추가 회귀 테스트 | 18개: 무조작, 디지털/아날로그 이동·경계, 실제 캡슐 획득·투자, 계약 거절, 일부 투자, 리플레이/자동 조종/정지 제외, 재관찰, 화면 가림, 다시 보기·구 저장값, HUD 상태·빈 실드 |
| 테스트 증거 | `out/revamp/onboarding-unity.xml`, `onboarding-unity.log` |
| 변경 전 전투 UI | `out/revamp/battle-ui-before.png` |
| PC 강화 안내 | `out/revamp/battle-ui-ready.png`, 1280×720 |
| 터치 계약 잠금 안내 | `out/revamp/battle-ui-locked-touch-640.png`, 640×360 |
| PC/터치 옵션 | `out/revamp/options-ui-review.png`, `options-ui-review-touch-640.png` |
| 캡처 로그 | `battle-ui-after.log`, `battle-ui-touch.log`, `onboarding-options.log`, `onboarding-options-touch.log` |

캡처는 Unity CLI `1.0.0-beta.11` / Pipeline `0.8.0-exp.1`의 GPU batch Editor에서
생성했다. 전투 씬에는 실제 GameData로 만든 독립 런과 튜토리얼 3/3 표시 상태를 주입했다.
타이틀 배경 위 옵션 화면도 별도로 렌더했다. 문구·슬롯·옵션 버튼이 겹치지 않는 것을
확인했으며, 640×360 잠금 화면에서 발견한 모순 안내도 수정했다. 이 자료는 Play Mode,
완전한 터치 UI, 실제 장치 조작·애니메이션·성능 검증을 대신하지 않는다.

Core/GameData·씬·신규 아트/음원·패키지 설정은 변경하지 않았다. Unity가 남긴
TimeManager 직렬화 형식 변경은 원복했다. 커밋은 로컬 개편 브랜치에만 남기며,
WebGL 빌드·GitHub push·`rss-play` 배포는 진행하지 않았다.
다음 단위는 보상/계약 카드의 정보 우선순위와 선택 피드백, 이후 음악/효과음 분리다.

## 2026-09-29 — 2단계: 보상·계약 선택 개편

기존 보상 UI는 Core가 계약에 따라 4개를 생성해도 3개만 표시했다. 무기 교체는
실제 무기 이름 없이 SWAP으로 표시했고, 계약 제목은 카드 위에 겹쳤다. 봉인 계약의
`NO GAUGE x1.6`은 점수 배율이라는 설명이 빠져 있었다. 또한 직접 키 입력과
EventSystem Submit이 별도로 동작해 이전 선택이나 다음 화면까지 확정할 여지가 있었다.

변경:

- 보상 1~4개를 중앙 배치하고 네 번째 카드에 숫자 4/패드/포인터 선택 경로를 제공했다.
  이름, 실제 효과, 이번 런의 대가를 별도 영역에 표시한다. 미사일·드론 편대 교체 이름,
  캡슐의 커서 이동/화폐 효과, 실드의 재고 회복, 실제 주무기 피해 증가량을 명확히 했다.
  드롭 가중치 감소는 고정 개수나 확률 감소로 오해하지 않도록 FEWER CAPSULE DROPS로 표시한다.
- 계약 제목과 미리보기를 분리하고 모든 효과를 BENEFITS/TRADE-OFFS로 묶었다.
  SCORE는 독립된 항목이며, 봉인은 해당 게이지 입력/슬롯이 잠기는 것으로 설명한다.
  위험 등급은 제목에, 현재 선택은 앰버 테두리와 SELECTED 문자에 표시한다.
- 리롤에 R/패드 Y 단축키, 비용·잔고·지불 후 잔고, 부족한 캡슐 수와 완료 안내를 추가했다.
  성공 즉시 Core가 새로 생성한 후보로 다시 그리며, 부족하면 버튼을 비활성화한다.
- ChoiceButton은 포인터의 선택 강조/누름 상태를 유지하면서 중복 Submit을 받지 않는다.
  BattleDirector는 성공한 선택만 기록하고, 리롤·보상·계약 사이에서 같은 프레임의
  입력을 한 번만 받는다. 재생/일시정지/옵션/보스 격파 연출 중 수동 선택을 차단한다.
  재개 버튼으로 닫은 프레임도 PauseScreen이 차단해 뒤쪽 보상을 고르지 않게 했다.
- 빈 후보 목록에서는 이전 카드를 숨긴다. 디밍 영역이 뒤쪽 UI의 포인터 입력을 막는다.
  기존 아트·폰트·해상도를 사용하고 보상 화면에 보스 초상화를 다시 추가하지 않았다.

검증:

| 항목 | 결과 |
| --- | --- |
| CoreStandalone | **597 passed / 0 failed / 0 skipped** |
| Unity EditMode | **663 passed / 0 failed / 0 skipped** |
| 추가 회귀 테스트 | 19개: 실제 Core 1~4개 생성, 네 번째 키/클릭 선택, 리롤+확정 동시 입력, 중복 차감/다음 화면 확정 차단, 부족·정지·재생·연출, 재개, 포인터/패드 커서, 빈 목록, 전체 현재 보상·계약 문구의 공간 검사 |
| 최종 테스트 증거 | `out/revamp/choices-unity.xml`, `choices-unity.log` |
| 변경 전 | `choice-reward-before.png`, `choice-reward-four-before.png`, `choice-contract-before.png` |
| PC 최종 | `choice-reward-after.png`, `choice-reward-four-after.png`, `choice-reward-rerolled-after.png`, `choice-contract-after.png`, `choice-contract-final-after.png` |
| 터치 최종 | 위 화면의 `-touch-640.png` 변형, 캡슐 부족 상태 포함 |
| 캡처 로그 | `choices-before.log`, `choices-after-final.log`, `choices-touch-final.log` |

첫 회귀 실행의 2건 실패는 테스트가 1개 후보를 Main -2로 구성한 오류(실제는 Mid -1)와
네 장 배치에서 캡슐 설명이 4줄로 넘친 문제였다. 실제 경로로 테스트를 수정하고 설명을
짧게 한 뒤 통과했다. 최종 테스트에는 일시정지 재개와 계약 스틱 탐색 검증도 포함했다.

캡처는 `Tools/QaHarness/capture_choices.eval.cs`를 Unity CLI `1.0.0-beta.11` /
Pipeline `0.8.0-exp.1`에서 실행했다. 실제 Battle 씬 UI에 독립 런과 데이터 기반
후보를 주입한 GPU batch Editor 렌더다. PC 리롤 캡처는 실제 Core 리롤을 호출해
8→4 캡슐 차감과 새 후보 표시를 확인했다. 1280×720 및 640×360에서 문구·제목·카드가
겹치지 않는 것을 확인했다. 이 자료는 Play Mode, 실기기 조작, 전체 런 플레이 검증이 아니다.

Core/GameData·씬·신규 아트/음원·패키지는 변경하지 않았다. Unity가 다시 직렬화한
TimeManager 설정은 원복한다. 로컬 `codex/revamp` 커밋이며 GitHub push, WebGL 빌드,
`rss-play` 배포는 보류한다. 다음 단위는 BGM/SFX 볼륨 분리와 UI 효과음이다.

## 2026-09-30 — 3단계: 오디오 설정과 UI 소리 피드백

Unity CLI로 확인한 기존 구성은 WebGL 타깃 / 48kHz, 씬마다 리스너 1개,
TitleBgm 1개와 Battle의 Bgm/Sfx 2개 소스였다. 믹서 에셋은 없고 모든 소스는 2D다.
음악 원래 레벨은 0.45, 전투 효과음은 1이었다. 전체 볼륨은 전투 씬의 PauseScreen에서만
불러와 타이틀까지 일관되게 반영하지 못하고, 음악만 낮출 수도 없었다.

변경:

- 전체/음악/전투 효과음/UI 소리 네 가지 사용자 볼륨과 저장을 추가했다.
  기존 `rss.volume`은 전체 볼륨으로 그대로 읽어 기존 0(음소거)도 유지한다.
  새 채널은 100% 기본값으로 기존 소스 믹스를 보존하며, 읽을 때 잘못된 범위를 보정한다.
- AudioChannelSource가 작성된 원래 레벨 × 장면 연출 배율 × 사용자 채널 볼륨을
  적용한다. 음악 덕킹과 설정값을 분리해 음소거 해제·소스 재활성화에서 볼륨이
  이중으로 곱해지지 않으며, 격파 연출과 결과 화면에서도 사용자 설정은 즉시 반영된다.
  징글은 기존 음악 소스에 있으므로 MUSIC 설정을 따른다.
- 공용 오디오 설정 창에 4개 슬라이더, ± 버튼, 음소거 표시, 오디오 기본값 복원,
  뒤로 가기를 추가했다. 타이틀은 AUDIO / F3 / 패드 Start, 일시정지는 AUDIO SETTINGS /
  V / 패드 Y로 진입한다. 방향키/패드와 마우스/터치 모두 지원한다. Enter/A는
  선택한 채널 음소거/복구다. 다른 게임 설정은 오디오 기본값 복원으로 바꾸지 않는다.
- 오디오 창의 입력은 타이틀 출격·격납고·다른 옵션·보상에서 차단한다. 열기 입력으로
  곧바로 음소거되거나, 뒤로 가기로 곧바로 재개되는 같은 프레임 입력도 차단했다.
  일시정지에 PC 클릭 버튼과 RESUME 초기 포커스를 제공한다.
- UI 전용 소스 1개가 씬 사이에서 유지된다. 이동/확정/취소/거절은 채택된
  sfx_pickup / sfx_powerup / sfx_hit 클립의 낮은 재생 레벨과 피치로 구분한다.
  같은 프레임 중복을 억제하고, 거절이 확정보다 우선하며, 이동음 간격을 제한한다.
  전투 일시정지 동안 UI 소리는 허용하되 전체/UI 음소거는 존중한다.
- 타이틀·격납고·옵션·보상/계약 및 공용 버튼에 소리 피드백을 연결했다.
  신규 음악/효과음 파일을 생성하거나 채택하지 않았다. 공개 AudioSource API를 사용했고
  믹서 내부 API나 수동 mixer 파일 편집은 사용하지 않았다.

검증:

| 항목 | 결과 |
| --- | --- |
| CoreStandalone | **597 passed / 0 failed / 0 skipped** |
| Unity EditMode | **683 passed / 0 failed / 0 skipped** |
| 추가 회귀 테스트 | 20개: 기존 전체 음소거, 개별 저장/복원, 손상된 값, 음악 덕킹/재활성화/연출 중 설정, UI 음소거·중복·없는 클립, 키보드/패드 조작, 열기/닫기 입력 전파, 슬라이더 콜백, 현재 씬의 채널/클립/리스너 배선 |
| 테스트 증거 | `out/revamp/audio-unity.xml`, `audio-unity.log` |
| PC 타이틀 | `audio-title-entry.png`, `audio-title-settings.png` |
| PC 일시정지 | `audio-battle-entry.png`, `audio-battle-settings.png` |
| 640×360 터치 | `audio-battle-entry-touch-640.png`, `audio-battle-settings-touch-640.png` |
| 도구 로그 | `audio-inventory.log`, `audio-wiring.log`, `audio-title-capture.log`, `audio-touch-capture.log`, `audio-pause-final.log` |

최초 실행의 2건 실패는 ignoreListenerPause를 저장된 씬 속성으로 검사한 테스트 오류였다.
해당 값은 UiAudio.Awake에서 설정하므로 런타임 초기화를 거친 검사로 수정했고 통과했다.
추가로 BgmPlayer.Update와 격파 연출의 조기 반환에서도 음소거가 유지되는지 검증했다.

캡처는 Unity CLI `1.0.0-beta.11` / Pipeline `0.8.0-exp.1` GPU batch Editor에서
실제 UI를 생성한 정적 렌더다. 표시용 볼륨을 주입하고 기존 설정값은 복구한다.
픽셀 배치와 제어 상태를 확인했으며, 테스트/캡처 중 UI 소리를 재생하지 않는다.
이는 실제 청음, Play Mode 전환, 모바일 터치, WebGL의 최초 사용자 제스처 이후 오디오
시작 검증을 대체하지 않는다. 위험 경고가 전투 소리 속에 묻히는지와 UI 음량 체감도는
사용자가 미룬 Git 플레이 테스트에서 확인한다.

두 씬은 기존 장면을 재생성하지 않고 Editor API로 오디오 구성만 추가했다. 저장 과정에서
이전에 제거된 사용하지 않는 Attack 액션 이름 직렬화 필드가 정리됐다. Core/GameData,
음원 임포트 설정, 패키지, 카메라 규격은 변경하지 않았다. TimeManager의 자동 직렬화
차이는 원복한다. 로컬 개편 브랜치에 커밋하며 WebGL 빌드·GitHub push·rss-play 배포는
계속 보류한다. 다음 단위는 전투 경고음·피격음·폭발음이 겹칠 때의 우선순위와 반복 피로다.
