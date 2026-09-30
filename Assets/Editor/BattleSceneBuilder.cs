using System;
using System.Collections.Generic;
using System.IO;
using Shmup.Presentation.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.U2D;
using UnityEditor.U2D;

namespace Shmup.EditorTools
{
    /// <summary>
    /// Battle 씬을 통째로 다시 만든다 — 플레이스홀더 스프라이트, 탄 프리팹, 카메라, 씬 배선까지.
    /// 씬/프리팹 .unity YAML을 손으로 쓰면 GUID가 깨지기 쉬워서, 생성기를 원본으로 삼는다.
    ///
    /// 에디터:   Tools → Shmup → Rebuild Battle Scene
    /// 헤드리스: Unity.exe -batchmode -projectPath . -executeMethod Shmup.EditorTools.BattleSceneBuilder.Build -quit
    /// </summary>
    public static class BattleSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Battle.unity";
        const string SpriteDir = "Assets/Art/Sprites";
        const string PrefabDir = "Assets/Prefabs";
        const string BulletPrefabPath = PrefabDir + "/Bullet.prefab";
        const string ShipSpritePath = SpriteDir + "/player_ship.png";
        const string BulletSpritePath = SpriteDir + "/bullet.png";
        const string InputActionsPath = "Assets/Settings/InputSystem_Actions.inputactions";

        // 고정된 기술 결정 (CLAUDE.md) — 아트 원본 해상도라 변경 금지.
        // 2026-07-28 사람 승인으로 384×224 → 640×360 상향 (ROADMAP.md M0).
        const int AssetsPPU = 16;
        const int RefResolutionX = 640;
        const int RefResolutionY = 360;

        // '.' 투명 / 'O' 외곽선 / 'B' 본체 / 'L' 하이라이트 / 'C' 캐노피
        static readonly string[] ShipPixels =
        {
            "...OO...........",
            "..OLLOO.........",
            ".OLLLBBOO.......",
            ".OLLBBBBBOO.....",
            "OLLBBBCCBBBOO...",
            "OLLBBBCCBBBBBOO.",
            "OLLBBBCCBBBBBOO.",
            "OLLBBBCCBBBOO...",
            ".OLLBBBBBOO.....",
            ".OLLLBBOO.......",
            "..OLLOO.........",
            "...OO..........."
        };

        static readonly Dictionary<char, Color32> ShipPalette = new Dictionary<char, Color32>
        {
            ['O'] = new Color32(0x18, 0x22, 0x40, 0xFF),
            ['B'] = new Color32(0x3C, 0x6E, 0xB4, 0xFF),
            ['L'] = new Color32(0x9C, 0xD4, 0xFF, 0xFF),
            ['C'] = new Color32(0xFF, 0xE0, 0x8C, 0xFF)
        };

        // 'W' 코어 / 'O' 글로우
        static readonly string[] BulletPixels =
        {
            "..OOOO..",
            "OWWWWWWO",
            "..OOOO.."
        };

        static readonly Dictionary<char, Color32> BulletPalette = new Dictionary<char, Color32>
        {
            ['W'] = new Color32(0xFF, 0xFB, 0xD8, 0xFF),
            ['O'] = new Color32(0xFF, 0x9C, 0x28, 0xFF)
        };

        // 적 16×12: 밝은 회백색으로 그려 두고 BattleDirector가 종류별로 틴트한다.
        const string EnemySpritePath = SpriteDir + "/enemy.png";
        const string EnemyPrefabPath = PrefabDir + "/Enemy.prefab";

        static readonly string[] EnemyPixels =
        {
            "....OOOOOOOO....",
            "..OOBBBBBBBBOO..",
            ".OBBBBLLLLBBBBO.",
            "OBBBLLBBBBLLBBBO",
            "OBBLLBBBBBBLLBBO",
            "OBBLBBBOOBBBLBBO",
            "OBBLBBBOOBBBLBBO",
            "OBBLLBBBBBBLLBBO",
            "OBBBLLBBBBLLBBBO",
            ".OBBBBLLLLBBBBO.",
            "..OOBBBBBBBBOO..",
            "....OOOOOOOO...."
        };

        static readonly Dictionary<char, Color32> EnemyPalette = new Dictionary<char, Color32>
        {
            ['O'] = new Color32(0x40, 0x40, 0x48, 0xFF),
            ['B'] = new Color32(0xD8, 0xD8, 0xE0, 0xFF),
            ['L'] = new Color32(0xFF, 0xFF, 0xFF, 0xFF)
        };

        // 적탄 6×6 오렌지 구체 (플레이스홀더 — REQ-007 적탄 뷰)
        const string EnemyShotSpritePath = SpriteDir + "/enemy_shot.png";

        static readonly string[] EnemyShotPixels =
        {
            ".OWWO.",
            "OWCCWO",
            "WCCCCW",
            "WCCCCW",
            "OWCCWO",
            ".OWWO."
        };

        // 적탄은 **마젠타 계열**이다 (사람 지적 2026-08-03: "적 탄 색이 내 탄과 같아서
        // 헷갈린다"). 예전에는 주황(#FF7820)이라 플레이어 주무기(#FF9C28)와 같은 계열이라
        // 탄막 속에서 내 탄과 적 탄이 섞였다.
        //
        // 마젠타를 고른 이유:
        //   - 주황과 색상환에서 가장 멀다 (플레이어 탄·폭발·캡슐이 전부 난색이다)
        //   - 청록은 이미 "지금 못 깎는다"(무적 파츠·게이트 맥동)에 쓰고 있어 겹치면 안 된다
        //   - 5개 테마 배경(청회색·초록·진홍·보라·코어) 어디서도 채도로 이긴다.
        //     보라 성운에서 가장 아슬아슬한데, 채도와 흰 코어로 갈린다.
        static readonly Dictionary<char, Color32> EnemyShotPalette = new Dictionary<char, Color32>
        {
            ['O'] = new Color32(0x5A, 0x0B, 0x3A, 0xFF),   // 깊은 자주 테두리 — 배경과 분리
            ['W'] = new Color32(0xFF, 0x4D, 0xA6, 0xFF),   // 마젠타 본체
            ['C'] = new Color32(0xFF, 0xD9, 0xF2, 0xFF)    // 흰 코어 — 가시성 최상단
        };

        // 파워업 캡슐 10×8 (그라디우스 오렌지 캡슐 오마주)
        const string CapsuleSpritePath = SpriteDir + "/capsule.png";
        const string CapsulePrefabPath = PrefabDir + "/Capsule.prefab";

        // 파워업 캡슐 10×8. 옵션 드론과 **형태도 색도 겹치지 않아야** 한다 —
        // 예전에는 둘 다 주황 둥근 사각이라 화면에서 구분이 되지 않았다
        // ("옵션 파츠외 파워업 아이템 아이콘이 너무 비슷한거 같아", 2026-07-30).
        // 캡슐은 먹어야 하는 것이므로 다이아몬드 실루엣 + 기체/옵션의 보색인
        // 청록 계열로 두고, 중앙을 밝게 비워 "빛나는 획득물"로 읽히게 한다.
        static readonly string[] CapsulePixels =
        {
            "....CC....",
            "..CCWWCC..",
            ".CWWPPWWC.",
            "CWWPCCPWWC",
            "CWWPCCPWWC",
            ".CWWPPWWC.",
            "..CCWWCC..",
            "....CC...."
        };

        static readonly Dictionary<char, Color32> CapsulePalette = new Dictionary<char, Color32>
        {
            ['C'] = new Color32(0xB4, 0xF4, 0xFF, 0xFF),   // 밝은 시안 (외곽·중심)
            ['W'] = new Color32(0x3C, 0xC8, 0xE8, 0xFF),   // 청록 본체
            ['P'] = new Color32(0x14, 0x5C, 0x9C, 0xFF)    // 진청 음영
        };

        // 폭발 12×12 스타버스트 (런타임에 확대+페이드)
        const string ExplosionSpritePath = SpriteDir + "/explosion.png";
        const string ExplosionPrefabPath = PrefabDir + "/Explosion.prefab";

        static readonly string[] ExplosionPixels =
        {
            ".....WW.....",
            "..O..WW..O..",
            ".OO.WYYW.OO.",
            "..OWYYYYWO..",
            "..WYYCCYYW..",
            "WWYYCCCCYYWW",
            "WWYYCCCCYYWW",
            "..WYYCCYYW..",
            "..OWYYYYWO..",
            ".OO.WYYW.OO.",
            "..O..WW..O..",
            ".....WW....."
        };

        static readonly Dictionary<char, Color32> ExplosionPalette = new Dictionary<char, Color32>
        {
            ['C'] = new Color32(0xFF, 0xFF, 0xE0, 0xFF),
            ['Y'] = new Color32(0xFF, 0xC8, 0x3C, 0xFF),
            ['W'] = new Color32(0xFF, 0x78, 0x20, 0xFF),
            ['O'] = new Color32(0xB4, 0x3C, 0x14, 0xFF)
        };

        // 미사일 10×4 (전방 하강탄 — BulletKind.Missile 뷰)
        const string MissileSpritePath = SpriteDir + "/missile.png";

        static readonly string[] MissilePixels =
        {
            "..OOOOOOO.",
            "OWWWWWWWFO",
            "..OOOOOOO.",
            ".....F...."
        };

        static readonly Dictionary<char, Color32> MissilePalette = new Dictionary<char, Color32>
        {
            ['W'] = new Color32(0xC8, 0xD0, 0xDC, 0xFF),
            ['O'] = new Color32(0x50, 0x5C, 0x70, 0xFF),
            ['F'] = new Color32(0xFF, 0xA0, 0x30, 0xFF)
        };

        // 옵션 8×8 주황 구체 (그라디우스 오마주)
        const string OptionSpritePath = SpriteDir + "/option.png";
        const string OptionPrefabPath = PrefabDir + "/Option.prefab";

        static readonly string[] OptionPixels =
        {
            "..OOOO..",
            ".OWWWWO.",
            "OWWCCWWO",
            "OWCCCCWO",
            "OWCCCCWO",
            "OWWCCWWO",
            ".OWWWWO.",
            "..OOOO.."
        };

        static readonly Dictionary<char, Color32> OptionPalette = new Dictionary<char, Color32>
        {
            ['O'] = new Color32(0x8C, 0x2A, 0x0A, 0xFF),
            ['W'] = new Color32(0xFF, 0x78, 0x20, 0xFF),
            ['C'] = new Color32(0xFF, 0xD0, 0x80, 0xFF)
        };

        // 스크랩야드 고철 잔해 32×32 (REQ-055). 스크랩야드의 기믹은 "떠다니는 잔해를
        // 엄폐물로 쓰거나 부순다"인데 크리스탈 스프라이트를 쓰고 있어서 고철 폐기장에
        // 청록 결정이 떠 있었다 — 기믹 의도가 전달되지 않는다.
        //
        // 크리스탈의 규칙적 기하와 대비되는 **불규칙한 파편**이어야 "부술 것"으로 읽힌다.
        const string ScrapDebrisSpritePath = SpriteDir + "/obstacle_scrap_debris.png";

        static string[] BuildScrapDebrisPixels()
        {
            const int size = 32;
            const int spokes = 16;

            // 각도별 반지름을 고정 시드 의사난수로 흔들어 울퉁불퉁한 실루엣을 만든다.
            // 시드를 박아 두어 빌드마다 같은 모양이 나온다.
            var radii = new float[spokes];
            uint h = 0x9E3779B9u;
            for (int i = 0; i < spokes; i++)
            {
                h = h * 1664525u + 1013904223u;
                radii[i] = 9.5f + (h >> 24) / 255f * 5f;
            }

            var rows = new string[size];
            float centre = size / 2f - 0.5f;
            for (int y = 0; y < size; y++)
            {
                var row = new char[size];
                for (int x = 0; x < size; x++)
                {
                    float dx = x - centre, dy = y - centre;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    float ang = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f);
                    if (ang < 0f) ang += 1f;
                    float s = ang * spokes;
                    int i0 = (int)s % spokes;
                    float r = Mathf.Lerp(radii[i0], radii[(i0 + 1) % spokes], s - (int)s);

                    if (dist > r) { row[x] = '.'; continue; }
                    if (dist > r - 1.6f) { row[x] = 'O'; continue; }

                    // 내부를 대각 띠로 갈라 찌그러진 판이 겹친 것처럼 보이게 한다.
                    float band = Mathf.Repeat(x + y * 0.6f, 7f);
                    row[x] = band < 2f ? 'H' : band < 4.5f ? 'M' : 'D';
                }
                rows[y] = new string(row);
            }
            return rows;
        }

        // 녹슨 고철 — 크리스탈(청록)·포자(자주)와 색으로도 갈린다.
        static readonly Dictionary<char, Color32> ScrapDebrisPalette = new Dictionary<char, Color32>
        {
            ['O'] = new Color32(0x2C, 0x22, 0x1C, 0xFF),   // 외곽
            ['D'] = new Color32(0x4E, 0x3C, 0x2E, 0xFF),   // 그늘진 면
            ['M'] = new Color32(0x78, 0x60, 0x48, 0xFF),   // 중간 면
            ['H'] = new Color32(0xAC, 0x92, 0x70, 0xFF)    // 빛 받는 면
        };

        // 레이저 포탑 32×32 (2026-08-02 사람 지적: "고철이 레이저를 발사하는 건 좀 이상.
        // 레이저를 발사하는 포대 같은 게 있는 게 맞을듯").
        //
        // ObstacleType.LaserEmitter가 파괴 가능 잔해와 같은 스프라이트로 그려지고 있었다.
        // **위험한 것은 위험해 보여야 한다** — 예고선이 뜨기 전에도 "저건 쏘는 것"이
        // 실루엣만으로 읽혀야 피할 자리를 미리 잡는다.
        //
        // 도안 규칙 세 가지:
        //   1. 포신은 **-X(왼쪽)**를 향한다. BattleDirector가 Core의 레이저 선분 각도로
        //      뷰를 돌리므로, 기본 방향이 바뀌면 EmitterBarrelBaseAngle도 같이 바뀐다.
        //   2. 뜨거운 색(R/W)은 방출구 렌즈에만 쓴다. 몸통은 차가운 강철이라
        //      "여기서 빔이 나온다"가 한 점으로 모인다.
        //   3. 좌우로 잔해(불규칙 실루엣)와 겹치지 않게 기계적 직선·팔각으로 짠다.
        const string LaserTurretSpritePath = SpriteDir + "/obstacle_laser_turret.png";

        static readonly string[] LaserTurretPixels =
        {
            "................................",
            "................................",
            "................................",
            "................................",
            "................................",
            "................................",
            "................................",
            "................................",
            "...................OOOOOO.......",
            "..................OHHHHHHO......",
            ".............OOOOOHAHHHAHHO.....",
            ".....OO......OHHHHHHHHHHHHHO....",
            "...OOHH......OHHHHHHHHHHHOHOO...",
            "..OHHHHOOOOOOOHHHHHHHHHHHOHOHO..",
            "..RRRRRMHHHHHMMMMMMMMMMMMOMOHO..",
            ".RWWWWRMMMMMMMMMMMMMMMMMMOMOHO..",
            ".RWWWWRMMMMMMMMMMDDDDDDDDODODO..",
            "..RRRRRDDDDDDDDDDDDDDDDDDODODO..",
            "..ODDDDOOOOOOODDDDDDDDDDDODODO..",
            "...OODD......ODDDDDDDDDDDODOO...",
            ".....OO......ODDDDDDDDDDDDDO....",
            ".............OOOOODADDDADDO.....",
            "..................ODDDDDDO......",
            "...................OOOOOO.......",
            "................................",
            "................................",
            "................................",
            "................................",
            "................................",
            "................................",
            "................................",
            "................................"
        };

        // 차가운 강철 + 앰버 리벳 하나. 뜨거운 색은 방출구에만 쓴다.
        static readonly Dictionary<char, Color32> LaserTurretPalette = new Dictionary<char, Color32>
        {
            ['O'] = new Color32(0x14, 0x12, 0x1A, 0xFF),   // 외곽·홈
            ['D'] = new Color32(0x2C, 0x30, 0x40, 0xFF),   // 그늘진 장갑
            ['M'] = new Color32(0x48, 0x50, 0x66, 0xFF),   // 중간 장갑
            ['H'] = new Color32(0x72, 0x7E, 0x9A, 0xFF),   // 빛 받는 면
            ['A'] = new Color32(0xE0, 0x9A, 0x2C, 0xFF),   // 앰버 리벳
            ['R'] = new Color32(0xE8, 0x34, 0x3C, 0xFF),   // 방출구 링
            ['W'] = new Color32(0xFF, 0xE8, 0xC8, 0xFF)    // 방출구 코어
        };

        // 전멸 폭탄 픽업 10×10 방사 별. 캡슐(시안 다이아몬드)·옵션(주황 구체)과 한눈에
        // 구분되어야 해서 시안의 보색인 자홍으로 두고, 방사형 실루엣으로 "터지는 것"임을
        // 알린다. 대각선 점은 반짝임이다.
        const string BombPickupSpritePath = SpriteDir + "/bomb_pickup.png";
        const string BombPickupPrefabPath = PrefabDir + "/BombPickup.prefab";

        static readonly string[] BombPickupPixels =
        {
            "....WW....",
            "...WMMW...",
            ".W.WMMW.W.",
            "..WMMMMW..",
            "WWMMCCMMWW",
            "WWMMCCMMWW",
            "..WMMMMW..",
            ".W.WMMW.W.",
            "...WMMW...",
            "....WW...."
        };

        static readonly Dictionary<char, Color32> BombPickupPalette = new Dictionary<char, Color32>
        {
            ['W'] = new Color32(0xFF, 0xD8, 0xFF, 0xFF),   // 바깥 섬광
            ['M'] = new Color32(0xE0, 0x40, 0xC0, 0xFF),   // 자홍 본체
            ['C'] = new Color32(0xFF, 0xFF, 0xFF, 0xFF)    // 흰 중심
        };

        // 하이브 촉수 20×40 (REQ-055). 히트박스 1.25×2.5 유닛 × PPU 16 = 정확히 20×40이라
        // 런타임 스케일이 1.0으로 떨어진다 — 다른 적처럼 확대되어 뭉개지지 않는다.
        //
        // 통로 **위아래 어느 벽**에도 배치될 수 있고 Core는 방향을 주지 않으므로,
        // 한쪽 끝만 굵은 형태는 절반의 경우 거꾸로 보인다. 가운데가 굵은 방추형으로 두면
        // 어느 쪽에서 뻗어 나와도 자연스럽다.
        const string HiveTentacleSpritePath = SpriteDir + "/enemy_hive_tentacle.png";

        static string[] BuildHiveTentaclePixels()
        {
            const int width = 20, height = 40;
            var rows = new string[height];
            for (int y = 0; y < height; y++)
            {
                float t = y / (float)(height - 1);
                // 방추형: 중앙보다 살짝 위(0.42)에서 최대 굵기 — 완전 대칭은 무기물처럼 보인다.
                float taper = 1f - Mathf.Pow(Mathf.Abs(t - 0.42f) / 0.58f, 1.7f);
                float halfThick = Mathf.Lerp(1.2f, 7.2f, Mathf.Clamp01(taper));
                // S자로 굽어 살아있는 것으로 읽히게. 양 끝은 덜 흔들려 벽에 붙은 느낌을 남긴다.
                float sway = Mathf.Sin(t * 5.4f) * 3.2f * Mathf.Sin(t * Mathf.PI);
                float centre = width * 0.5f - 0.5f + sway;

                var row = new char[width];
                for (int x = 0; x < width; x++)
                {
                    float d = x - centre;
                    float ad = Mathf.Abs(d);
                    if (ad > halfThick) { row[x] = '.'; continue; }
                    if (ad > halfThick - 1.1f) { row[x] = 'O'; continue; }

                    // 빨판: 굽은 안쪽에 4px 간격으로. 촉수의 방향감을 만든다.
                    bool suckerRow = y % 4 == 2 && halfThick > 3.4f;
                    if (suckerRow && d < -halfThick * 0.25f && d > -halfThick * 0.72f)
                    {
                        row[x] = 'S';
                        continue;
                    }
                    // 광원은 오른쪽 위 — 기체·옵션과 같은 방향으로 통일한다.
                    row[x] = d > halfThick * 0.3f ? 'H' : 'B';
                }
                rows[y] = new string(row);
            }
            return rows;
        }

        // 하이브 생체 = 자주/분홍. 기존 적이 회색·주황 계열이라 한눈에 구분된다.
        static readonly Dictionary<char, Color32> HiveTentaclePalette = new Dictionary<char, Color32>
        {
            ['O'] = new Color32(0x40, 0x10, 0x38, 0xFF),   // 외곽 (짙은 자주)
            ['B'] = new Color32(0x8C, 0x24, 0x6C, 0xFF),   // 본체
            ['H'] = new Color32(0xD8, 0x5C, 0xA4, 0xFF),   // 하이라이트 (분홍)
            ['S'] = new Color32(0xFF, 0xC8, 0xE4, 0xFF)    // 빨판
        };

        // 실드 링 20×20 (플레이어 자식, 알파는 런타임 조절)
        const string ShieldSpritePath = SpriteDir + "/shield.png";

        static string[] BuildShieldPixels()
        {
            const int size = 20;
            const float outer = 9.5f, inner = 7.5f;
            var rows = new string[size];
            for (int y = 0; y < size; y++)
            {
                var row = new char[size];
                for (int x = 0; x < size; x++)
                {
                    float dx = x - (size - 1) / 2f, dy = y - (size - 1) / 2f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    row[x] = d <= outer && d >= inner ? 'W' : '.';
                }
                rows[y] = new string(row);
            }
            return rows;
        }

        static readonly Dictionary<char, Color32> ShieldPalette = new Dictionary<char, Color32>
        {
            ['W'] = new Color32(0x6A, 0xC8, 0xFF, 0xFF)
        };

        // 피격 플래시용 흰색 타일 (풀스크린으로 스케일해 알파만 조절)
        const string WhiteSpritePath = SpriteDir + "/px_white.png";
        static readonly string[] WhitePixels = { "WW", "WW" };
        static readonly Dictionary<char, Color32> WhitePalette = new Dictionary<char, Color32>
        {
            ['W'] = new Color32(0xFF, 0xFF, 0xFF, 0xFF)
        };

        // HUD 슬롯 글자 아이콘 5×5 (S/M/O/B) — 프레임 안 중앙, 정적
        static readonly string[][] HudIconPixels =
        {
            new[] { ".WWW", "W...", ".WW.", "...W", "WWW." },            // S (MainShot)
            new[] { "W...W", "WW.WW", "W.W.W", "W...W", "W...W" },       // M (Missile)
            new[] { ".WWW.", "W...W", "W...W", "W...W", ".WWW." },       // O (Option)
            new[] { "WWW.", "W..W", "WWW.", "W..W", "WWW." }             // B (Barrier/Shield)
        };

        // HUD 슬롯 프레임 22×12: 'W' 테두리(런타임 틴트로 상태 표시), 'D' 반투명 내부
        const string HudSlotSpritePath = SpriteDir + "/hud_slot.png";
        const string HudPipSpritePath = SpriteDir + "/hud_pip.png";

        static readonly string[] HudSlotPixels = BuildHudSlotPixels();
        static readonly string[] HudPipPixels = { "WWW", "WWW", "WWW" };

        static readonly Dictionary<char, Color32> HudPalette = new Dictionary<char, Color32>
        {
            ['W'] = new Color32(0xFF, 0xFF, 0xFF, 0xFF),
            ['D'] = new Color32(0x10, 0x18, 0x28, 0xB4)
        };

        static string[] BuildHudSlotPixels()
        {
            const int width = 22, height = 12;
            var rows = new string[height];
            rows[0] = rows[height - 1] = new string('W', width);
            for (int y = 1; y < height - 1; y++)
                rows[y] = "W" + new string('D', width - 2) + "W";
            return rows;
        }

        // 패럴랙스 스타필드 타일 (화면 크기 1장, 레이어 루트가 2장을 이어 붙여 래핑)
        const string StarsFarSpritePath = SpriteDir + "/stars_far.png";
        const string StarsNearSpritePath = SpriteDir + "/stars_near.png";

        /// <summary>
        /// 고정 시드로 별을 뿌린 타일 픽셀을 만든다. 에디터 생성 시점에만 도는 코드라
        /// System.Random을 써도 되지만(AGENTS.md §4는 Core 규칙), 재생성 때마다 배경이
        /// 바뀌지 않도록 시드는 상수로 박는다.
        /// </summary>
        static string[] BuildStarPixels(int seed, int starCount, bool bigStars)
        {
            const int width = RefResolutionX, height = RefResolutionY;
            var grid = new char[height][];
            for (int y = 0; y < height; y++)
                grid[y] = new string('.', width).ToCharArray();

            var random = new System.Random(seed);
            for (int i = 0; i < starCount; i++)
            {
                int x = random.Next(width);
                int y = random.Next(height);
                char shade = random.Next(3) == 0 ? 'W' : 'G';
                grid[y][x] = shade;
                if (bigStars && random.Next(5) == 0 && x + 1 < width && y + 1 < height)
                {
                    grid[y][x + 1] = shade;
                    grid[y + 1][x] = shade;
                }
            }

            var rows = new string[height];
            for (int y = 0; y < height; y++) rows[y] = new string(grid[y]);
            return rows;
        }

        static readonly Dictionary<char, Color32> StarsFarPalette = new Dictionary<char, Color32>
        {
            ['W'] = new Color32(0x6A, 0x74, 0x94, 0xFF),
            ['G'] = new Color32(0x3A, 0x44, 0x64, 0xFF)
        };

        static readonly Dictionary<char, Color32> StarsNearPalette = new Dictionary<char, Color32>
        {
            ['W'] = new Color32(0xE0, 0xEC, 0xFF, 0xFF),
            ['G'] = new Color32(0x8C, 0xA4, 0xD4, 0xFF)
        };

        [MenuItem("Tools/Shmup/Rebuild Battle Scene")]
        public static void Build()
        {
            try
            {
                CopyGameDataToResources();
                EnsureSpriteAtlas();

                var shipSprite = LoadAdoptedSprite("sfc_player_ship");
                var bulletSprite = WritePixelSprite(BulletSpritePath, BulletPixels, BulletPalette);
                var hudSlotSprite = WritePixelSprite(HudSlotSpritePath, HudSlotPixels, HudPalette);
                var hudPipSprite = WritePixelSprite(HudPipSpritePath, HudPipPixels, HudPalette);
                // 별 개수는 384×224 시절 110/45를 새 캔버스 면적비(×2.68)로 환산한 값 — 밀도 유지.
                var starsFarSprite = WritePixelSprite(StarsFarSpritePath, BuildStarPixels(9001, 295, false), StarsFarPalette);
                var starsNearSprite = WritePixelSprite(StarsNearSpritePath, BuildStarPixels(4242, 120, true), StarsNearPalette);
                var bulletPrefab = WriteBulletPrefab(bulletSprite);
                var enemySprite = WriteExternalOrPixelSprite(EnemySpritePath, "enemy_zako.png", EnemyPixels, EnemyPalette);
                var enemyPrefab = WriteSpritePrefab(EnemyPrefabPath, "Enemy", enemySprite, 8);
                var capsuleSprite = WriteExternalOrPixelSprite(CapsuleSpritePath, "capsule.png", CapsulePixels, CapsulePalette);
                var capsulePrefab = WriteSpritePrefab(CapsulePrefabPath, "Capsule", capsuleSprite, 7);
                var bombPickupSprite = WriteExternalOrPixelSprite(
                    BombPickupSpritePath, "bomb_pickup.png", BombPickupPixels, BombPickupPalette);
                WriteSpritePrefab(BombPickupPrefabPath, "BombPickup", bombPickupSprite, 7);
                var explosionFrames = LoadExplosionFrames();
                var explosionSprite = explosionFrames.Length > 0
                    ? explosionFrames[0]
                    : WritePixelSprite(ExplosionSpritePath, ExplosionPixels, ExplosionPalette);
                var explosionPrefab = WriteSpritePrefab(ExplosionPrefabPath, "Explosion", explosionSprite, 20);
                var whiteSprite = WritePixelSprite(WhiteSpritePath, WhitePixels, WhitePalette);
                var missileSprite = WritePixelSprite(MissileSpritePath, MissilePixels, MissilePalette);
                var enemyShotSprite = WritePixelSprite(EnemyShotSpritePath, EnemyShotPixels, EnemyShotPalette);
                var optionSprite = WritePixelSprite(OptionSpritePath, OptionPixels, OptionPalette);
                // art-input에 손으로 그린 촉수가 들어오면 그쪽이 우선된다.
                WriteExternalOrPixelSprite(
                    HiveTentacleSpritePath, "enemy_hive_tentacle.png",
                    BuildHiveTentaclePixels(), HiveTentaclePalette);
                var optionPrefab = WriteSpritePrefab(OptionPrefabPath, "Option", optionSprite, 9);
                var shieldSprite = WritePixelSprite(ShieldSpritePath, BuildShieldPixels(), ShieldPalette);

                BuildScene(shipSprite, bulletPrefab, enemyPrefab, capsulePrefab, explosionPrefab,
                           whiteSprite, missileSprite, optionPrefab, shieldSprite,
                           hudSlotSprite, hudPipSprite, starsFarSprite, starsNearSprite,
                           explosionFrames, enemyShotSprite);
                BuildTitleScene(starsFarSprite, starsNearSprite);
                RegisterInBuildSettings();

                AssetDatabase.SaveAssets();
                Debug.Log($"[BattleSceneBuilder] 완료 — {ScenePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BattleSceneBuilder] 실패: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ── GameData ─────────────────────────────────────────────────────────────

        /// <summary>
        /// GameData/*.json(저장소 원본, GROK 소유)을 Resources로 복사해 빌드에 포함시킨다.
        /// 런타임은 이 사본을 읽고, 파싱은 Core(GameDataParser)가 한다. 원본 수정 후에는
        /// 씬 재생성을 다시 돌려야 사본이 갱신된다.
        /// </summary>
        /// <summary>
        /// Assets/Art/Sprites 전체를 하나의 SpriteAtlas로 묶는다 (드로우콜 절감).
        /// 픽셀 아트 규격: 회전 금지, 풀렉트 패킹, 포인트 필터, 무압축.
        /// 이미 있으면 재생성하지 않는다 — 폴더 참조라 새 스프라이트는 자동 포함된다.
        /// </summary>
        static void EnsureSpriteAtlas()
        {
            const string atlasPath = "Assets/Art/GameSprites.spriteatlas";
            if (AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath) != null) return;

            var atlas = new SpriteAtlas();
            var packing = atlas.GetPackingSettings();
            packing.enableRotation = false;
            packing.enableTightPacking = false;
            packing.padding = 2;
            atlas.SetPackingSettings(packing);

            var texture = atlas.GetTextureSettings();
            texture.filterMode = FilterMode.Point;
            texture.generateMipMaps = false;
            atlas.SetTextureSettings(texture);

            var platform = atlas.GetPlatformSettings("DefaultTexturePlatform");
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            platform.maxTextureSize = 2048;
            atlas.SetPlatformSettings(platform);

            var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SpriteDir);
            if (folder != null)
                atlas.Add(new[] { folder });
            AssetDatabase.CreateAsset(atlas, atlasPath);
            Debug.Log($"[BattleSceneBuilder] SpriteAtlas 생성: {atlasPath} (폴더 {SpriteDir} 참조)");
        }

        /// <summary>
        /// GameData/*.json(GROK 소유 원본)을 Resources 사본으로 밀어 넣는다.
        ///
        /// **빌드가 직접 부른다.** 예전에는 씬 재생성에서만 불렀는데, 아트가 안
        /// 바뀌었으면 씬 재생성을 건너뛰는 것이 자연스러워서 데이터만 바뀐 날에는
        /// 사본이 통째로 낡은 채 빌드됐다. 2026-08-05에 전함 2막 부무장 탄막이
        /// 그렇게 날아갔다 — Core 테스트는 원본을 읽어 582개가 다 통과하는데
        /// 게임에는 그 기능이 아예 없었고, 원인을 찾는 데 왕복을 여러 번 했다.
        /// 사람이 기억해야 하는 단계로 두지 않는다.
        /// </summary>
        [MenuItem("Tools/Shmup/Sync GameData To Resources")]
        public static void SyncGameDataToResources()
        {
            CopyGameDataToResources();
            AssetDatabase.SaveAssets();
        }

        static void CopyGameDataToResources()
        {
            const string targetDir = "Assets/Resources/GameData";
            string sourceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "GameData"));
            Directory.CreateDirectory(targetDir);

            // Play 진입마다 도는 경로라 같은 내용이면 임포트를 건너뛴다.
            var sourceNames = new HashSet<string>();
            int copied = 0;
            foreach (string source in Directory.GetFiles(sourceDir, "*.json"))
            {
                string name = Path.GetFileName(source);
                sourceNames.Add(name);
                string target = $"{targetDir}/{name}";
                if (File.Exists(target)
                    && FilesHaveSameBytes(source, target))
                    continue;
                File.Copy(source, target, true);
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate);
                copied++;
            }

            // 원본에서 삭제/개명된 파일의 사본을 지운다 — 유령 JSON이 Resources에
            // 남으면 "지웠는데 게임에는 남아 있다"는 또 다른 침묵 불일치가 된다.
            int removed = 0;
            foreach (string target in Directory.GetFiles(targetDir, "*.json"))
            {
                if (sourceNames.Contains(Path.GetFileName(target)))
                    continue;
                AssetDatabase.DeleteAsset(target.Replace('\\', '/'));
                removed++;
            }

            if (copied > 0 || removed > 0)
                Debug.Log(
                    $"[BattleSceneBuilder] GameData → Resources 동기화: 복사 {copied}, 고아 삭제 {removed}");
        }

        static bool FilesHaveSameBytes(string a, string b)
        {
            byte[] left = File.ReadAllBytes(a);
            byte[] right = File.ReadAllBytes(b);
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i]) return false;
            return true;
        }

        // ── 스프라이트 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 외부 아트 파이프라인 (ART-DIRECTION.md): art-input/<fileName>이 있으면 그 파일이
        /// 절차 생성 플레이스홀더를 대체한다. 없으면 기존 픽셀 배열로 생성.
        /// </summary>
        static Sprite WriteExternalOrPixelSprite(string assetPath, string externalFileName, string[] rows, Dictionary<char, Color32> palette)
        {
            string external = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "art-input", externalFileName));
            if (File.Exists(external))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
                File.Copy(external, assetPath, true);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                ApplySpriteImporter(assetPath);
                var externalSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (externalSprite == null) throw new InvalidOperationException($"{assetPath} 외부 스프라이트 로드 실패");
                Debug.Log($"[BattleSceneBuilder] 외부 아트 적용: {externalFileName} → {assetPath}");
                return externalSprite;
            }
            return WritePixelSprite(assetPath, rows, palette);
        }

        /// <summary>art-input의 선택적 외부 스프라이트. 없으면 null (해당 요소는 생략).</summary>
        static Sprite LoadExternalSprite(string externalFileName, string assetName)
        {
            string external = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "art-input", externalFileName));
            if (!File.Exists(external)) return null;

            string assetPath = $"{SpriteDir}/{assetName}.png";
            Directory.CreateDirectory(SpriteDir);
            File.Copy(external, assetPath, true);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            ApplySpriteImporter(assetPath);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite != null)
                Debug.Log($"[BattleSceneBuilder] 외부 아트 적용: {externalFileName} → {assetPath}");
            return sprite;
        }

        /// <summary>
        /// art-input에 원본이 있으면 임포트하고, 없으면 이미 프로젝트에 들어와 있는
        /// 같은 이름의 스프라이트를 쓴다. art-input이 없는 머신(CI·다른 클론)에서도
        /// 씬 재생성이 아트를 잃지 않게 한다.
        /// </summary>
        static Sprite LoadOrCachedSprite(string externalFileName, string assetName)
        {
            var sprite = LoadExternalSprite(externalFileName, assetName);
            if (sprite != null) return sprite;

            // 이미 프로젝트에 들어와 있는 것을 쓸 때도 **임포터 설정을 반드시 건다.**
            //
            // 안 걸었더니 이런 일이 났다 (사람 보고 2026-08-04 "히든 스테이지 배경이
            // 뒤를 꽉채우는게 아니라 가운데만 조그맣게 들어가면 안됨"): Assets에 직접
            // 복사해 넣은 PNG는 Unity 기본값으로 임포트된다 — PPU 100(정상 16),
            // 필터 Bilinear(정상 Point). 640×360 배경이 6.4×3.6 유닛짜리 흐릿한
            // 조각이 되어 화면 가운데에 조그맣게 박혔다.
            //
            // art-input 경로는 LoadExternalSprite가 이미 이걸 하고 있었다. 폴백
            // 경로만 빠져 있었고, 그 폴백은 내가 이 세션에서 추가한 것이다.
            // 파일이 디스크에 있으면 **먼저 설정을 걸고** 읽는다. 순서를 반대로
            // 했더니(읽어서 null이면 포기) 방금 복사해 넣어 아직 임포트되지 않은
            // 파일이 그대로 빠져나가, 나중에 Unity가 기본값으로 임포트해 버렸다.
            string assetPath = $"{SpriteDir}/{assetName}.png";
            if (!File.Exists(assetPath)) return null;
            ApplySpriteImporter(assetPath);
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        /// <summary>art-input/fx_explosion_00.png…을 순서대로 임포트한다 (M2 폭발 애니).</summary>
        static Sprite[] LoadExplosionFrames()
        {
            return LoadFrameSequence("fx_explosion_");
        }

        static Sprite[] LoadShipAnimationFrames()
        {
            // The curated loop has two stable poses. Do not append old ship_anim frames.
            return new[] { LoadAdoptedSprite("sfc_player_ship"), LoadAdoptedSprite("sfc_player_engine_01") };
        }

        static void ConfigureShipAnimator(PlayerShipAnimator animator, SpriteRenderer renderer)
        {
            SetReference(animator, "_renderer", renderer);
            SetReferenceArray(animator, "_frames", LoadShipAnimationFrames());
            SetReferenceArray(animator, "_bankUpFrames", new[] {
                LoadAdoptedSprite("sfc_player_bank_up_00"), LoadAdoptedSprite("sfc_player_bank_up_01") });
            SetReferenceArray(animator, "_bankDownFrames", new[] {
                LoadAdoptedSprite("sfc_player_bank_down_00"), LoadAdoptedSprite("sfc_player_bank_down_01") });
        }

        // Approved SFC originals live in this repository. Untracked legacy art-input
        // must never overwrite them during scene regeneration on another machine.
        static Sprite LoadAdoptedSprite(string assetName)
        {
            string path = $"{SpriteDir}/{assetName}.png";
            if (!File.Exists(path)) throw new InvalidOperationException($"채택한 원본 누락: {path}");
            ApplySpriteImporter(path);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException($"채택한 스프라이트 로드 실패: {path}");
            return sprite;
        }

        static Sprite LoadSfcPilotEnemySprite(string prefix)
        {
            switch (prefix)
            {
                case "zako_straight": return LoadAdoptedSprite("enemy_sfc_drone");
                case "zako_fast": return LoadAdoptedSprite("enemy_sfc_fast");
                case "turret": return LoadAdoptedSprite("enemy_sfc_turret");
                default: return null;
            }
        }

        static Sprite[] LoadFrameSequence(string prefix)
        {
            var frames = new List<Sprite>();
            for (int i = 0; i < 16; i++)
            {
                // art-input이 없는 머신에서도 이미 임포트된 프레임을 쓴다 —
                // 이걸 안 하면 씬 재생성이 애니메이션을 통째로 잃는다.
                var sprite = LoadOrCachedSprite($"{prefix}{i:d2}.png", $"{prefix}{i:d2}");
                if (sprite == null) break;
                frames.Add(sprite);
            }
            return frames.ToArray();
        }

        static void ApplySpriteImporter(string assetPath)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = AssetsPPU;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        static Sprite WritePixelSprite(string assetPath, string[] rows, Dictionary<char, Color32> palette)
        {
            int height = rows.Length;
            int width = rows[0].Length;
            for (int y = 0; y < height; y++)
                if (rows[y].Length != width)
                    throw new InvalidOperationException($"{assetPath}: {y}행의 길이가 {width}가 아니다.");

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            var clear = new Color32(0, 0, 0, 0);

            for (int y = 0; y < height; y++)
            {
                // rows[0]이 맨 윗줄, 텍스처는 y=0이 맨 아랫줄이라 뒤집는다.
                string row = rows[height - 1 - y];
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = palette.TryGetValue(row[x], out var c) ? c : clear;
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = AssetsPPU;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null) throw new InvalidOperationException($"{assetPath} 스프라이트 로드 실패");
            return sprite;
        }

        // ── 프리팹 ────────────────────────────────────────────────────────────────

        static GameObject WriteBulletPrefab(Sprite sprite)
            // 정렬 30 = 보스보다 **앞**.
            //
            // 오래 5였다. 그때는 보스가 작아 탄이 곧 몸통 밖으로 나왔지만, 하이브를
            // 16유닛으로 키우고 전함을 34x17로 키운 뒤로는 보스가 쏜 탄이 한참 동안
            // 자기 그림 뒤에 숨는다. 사람 보고 2026-08-03: "다리 부수고 나면 눈에
            // 보이지도 않는 효과가 있는데 너무 어렵다." 안 보이는 탄은 난이도가
            // 아니라 결함이다.
            //
            // 보스 본체 15 · 하이브 파츠 14~17 · 전함 하드포인트 16 · 총구 섬광 21보다
            // 위, 피격 플래시(90)와 HUD(98+)보다 아래.
            => WriteSpritePrefab(BulletPrefabPath, "Bullet", sprite, 30);

        static GameObject WriteSpritePrefab(string prefabPath, string name, Sprite sprite, int sortingOrder)
        {
            Directory.CreateDirectory(PrefabDir);

            var temp = new GameObject(name);
            var renderer = temp.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;

            var prefab = PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
            UnityEngine.Object.DestroyImmediate(temp);

            if (prefab == null) throw new InvalidOperationException($"{prefabPath} 저장 실패");
            return prefab;
        }

        // ── 씬 ────────────────────────────────────────────────────────────────────

        static void BuildScene(Sprite shipSprite, GameObject bulletPrefab, GameObject enemyPrefab, GameObject capsulePrefab, GameObject explosionPrefab, Sprite whiteSprite, Sprite missileSprite, GameObject optionPrefab, Sprite shieldSprite, Sprite hudSlotSprite, Sprite hudPipSprite, Sprite starsFarSprite, Sprite starsNearSprite, Sprite[] explosionFrames, Sprite enemyShotSprite)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();

            var battleRoot = new GameObject("Battle");

            var player = new GameObject("Player");
            player.transform.SetParent(battleRoot.transform, false);
            player.transform.localPosition = new Vector3(-8f, 0f, 0f);
            var playerRenderer = player.AddComponent<SpriteRenderer>();
            playerRenderer.sprite = shipSprite;
            playerRenderer.sortingOrder = 10;

            var bulletRoot = new GameObject("Bullets");
            bulletRoot.transform.SetParent(battleRoot.transform, false);

            var enemyRoot = new GameObject("Enemies");
            enemyRoot.transform.SetParent(battleRoot.transform, false);

            var capsuleRoot = new GameObject("Capsules");
            capsuleRoot.transform.SetParent(battleRoot.transform, false);

            var bombPickupRoot = new GameObject("BombPickups");
            bombPickupRoot.transform.SetParent(battleRoot.transform, false);

            var fxRoot = new GameObject("Fx");
            fxRoot.transform.SetParent(battleRoot.transform, false);

            var optionRoot = new GameObject("Options");
            optionRoot.transform.SetParent(battleRoot.transform, false);

            // 실드 링: 플레이어 자식이라 위치를 따로 동기화할 필요가 없다.
            var shield = new GameObject("Shield");
            shield.transform.SetParent(player.transform, false);
            var shieldRenderer = shield.AddComponent<SpriteRenderer>();
            shieldRenderer.sprite = shieldSprite;
            shieldRenderer.sortingOrder = 11;
            shieldRenderer.color = new Color(0.42f, 0.78f, 1f, 0.4f);
            shieldRenderer.enabled = false;

            // 피격 플래시: 2px 흰 타일을 화면 전체(24×14u)로 늘려 알파만 조절한다.
            var damageFlash = new GameObject("DamageFlash");
            damageFlash.transform.SetParent(battleRoot.transform, false);
            // 타일이 2px이므로 스케일 = 화면 픽셀 / 2
            damageFlash.transform.localScale = new Vector3(RefResolutionX / 2f, RefResolutionY / 2f, 1f);
            var damageFlashRenderer = damageFlash.AddComponent<SpriteRenderer>();
            damageFlashRenderer.sprite = whiteSprite;
            damageFlashRenderer.sortingOrder = 90;
            damageFlashRenderer.color = new Color(1f, 0.2f, 0.2f, 0f);

            var inputReader = battleRoot.AddComponent<PlayerInputReader>();
            var director = battleRoot.AddComponent<BattleDirector>();

            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (actions == null)
                Debug.LogWarning($"[BattleSceneBuilder] {InputActionsPath} 를 못 찾았다 — " +
                                 "PlayerInputReader의 Actions를 수동으로 지정해라.");
            SetReference(inputReader, "_actions", actions);

            SetReference(director, "_input", inputReader);
            SetReference(director, "_playerTransform", player.transform);
            SetReference(director, "_bulletPrefab", bulletPrefab);
            SetReference(director, "_bulletRoot", bulletRoot.transform);
            SetReference(director, "_enemyPrefab", enemyPrefab);
            SetReference(director, "_enemyRoot", enemyRoot.transform);
            SetReference(director, "_capsulePrefab", capsulePrefab);
            SetReference(director, "_capsuleRoot", capsuleRoot.transform);
            SetReference(director, "_bombPickupPrefab",
                AssetDatabase.LoadAssetAtPath<GameObject>(BombPickupPrefabPath));
            SetReference(director, "_bombPickupRoot", bombPickupRoot.transform);
            SetReference(director, "_explosionPrefab", explosionPrefab);
            SetReference(director, "_fxRoot", fxRoot.transform);
            SetReference(director, "_damageFlash", damageFlashRenderer);
            SetReference(director, "_missileSprite", missileSprite);
            // 미사일 계열별 스프라이트 (REQ-034) — MissileFamily 열거 순서와 정렬
            SetReferenceArray(director, "_missileFamilySprites", new[]
            {
                LoadExternalSprite("missile_straight.png", "missile_straight"),
                LoadExternalSprite("missile_bomb.png", "missile_bomb"),
                LoadExternalSprite("missile_lance.png", "missile_lance")
            });
            SetReference(director, "_optionPrefab", optionPrefab);
            SetReference(director, "_optionRoot", optionRoot.transform);
            SetReference(director, "_shieldView", shieldRenderer);
            SetReferenceArray(director, "_explosionFrames", explosionFrames);
            SetReference(director, "_enemyShotSprite", enemyShotSprite);

            // 보스 뷰 (REQ-007): 시뮬 BossActive일 때만 director가 렌더러를 켠다.
            var bossSprite = LoadExternalSprite("boss_stage1.png", "boss_stage1");
            var boss = new GameObject("Boss");
            boss.transform.SetParent(battleRoot.transform, false);
            var bossRenderer = boss.AddComponent<SpriteRenderer>();
            bossRenderer.sprite = bossSprite != null ? bossSprite : shipSprite;
            bossRenderer.sortingOrder = 15;
            bossRenderer.enabled = false;
            if (bossSprite == null)
            {
                boss.transform.localScale = Vector3.one * 4f;   // 외부 아트 없을 때 임시 확대
                bossRenderer.color = new Color32(0xC8, 0x50, 0x50, 0xFF);
            }
            SetReference(director, "_bossRenderer", bossRenderer);

            // 보스ID → 스프라이트 매핑 (M3 테마별 보스)
            var bossPrefixes = new List<string>();
            var bossSprites = new List<Sprite>();
            void AddBossSprite(string prefix, Sprite sprite)
            {
                if (sprite == null) return;
                bossPrefixes.Add(prefix);
                bossSprites.Add(sprite);
            }
            AddBossSprite("boss_stage1", bossSprite);
            AddBossSprite("boss_hive", LoadExternalSprite("boss_hive.png", "boss_hive"));
            AddBossSprite("boss_fortress", LoadExternalSprite("boss_fortress.png", "boss_fortress"));
            AddBossSprite("boss_storm", LoadExternalSprite("boss_storm.png", "boss_storm"));
            // 전함 최종 폼 (REQ-139). 접두사가 boss_fortress보다 길어 우선한다 —
            // 등록이 없으면 20유닛짜리 함체 그림이 5유닛 로봇 자리에 그려진다.
            AddBossSprite("boss_fortress_robot",
                LoadOrCachedSprite("boss_fortress_robot.png", "boss_fortress_robot"));
            AddBossSprite("boss_core", LoadExternalSprite("boss_core.png", "boss_core"));
            // St5+ 순환 보스 — 아트는 art-input에 이미 있었는데 등록이 빠져 있었다.
            // 등록이 없으면 ApplyBossSprite가 best=null로 이전 스프라이트를 유지해
            // 브루드마더/리바이어던이 stage1 아트로 나온다.
            // (boss_core_prism은 의도적으로 미등록 — prefix 매칭으로 boss_core를 물려받는다.)
            AddBossSprite("boss_broodmother",
                LoadExternalSprite("boss_broodmother.png", "boss_broodmother"));
            AddBossSprite("boss_leviathan",
                LoadExternalSprite("boss_leviathan.png", "boss_leviathan"));
            // 히든 보스 4페이즈 = form2 소형 비행체 (REQ-154). 접두사가 본체보다 길어
            // 우선한다 — 등록이 없으면 10유닛짜리 본체 그림이 2.5유닛 자리에 그려진다
            // (전함 로봇에서 겪은 것과 같은 문제).
            AddBossSprite("boss_leviathan_drone",
                LoadOrCachedSprite("boss_leviathan_drone.png", "boss_leviathan_drone"));
            AddBossSprite("boss_broodmother_spawn",
                LoadOrCachedSprite("boss_broodmother_spawn.png", "boss_broodmother_spawn"));
            SetStringArray(director, "_bossSpritePrefixes", bossPrefixes.ToArray());
            SetReferenceArray(director, "_bossSprites", bossSprites.ToArray());

            // 보상 화면 초상화 (REQ-156). 전투용 본체 스프라이트와 따로 등록한다 —
            // 하이브와 전함은 조각을 조립해 그리므로 등록된 본체 그림이 조립 이전의
            // 구버전이고, 그걸 보상 카드에 걸면 방금 싸운 보스와 다른 그림이 뜬다.
            var portraitPrefixes = new List<string>();
            var portraits = new List<Sprite>();
            void AddPortrait(string prefix, Sprite sprite)
            {
                if (sprite == null) return;
                portraitPrefixes.Add(prefix);
                portraits.Add(sprite);
            }
            AddPortrait("boss_hive",
                LoadOrCachedSprite("boss_hive_torso.png", "boss_hive_torso"));
            AddPortrait("boss_fortress",
                LoadOrCachedSprite("warship_hull.png", "warship_hull"));
            AddPortrait("boss_fortress_robot",
                LoadOrCachedSprite("boss_fortress_robot.png", "boss_fortress_robot"));
            AddPortrait("boss_leviathan_drone",
                LoadOrCachedSprite("boss_leviathan_drone.png", "boss_leviathan_drone"));
            AddPortrait("boss_broodmother_spawn",
                LoadOrCachedSprite("boss_broodmother_spawn.png", "boss_broodmother_spawn"));
            SetStringArray(director, "_bossPortraitPrefixes", portraitPrefixes.ToArray());
            SetReferenceArray(director, "_bossPortraits", portraits.ToArray());

            // 보스 HP 바: 상단 중앙, px_white 스케일 방식. BossActive 동안만 director가 켠다.
            var bossHpRoot = new GameObject("BossHp");
            // 화면 **위**로 되돌렸다. PAUSE 버튼과 겹쳐서 한 번 아래로 내렸는데,
            // 사람이 "HP는 기존처럼 위쪽에 배치해줘. 잘안보이네"라고 했다 —
            // 아래는 게이지 줄·폭탄 버튼과 시선이 몰려 바가 묻힌다. 자리를 비킨
            // 것은 PAUSE 버튼 쪽이다 (좌하단으로 옮겼다 — PauseScreen 참고).
            //
            // 실드 0 경고 띠(상단 10px ≈ 화면 위 0.625u)와 겹치지 않게 그 아래에 둔다.
            bossHpRoot.transform.localPosition = new Vector3(0f, 9.7f, 0f);
            var hpBack = new GameObject("Back");
            hpBack.transform.SetParent(bossHpRoot.transform, false);
            var hpBackRenderer = hpBack.AddComponent<SpriteRenderer>();
            hpBackRenderer.sprite = whiteSprite;
            hpBackRenderer.sortingOrder = 98;
            hpBackRenderer.color = new Color32(0x10, 0x14, 0x20, 0xE0);
            hpBack.transform.localScale = new Vector3(16f / (2f / 16f), 3f, 1f);
            var hpFill = new GameObject("Fill");
            hpFill.transform.SetParent(bossHpRoot.transform, false);
            var hpFillRenderer = hpFill.AddComponent<SpriteRenderer>();
            hpFillRenderer.sprite = whiteSprite;
            hpFillRenderer.sortingOrder = 99;
            hpFillRenderer.color = new Color32(0xE8, 0x4A, 0x2A, 0xFF);
            hpFill.transform.localScale = new Vector3(16f / (2f / 16f), 2f, 1f);
            bossHpRoot.SetActive(false);
            SetReference(director, "_bossHpRoot", bossHpRoot);
            SetReference(director, "_bossHpFill", hpFill.transform);

            // 적 종별 전용 스프라이트 (M2 비주얼 다양화). 없는 종류는 기본+틴트 폴백.
            var enemyTypeSprites = new List<Sprite>();
            var enemyTypePrefixes = new List<string>();
            void AddEnemySprite(string prefix, Sprite sprite)
            {
                if (sprite == null) return;
                enemyTypePrefixes.Add(prefix);
                enemyTypeSprites.Add(sprite);
            }
            AddEnemySprite("zako_straight", LoadSfcPilotEnemySprite("zako_straight"));
            AddEnemySprite("zako_fast", LoadSfcPilotEnemySprite("zako_fast"));
            AddEnemySprite("zako_sine", LoadExternalSprite("enemy_scarab.png", "enemy_scarab"));
            AddEnemySprite("turret", LoadSfcPilotEnemySprite("turret"));
            AddEnemySprite("zako_tank", LoadExternalSprite("enemy_tank.png", "enemy_tank"));
            AddEnemySprite("elite", LoadExternalSprite("enemy_elite.png", "enemy_elite"));
            AddEnemySprite("spore", LoadExternalSprite("enemy_spore.png", "enemy_spore"));
            AddEnemySprite("lancer", LoadExternalSprite("enemy_lancer.png", "enemy_lancer"));
            AddEnemySprite("sentry", LoadExternalSprite("enemy_sentry.png", "enemy_sentry"));
            AddEnemySprite("interceptor", LoadExternalSprite("enemy_interceptor.png", "enemy_interceptor"));
            AddEnemySprite("wisp", LoadExternalSprite("enemy_wisp.png", "enemy_wisp"));
            AddEnemySprite("guardian", LoadExternalSprite("enemy_guardian.png", "enemy_guardian"));
            AddEnemySprite("mini_destroyer", LoadExternalSprite("enemy_mini_destroyer.png", "enemy_mini_destroyer"));
            AddEnemySprite("mini_horror", LoadExternalSprite("enemy_mini_horror.png", "enemy_mini_horror"));
            AddEnemySprite("mini_walker", LoadExternalSprite("enemy_mini_walker.png", "enemy_mini_walker"));
            AddEnemySprite("mini_crystal", LoadExternalSprite("enemy_mini_crystal.png", "enemy_mini_crystal"));
            AddEnemySprite("mini_core", LoadExternalSprite("enemy_mini_core.png", "enemy_mini_core"));
            AddEnemySprite("scrap_tumbler", LoadExternalSprite("enemy_scrap_tumbler.png", "enemy_scrap_tumbler"));
            AddEnemySprite("brood_spitter", LoadExternalSprite("enemy_brood_spitter.png", "enemy_brood_spitter"));
            AddEnemySprite("mortar_drone", LoadExternalSprite("enemy_mortar_drone.png", "enemy_mortar_drone"));
            AddEnemySprite("echo_wisp", LoadExternalSprite("enemy_echo_wisp.png", "enemy_echo_wisp"));
            AddEnemySprite("rust_skimmer", LoadExternalSprite("enemy_rust_skimmer.png", "enemy_rust_skimmer"));
            AddEnemySprite("junk_roller", LoadExternalSprite("enemy_junk_roller.png", "enemy_junk_roller"));
            AddEnemySprite("void_moth", LoadExternalSprite("enemy_void_moth.png", "enemy_void_moth"));
            // REQ-146 테마 보강 졸개. 테마당 고유 졸개가 4종뿐이던 고철·성운에
            // 하나씩 채웠다 — 종류가 부족하면 같은 적만 계속 나와 스테이지 성격이
            // 흐려진다 (사람 지시 2026-08-03).
            AddEnemySprite("magnet_claw",
                LoadOrCachedSprite("enemy_magnet_claw.png", "enemy_magnet_claw"));
            AddEnemySprite("mist_specter",
                LoadOrCachedSprite("enemy_mist_specter.png", "enemy_mist_specter"));
            AddEnemySprite("shard_prism", LoadExternalSprite("enemy_shard_prism.png", "enemy_shard_prism"));
            AddEnemySprite("sting_hornet", LoadExternalSprite("enemy_sting_hornet.png", "enemy_sting_hornet"));
            AddEnemySprite("pipe_rat", LoadExternalSprite("enemy_pipe_rat.png", "enemy_pipe_rat"));
            AddEnemySprite("phase_disc", LoadExternalSprite("enemy_phase_disc.png", "enemy_phase_disc"));
            AddEnemySprite("rift_blade", LoadExternalSprite("enemy_rift_blade.png", "enemy_rift_blade"));
            // 레이저 적 2종 (REQ-075). PixelLab 500으로 아트 생성이 막혀 지금은
            // 폴백(기본+틴트)이다 — art-input에 파일이 생기면 자동 반영된다.
            AddEnemySprite("laser_sentry", LoadExternalSprite("enemy_laser_sentry.png", "enemy_laser_sentry"));
            AddEnemySprite("prism_beamer", LoadExternalSprite("enemy_prism_beamer.png", "enemy_prism_beamer"));
            AddEnemySprite("hive_tentacle",
                AssetDatabase.LoadAssetAtPath<Sprite>(HiveTentacleSpritePath));
            SetStringArray(director, "_enemySpritePrefixes", enemyTypePrefixes.ToArray());
            SetReferenceArray(director, "_enemySprites", enemyTypeSprites.ToArray());

            // 아이들 애니메이션 (art-input/anim_<prefix>_XX.png 시퀀스가 있으면)
            var animPrefixes = new List<string>();
            var animCounts = new List<int>();
            var animFlat = new List<Sprite>();
            void AddAnim(string prefix)
            {
                // These three new originals have no approved body-motion loop yet.
                // A one-frame clip prevents the old five-frame art from reappearing.
                var adopted = LoadSfcPilotEnemySprite(prefix);
                var frames = adopted != null ? new[] { adopted } : LoadFrameSequence($"anim_{prefix}_");
                if (frames.Length == 0) return;
                animPrefixes.Add(prefix);
                animCounts.Add(frames.Length);
                animFlat.AddRange(frames);
            }
            foreach (string prefix in new[]
            {
                "zako_straight", "zako_fast", "zako_sine", "turret", "zako_tank", "elite",
                "spore", "lancer", "sentry", "interceptor", "wisp", "guardian",
                "scrap_tumbler", "brood_spitter", "mortar_drone", "echo_wisp",
                "rust_skimmer", "junk_roller", "void_moth", "shard_prism",
                "sting_hornet", "pipe_rat", "phase_disc", "rift_blade",
                "mini_destroyer", "mini_horror", "mini_walker", "mini_crystal", "mini_core",
                "boss_stage1", "boss_hive", "boss_fortress", "boss_storm", "boss_core",
                // 히든 보스 둘은 여기 빠져 있었다 — 사람 보고 2026-08-04
                // "히든 보스 애니메이션이 없음"의 정체다. 그림은 있는데 아무도
                // 등록하지 않아 정지 화면으로 서 있었다.
                "boss_leviathan", "boss_broodmother"
            })
                AddAnim(prefix);
            SetStringArray(director, "_animPrefixes", animPrefixes.ToArray());
            SetIntArray(director, "_animFrameCounts", animCounts.ToArray());
            SetReferenceArray(director, "_animFrames", animFlat.ToArray());

            // 큐레이션한 중립/상승/하강 자세마다 고정된 몸체 + 엔진 A/B.
            ConfigureShipAnimator(player.AddComponent<PlayerShipAnimator>(), playerRenderer);

            // 함선별 스프라이트 (밸런스/스피드/탱커 차별화, 2026-07-29 사람 지시)
            var shipIds = new[] { "starter", "interceptor", "bulwark" };
            var shipSprites = new Sprite[]
            {
                shipSprite,
                LoadExternalSprite("ship_interceptor.png", "ship_interceptor"),
                LoadExternalSprite("ship_bulwark.png", "ship_bulwark")
            };
            SetStringArray(director, "_shipSpriteIds", shipIds);
            SetReferenceArray(director, "_shipSprites", shipSprites);
            SetReference(director, "_laserShotSprite",
                LoadExternalSprite("bullet_laser.png", "bullet_laser"));
            SetReference(director, "_spreadShotSprite",
                LoadExternalSprite("bullet_spread.png", "bullet_spread"));

            // 장애물 (REQ-023): 테마×계열 스프라이트, _themeIds 순서와 정렬
            var obstacleRoot = new GameObject("Obstacles");
            obstacleRoot.transform.SetParent(battleRoot.transform, false);
            var armorSprite = LoadExternalSprite("obstacle_armor_block.png", "obstacle_armor_block");
            var sporeSprite = LoadExternalSprite("obstacle_spore_pillar.png", "obstacle_spore_pillar");
            var crystalSprite = LoadExternalSprite("obstacle_crystal.png", "obstacle_crystal");
            var coreBlockSprite = LoadExternalSprite("obstacle_core_block.png", "obstacle_core_block");
            var obstaclePrefab = WriteSpritePrefab(
                PrefabDir + "/Obstacle.prefab", "Obstacle",
                armorSprite != null ? armorSprite : shipSprite, 9);
            SetReference(director, "_obstacleRoot", obstacleRoot.transform);
            SetReference(director, "_obstaclePrefab", obstaclePrefab);
            SetReferenceArray(director, "_obstacleSolidSprites", new[]
                { armorSprite, armorSprite, armorSprite, armorSprite, coreBlockSprite });
            // 파괴 가능 장애물은 스테이지 테마를 따라야 한다 (REQ-055). 예전에는 스크랩야드와
            // 포트리스까지 크리스탈이라 고철 폐기장·금속 요새에 청록 결정이 떠 있었다.
            var scrapDebrisSprite = WriteExternalOrPixelSprite(
                ScrapDebrisSpritePath, "obstacle_scrap_debris.png",
                BuildScrapDebrisPixels(), ScrapDebrisPalette);
            // 포트리스는 예전에 스크랩야드와 같은 고철 스프라이트를 썼는데, 사람이
            // "파괴 가능한 고철은 스테이지1에서만 나와야 한다"고 지적했다. 요새에
            // 폐선 고철이 굴러다니면 테마가 섞인다 — 균열 간 벙커 블록으로 갈랐다.
            var fortressRubbleSprite =
                LoadExternalSprite("obstacle_fortress_rubble.png", "obstacle_fortress_rubble")
                ?? scrapDebrisSprite;
            SetReferenceArray(director, "_obstacleBreakableSprites", new[]
            {
                scrapDebrisSprite,      // 1 스크랩야드 — 고철 (여기서만 쓴다)
                sporeSprite,            // 2 바이오 하이브 — 포자
                fortressRubbleSprite,   // 3 포트리스 — 균열 간 벙커 블록
                crystalSprite,          // 4 네뷸라 — 결정
                crystalSprite           // 5 코어 — 결정
            });
            // 레이저 포탑 (ObstacleType.LaserEmitter). 테마와 무관하게 한 실루엣이다 —
            // "저건 쏘는 것"은 스테이지가 바뀌어도 같은 모양으로 읽혀야 한다.
            var laserTurretSprite = WriteExternalOrPixelSprite(
                LaserTurretSpritePath, "obstacle_laser_turret.png",
                LaserTurretPixels, LaserTurretPalette);
            SetReference(director, "_obstacleEmitterSprite", laserTurretSprite);

            // UI 픽셀 폰트 (Galmuri, OFL — Assets/Fonts/Galmuri-LICENSE-OFL.txt)
            var uiFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Galmuri9.ttf");
            var uiFontBold = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Galmuri11-Bold.ttf");
            if (uiFont == null || uiFontBold == null)
                Debug.LogWarning("[BattleSceneBuilder] Assets/Fonts/Galmuri*.ttf 를 못 찾았다 — UI 텍스트가 비어 보인다.");

            var rewardScreen = battleRoot.AddComponent<RewardScreen>();
            SetReference(rewardScreen, "_director", director);
            SetReference(rewardScreen, "_font", uiFont);
            SetReference(rewardScreen, "_fontBold", uiFontBold);

            // 섹터 계약 선택 (REQ-070) — 스테이지 경계에서 다음 구간 조건을 보고 고른다
            var contractScreen = battleRoot.AddComponent<ContractScreen>();
            SetReference(contractScreen, "_director", director);
            SetReference(contractScreen, "_font", uiFont);
            SetReference(contractScreen, "_fontBold", uiFontBold);
            // 목적지 프리뷰 (REQ-086): 테마 중경 + 대표 잡몹 + 보스를 카드에 합성한다.
            // LoadExternalSprite는 멱등이라 이미 임포트된 에셋을 그대로 재사용한다.
            SetStringArray(contractScreen, "_themeIds",
                new[] { "scrapyard", "hive", "fortress", "nebula", "core" });
            // 프리뷰 배경은 **원경**이다. 중경(_mid)은 투명 배경 위 낱개 오브젝트라
            // 카드 안에서 "스테이지"로 읽히지 않았다 — 덩어리 몇 개가 떠 있을 뿐이다
            // (사람 지시 2026-08-04: "카드 안쪽에 스테이지 배경과 보스 일부 보이는
            // 정도로"). 원경은 화면을 꽉 채우는 장면이라 창밖 풍경으로 읽힌다.
            SetReferenceArray(contractScreen, "_themeBgs", new UnityEngine.Object[]
            {
                LoadOrCachedSprite("scrap_far.png", "scrap_far"),
                LoadOrCachedSprite("hive_far.png", "hive_far"),
                LoadOrCachedSprite("fort_far.png", "fort_far"),
                LoadOrCachedSprite("nebula_far.png", "nebula_far"),
                LoadOrCachedSprite("core_far.png", "core_far"),
            });
            // 카드에 걸리는 보스 그림은 **지금 화면에 나오는 그것**이어야 한다.
            // 하이브와 전함은 조각을 조립해 그리므로 boss_hive/boss_fortress는
            // 조립 이전의 구버전이다 — 그걸 카드에 걸면 방금 본 보스와 다른 그림이
            // 뜬다 (사람 지적 2026-08-05: "스테이지 선택 카드에서 전함/하이브가
            // 이전 버전이네"). 보상 화면 초상화는 이미 아래와 같은 그림을 쓴다.
            SetReferenceArray(contractScreen, "_themeBosses", new UnityEngine.Object[]
            {
                LoadExternalSprite("boss_stage1.png", "boss_stage1"),
                LoadOrCachedSprite("boss_hive_torso.png", "boss_hive_torso"),
                LoadOrCachedSprite("warship_hull.png", "warship_hull"),
                LoadExternalSprite("boss_storm.png", "boss_storm"),
                LoadExternalSprite("boss_core.png", "boss_core"),
            });
            SetReferenceArray(contractScreen, "_themeEnemies", new UnityEngine.Object[]
            {
                LoadExternalSprite("enemy_scrap_tumbler.png", "enemy_scrap_tumbler"),
                LoadExternalSprite("enemy_brood_spitter.png", "enemy_brood_spitter"),
                LoadExternalSprite("enemy_sentry.png", "enemy_sentry"),
                LoadExternalSprite("enemy_void_moth.png", "enemy_void_moth"),
                LoadExternalSprite("enemy_phase_disc.png", "enemy_phase_disc"),
            });
            var pause = battleRoot.AddComponent<PauseScreen>();
            SetReference(pause, "_font", uiFont);
            SetReference(pause, "_fontBold", uiFontBold);
            SetReference(pause, "_director", director);
            var options = battleRoot.AddComponent<OptionsScreen>();
            SetReference(options, "_input", inputReader);
            SetReference(options, "_font", uiFont);
            SetReference(options, "_fontBold", uiFontBold);
            // 경로 선택 화면(REQ-028)은 제거했다 — 분기 대신 스테이지 안에서
            // 중간보스 → 중간 보상으로 리듬을 만든다 (REQ-054). 조우 아이콘과 테마
            // 이름은 구간 표시에 재활용하므로 UiText에 남겨 둔다.

            var gameOver = battleRoot.AddComponent<GameOverScreen>();
            SetReference(gameOver, "_director", director);
            SetReference(gameOver, "_font", uiFont);
            SetReference(gameOver, "_fontBold", uiFontBold);
            // 완주 축하 그림 두 장 (REQ-155). 일반 완주와 미지의 구역까지 잡은
            // 완전 클리어를 그림으로도 가른다 — 문면만 다르면 "끝났다"로만 읽힌다.
            SetReference(gameOver, "_clearStandardSprite",
                LoadOrCachedSprite("clear_standard.png", "clear_standard"));
            SetReference(gameOver, "_clearPerfectSprite",
                LoadOrCachedSprite("clear_perfect.png", "clear_perfect"));
            var onboarding = battleRoot.AddComponent<OnboardingHints>();
            SetReference(onboarding, "_director", director);
            SetReference(onboarding, "_font", uiFont);

            // 모바일 터치 조작 (원격 플레이) — 터치 기기에서만 표시된다
            var touchControls = battleRoot.AddComponent<TouchControls>();
            SetReference(touchControls, "_font", uiFont);
            SetReference(touchControls, "_director", director);
            // SELECT 버튼 아이콘 = 캡슐 — 게이지 활성화가 소비하는 그 아이템 (2026-07-31 UIUX)
            SetReference(touchControls, "_selectIcon",
                AssetDatabase.LoadAssetAtPath<Sprite>(CapsuleSpritePath));

            // 오류 오버레이: 원격 플레이(폰)에서는 콘솔을 볼 수 없다. C# 예외가 화면에
            // 보이지 않으면 "게임이 멈춘다"는 보고만 남고 원인을 추측할 수밖에 없다.
            var errorOverlay = battleRoot.AddComponent<ErrorOverlay>();
            SetReference(errorOverlay, "_font", uiFont);

            // 전멸 폭탄 버튼 (REQ-046). Core는 폭탄을 완전히 지원했지만 이 버튼이 없어서
            // 발동할 방법이 아예 없었다. TouchControls보다 뒤에 추가해 Start 순서상
            // ReserveRect가 이미 만들어진 드래그 영역에 등록되게 한다.
            var bombButton = battleRoot.AddComponent<BombButton>();
            SetReference(bombButton, "_font", uiFont);
            SetReference(bombButton, "_director", director);
            SetReference(bombButton, "_icon",
                AssetDatabase.LoadAssetAtPath<Sprite>(BombPickupSpritePath));
            SetReference(director, "_bombButton", bombButton);

            // 초대형 보스 파츠 오버레이 (REQ-035)
            var partsRoot = new GameObject("BossParts");
            partsRoot.transform.SetParent(battleRoot.transform, false);
            var partsView = battleRoot.AddComponent<BossPartsView>();
            SetReference(partsView, "_director", director);
            SetReference(partsView, "_root", partsRoot.transform);
            SetReference(director, "_bossPartsView", partsView);

            // St3 거대 전함 (REQ-110/111). 전용 함체 아트가 아직 없어서 **조립**한다:
            // px_white 판으로 어두운 실루엣을 깔고 그 위에 기존 스프라이트를 파츠 위치에
            // 얹는다 (함미=boss_fortress · 포탑=obstacle_laser_turret · 함수=boss_core).
            // art-input/warship_hull.png(권장 320×160, PPU16 → 20×10 유닛)이 들어오면
            // _hullSprite가 채워져 실루엣 판을 통째로 대체한다 — 씬 재생성만으로 교체된다.
            var warshipRoot = new GameObject("Warship");
            warshipRoot.transform.SetParent(battleRoot.transform, false);
            // 하이브 조립 뷰. 조각은 **전신 렌더 한 장을 잘라** 만든 것이고
            // (Tools/ArtGen/cut_hive_parts.py) 전부 같은 캔버스를 쓴다 - 같은 자리에
            // 겹쳐 그리면 원본과 픽셀 단위로 같아진다. 따로 생성한 그림을 이어 붙이던
            // 옛 방식은 관절이 "뚝뚝 끊어진다"는 지적을 받고 버렸다.
            var hiveRoot = new GameObject("HiveBoss");
            hiveRoot.transform.SetParent(battleRoot.transform, false);
            var hiveView = battleRoot.AddComponent<HiveBossView>();
            SetReference(hiveView, "_director", director);
            SetReference(hiveView, "_root", hiveRoot.transform);
            SetReference(hiveView, "_torsoSprite",
                LoadOrCachedSprite("boss_hive_torso.png", "boss_hive_torso"));
            SetReference(hiveView, "_legLeftSprite",
                LoadOrCachedSprite("boss_hive_leg_l.png", "boss_hive_leg_l"));
            SetReference(hiveView, "_legRightSprite",
                LoadOrCachedSprite("boss_hive_leg_r.png", "boss_hive_leg_r"));
            SetReference(hiveView, "_woundLeftSprite",
                LoadOrCachedSprite("boss_hive_wound_l.png", "boss_hive_wound_l"));
            SetReference(hiveView, "_woundRightSprite",
                LoadOrCachedSprite("boss_hive_wound_r.png", "boss_hive_wound_r"));
            SetReference(hiveView, "_shieldSprite",
                LoadOrCachedSprite("boss_hive_shield.png", "boss_hive_shield"));
            SetReference(hiveView, "_glowSprite", whiteSprite);   // 양손 난사 발광
            // 하이브가 화면을 소유하는 동안 범용 파츠 오버레이는 비켜난다
            // (전함과 같은 이유 - 같은 자리에 두 겹으로 그리면 사각형이 두 개로 읽힌다).
            SetReference(partsView, "_hiveView", hiveView);

            var warshipView = battleRoot.AddComponent<WarshipView>();
            SetReference(warshipView, "_director", director);
            SetReference(warshipView, "_root", warshipRoot.transform);
            SetReference(warshipView, "_pixelSprite", whiteSprite);
            SetReference(warshipView, "_hullSprite",
                LoadExternalSprite("warship_hull.png", "warship_hull"));
            // 함미·함수는 전용 아트가 들어오기 전까지 다른 보스 스프라이트를 빌려 썼는데,
            // 사람이 스크린샷을 보고 "전함·코어·로봇 세 보스가 하나로 보인다"고 지적했다.
            // 빌린 그림이 각자 다른 보스의 조형이라 배의 일부로 안 읽힌 것이다.
            // 이제 함체와 같은 팔레트로 그린 전용 파츠를 쓰고, 없을 때만 옛 스프라이트로
            // 되돌아간다 (art-input이 없는 클론에서도 씬 재생성이 아트를 잃지 않게).
            SetReference(warshipView, "_sternSprite",
                LoadExternalSprite("warship_stern.png", "warship_stern")
                ?? LoadExternalSprite("boss_fortress.png", "boss_fortress"));
            // 전함 전용 함포 (2026-08-04). 예전에는 잡몹용 obstacle_laser_turret를
            // 빌려 썼는데 회적색 32×32라 남색 함체 위에서 겉돌았다 — 사람이
            // "포대가 그래픽이 어색한데 몸체랑 맞춰줘"라고 했다. 함체 팔레트
            // (남색·강청 + 호박 경고등)로 다시 그렸고 판정 크기(48×40)와 1:1이라
            // 늘어나지 않는다. 없으면 옛 스프라이트로 폴백한다.
            var warshipTurret = LoadOrCachedSprite(
                "warship_turret.png", "warship_turret");
            SetReference(warshipView, "_turretSprite",
                warshipTurret != null ? warshipTurret : laserTurretSprite);
            // 부서진 모습 (사람 지시 2026-08-04 "각 페이즈별로 파괴되는 부위도
            // 나오게 그려줘"). 없으면 뷰가 그을음 틴트만 쓴다.
            SetReference(warshipView, "_turretWreckSprite",
                LoadOrCachedSprite("warship_turret_wreck.png", "warship_turret_wreck"));
            SetReference(warshipView, "_sternWreckSprite",
                LoadOrCachedSprite("warship_stern_wreck.png", "warship_stern_wreck"));
            SetReference(warshipView, "_coreSprite",
                LoadExternalSprite("warship_core.png", "warship_core")
                ?? LoadExternalSprite("boss_core.png", "boss_core"));
            // 전함이 떠 있는 동안 범용 파츠 오버레이는 비켜난다 (회색 사각 이중 표시 방지)
            SetReference(partsView, "_warshipView", warshipView);

            // 적·지형 지속 레이저 (REQ-042). Core가 선분과 4단계를 노출하고 여기서 그린다.
            var laserRoot = new GameObject("Lasers");
            laserRoot.transform.SetParent(battleRoot.transform, false);
            // 흡입 연출 (브루드마더 최종 패턴). Core는 SuctionActive를 계속
            // 내보내고 있었는데 읽는 쪽이 없어 화면이 아무 말도 하지 않았다.
            var suctionView = battleRoot.AddComponent<SuctionView>();
            SetReference(suctionView, "_director", director);
            SetReference(suctionView, "_root", laserRoot.transform);
            SetReference(suctionView, "_ringSprite",
                LoadOrCachedSprite("laser_cap.png", "laser_cap"));
            SetReference(suctionView, "_softSprite",
                LoadOrCachedSprite("laser_soft.png", "laser_soft"));

            var laserView = battleRoot.AddComponent<LaserBeamView>();
            SetReference(laserView, "_director", director);
            SetReference(laserView, "_pixelSprite", whiteSprite);
            // 예고 중 발사 원점 차지 글로우 — 머즐 플래시 스프라이트를 재활용한다
            // ("갑자기 출현한다", 2026-08-02). 어디서 나오는지 보여야 피할 방향이 정해진다.
            SetReference(laserView, "_glowSprite",
                LoadOrCachedSprite("fx_muzzle_00.png", "fx_muzzle_00"));
            // 후보 C (사람 확정 2026-08-04): 감쇠가 알파에 구워진 띠 + 총구/착탄 캡.
            // 없으면 뷰가 픽셀 스프라이트로 폴백해 예전 각진 모습이 된다.
            SetReference(laserView, "_softSprite",
                LoadOrCachedSprite("laser_soft.png", "laser_soft"));
            SetReference(laserView, "_capSprite",
                LoadOrCachedSprite("laser_cap.png", "laser_cap"));
            SetReference(laserView, "_root", laserRoot.transform);

            // St4 번개룡 = 세그먼트 체인 미니언 (REQ-115b). Core가 Enemies가 아니라
            // SegmentChains라는 **별도 관측**으로 노출해서 지금까지 뷰가 아예 없었다 —
            // 접촉 데미지만 주는 투명 미니언이었다 (build26/27 테스터 2회 보고).
            // 전용 아트가 아직 없어 기존 전기 구체(enemy_echo_wisp)를 머리·몸통에 쓰고,
            // 절 사이는 px_white 아크로 잇는다. art-input/enemy_chain_head.png 등이
            // 들어오면 이 슬롯만 갈아 끼우면 된다.
            var chainRoot = new GameObject("SegmentChains");
            chainRoot.transform.SetParent(battleRoot.transform, false);
            var chainView = battleRoot.AddComponent<SegmentChainView>();
            SetReference(chainView, "_director", director);
            SetReference(chainView, "_root", chainRoot.transform);
            SetReference(chainView, "_pixelSprite", whiteSprite);
            SetReference(chainView, "_glowSprite",
                LoadExternalSprite("fx_muzzle_00.png", "fx_muzzle_00"));
            var chainHeadSprite = LoadOrCachedSprite("enemy_chain_head.png", "enemy_chain_head")
                ?? LoadOrCachedSprite("enemy_echo_wisp.png", "enemy_echo_wisp");
            var chainBodySprite = LoadOrCachedSprite("enemy_chain_body.png", "enemy_chain_body")
                ?? chainHeadSprite;
            SetReference(chainView, "_headSprite", chainHeadSprite);
            SetReference(chainView, "_bodySprite", chainBodySprite);

            // 스테이지 기믹 시각화 (REQ-055): 통로 벽·시야 구름·제한 시간.
            // 벽과 카운트다운은 보이지 않으면 불공정하므로 반드시 그린다.
            var gimmickRoot = new GameObject("StageGimmicks");
            gimmickRoot.transform.SetParent(battleRoot.transform, false);
            var gimmickView = battleRoot.AddComponent<StageGimmickView>();
            SetReference(gimmickView, "_director", director);
            SetReference(gimmickView, "_pixelSprite", whiteSprite);
            SetReference(gimmickView, "_font", uiFont);
            SetReference(gimmickView, "_root", gimmickRoot.transform);
            SetReference(director, "_gimmickView", gimmickView);

            // 바이옴/룸 진행도 HUD + 바이옴 진입 배너 (REQ-032)
            var progress = battleRoot.AddComponent<ProgressHud>();
            SetReference(progress, "_director", director);
            SetReference(progress, "_font", uiFont);
            SetReference(progress, "_fontBold", uiFontBold);
            SetStringArray(progress, "_themeIds",
                new[] { "scrapyard", "hive", "fortress", "nebula", "core" });
            SetStringArray(progress, "_themeNames", UiText.ThemeNames);

            // 점수 팝업 + 저체력 경고 (즉효 개선 묶음)
            var popups = battleRoot.AddComponent<ScorePopups>();
            SetReference(popups, "_font", uiFontBold);
            SetReference(director, "_scorePopups", popups);
            // 적 등장 예고 마커
            var telegraphRoot = new GameObject("SpawnMarkers");
            telegraphRoot.transform.SetParent(battleRoot.transform, false);
            var telegraph = battleRoot.AddComponent<SpawnTelegraph>();
            SetReference(telegraph, "_root", telegraphRoot.transform);
            SetReference(telegraph, "_markerSprite",
                LoadExternalSprite("fx_spawn_marker.png", "fx_spawn_marker"));
            SetReference(director, "_spawnTelegraph", telegraph);

            // 실드 0 경고는 시각 전용이다 (사람 지시 2026-08-02) — 경고음 배선 없음.
            var lowHp = battleRoot.AddComponent<LowHpWarning>();
            SetReference(lowHp, "_director", director);

            // 주스 연출 허브 (M4+ 게임 필): 히트스톱·슬로모·화면 흔들림 + 접근성 토글
            var juice = battleRoot.AddComponent<JuiceDirector>();
            var mainCamera = GameObject.Find("Main Camera");
            if (mainCamera != null)
                SetReference(juice, "_cameraTransform", mainCamera.transform);
            SetReference(director, "_juice", juice);
            SetReference(
                battleRoot.GetComponent<BossPartsView>(), "_juice", juice);
            SetReference(options, "_juice", juice);
            SetReference(lowHp, "_juice", juice);
            // 전함 그룹 전환 흔들림 (함미 전멸 = 중간보스 격파와 같은 무게)
            SetReference(warshipView, "_juice", juice);
            SetReference(hiveView, "_juice", juice);   // 다리 절단 흔들림
            // 체인 아크/머리 맥동은 접근성 토글(플래시 감소)을 따라야 한다
            SetReference(chainView, "_juice", juice);

            // 머즐 플래시: 기체 코 앞에 고정, PlayerFired 이벤트로 director가 깜빡인다
            var muzzleSprite = LoadExternalSprite("fx_muzzle_00.png", "fx_muzzle_00");
            if (muzzleSprite != null)
            {
                var muzzle = new GameObject("MuzzleFlash");
                muzzle.transform.SetParent(player.transform, false);
                muzzle.transform.localPosition = new Vector3(0.85f, 0f, 0f);
                var muzzleRenderer = muzzle.AddComponent<SpriteRenderer>();
                muzzleRenderer.sprite = muzzleSprite;
                muzzleRenderer.sortingOrder = 21;
                muzzleRenderer.enabled = false;
                SetReference(director, "_muzzleFlash", muzzleRenderer);
            }
            var bossIntro = battleRoot.AddComponent<BossIntro>();
            SetReference(director, "_bossIntro", bossIntro);
            SetReference(bossIntro, "_fontBold", uiFontBold);
            var scoreHud = battleRoot.AddComponent<ScoreHud>();
            SetReference(scoreHud, "_director", director);
            SetReference(scoreHud, "_fontBold", uiFontBold);
            // 배율 발광 맥동을 접근성 설정(플래시 감소)에 맞춰 줄이기 위해 필요하다.
            SetReference(scoreHud, "_juice", juice);

            var themeRoots = CreateBackground(director, starsFarSprite, starsNearSprite);

            // 구간(섹션) 배경 전환 (Phase C 1단계, 설계안 §2). 파티클·틴트·스크롤 체감만으로
            // 5스테이지 × 4구간 = 20룩을 만든다 — 아트 0장. 순수 표현이라 Core에 되먹임이 없다.
            var sectionThemes = battleRoot.AddComponent<SectionThemeDirector>();
            SetReference(sectionThemes, "_director", director);
            SetReference(sectionThemes, "_juice", juice);
            SetReference(sectionThemes, "_pixelSprite", whiteSprite);
            SetReferenceArray(sectionThemes, "_themeRoots", themeRoots);
            CreateSectionArtSlots(sectionThemes);

            CreateHud(director, hudSlotSprite, hudPipSprite, sectionThemes, warshipView, chainView, partsView);
            CreateSfx(director);
            CreateBgm(director);
            ConfigureSceneAudio();

            VerifyEssentialComponents("Battle");

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        /// <summary>
        /// 코드로 만든 씬에 빠지기 쉬운 기반 컴포넌트를 저장 전에 확인한다.
        ///
        /// 에디터 GUI로 오브젝트를 추가하면 Unity가 자동으로 붙여 주지만 코드 경로에서는
        /// 빠지고, **예외 없이 조용히 기능만 죽는다.** 실제로 두 번 물렸다 —
        /// EventSystem이 없어 버튼이 하나도 안 눌렸고, AudioListener가 없어 무음이었다.
        /// 둘 다 배선을 한참 뒤진 뒤에야 씬을 의심했다. 여기서 걸러 그 낭비를 없앤다.
        ///
        /// EventSystem은 여기서 보지 않는다 — <see cref="UiKit.EnsureEventSystem"/>이
        /// 런타임에 만들므로 씬에 없는 것이 정상이다.
        /// </summary>
        static void VerifyEssentialComponents(string sceneLabel)
        {
            int listeners = UnityEngine.Object
                .FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length;
            if (listeners != 1)
            {
                // 0개면 완전 무음, 2개 이상이면 Unity가 경고를 내고 한쪽을 무시한다.
                Debug.LogError(
                    $"[SceneBuilder] {sceneLabel}: AudioListener가 {listeners}개다 (1개여야 한다). " +
                    "0개면 AudioSource가 재생돼도 소리가 전혀 나지 않는다.");
            }
        }

        static void CreateCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.position = new Vector3(0f, 0f, -10f);

            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = RefResolutionY / 2f / AssetsPPU;   // 11.25
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(0x0A, 0x0E, 0x1A, 0xFF);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;

            go.AddComponent<UniversalAdditionalCameraData>();

            // AudioListener가 없으면 Unity는 **어떤 소리도 출력하지 않는다** — AudioSource가
            // 재생을 시작해도 듣는 귀가 없어 오디오 노드조차 만들어지지 않는다. 에디터에서
            // 카메라를 GUI로 추가하면 자동으로 따라붙지만 코드로 만들면 빠지고,
            // AudioListener.volume은 리스너가 없어도 예외 없이 설정되는 전역 정적 속성이라
            // 조용히 무음이 된다 (EventSystem 누락과 같은 함정).
            go.AddComponent<AudioListener>();

            var ppc = go.AddComponent<PixelPerfectCamera>();
            ppc.assetsPPU = AssetsPPU;
            ppc.refResolutionX = RefResolutionX;
            ppc.refResolutionY = RefResolutionY;
            // 크롭·스트레치는 **켜지 않는다** (2026-08-03 사람 지시: "해상도는 절대 건들지 마").
            //
            // 한 번 켰다가 되돌렸다. 배경 밖 여백을 없애려고 두 축 크롭 + 스트레치를
            // 걸었는데, 실제 기기에서 화면이 크게 줄어들었다 — 크롭이 걸리면 카메라가
            // 기준 해상도 밖을 잘라내고, 스트레치가 기대만큼 채우지 못하면 그대로
            // 작은 화면이 된다.
            //
            // 배경이 화면을 못 채우는 문제는 카메라가 아니라 **워시·틴트**가 원인이었고
            // 그쪽에서 이미 해결했다(SectionTheme의 알파 상한·밝기 바닥). 여백은
            // 캔버스 크기에 따라 남을 수 있지만, 화면 크기를 희생할 값이 아니다.
            ApplyRetroAA(ppc);
        }

        /// <summary>
        /// Filter Mode = Retro AA (CLAUDE.md 고정 결정). URP 17의 PixelPerfectCamera는
        /// 이 값을 public 프로퍼티로 노출하지 않고 private [SerializeField] m_FilterMode로만
        /// 들고 있어서, SerializedObject로 직접 쓴다. RetroAA는 열거형 0번이자 기본값이지만
        /// 버전이 올라가며 기본값이 바뀔 수 있으니 명시적으로 박아 둔다.
        /// </summary>
        static void ApplyRetroAA(PixelPerfectCamera ppc)
        {
            var so = new SerializedObject(ppc);
            var property = so.FindProperty("m_FilterMode");
            if (property == null)
            {
                Debug.LogWarning("[BattleSceneBuilder] PixelPerfectCamera.m_FilterMode 를 못 찾았다 — " +
                                 "인스펙터에서 Filter Mode를 Retro AA로 직접 설정해라.");
                return;
            }
            property.enumValueIndex = (int)PixelPerfectCamera.PixelPerfectFilterMode.RetroAA;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── 배경 ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// 2층 패럴랙스 스타필드. 레이어마다 화면 폭 타일 2장을 이어 붙이고
        /// ParallaxBackground가 스크롤 팩터별로 왼쪽으로 밀며 래핑한다.
        /// </summary>
        static readonly float[] StarLayerFactors = { 0.25f, 0.6f };

        static GameObject CreateStarLayers(Sprite farSprite, Sprite nearSprite, out Transform[] layers)
        {
            const float tileWidth = RefResolutionX / (float)AssetsPPU;   // 40u

            var root = new GameObject("Background");
            layers = new Transform[2];
            var sprites = new[] { farSprite, nearSprite };
            var orders = new[] { -100, -90 };

            for (int i = 0; i < layers.Length; i++)
            {
                var layer = new GameObject(i == 0 ? "StarsFar" : "StarsNear");
                layer.transform.SetParent(root.transform, false);
                layers[i] = layer.transform;

                for (int tile = 0; tile < 2; tile++)
                {
                    var tileGo = new GameObject($"Tile{tile}");
                    tileGo.transform.SetParent(layer.transform, false);
                    tileGo.transform.localPosition = new Vector3(tile * tileWidth, 0f, 0f);
                    var renderer = tileGo.AddComponent<SpriteRenderer>();
                    renderer.sprite = sprites[i];
                    renderer.sortingOrder = orders[i];
                }
            }

            return root;
        }

        /// <summary>
        /// 배틀 배경: 테마 루트를 여러 개 만들고 director가 StageIndex로 로테이션한다 (M3).
        /// 각 루트 = 별 2겹 + 테마 3겹(art-input/&lt;theme&gt;_far/mid/near.png). 팩터가 작을수록 원경,
        /// near 레이어는 스크롤보다 빠르게(1.15) 지나가는 전경 실루엣이다 —
        /// 정렬은 게임플레이 **아래**(탄 5 아래): 기체/탄을 가리면 안 된다.
        /// </summary>
        static GameObject[] CreateBackground(BattleDirector director, Sprite farSprite, Sprite nearSprite)
        {
            var themeRoots = new List<GameObject>();
            var themeIds = new List<string>();
            void AddTheme(string themeId, string rootName, string filePrefix)
            {
                var root = CreateThemeRoot(director, rootName, farSprite, nearSprite, filePrefix);
                if (root == null) return;
                themeRoots.Add(root);
                themeIds.Add(themeId);   // waves.json의 theme 값과 일치해야 한다 (테마-보스 바인딩)
            }
            AddTheme("scrapyard", "Background_Scrapyard", "scrap");
            AddTheme("hive", "Background_Hive", "hive");
            AddTheme("fortress", "Background_Fortress", "fort");
            AddTheme("nebula", "Background_Nebula", "nebula");
            AddTheme("core", "Background_Core", "core");
            // 히든 전용 (REQ-159). 이 루트가 없으면 director가 켤 배경을 못 찾아
            // **직전 바이옴의 배경이 그대로 남는다** — 테마만 바꾸고 여기를 안 늘려서
            // abyss 스테이지가 보라색으로 나왔다.
            AddTheme("abyss", "Background_Abyss", "abyss");
            AddTheme("brood", "Background_Brood", "brood");

            // 두 번째 이후 테마는 director의 ApplyStageTheme이 켤 때까지 꺼 둔다.
            for (int i = 1; i < themeRoots.Count; i++)
                themeRoots[i].SetActive(false);

            var roots = themeRoots.ToArray();
            SetReferenceArray(director, "_themeBackgrounds", roots);
            SetStringArray(director, "_themeIds", themeIds.ToArray());
            return roots;
        }

        static GameObject CreateThemeRoot(
            BattleDirector director, string rootName, Sprite starsFar, Sprite starsNear, string themePrefix)
        {
            // art-input이 없는 머신에서도 이미 임포트된 것을 쓴다 — 이걸 안 하면
            // 씬 재생성이 배경 레이어를 통째로 잃고 테마 루트가 아예 안 만들어진다.
            var themeFar = LoadOrCachedSprite($"{themePrefix}_far.png", $"{themePrefix}_far");
            var themeMid = LoadOrCachedSprite($"{themePrefix}_mid.png", $"{themePrefix}_mid");
            var themeNear = LoadOrCachedSprite($"{themePrefix}_near.png", $"{themePrefix}_near");
            // 기본 테마(첫 번째)는 테마 스프라이트가 없어도 별만으로 만든다.
            if (themeFar == null && themeMid == null && themeNear == null
                && themePrefix != "scrap")
                return null;

            var sprites = new List<Sprite>();
            var factors = new List<float>();
            var orders = new List<int>();
            var roles = new List<int>();
            // 아트가 없으면 레이어가 통째로 빠지므로 인덱스는 테마마다 달라진다.
            // SectionThemeDirector는 인덱스가 아니라 이 역할(BgLayerRole)로 레이어를 지목한다.
            void AddLayer(Sprite sprite, float factor, int order, BgLayerRole role)
            {
                if (sprite == null) return;
                sprites.Add(sprite);
                factors.Add(factor);
                orders.Add(order);
                roles.Add((int)role);
            }

            AddLayer(starsFar, 0.1f, -100, BgLayerRole.SkyFar);
            AddLayer(themeFar, 0.2f, -96, BgLayerRole.Far);
            AddLayer(starsNear, 0.45f, -90, BgLayerRole.SkyNear);
            AddLayer(themeMid, 0.6f, -85, BgLayerRole.Mid);
            // 전경 실루엣은 게임플레이 **아래**(탄 5 아래)다. 예전에는 55(게임플레이 위)라
            // <theme>_fg.png의 불투명 실루엣 띠가 화면 아래쪽 기체·옵션·주무기탄을 통째로
            // 가렸다 — build25~28 "전함 룸 플레이어 영구 소실"의 정체.
            // SectionThemeDirector.NearSortingOrder가 런타임에도 같은 값으로 덮어쓴다.
            AddLayer(themeNear, 1.15f, SectionThemeDirector.NearSortingOrder, BgLayerRole.Near);

            const float tileWidth = RefResolutionX / (float)AssetsPPU;
            var root = new GameObject(rootName);
            var layers = new Transform[sprites.Count];
            for (int i = 0; i < sprites.Count; i++)
            {
                var layer = new GameObject($"Layer{i}_{sprites[i].name}");
                layer.transform.SetParent(root.transform, false);
                layers[i] = layer.transform;
                for (int tile = 0; tile < 2; tile++)
                {
                    var tileGo = new GameObject($"Tile{tile}");
                    tileGo.transform.SetParent(layer.transform, false);
                    tileGo.transform.localPosition = new Vector3(tile * tileWidth, 0f, 0f);
                    var renderer = tileGo.AddComponent<SpriteRenderer>();
                    renderer.sprite = sprites[i];
                    renderer.sortingOrder = orders[i];
                }
            }

            // 랜드마크 슬롯 (설계안 §2 "랜드마크 스프라이트 1개" — 반복 배경 착시 해소).
            // 지금은 스프라이트가 없어 꺼져 있고, SectionThemeDirector가 룩 테이블의
            // landmarkSlot에 아트가 꽂히는 순간 켠다. 타일링하지 않는 단일 스프라이트라
            // 패럴랙스 레이어에 넣지 않았다 — 지금은 제자리에서 스케일만 커진다(접근감).
            // 아트가 들어온 뒤 "한 번 지나가는" 연출이 필요하면 그때 전용 이동을 붙인다.
            var landmark = new GameObject("Landmark");
            landmark.transform.SetParent(root.transform, false);
            var landmarkRenderer = landmark.AddComponent<SpriteRenderer>();
            landmarkRenderer.sprite = LoadOrCachedSprite(
                $"{themePrefix}_landmark.png", $"{themePrefix}_landmark");
            landmarkRenderer.sortingOrder = -84;   // 중경(-85) 앞, 워시(0) 뒤
            landmarkRenderer.enabled = false;

            var parallax = root.AddComponent<ParallaxBackground>();
            SetReference(parallax, "_director", director);
            SetReferenceArray(parallax, "_layers", layers);
            SetFloatArray(parallax, "_factors", factors.ToArray());
            SetIntArray(parallax, "_layerRoles", roles.ToArray());
            SetFloat(parallax, "_tileWidth", tileWidth);
            return root;
        }

        // ── SFX ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// SfxPlayer 배선. 채택음(Tools/SfxGen 시드 0)은 Assets/Audio/Sfx에 커밋되어 있다.
        /// 클립이 없으면 경고만 내고 무음으로 둔다 — 씬 재생성이 막히면 안 된다.
        /// </summary>
        const string SfxDir = "Assets/Audio/Sfx";

        /// <summary>Idempotent audio wiring, also used to upgrade existing scenes without rebuilding them.</summary>
        public static void ConfigureSceneAudio()
        {
            foreach (var source in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                AudioChannel channel;
                if (source.GetComponent<SfxPlayer>() != null) channel = AudioChannel.Effects;
                else if (source.GetComponent<BgmPlayer>() != null
                    || (source.clip != null && source.clip.name.StartsWith("bgm_"))) channel = AudioChannel.Music;
                else continue;
                var gain = source.GetComponent<AudioChannelSource>();
                if (gain == null) gain = source.gameObject.AddComponent<AudioChannelSource>();
                gain.Configure(channel);
                EditorUtility.SetDirty(gain);
            }
            var ui = UnityEngine.Object.FindAnyObjectByType<UiAudio>();
            if (ui == null)
            {
                var go = new GameObject("UiAudio");
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.ignoreListenerPause = true;
                go.AddComponent<AudioChannelSource>().Configure(AudioChannel.Interface);
                ui = go.AddComponent<UiAudio>();
            }
            SetReference(ui, "_navigate", LoadClip("sfx_pickup"));
            SetReference(ui, "_confirm", LoadClip("sfx_powerup"));
            SetReference(ui, "_reject", LoadClip("sfx_hit"));
        }

        static void CreateSfx(BattleDirector director)
        {
            var go = new GameObject("Sfx");
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            var player = go.AddComponent<SfxPlayer>();
            SetReference(player, "_source", source);
            SetReference(player, "_laser", LoadClip("sfx_laser"));
            SetReference(player, "_hit", LoadClip("sfx_hit"));
            SetReference(player, "_explosion", LoadClip("sfx_explosion"));
            SetReference(player, "_pickup", LoadClip("sfx_pickup"));
            SetReference(player, "_powerup", LoadClip("sfx_powerup"));
            SetReference(player, "_laserBeam", LoadClip("sfx_laser_beam"));
            SetReference(player, "_spreadShot", LoadClip("sfx_laser_spread"));
            SetReference(player, "_warning", LoadClip("sfx_warning"));
            // 적·지형 레이저 (Tools/SfxGen/sfxgen_laser.py 후보 b). 예고 차지 → 발사 잽.
            SetReference(player, "_laserCharge", LoadClip("sfx_laser_charge"));
            SetReference(player, "_laserFire", LoadClip("sfx_laser_fire"));
            // 초대형 빔 전용 발사음 (반폭 2유닛 초과). 없으면 SfxPlayer가 기존
            // 발사음으로 폴백한다.
            SetReference(player, "_laserFireHeavy", LoadClip("sfx_laser_heavy"));
            SetReference(director, "_sfx", player);
        }

        /// <summary>테마별 BGM 루프 (Tools/SfxGen/bgmgen.py 테마 프리셋 산출물). 없으면 무음.</summary>
        static void CreateBgm(BattleDirector director)
        {
            string[] themeIds = { "scrapyard", "hive", "fortress", "nebula", "core" };
            var clips = new AudioClip[themeIds.Length];
            AudioClip first = null;
            for (int i = 0; i < themeIds.Length; i++)
            {
                clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(
                    $"Assets/Audio/Bgm/bgm_{themeIds[i]}.wav");
                if (first == null && clips[i] != null) first = clips[i];
            }
            if (first == null)
            {
                Debug.LogWarning("[BattleSceneBuilder] Assets/Audio/Bgm 트랙 없음 — BGM 무음.");
                return;
            }

            var go = new GameObject("Bgm");
            var source = go.AddComponent<AudioSource>();
            source.clip = first;
            source.loop = true;
            source.playOnAwake = true;
            source.volume = 0.45f;
            source.spatialBlend = 0f;

            var player = go.AddComponent<BgmPlayer>();
            SetReference(player, "_director", director);
            SetReference(player, "_source", source);
            SetStringArray(player, "_themeIds", themeIds);
            SetReferenceArray(player, "_clips", clips);
            SetReference(player, "_bossClip",
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Bgm/bgm_boss.wav"));
            // 보스 종류별 전용 곡 (REQ-149). 없으면 BgmPlayer가 기존 전투곡으로 되돌아간다.
            SetReference(player, "_stageBossClip",
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Bgm/bgm_boss_stage.wav"));
            SetReference(player, "_hiddenBossClip",
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Bgm/bgm_boss_hidden.wav"));
            SetReference(player, "_clearJingle",
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Sfx/jingle_clear.wav"));
            SetReference(player, "_gameOverJingle",
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Sfx/jingle_gameover.wav"));
        }

        static AudioClip LoadClip(string name)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxDir}/{name}.wav");
            if (clip == null)
                Debug.LogWarning($"[BattleSceneBuilder] {SfxDir}/{name}.wav 없음 — 해당 SFX는 무음.");
            return clip;
        }

        // ── 타이틀 씬 ─────────────────────────────────────────────────────────────

        const string TitleScenePath = "Assets/Scenes/Title.unity";

        static void BuildTitleScene(Sprite farSprite, Sprite nearSprite)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateCamera();

            var root = CreateStarLayers(farSprite, nearSprite, out var layers);
            // 키아트 (gpt-image-1.5, 잠정 채택 — 우주 영역이 투명이라 뒤의 패럴랙스 별이 비친다)
            var keyartSprite = LoadExternalSprite("title_keyart.png", "title_keyart");
            if (keyartSprite != null)
            {
                var keyart = new GameObject("TitleKeyArt");
                keyart.transform.SetParent(root.transform, false);
                var keyartRenderer = keyart.AddComponent<SpriteRenderer>();
                keyartRenderer.sprite = keyartSprite;
                keyartRenderer.sortingOrder = 3;   // 별 레이어 위, UI(오버레이 캔버스) 아래
            }

            var titleFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Galmuri9.ttf");
            var titleFontBold = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Galmuri11-Bold.ttf");
            var title = root.AddComponent<TitleScreen>();
            SetReferenceArray(title, "_layers", layers);
            SetFloatArray(title, "_factors", StarLayerFactors);
            SetFloat(title, "_tileWidth", RefResolutionX / (float)AssetsPPU);
            SetReference(title, "_font", titleFont);
            SetReference(title, "_fontBold", titleFontBold);
            var hangar = root.AddComponent<HangarScreen>();   // 함선 해금형 메타 (2026-07-29)
            SetReference(hangar, "_font", titleFont);
            SetReference(hangar, "_fontBold", titleFontBold);
            SetStringArray(hangar, "_shipIds", new[] { "starter", "interceptor", "bulwark" });
            SetReferenceArray(hangar, "_shipSprites", new Sprite[]
            {
                LoadAdoptedSprite("sfc_player_ship"),
                LoadExternalSprite("ship_interceptor.png", "ship_interceptor"),
                LoadExternalSprite("ship_bulwark.png", "ship_bulwark")
            });

            // 타이틀 BGM (bgmgen title 프리셋, 시드 0 잠정)
            var titleBgm = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Bgm/bgm_title.wav");
            if (titleBgm != null)
            {
                var bgmGo = new GameObject("TitleBgm");
                bgmGo.transform.SetParent(root.transform, false);
                var bgmSource = bgmGo.AddComponent<AudioSource>();
                bgmSource.clip = titleBgm;
                bgmSource.loop = true;
                bgmSource.playOnAwake = true;
                bgmSource.volume = 0.45f;
                bgmSource.spatialBlend = 0f;
            }

            ConfigureSceneAudio();
            VerifyEssentialComponents("Title");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, TitleScenePath);
        }

        static void SetIntArray(UnityEngine.Object target, string fieldName, int[] values)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(fieldName);
            if (property == null)
                throw new InvalidOperationException($"{target.GetType().Name}.{fieldName} 직렬화 필드를 못 찾았다.");
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).intValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetStringArray(UnityEngine.Object target, string fieldName, string[] values)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(fieldName);
            if (property == null)
                throw new InvalidOperationException($"{target.GetType().Name}.{fieldName} 직렬화 필드를 못 찾았다.");
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).stringValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloat(UnityEngine.Object target, string fieldName, float value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(fieldName);
            if (property == null)
                throw new InvalidOperationException($"{target.GetType().Name}.{fieldName} 직렬화 필드를 못 찾았다.");
            property.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloatArray(UnityEngine.Object target, string fieldName, float[] values)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(fieldName);
            if (property == null)
                throw new InvalidOperationException($"{target.GetType().Name}.{fieldName} 직렬화 필드를 못 찾았다.");
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).floatValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── HUD ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// 파워업 게이지 HUD (REQ-074에서 재작성). 슬롯 수·이름이 GameData 주도가 되어
        /// 씬에 스프라이트를 박지 않는다 — PowerUpHudView가 게이지 관측 API를 순회하며
        /// 런타임에 UGUI로 조립한다. 여기서는 컴포넌트와 폰트만 배선한다.
        /// </summary>
        static void CreateHud(
            BattleDirector director, Sprite slotSprite, Sprite pipSprite,
            SectionThemeDirector sectionThemes, WarshipView warshipView,
            SegmentChainView chainView, BossPartsView partsView)
        {
            var hudRoot = new GameObject("Hud");

            var hudView = hudRoot.AddComponent<PowerUpHudView>();
            SetReference(hudView, "_director", director);
            // 슬롯 풀네임 + LV/MAX 라벨과 실드 잔량 숫자용 (2026-07-31 피드백 1·3)
            SetReference(hudView, "_font",
                AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Galmuri9.ttf"));

            var cheats = hudRoot.AddComponent<DevCheats>();
            SetReference(cheats, "_director", director);
            SetReference(cheats, "_sectionThemes", sectionThemes);   // F7 구간 룩 미리보기
            SetReference(cheats, "_warship", warshipView);           // 전함 그룹/포탑 잔량 한 조각
            SetReference(cheats, "_chains", chainView);              // St4 체인: Core 절 수 vs 그린 절 수
            SetReference(cheats, "_bossParts", partsView);           // 붉은 피격 플래시가 몇 개 떠 있는가
        }

        /// <summary>
        /// 구간 룩의 아트 교체 슬롯. **지금은 art-input에 파일이 없어 대부분 비어 있다** —
        /// 이 배선이 곧 아트 2단계의 꽂는 자리다. 파일을 넣고 SectionThemeTable의
        /// spriteSlot/landmarkSlot에 같은 키를 적으면 그 순간 살아난다.
        ///
        /// 키 규칙: &lt;prefix&gt;_far_dusk / _far_dark / _fg / _landmark
        /// (prefix = scrap · hive · fort · nebula · core).
        /// </summary>
        static void CreateSectionArtSlots(SectionThemeDirector sectionThemes)
        {
            // abyss/brood는 히든 전용이다 — 레비아탄·브루드마더로 가는 구간이
            // 직전 바이옴을 재사용하지 않게 하려고 REQ-159에서 추가했다.
            string[] prefixes =
                { "scrap", "hive", "fort", "nebula", "core", "abyss", "brood" };
            string[] suffixes = { "far_dusk", "far_dark", "fg", "landmark" };

            var keys = new List<string>();
            var sprites = new List<UnityEngine.Object>();
            foreach (string prefix in prefixes)
                foreach (string suffix in suffixes)
                {
                    string key = $"{prefix}_{suffix}";
                    // art-input이 없는 머신에서도 이미 임포트된 것을 쓴다 —
                    // 이걸 안 하면 씬 재생성이 배경을 통째로 잃는다.
                    var sprite = LoadOrCachedSprite($"{key}.png", key);
                    if (sprite == null) continue;
                    keys.Add(key);
                    sprites.Add(sprite);
                }

            SetStringArray(sectionThemes, "_slotKeys", keys.ToArray());
            SetReferenceArray(sectionThemes, "_slotSprites", sprites.ToArray());
        }

        static void SetReferenceArray(UnityEngine.Object target, string fieldName, UnityEngine.Object[] values)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(fieldName);
            if (property == null)
                throw new InvalidOperationException($"{target.GetType().Name}.{fieldName} 직렬화 필드를 못 찾았다.");
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(fieldName);
            if (property == null)
                throw new InvalidOperationException($"{target.GetType().Name}.{fieldName} 직렬화 필드를 못 찾았다.");
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void RegisterInBuildSettings()
        {
            // 타이틀이 0번(시작 씬), 전투가 1번. SampleScene은 빌드에서 제외.
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(TitleScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true)
            };
        }
    }
}
