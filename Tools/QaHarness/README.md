# QaHarness — WebGL 빌드 headless 검증

세션마다 새로 쓰던 puppeteer 스크립트를 여기로 옮겼다. 스크린샷을 눈으로 읽는 대신
**픽셀 수치 어서션**으로 PASS/FAIL을 낸다 (REQ-124).

## Unity CLI 타이틀 검토 이미지

WebGL 빌드 없이 현재 `Title.unity`와 런타임 UI 코드의 배치를 확인하려면 저장소
루트에서 아래 명령을 실행한다. Unity CLI `1.0.0-beta.11` / Pipeline `0.8.0-exp.1`에서 검증했다.

```powershell
$captureScript = (Resolve-Path Tools/QaHarness/capture_title.eval.cs).Path
unity run . --command eval_file --timeout 180 --no-tail --format json --non-interactive -- --file $captureScript --timeout 30000
```

- 출력: `out/revamp/title-ui-review.png` (1280×720, 기준 UI의 2배).
- 선택 환경변수: `RSS_CAPTURE_TOUCH=1`은 터치 배치(`-touch`),
  `RSS_CAPTURE_DEV=1`은 개발 모달(`-dev`), `RSS_CAPTURE_SCALE=1`은
  640×360 원본 배율(`-640`)로 출력한다. 예: `title-ui-review-touch-640.png`.
  비교 시드는 캡처 프로세스 안에서만 12345로 고정한다.
- 개발 모달에서는 Stage 버튼에 표준 선택 이벤트를 주입해 포커스 테두리를 보여준다.
  EditMode에는 실행 중인 UI 입력 루프가 없으므로 실제 키보드/패드 조작 증거는 아니다.
- GPU가 있는 batch Editor가 필요하다. `-nographics`를 붙이지 않는다. 창은 표시하지 않는다.
- 스크립트는 일반 GUI Editor나 Play Mode에서 실행을 거절한다. 실행 전 연결된 Editor가
  없는지 확인한다. UI를 초기화하고 Canvas를 임시로 카메라에 연결하며 씬은 저장하지 않는다.
- **에디터 개발 모드의 오프스크린 렌더**다. 개발 도구 입구가 포함되며 저장/설정에 따라
  표시가 달라질 수 있다. 실제 플레이·입력·전환·오디오 검증이나 출시 화면의 증거는 아니다.
- CLI 종료 후 `git diff -- ProjectSettings/ProjectSettings.asset`를 확인한다.
  CLI가 바꾼 `runInBackground`만 원복하고 다른 사용자 변경은 보존한다.

## Unity CLI 전투 UI 검토 이미지

`capture_battle_ui.eval.cs`는 현재 Battle 씬과 실제 GameData로 만든 독립 런에
UI 표시용 상태를 주입한다. Director의 저장/리플레이 초기화는 실행하지 않으며,
튜토리얼 완료 설정도 원래 값으로 복원한다. 씬은 저장하지 않는다.

```powershell
$captureScript = (Resolve-Path Tools/QaHarness/capture_battle_ui.eval.cs).Path
unity run . --command eval_file --timeout 180 --no-tail --format json --non-interactive -- --file $captureScript --timeout 30000
```

- 출력: `out/revamp/battle-ui-ready.png`, 기본 1280×720, 시드 12345.
- `RSS_CAPTURE_HUD=locked`: 계약으로 강화가 막힌 상태. `empty`: 선택 없음·실드 0.
- `RSS_CAPTURE_TOUCH=1`, `RSS_CAPTURE_SCALE=1`: 터치 안내와 640×360 원본 배율.
- 예: `battle-ui-locked-touch-640.png`. 튜토리얼은 표시 검토를 위해 3/3 단계로 주입한다.
- GPU batch Editor 전용 오프스크린 렌더이며, 실플레이·완전한 터치 UI·성능 검증은 아니다.
  실행 후 `ProjectSettings/`에 의도하지 않은 변경이 없는지 확인한다.

## 준비 (1회)

```
cd Tools/QaHarness
npm i                     # puppeteer-core + pngjs
```

Chrome 경로가 기본값과 다르면 `RSS_CHROME` 환경변수로 준다.

## 실행

```
# 1) 빌드 (batchmode 직접 호출 금지 — AGENTS.md §9)
unity build --target WebGL --execute-method Shmup.EditorTools.MobileBuilder.BuildWebGl --log-file build.log

# 2) 서버 (별도 셸, 백그라운드)
cd Builds/Web && python -m http.server 8099 --bind 127.0.0.1

# 3) 검증
cd Tools/QaHarness
node rss-verify.js --stage 3 --warp boss --seed 2 --seconds 40 --out ./out/st3
```

종료 코드 0 = PASS. `out/<이름>/report.json`에 HP 시계열이 남고, 10초마다 스크린샷이
떨어진다(회귀 시 눈으로 볼 근거).

## 옵션

| 옵션 | 뜻 |
|---|---|
| `--stage N` | N스테이지에서 시작 (`?stage=N`) |
| `--warp early\|midboss\|late\|boss` | 해당 구간까지 즉시 워프 (REQ-124) |
| `--seed N` | 시드 고정. **URL이 아니라 타이틀 키 입력으로 넣는다** — `DevArgs.OverrideSeed`는 커맨드라인 인자만 읽어서 `?seed=`는 무시된다 |
| `--seconds N` | 도착 후 관찰 시간 (1초 간격 샘플링) |
| `--god 0` | 무적 끄기 (기본 켜짐) |
| `--uncharted 1` | 미지의 구역에서 시작 (REQ-123). `--warp boss`와 같이 쓰면 거대 보스(레비아탄/브루드마더) 앞에서 시작한다 — 원래는 5바이옴 완주가 전제라 검증이 불가능했다 |

## 함정 (다시 밟지 마라)

- **키 연타는 프레임당 1회만 인식된다.** `Backspace`로 시드를 지울 때 간격(80ms+)이
  없으면 한 글자만 지워진다. 시드가 안 바뀌어 "왜 매번 다른 판이지?"가 된다.
- **워프는 런 시작 1회만 돈다.** 런 도중 다른 구간으로 건너뛸 수는 없다.
  미지의 구역은 `--uncharted 1`로 시작 지점 자체를 옮긴다(REQ-123 반영 완료).
- **좌표를 박지 마라.** 캔버스 배율이 바뀌면 조용히 틀린다 — `measureBossHpBar`처럼
  색으로 찾아라.
- **hive 계열 보스는 기체를 세로로 움직여야 진행된다.** 기본 y=0에 서 있으면 무적
  코어만 때리고 진행이 0인데 탄은 사라져서 맞고 있는 것처럼 보인다 (REQ-125).

## 공유 PC 제약

AGENTS.md §9: 사람이 해제할 때까지 **항상 headless**다. `headless: false`로 바꾸지 마라 —
작업자 화면에 창이 튀어나온다.

## README 스크린샷 만들기

`docs/screenshots/*.png`는 손으로 찍지 않는다. 게임이 바뀌어도 아무도 다시 찍지
않아서, 한때 README의 전함 사진은 함체를 34×17로 키우기 전 것이었고 타이틀 사진에는
없어진 시드 버튼이 남아 있었다.

```
cd Builds/Web && python -m http.server 8099 --bind 127.0.0.1     # 서버
node Tools/QaHarness/doc_shots.js                                 # 캡처
python Tools/QaHarness/compose_docs.py                            # 시트 합성
```

- 장면 목록은 `doc_shots.js`의 `SCENES`에 있다. **시드를 박아 둔다** — 테마는 시드가
  정하므로 안 박으면 "3스테이지 전함"을 찍으려다 하이브가 나온다(실제로 그랬다).
- 출격 후 **F3**으로 진단 오버레이를 끈다. 워프·무적은 그대로 쓰되 좌표·시드가
  문서에 남지 않게 한다.
- 시트 라벨은 영문이다 — PIL 기본 폰트에 한글 글리프가 없어 네모로 찍힌다.
