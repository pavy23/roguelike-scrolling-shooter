#!/usr/bin/env python3
"""Assets/Art/Sprites 의 그림이 art-input 의 원본과 같은지 검사한다.

왜 있는가: 2026-08-04에 사람이 "코어가 왜 디자인 보여준거랑 다른게 적용됐지?"라고
물었다. 새로 그린 그림 11장이 **씬을 재생성할 때마다 옛 버전으로 되돌아가고**
있었다.

BattleSceneBuilder는 `art-input/<이름>.png` 를 먼저 찾고, 있으면 그것을
Assets 위에 복사한다 — art-input이 원본이고 Assets는 산출물이다. 그런데 새 아트를
Assets에 직접 넣었더니, 매 빌드마다 art-input의 옛 파일이 그것을 덮었다.

증상이 조용하다. 컴파일도 되고 테스트도 통과하고 스프라이트 임포트 검사도
통과한다 — **파일은 멀쩡하고 내용만 옛것**이기 때문이다. 화면을 봐도 "아트가
왜 이래?"로만 보이지 되돌아갔다고는 생각하기 어렵다.

사용:
    python art_source_check.py
"""
import hashlib
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
ASSETS = ROOT / "Assets" / "Art" / "Sprites"
ART_INPUT = ROOT.parent / "art-input"


def digest(path):
    return hashlib.md5(path.read_bytes()).hexdigest()


def main():
    # Curated revamp originals are tracked with their acceptance hashes. They
    # intentionally bypass the workstation's older, untracked art-input folder.
    for manifest in sorted((ROOT / "ArtRevamp").glob("*/adoption.json")):
        for entry in json.loads(manifest.read_text(encoding="utf-8"))["assets"]:
            for field in ("source", "asset"):
                path = (ROOT / entry[field]).resolve()
                if not path.is_relative_to(ROOT) or not path.is_file():
                    print(f"채택한 아트 {field} 누락/경로 오류: {entry[field]}", file=sys.stderr)
                    return 1
                if hashlib.sha256(path.read_bytes()).hexdigest() != entry["sha256"]:
                    print(f"큐레이션 원본과 불일치: {entry[field]}", file=sys.stderr)
                    return 1
    if not ART_INPUT.is_dir():
        # art-input이 없는 머신(CI·다른 클론)에서는 검사할 원본이 없다.
        return 0

    mismatched = []
    for source in sorted(ART_INPUT.glob("*.png")):
        target = ASSETS / source.name
        if not target.exists():
            continue
        if digest(source) != digest(target):
            mismatched.append(source.name)

    if not mismatched:
        return 0

    print(
        "art-input 원본과 Assets 산출물이 다르다 "
        f"({len(mismatched)}장):", file=sys.stderr)
    for name in mismatched:
        print(f"  {name}", file=sys.stderr)
    print(
        "\n둘 중 하나다:\n"
        "  1. 새 그림을 Assets에만 넣었다 → **art-input에 넣어라.** 거기가 원본이고,\n"
        "     씬 재생성이 art-input을 Assets 위에 복사한다. Assets에만 넣으면\n"
        "     다음 재생성에서 조용히 되돌아간다.\n"
        "  2. art-input을 고친 뒤 씬을 재생성하지 않았다 → 재생성해라:\n"
        "     unity run . -- -executeMethod "
        "Shmup.EditorTools.BattleSceneBuilder.Build -logFile scene.log",
        file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
