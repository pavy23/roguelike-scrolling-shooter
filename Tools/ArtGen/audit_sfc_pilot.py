"""Read native PNGs and record pilot quality checks. Never modify image bytes."""
import hashlib
import json
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
PILOT = ROOT / "ArtRevamp/SFC-20260930/pilot"
REVIEW = PILOT / "review"
PROPOSED = [
    ("starter", "raw/starter/candidate_05.png", (48, 30)),
    ("drone", "raw/drone.png", (24, 24)),
    ("fast", "raw/fast-v2.png", (24, 24)),
    ("turret", "raw/turret-v2.png", (24, 24)),
]
FLAME_COLORS = {(225, 67, 18), (255, 157, 28), (255, 235, 93),
                (255, 251, 154), (251, 239, 181)}


def pixels(path):
    with Image.open(path) as image:
        return image.convert("RGBA")


def audit_png(path):
    image = pixels(path)
    # Pillow 14 removes getdata(); retain compatibility with older workstations.
    values = list(image.get_flattened_data() if hasattr(image, "get_flattened_data") else image.getdata())
    colors = {p for p in values if p[3]}
    return {"file": path.relative_to(PILOT).as_posix(), "width": image.width,
        "height": image.height, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "visibleColors": len(colors), "partialAlphaPixels": sum(0 < p[3] < 255 for p in values),
        "opaqueBoundsTopLeft": image.getbbox(), "originalGeneratedBytes": True}


def changed_pixels(before, after):
    if before.size != after.size:
        raise ValueError("Animation frame canvas changed")
    return [(x, y, before.getpixel((x, y)), after.getpixel((x, y)))
        for y in range(before.height) for x in range(before.width)
        if before.getpixel((x, y)) != after.getpixel((x, y))
        and (before.getpixel((x, y))[3] or after.getpixel((x, y))[3])]


def engine_change_allowed(change):
    x, y, a, b = change
    return (x < 9 and 6 <= y < 18
        and all(p[3] == 0 or p[3] == 255 and p[:3] in FLAME_COLORS for p in (a, b)))


def main():
    REVIEW.mkdir(parents=True, exist_ok=True)
    inventory = json.loads((PILOT.parent / "foreground-inventory.json").read_text(encoding="utf-8"))
    modified = [a["path"] for a in inventory["assets"]
        if hashlib.sha256((ROOT / a["path"]).read_bytes()).hexdigest() != a["sha256"]]
    if modified:
        raise SystemExit("Production art unexpectedly changed: " + ", ".join(modified))

    candidates = []
    for identity, relative, size in PROPOSED:
        row = audit_png(PILOT / relative)
        if (row["width"], row["height"]) != size or row["partialAlphaPixels"] or row["visibleColors"] > 48:
            raise SystemExit("Proposed sprite failed native size / alpha / palette: " + relative)
        row.update(id=identity, curation="proposed_for_user_review", engineReady=False)
        candidates.append(row)

    base = pixels(PILOT / PROPOSED[0][1])
    engine = []
    for path in sorted((PILOT / "raw/starter-engine").glob("frame_*.png")):
        changes = changed_pixels(base, pixels(path))
        outside = sum(not engine_change_allowed(c) for c in changes)
        row = audit_png(path)
        row.update(changedPixels=len(changes), changesOutsideFlameContract=outside,
            usableWithStationaryHull=bool(changes) and outside == 0)
        engine.append(row)
    selected = next(row for row in engine if row["file"].endswith("frame_01.png"))
    if not selected["usableWithStationaryHull"] or selected["partialAlphaPixels"] or selected["visibleColors"] > 48:
        raise SystemExit("Selected engine pose failed the stationary hull contract")

    drone_base = pixels(PILOT / "raw/drone.png")
    drone = []
    for path in sorted((PILOT / "raw/drone-sensor").glob("frame_*.png")):
        changes = changed_pixels(drone_base, pixels(path))
        row = audit_png(path)
        outside = sum(not (2 <= x < 9 and 6 <= y < 16) for x, y, _, _ in changes)
        row.update(changedPixels=len(changes), changesOutsideSensorArea=outside,
            usableSensorLoopPose=bool(changes) and outside == 0)
        drone.append(row)

    result = {
        "kind": "Read-only native PNG audit; neither imports nor gameplay testing",
        "productionArtUnchanged": {"checked": len(inventory["assets"]), "modified": modified},
        "proposedStaticSprites": candidates,
        "engineLoop": {"frames": [PROPOSED[0][1], selected["file"]], "fps": 10,
            "periodSeconds": 0.2, "changedExhaustPixels": selected["changedPixels"],
            "hullAndMuzzlePixelDifferences": 0, "candidateFrames": engine,
            "firstJobIdPrefix": "dd9e97b7", "firstJobIdRecordedInFull": False,
            "note": "Initial legacy client saved 7 image items for a 6-frame request; no order/count assumption is used. Only the independently checked pose is proposed."},
        "droneAnimation": {"candidateFrames": drone,
            "decision": "Keep original static drone; moving output frames drift vertically and are rejected."},
        "experimentalExplosion": audit_png(PILOT / "raw/explosion-v2.png"),
        "productionArtAdopted": False,
        "gameplayAndMuzzleAttachmentValidated": False,
    }
    (REVIEW / "native-audit.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"originalPngUnchanged": len(inventory["assets"]),
        "proposedSprites": len(candidates), "engineExhaustPixelsChanged": selected["changedPixels"],
        "engineHullDifferences": 0, "droneMovingFramesRejected": sum(row["changesOutsideSensorArea"] > 0 for row in drone)}))


if __name__ == "__main__":
    main()
