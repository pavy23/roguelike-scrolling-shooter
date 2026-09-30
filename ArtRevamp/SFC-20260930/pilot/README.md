# SFC 2안 — 첫 원본 검수 묶음

스타일 선택은 완료됐다. 이번에는 시안의 기체·적을 실제 게임 캔버스에서 새로 제작했다.
**중립 기체 1종, 적 3종, 엔진 2자세**가 첫 채택 검수 대상이다. 전체 아트 개편이나
파일럿 전체 완료를 뜻하지 않는다. 아직 `Assets/Art/`에 채택하거나 rss-play에 배포하지 않았다.

- [현재 / 새 후보 비교표](review/pilot-static-comparison.png)
- [원본 배율·엔진 재생·배경 비교](review/index.html) — 로컬 HTML, 네트워크 연결 없음
- [기체 후보 16장](review/starter-candidates.png)
- [Unity 검증](review/pilot-audit.json), [원본·프레임 감사](review/native-audit.json)

## 검수 대상

| 대상 | 제안 원본 | 크기 | 불투명 색 수 | 검토 결과 |
| --- | --- | --- | --- | --- |
| 기본 기체 | `raw/starter/candidate_05.png` | 48×30 | 30 | 아이보리 장갑 / 코발트 그림자 / 청록 캐노피. 16개 중 몸체가 잘리지 않고 명암 면이 가장 명료한 후보 |
| 직진 드론 | `raw/drone.png` | 24×24 | 44 | 붉은 둥근 장갑과 앞쪽 주황 센서. 고속 적과 실루엣 구분 |
| 고속 적 | `raw/fast-v2.png` | 24×24 | 41 | 첫 후보가 너무 가늘고 어두워 재제작. 밝은 붉은 삼각 실루엣 |
| 포탑 | `raw/turret-v2.png` | 24×24 | 26 | 첫 후보보다 받침대와 상부 포신의 관계를 명확하게 재제작 |
| 엔진 B | `raw/starter-engine/frame_01.png` | 48×30 | 29 | 기본 기체 A와 불꽃 26픽셀만 다름. 몸체·포구 원본 픽셀 차이 0 |

모두 부분 알파 0, 기존 캔버스와 같은 크기다. 48색 상한은 통과했으나 소형 적의
목표 12–20색보다 많다. 기체 불꽃 끝이 왼쪽 경계에 닿는다. 실루엣/색 수/꼬리 여백은
사람의 원본 검토에서 함께 평가하며, 자동 합격이나 엄밀한 SFC 하드웨어 재현으로 보지 않는다.
확대·크롭·색 양자화로 원본을 고치지 않았고 생성 PNG 바이트를 그대로 보관한다.

## 애니메이션에서 확인한 문제

- 엔진 생성은 6프레임을 요청했지만 기존 클라이언트가 이미지 항목 7개를 저장했다.
  초기 클라이언트는 전체 job ID를 저장하지 않았으므로 `dd9e97b7` 접두어만 남았다.
  요청/시드/입력 해시는 보존했다. 반환 순서나 수를 정상이라고 가정하지 않고 각 PNG를
  따로 비교했다. 02–05는 몸체가 1픽셀 아래로 이동하며, 00/06도 몸체 픽셀을 바꿨다.
- **원본 A + 반환 항목 01의 B**만 10 fps로 반복하는 2자세 루프를 제안한다.
  변경 위치뿐 아니라 양쪽 색이 허용한 불꽃 팔레트인지 검사해 장갑 픽셀 변화까지 거른다.
  위치 보정이나 강제 리사이즈로 흔들림을 가리지 않았다.
- 드론 센서 애니메이션은 4프레임 요청에 5프레임을 반환했다. 처음/끝은 원본과 같고,
  가운데 3장은 몸체 전체가 위아래로 움직였다. 이 루프는 **불합격**, 드론은 정적 원본만 제안한다.
  API가 `last_response.images`에 실제로 5개를 반환한 점을 기록했다.
- `artgen animate`에 요청·job·해시 기록, 동일 job 재개, 기존 출력 덮어쓰기 방지를 추가했다.
  입력 에코/썸네일은 프레임에 포함하지 않고, 프레임 수 불일치는 숨기지 않는다.

## 제외·보류한 후보

- `raw/starter-pixen.png`: 48×32, 기존 48×30 계약과 다르고 상하 날개가 경계에 닿음.
- 기체 Pro의 다른 15개: 변형 후보로 보관. 여러 장에서 잘린 코/날개·분리된 조각·위치 문제가 있음.
- `raw/fast.png`: 전투 크기에서 너무 가늘고 어두움.
- `raw/turret.png`: 몸체가 작고 지지대가 약해 재제작.
- `raw/explosion.png`: 52색이며 작은 점화 자세 요청에 큰 별 모양 폭발이 나옴. 제외.
- `raw/explosion-v2.png`: 48×48 / 17색 / 하드 알파. 둥근 불덩이 원본 실험으로 보관하지만
  요청한 30px보다 큰 42px 바운드다. 점화→팽창→파열→잔화 시퀀스가 없어 아직 채택 대상으로 올리지 않는다.
- `references/starter-reference.png`: built-in imagegen으로 만든 고해상도 투명 참조다.
  native PNG가 아니며 PixelLab에 전송하지 않았다. 실제 기체 Pro 생성은 이미 공개된 v2 시트만 참조했다.

## Unity 검증 범위

Unity CLI 1.0.0-beta.11 / Editor 6000.5.3f1 / Pipeline 0.8.0-exp.1 / URP.
창을 띄우지 않는 배치 `eval_file`로 후보를 메모리에만 로드했다. Point / PPU16 /
중앙 피벗, 기존 640×360 Pixel Perfect Camera를 사용했다.

기체 16장 접촉 시트와 현재/새 원본 비교표를 1배 및 정수 확대 배율로 렌더했다.
Battle의 테마 0·2·4에서 동일 위치의 전후 6장을 만들었다. 적의 배율은
`GameData/enemies.json`과 `BattleDirector.ApplyEnemyScale`의 동일한 폭 계산식에서
읽었다. 저장된 씬의 배경은 그대로이며 UI는 비교를 위해 임시로 숨겼다.

이는 **정적 Editor 배치**다. 포탑의 실제 지형 부착, 탄막 중 가독성, 충돌 박스/포구
오버레이, 뱅킹, 일시정지/감소 설정/풀 재사용, 브라우저 실플레이는 검증하지 않았다.
후보를 임포트하거나 씬·사용자 세이브를 저장하지 않았다. 기존 PNG 338개 SHA-256이
인벤토리와 모두 같다. CoreStandalone 611개 통과, 생성 파이프라인 오프라인 테스트 8개 통과.
HTML의 파일 참조와 JavaScript 구문을 검사했고 브라우저 자동화는 실행하지 않았다.

## 생성 기록·비용

[상위 큐레이션 기록](../CURATION.md)에 스타일 선택과 사용자의 PixelLab 전송 승인을 남겼다.
이번 파일럿 후보 전송·애니메이션에 기존 크레딧 **US$1까지** 승인됐고, 관측 잔액 감소는
**US$0.111962457948**이다. 새 결제/충전/소스 코드 전송 없음. 사용액 검증 기록은
[usage.json](usage.json), 입력 프롬프트와 매개변수는 `requests/`, 결과 해시는
[manifest.json](manifest.json)에 있다. 시드가 있어도 외부 모델의 결정론적 재생성을 보장하지 않는다.

## 다음 적용 범위

사람의 아트 큐레이션 후 이 첫 묶음을 기존 `.meta`/판정/PPU 계약에 맞춰 임포트하고,
현재 씬 및 `BattleSceneBuilder`의 재생성 경로를 함께 연결한다. 채택되지 않은 기존
프레임이 섞이지 않는지 확인하고 Unity 회귀 검사와 WebGL 빌드를 거쳐 rss-play에 순차 배포한다.
뱅킹, 탄·캡슐, 폭발 연속 프레임은 그다음 원본 제작 범위다.

재검증:

```powershell
python -B Tools/ArtGen/audit_sfc_pilot.py
python -B -m unittest discover -s Tools/ArtGen/tests -v
unity run . --command eval_file --timeout 600 --no-tail --format json --non-interactive -- --file Tools/QaHarness/capture_sfc_pilot_candidates.eval.cs
unity run . --command eval_file --timeout 600 --no-tail --format json --non-interactive -- --file Tools/QaHarness/capture_sfc_pilot_scene.eval.cs
```

`raw/starter/request.json`과 `requests/*.json`이 원본 생성 요청이다. 완료된 결과를 보기
위해 유료 생성 명령을 다시 실행하지 않는다. 중단된 Pro/애니메이션 job만 원래 인자와
`--resume`으로 재개한다. Pixen 생성은 재호출하면 새 작업이며 기존 출력이 있어도 덮어쓴다.
