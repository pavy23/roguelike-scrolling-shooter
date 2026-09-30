"""Read-only source/canvas/padding audit for the combat native candidates."""
import hashlib
import json
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
COMBAT = ROOT / "ArtRevamp/SFC-20260930/combat"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8"))


def rgba(path):
    with Image.open(path) as image:
        return image.convert("RGBA")


def main():
    controls = read(COMBAT / "controls/inputs.json")["sources"]
    control_rows = []
    for item in controls:
        source = rgba(ROOT / item["source"])
        padded = rgba(COMBAT / ("controls/" + item["id"] + "-input.png"))
        mask = rgba(COMBAT / ("controls/" + item["id"] + "-mask.png"))
        x0, bottom, w, h = item["spriteRectBottomLeft"]
        top = 16 - bottom - h
        assert source.size == (w, h) and padded.size == mask.size == (16, 16)
        for y in range(16):
            for x in range(16):
                inside = x0 <= x < x0 + w and top <= y < top + h
                assert mask.getpixel((x, y)) == ((255, 255, 255, 255) if inside else (0, 0, 0, 255))
                assert padded.getpixel((x, y)) == (source.getpixel((x - x0, y - top)) if inside else (0, 0, 0, 0))
        control_rows.append(dict(item, sourceSha256=sha(ROOT / item["source"]), unchangedSourcePixels=True))

    batches = []
    for directory in sorted((COMBAT / "raw").iterdir()):
        if not (directory / "outputs.json").exists():
            continue
        outputs, request = read(directory / "outputs.json"), read(directory / "request.json")
        frames = []
        source = mask = None
        if "mask_file" in request:
            source, mask = rgba(ROOT / request["source_file"]), rgba(ROOT / request["mask_file"])
            assert sha(ROOT / request["source_file"]) == request["source_sha256"]
            assert sha(ROOT / request["mask_file"]) == request["mask_sha256"]
        for output in outputs["outputs"]:
            path = directory / output["file"]
            image = rgba(path)
            assert sha(path) == output["sha256"], path
            assert image.size == (output["width"], output["height"]), path
            values = list(image.get_flattened_data() if hasattr(image, "get_flattened_data") else image.getdata())
            visible = [(x, y, image.getpixel((x, y))) for y in range(image.height) for x in range(image.width)
                       if image.getpixel((x, y))[3]]
            outside = None
            if source is not None:
                assert image.size == source.size == mask.size
                outside = sum(image.getpixel((x, y)) != source.getpixel((x, y))
                              and mask.getpixel((x, y))[:3] != (255, 255, 255)
                              for y in range(image.height) for x in range(image.width))
            details = {
                "file": path.relative_to(COMBAT).as_posix(), "sha256": sha(path), "canvas": list(image.size),
                "visiblePixels": len(visible), "visibleColors": len({p[:3] for p in values if p[3]}),
                "partialAlphaPixels": sum(0 < p[3] < 255 for p in values),
                "opaqueBoundsTopLeft": image.getbbox(),
                "visibleCentroidTopLeft": [round(sum(v[axis] + .5 for v in visible) / len(visible), 3)
                                          for axis in (0, 1)] if visible else None,
                "changedRgbaPixelsOutsideMask": outside, "pngPostprocessed": False,
            }
            if image.size == (144, 144):
                cells = []
                for index in range(9):
                    left, top = index % 3 * 48, index // 3 * 48
                    tile = image.crop((left, top, left + 48, top + 48))
                    points = [(x, y, tile.getpixel((x, y))) for y in range(48) for x in range(48)
                              if tile.getpixel((x, y))[3]]
                    cells.append({
                        "frame": index, "spriteRectBottomLeft": [left, 144 - top - 48, 48, 48],
                        "visiblePixels": len(points), "opaqueBoundsTopLeft": tile.getbbox(),
                        "visibleCentroidTopLeft": [round(sum(v[axis] + .5 for v in points) / len(points), 3)
                                                  for axis in (0, 1)] if points else None,
                        "visiblePixelsInFourPixelMargin": sum(x < 4 or x >= 44 or y < 4 or y >= 44
                                                             for x, y, _ in points),
                        "brightPixels": sum(min(p[:3]) > 175 for _, _, p in points),
                    })
                details["nineFrameGrid"] = cells
                details["nineFrameTimingSeconds"] = .3
            frames.append(details)
        batches.append({"name": directory.name, "requestedFrames": outputs.get("requested_frames"),
                        "returnedFrames": len(frames), "usage": outputs.get("completed_usage") or outputs.get("usage"),
                        "frames": frames})

    baseline = read(COMBAT.parent / "foreground-inventory.json")["assets"]
    count = 0
    for asset in baseline:
        assert sha(ROOT / asset["path"]) == asset["sha256"], asset["path"]
        count += 1
    for name in ("adoption.json", "banking/adoption.json"):
        for asset in read(COMBAT.parent / name)["assets"]:
            assert sha(ROOT / asset["asset"]) == sha(ROOT / asset["source"]) == asset["sha256"]
            count += 1
    report = {"kind": "Read-only candidate audit; not art acceptance or gameplay verification",
              "controls": control_rows, "batches": batches, "existingProductionPngUnchanged": count,
              "newCombatArtAdopted": False, "productionPngEdited": False}
    review = COMBAT / "review"
    review.mkdir(exist_ok=True)
    (review / "native-audit.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"batches": len(batches), "rawImages": sum(len(b["frames"]) for b in batches),
                      "existingProductionPngUnchanged": count, "controlsPreserveSource": len(control_rows)}))


if __name__ == "__main__":
    main()
