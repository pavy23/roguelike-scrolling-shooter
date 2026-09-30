"""Audit native banking candidates without changing any image or production asset."""
import hashlib
import json
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
BANK = ROOT / "ArtRevamp/SFC-20260930/banking"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    with Image.open(path) as image:
        return image.convert("RGBA")


def main():
    source_path = ROOT / "Assets/Art/Sprites/sfc_player_ship.png"
    rows = []
    usage = 0.0
    for directory in sorted((BANK / "raw").iterdir()):
        outputs = json.loads((directory / "outputs.json").read_text(encoding="utf-8"))
        request = json.loads((directory / "request.json").read_text(encoding="utf-8"))
        billed = outputs.get("usage") or outputs.get("completed_usage") or {}
        usage += billed.get("usd", 0.0)
        mask_path = ROOT / request["mask_file"] if "mask_file" in request else None
        mask = load(mask_path) if mask_path else None
        input_path = ROOT / request["source_file"] if mask else source_path
        source = load(input_path)
        if mask_path and (sha(mask_path) != request["mask_sha256"] or sha(input_path) != request["source_sha256"]):
            raise SystemExit("Source or mask provenance changed: " + directory.name)
        frames = []
        for record in outputs["outputs"]:
            path = directory / record["file"]
            image = load(path)
            if sha(path) != record["sha256"] or image.size != (48, 30):
                raise SystemExit("Native bytes or canvas changed: " + str(path))
            values = list(image.get_flattened_data() if hasattr(image, "get_flattened_data") else image.getdata())
            changes = [(x, y) for y in range(30) for x in range(48)
                if image.getpixel((x, y)) != source.getpixel((x, y))]
            visible_changes = [xy for xy in changes if source.getpixel(xy)[3] or image.getpixel(xy)[3]]
            frame = {"file": path.relative_to(BANK).as_posix(), "sha256": sha(path),
                "size": list(image.size), "visibleColors": len({p for p in values if p[3]}),
                "partialAlphaPixels": sum(0 < p[3] < 255 for p in values),
                "opaqueBoundsTopLeft": image.getbbox(), "changedRgbaPixels": len(changes),
                "changedVisiblePixels": len(visible_changes),
                "changedNosePixels": sum(x >= 40 for x, y in visible_changes),
                "changedPixelsOutsideMask": sum(mask.getpixel(xy)[:3] != (255, 255, 255) for xy in changes) if mask else None,
                "pngPostprocessed": False}
            if mask and frame["changedPixelsOutsideMask"]:
                raise SystemExit("Masked output changed protected pixels: " + str(path))
            if "engine" in directory.name:
                # The restricted-palette attempt returned one new warm cream highlight.
                # Record it explicitly; do not describe the provider's palette as exact.
                flame_colors = {(225, 67, 18), (255, 157, 28), (255, 235, 93), (255, 251, 154), (251, 239, 181), (242, 221, 192)}
                frame["allowedAdditionalWarmHighlightRgb"] = [242, 221, 192]
                frame["additionalWarmHighlightPixels"] = sum(image.getpixel(xy) == (242, 221, 192, 255) for xy in visible_changes)
                frame["changesOutsideFlameContract"] = sum(not (x < 9 and 6 <= y < 18
                    and all(p[3] == 0 or p[3] == 255 and p[:3] in flame_colors
                        for p in (source.getpixel((x, y)), image.getpixel((x, y))))) for x, y in visible_changes)
                frame["stationaryHullEngineUsable"] = bool(visible_changes) and frame["changesOutsideFlameContract"] == 0
            frames.append(frame)
        rows.append({"name": directory.name, "endpoint": request.get("endpoint"),
            "mask": mask_path.relative_to(BANK).as_posix() if mask else None,
            "usageUsd": billed.get("usd"), "requestedFrames": outputs.get("requested_frames"),
            "returnedFrames": len(frames), "frames": frames})

    proposed_batches = {"masked-up-fold", "masked-down-fold", "bank-up-engine-warm", "bank-down-engine-warm"}
    proposed = [frame for batch in rows if batch["name"] in proposed_batches for frame in batch["frames"]]
    if len(proposed) != 4:
        raise SystemExit("Expected four review candidates.")
    for frame in proposed:
        if frame["partialAlphaPixels"] or frame["visibleColors"] > 48 or frame["changedPixelsOutsideMask"] != 0:
            raise SystemExit("Proposed frame failed native contract: " + frame["file"])
        if "engine" in frame["file"] and not frame["stationaryHullEngineUsable"]:
            raise SystemExit("Proposed engine failed flame contract: " + frame["file"])

    inventory = json.loads((BANK.parent / "foreground-inventory.json").read_text(encoding="utf-8"))
    for asset in inventory["assets"]:
        if sha(ROOT / asset["path"]) != asset["sha256"]:
            raise SystemExit("Existing production art changed: " + asset["path"])
    adoption = json.loads((BANK.parent / "adoption.json").read_text(encoding="utf-8"))
    for asset in adoption["assets"]:
        if any(sha(ROOT / asset[key]) != asset["sha256"] for key in ("source", "asset")):
            raise SystemExit("Approved production art changed: " + asset["asset"])
    report = {"kind": "Read-only native PNG and protected-pixel audit; not art acceptance or gameplay",
        "source": {"file": source_path.relative_to(ROOT).as_posix(), "sha256": sha(source_path)},
        "existingProductionPngUnchanged": len(inventory["assets"]) + len(adoption["assets"]),
        "bankingApiReportedUsageUsd": usage, "batches": rows, "proposedForHumanCuration": proposed,
        "newBankArtAdopted": False, "sceneBankClipsBound": False}
    review = BANK / "review"
    review.mkdir(parents=True, exist_ok=True)
    (review / "native-audit.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"batches": len(rows), "nativeFrames": sum(len(row["frames"]) for row in rows),
        "existingProductionPngUnchanged": report["existingProductionPngUnchanged"],
        "bankingApiReportedUsageUsd": usage}))


if __name__ == "__main__":
    main()
