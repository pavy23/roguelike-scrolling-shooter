# 기본 기체 뱅킹 — 승인 원본 적용

2026-09-30, `codex/revamp`. 사용자 “다음 가자” 지시에 따른 다음 전경 묶음.
사용자 **“현재후보 ㄱㄱ 그리고 다음작업”** 지시로 상승/하강 A/B 4장을 채택했다.
원본 바이트 그대로 새 에셋으로 임포트하고 Battle 씬과 빌더에 연결했다.
현재 적용·검증·배포 상태는 [채택 검증 기록](adoption-review/VALIDATION.md)을 따른다.

[확대 비교](review/banking-poses-fold.png) · [원본 배율/엔진 루프 뷰어](review/index.html) ·
[원본 감사](review/native-audit.json) · [저장된 씬 검증](adoption-review/scene-capture.json).
뷰어는 로컬 이미지로만 동작하며 외부 요청이나 사용자 저장 접근이 없다.

## 이번 구현

`PlayerShipAnimator`가 `BattleDirector`에서 관측한 Core의 실제 Y 이동으로 자세를
선택한다. 방향이 바뀌면 우선 중립, 같은 방향으로 3틱이 경과하면 해당 자세를 사용한다.
입력을 놓거나 경계에 막혀 위치가 멈추면 즉시 중립으로 돌아온다. 매 Core 스텝마다
관측하므로 렌더 프레임이 건너뛰어도 이동 이력을 잃지 않는다.

- 같은 틱 반복은 무시한다. 일시정지 중 자세와 엔진 프레임이 고정된다.
- 틱 되감기/관측 간격 누락/새 Battle/기종 변경 시 이전 자세를 초기화한다.
- 상승·하강 배열이 없거나 현재 프레임이 null이면 기존 중립 엔진으로 대체한다.
- 각 배열은 하나의 고정된 날개 자세와 배기 A/B로 구성한다. 움직이는 몸체 프레임을
  배열에 넣어 루프하지 않는다. 재생 속도는 기존 10fps 전투 틱 기준을 유지한다.
- Transform 위치·회전, 피격 tint, 충돌/발사 수치, Core와 GameData는 바꾸지 않는다.
- 저장된 씬과 빌더 모두 같은 상승/하강 A/B 배열을 사용한다.

## 후보와 제외 결과

모든 반환 PNG는 native 48×30 그대로 보존했다. 리사이즈·보정·색 치환·알파 수정은
하지 않았다. Unity가 만든 흑백 마스크와 색상 표는 API 제어 입력이며 생산 스프라이트가 아니다.

| 묶음 | 판단 |
| --- | --- |
| `bank-up`, `bank-down`, `bank-up-loop` | 몸체/기수가 이동하는 프레임이 섞인다. 연속 클립으로 채택하지 않는다. 요청 6/6/4프레임에 실제 반환 7/7/5장을 그대로 기록한다. 입력 복제 프레임도 포함한다. |
| `masked-up`, `masked-down` | 보호 영역 변화 0이나 날개가 넓고 각진 덩어리가 되어 제외한다. |
| `masked-*-refined` | 보호 영역 변화 0. 자세보다 명암 변화가 커 최종 제안에서 제외한다. 초기 비교는 `review/banking-poses.png`. |
| `masked-up-fold`, `masked-down-fold` | 날개 면/겹침을 다르게 한 최종 자세 후보. 29색, 부분 알파 0, 날개 마스크 밖 RGBA 변화 0. 실제 크기에서 자세 차이는 미세한 편이다. |
| `bank-*-engine` | 넓은 배기 마스크에 금속/청색이 섞여 제외한다. 자동 감사의 불꽃 기준도 실패한다. |
| `bank-*-engine-warm` | 자세별 B 후보. 원래 불꽃 영역 안의 44픽셀만 바뀌고 몸체 변화 0. 25색, 부분 알파 0. A보다 짧고 밝은 불꽃이며 사용자 승인을 받아 채택했다. |

팔레트 제어 이미지에 5색을 넣었어도 API는 새 따뜻한 크림색 `(242,221,192)`을
반환했다. 원본을 고치지 않고 감사에 추가 하이라이트로 명시했다. 강제 팔레트가
정확히 지켜졌다고 표현하지 않는다. 기존 5색 + 이 하이라이트 밖의 변경은 계속 제외한다.

새로 제안한 클립과 SHA-256은 [manifest.json](manifest.json)에 있다. 사용자에게
상승/중립/하강, A/B, 8배/2배/원본 배율 비교를 제시한 뒤 사람 승인을 받았다.
정확한 채택 범위와 원본/생산 파일 해시는 [adoption.json](adoption.json)에 있다.

## 후보 준비 시점의 확인과 한계

Unity CLI **1.0.0-beta.11**, Editor **6000.5.3f1**, Pipeline **0.8.0-exp.1**, URP.

| 검사 | 결과 |
| --- | --- |
| Unity EditMode 전체 | 791/791 통과, 실패/건너뜀 0. 새 뱅킹 회귀 5개 포함 |
| CoreStandalone | 611/611 통과, 실패/건너뜀 0 |
| 생성 클라이언트 오프라인 검사 | 12개 통과. 중복 과금 방지, 마스크 크기/흑백 검증, 비밀 제외, 원본 바이트 보존, 팔레트 요청 기록 |
| 원본/생산 PNG 감사 | 출력 29장, 13개 요청 기록. 기존 338개 + 첫 채택 5개 PNG 해시 불변 |
| 실제 표시 코드 검수 | 3테마 × 8상태 = 24장. 실제 Core 이동, 중립 복귀, 자세별 A/B, 같은 틱 반복, 포구 로컬 위치 불변 |

장면 캡처는 현재 Battle 씬과 실제 GameData를 사용한 **일회용 Editor 검수**다.
후보 스프라이트는 메모리로만 주입했다. Play Mode/브라우저/데스크톱 게임을 실행하지
않았고 HUD는 숨겼다. 실제 조작감, 연속 전투 가독성, WebGL 런타임의 통과 증거가 아니다.
코드 회귀 검사는 간격 누락·새 Battle·기종 변경·null 후보·피격 tint·Transform 보존을
별도로 다룬다. 전체 테스트 XML은 `out/revamp/sfc-banking/unity.xml`, `core.trx`에 있다.

원본/프롬프트/마스크/시드/API 사용량을 기록했지만 모델의 결정론적 재생성은 보장하지 않는다.
기존 PixelLab 전송·US$1 승인 범위에서 이 작업은 **US$0.150201418764**,
파일럿 누적 **US$0.262163876712**를 사용했다. API 합계와 잔액 차이 관측이 일치한다.
새 결제·충전·소스 코드 전송은 없으며 계정 잔액은 커밋하지 않는다.

## 재검증

```powershell
python Tools/ArtGen/audit_sfc_banking.py
python -m unittest discover -s Tools/ArtGen/tests -v
unity run . --command eval_file --timeout 300 --no-tail --format json --non-interactive -- --file Tools/QaHarness/capture_sfc_banking.eval.cs --timeout 60000
unity run . --command eval_file --timeout 300 --no-tail --format json --non-interactive -- --file Tools/QaHarness/capture_sfc_banking_scene.eval.cs --timeout 60000
```

공유 PC 규칙상 실제 Editor가 닫힌 상태에서 창 없는 배치로 실행한다. 원시 CLI 로그는
계정/라이선스 관련 값이 포함될 수 있으므로 추적하지 않는 `out/revamp/`에만 보관한다.
반환의 중첩 `data.result.success`도 확인한다. 테스트/캡처가 만든 TimeManager 자동
재직렬화만 diff 확인 후 원복한다. 검사 스크립트는 유료 생성 API를 호출하지 않는다.

채택 후 저장된 씬을 그대로 읽어 동일한 24상태를 다시 검수했다.
Unity 792개 / Core 611개 통과와 네이티브 기하·포구·A/B 연결 확인은
[채택 검증 기록](adoption-review/VALIDATION.md)에 있다. 다음 전경 묶음은
[기본탄·캡슐·소형 폭발](../combat/README.md)이다.
