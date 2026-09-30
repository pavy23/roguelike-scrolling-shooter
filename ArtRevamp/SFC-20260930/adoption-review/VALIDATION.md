# SFC 2안 첫 묶음 — 씬 적용 검증

사용자가 첫 native 묶음의 적용·검증·배포 제안에 “계속 가자”로 진행을 지시했다.
승인은 [CURATION.md](../CURATION.md), 정확한 5개 파일과 SHA-256은
[adoption.json](../adoption.json)에 기록했다. 새 유료 생성이나 외부 업로드는 없었다.

## 적용한 범위

- 기본 기체 48×30 및 불꽃만 26픽셀 다른 엔진 B. 10 fps의 두 자세 루프.
- 직진 드론·고속 적·포탑 24×24. 몸체가 흔들리는 생성 애니메이션은 제외하고 정적
  클립을 연결했다. 직진 적 변형과 지면/천장 포탑은 기존 prefix 선택 규칙을 따른다.
- Battle의 시작 이미지·기체 선택·애니메이션 테이블과 Title의 격납고 초상.
  `BattleSceneBuilder`도 같은 원본을 사용한다. 씬 전체 재생성은 하지 않았다.
- 신규 이름으로 추가하여 기존 PNG 338개와 GUID, 배경 52개를 보존했다.
  새 원본은 미추적 `art-input` 복사 경로를 사용하지 않는다. 채택 파일과 원본이
  승인 당시 해시와 다르면 `art_source_check.py`가 실패한다.
- WebGL 빌드가 갱신한 기존 SpriteAtlas의 5개 GUID/이름 인덱스도 저장했다.
  Atlas 설정 변경 없이 신규 원본의 packed 참조만 추가된다.

Core/GameData/판정/탄 발사 위치는 바꾸지 않았다. `.meta`는 Unity 임포터로 생성했고,
실제 Sprite의 전체 rect·중앙 피벗·PPU16·Point·무압축·mipmap 없음까지 확인했다.
테스트 실행이 만든 TimeManager 직렬화 형식 변경은 원복했다.

## 확인한 증거

Unity CLI 1.0.0-beta.11 / Editor 6000.5.3f1 / Pipeline 0.8.0-exp.1 / URP.
공유 PC 규칙에 따라 에디터 창 없이 배치로 실행했다.

| 검사 | 결과 |
| --- | --- |
| CoreStandalone | 611 / 611 통과, 실패·건너뜀 0 |
| Unity EditMode 전체 | 786 / 786 통과, 실패·건너뜀 0 |
| 새 씬 회귀 검사 | 60틱 엔진 샘플링, 같은 틱에서 정지, 재시작, 3기종 전환, 적 5개 ID, renderer 재사용·피격 tint 보존 |
| 실제 표시 메서드 캡처 | 테마 0·2·4 × 틱 0·6·12, 전후 18장. 실제 `SyncPlayerAnimation` / `SyncEnemies` / `SyncBullets` 호출 |
| 오브젝트 풀 재사용 | 제거 후 다른 ID의 고속 적 생성에도 `enemy_sfc_fast.png` 유지 |
| 원본 검사 | 기존 338개 SHA-256 불변, 채택 5개 원본/에셋 해시 일치, 몸체·포구 픽셀 차이 0 |
| 임포트 검사 | 전체 스프라이트 메타 검사 통과, 채택 5개 Unity Sprite 실제 기하 검사 통과 |

원시 로그/테스트 XML은 `out/revamp/sfc-adoption-*`, 전체 캡처는
`out/revamp/sfc-adoption/`에 있다. 재검증용 스크립트는
`Tools/QaHarness/capture_sfc_adoption.eval.cs`이며 환경변수 `RSS_SFC_CAPTURE=after`를 사용한다.
아래 대표 캡처와 원시 바인딩 기록은 저장소에도 보존했다.

| 배경 | 교체 전 | 교체 후 |
| --- | --- | --- |
| 테마 0 | [기존](battle-before-theme-0-tick-6.png) | [새 원본](battle-after-theme-0-tick-6.png) |
| 테마 2 | [기존](battle-before-theme-2-tick-6.png) | [새 원본](battle-after-theme-2-tick-6.png) |
| 테마 4 | [기존](battle-before-theme-4-tick-6.png) | [새 원본](battle-after-theme-4-tick-6.png) |

[판정/포구 오버레이](battle-after-geometry-theme-4.png): 노랑은 읽어 온 기존 Core 판정,
청록은 저장된 포구 위치다. 포구 로컬 좌표 `(0.85, 0, 0)`는 유지된다. 새 아트는
기존 캔버스/배율에 맞췄으며, 외곽선을 픽셀 단위 충돌 마스크로 해석하지 않는다.
[전후 바인딩 기록](after-capture.json)에 sprite 경로·배율·임포트·판정 크기가 있다.

## 한계와 후속

캡처는 저장된 실제 씬과 표시 코드를 쓰되, 상태를 격리 주입한 Editor 검증이다.
UI를 숨겼으며 브라우저 실플레이 PASS를 뜻하지 않는다. 연속 전투의 조작감, 포탑의
실제 지형 부착, 플래시 감소 모드의 동영상 체감은 별도 사용자 플레이 피드백이 필요하다.
테마 4의 붉은 배경에서 드론의 어두운 장갑은 밝은 센서보다 덜 읽히므로 움직이는
전투에서 추가 관찰한다. 이것을 밝기/색 보정만으로 자동 수정하지 않았다.

다음 제작은 기본 기체 상승/하강 자세, 아군/적 기본탄·캡슐, 소형 폭발 연속 프레임이다.
나머지 기종·일반 적·보스·아이콘은 아직 기존 아트다. 첫 5파일 적용을 전체 개편
완료로 간주하지 않는다.

## 배포 상태

소스 검증 완료. 이 소스의 clean 커밋으로 WebGL을 빌드한 뒤 rss-play에 배포하고,
이 절에 소스/배포 커밋·Pages 작업·공개 파일 해시 확인 결과를 추가한다.
