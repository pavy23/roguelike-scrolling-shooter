#!/usr/bin/env python3
"""ArtGen — AI 아트 생성 파이프라인 (ART-DIRECTION.md v2, ROADMAP M1).

생성(gpt-image-2 / PixelLab) → 후처리(트림·최근접 다운스케일·알파 경화·팔레트 양자화)
→ art-input/ 배치까지. 프롬프트·시드·파라미터는 커밋해 재현 가능하게 유지한다 (AGENTS.md).

사용 예:
  python artgen.py openai  --prompt "..." --out out/raw/ship.png [--n 2] [--model gpt-image-2]
  python artgen.py pixen   --prompt "..." --width 24 --height 24 --seed 7 --out out/raw/zako.png
  python artgen.py native  --prompt-file prompt.txt --width 48 --height 30 --seed 7 \
                           --reference concept.png --out-dir out/raw/ship [--resume]
  python artgen.py animate --first out/raw/boom0.png --action "exploding fireball" \
                           --frames 8 --out-dir out/raw/expl [--last last.png] [--resume]
  python artgen.py post    --src out/raw/ship.png --target 48x30 --colors 48 \
                           --out ../../../art-input/player_ship.png
  python artgen.py sheet   --src-dir out/raw/expl --out out/final/fx_explosion_m.png

환경변수: OPENAI_API_KEY, PIXELLAB_API_KEY
"""
import argparse
import base64
import hashlib
import json
import os
import sys
import time
import urllib.request
from pathlib import Path

from PIL import Image

OPENAI_URL = "https://api.openai.com/v1/images/generations"
PIXELLAB_BASE = "https://api.pixellab.ai/v2"
# xAI 이미지 (Grok). 크기 지정을 받지 않아 정사각 고정으로 나온다 — post에서 맞춘다.
XAI_URL = "https://api.x.ai/v1/images/generations"
# Google Gemini 이미지 ("Nano Banana"). 생성과 **편집**이 같은 엔드포인트다.
GEMINI_BASE = "https://generativelanguage.googleapis.com/v1beta/models"


def _post_json(url: str, payload: dict, token: str, timeout: int = 300) -> dict:
    req = urllib.request.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json", "Authorization": f"Bearer {token}"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="replace")
        raise SystemExit(f"HTTP {e.code} from {url}\n{body}") from e


def _get_json(url: str, token: str, timeout: int = 60) -> dict:
    req = urllib.request.Request(url, headers={"Authorization": f"Bearer {token}"})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def _wait_background_job(job_id: str, token: str, max_wait: int = 600) -> dict:
    """PixelLab 비동기 작업(/background-jobs/{id})을 완료까지 폴링한다."""
    deadline = time.monotonic() + max_wait
    while True:
        result = _get_json(f"{PIXELLAB_BASE}/background-jobs/{job_id}", token)
        status = str(result.get("status", "")).lower()
        if status in ("completed", "complete", "succeeded", "done", "finished"):
            return result
        if status in ("failed", "error", "cancelled"):
            raise SystemExit(f"작업 실패: {json.dumps(result)[:2000]}")
        if time.monotonic() > deadline:
            raise SystemExit(f"작업 타임아웃({max_wait}s): job_id={job_id}")
        print(f"  job {job_id[:8]}… {status or 'processing'}", flush=True)
        time.sleep(8)


def _require_env(name: str) -> str:
    value = os.environ.get(name)
    if not value:
        raise SystemExit(f"환경변수 {name} 가 없다.")
    return value


def _find_b64_images(node) -> list:
    """응답 JSON 어디에 있든 base64 이미지 문자열을 수집한다 (스키마 변화에 관대하게)."""
    found = []
    if isinstance(node, dict):
        for key, value in node.items():
            # Gemini는 inline_data.data / inlineData.data 로 준다.
            if key in ("b64_json", "base64", "image_b64", "data") and isinstance(value, str):
                found.append(value)
            else:
                found.extend(_find_b64_images(value))
    elif isinstance(node, list):
        for item in node:
            found.extend(_find_b64_images(item))
    return found


def _save_b64_png(b64: str, path: Path) -> None:
    if "," in b64 and b64.split(",", 1)[0].startswith("data:"):
        b64 = b64.split(",", 1)[1]
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(base64.b64decode(b64))
    print(f"saved: {path}")


def _b64_of(path: Path) -> str:
    return base64.b64encode(path.read_bytes()).decode("ascii")


def cmd_openai(args) -> None:
    token = _require_env("OPENAI_API_KEY")
    payload = {
        "model": args.model,
        "prompt": args.prompt,
        "size": args.size,
        "background": "opaque" if getattr(args, "opaque", False) else "transparent",
        "output_format": "png",
        "n": args.n,
    }
    if args.quality:
        payload["quality"] = args.quality
    result = _post_json(OPENAI_URL, payload, token, timeout=600)
    images = _find_b64_images(result)
    if not images:
        raise SystemExit(f"이미지가 응답에 없다:\n{json.dumps(result)[:2000]}")
    out = Path(args.out)
    for i, b64 in enumerate(images):
        target = out if len(images) == 1 else out.with_name(f"{out.stem}_{i}{out.suffix}")
        _save_b64_png(b64, target)


def cmd_xai(args) -> None:
    """xAI(Grok) 이미지 생성.

    크기·투명 배경 파라미터가 없다. 정사각으로 나오므로 post에서 규격을 잡고,
    투명이 필요하면 --cutout으로 배경을 뚫어야 한다.
    """
    token = _require_env("XAI_API_KEY")
    payload = {
        "model": args.model,
        "prompt": args.prompt,
        "n": args.n,
        "response_format": "b64_json",
    }
    result = _post_json(XAI_URL, payload, token, timeout=600)
    images = _find_b64_images(result)
    if not images:
        raise SystemExit(f"이미지가 응답에 없다:\n{json.dumps(result)[:2000]}")
    out = Path(args.out)
    for i, b64 in enumerate(images):
        target = out if len(images) == 1 else out.with_name(f"{out.stem}_{i}{out.suffix}")
        _save_b64_png(b64, target)


def cmd_gemini(args) -> None:
    """Google Gemini 이미지 ("Nano Banana"). --ref 를 주면 **편집**이 된다.

    편집이 되는 것이 이 모델의 요점이다. "이 스프라이트를 그대로 두고 부서진
    버전으로" 같은 요구는 새로 그리는 모델로는 화풍이 어긋난다 — 실제로 전함
    잔해가 그랬다.
    """
    key = _require_env("GEMINI_API_KEY")
    parts = [{"text": args.prompt}]
    for ref in (args.ref or []):
        parts.append({
            "inline_data": {
                "mime_type": "image/png",
                "data": _b64_of(Path(ref)),
            }
        })
    url = f"{GEMINI_BASE}/{args.model}:generateContent?key={key}"
    req = urllib.request.Request(
        url,
        data=json.dumps({"contents": [{"parts": parts}]}).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=600) as resp:
            result = json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        raise SystemExit(
            f"HTTP {e.code} from {url.split('?')[0]}\n"
            f"{e.read().decode('utf-8', errors='replace')}") from e
    images = _find_b64_images(result)
    if not images:
        raise SystemExit(f"이미지가 응답에 없다:\n{json.dumps(result)[:2000]}")
    _save_b64_png(images[0], Path(args.out))


def cmd_pixen(args) -> None:
    token = _require_env("PIXELLAB_API_KEY")
    payload = {
        "description": args.prompt,
        "image_size": {"width": args.width, "height": args.height},
        "no_background": True,
    }
    if args.seed is not None:
        payload["seed"] = args.seed
    for name in ("outline", "detail", "view", "direction"):
        value = getattr(args, name, None)
        if value is not None:
            payload[name] = value
    try:
        result = _post_json(f"{PIXELLAB_BASE}/create-image-pixen", payload, token)
    except SystemExit:
        # pixen(v2)이 500을 내는 기간이 있다 (2026-07-31 확인: 크레딧·인증은
        # 정상인데 pixen만 실패, v1 pixflux는 성공). 품질 우선순위는 pixen이지만
        # 죽어 있을 때는 pixflux로 폴백해 파이프라인을 세우지 않는다.
        print("pixen 실패 — v1 pixflux로 폴백")
        result = _post_json(
            "https://api.pixellab.ai/v1/generate-image-pixflux", payload, token)
    images = _find_b64_images(result)
    if not images:
        raise SystemExit(f"이미지가 응답에 없다:\n{json.dumps(result)[:2000]}")
    _save_b64_png(images[0], Path(args.out))


def cmd_native(args) -> None:
    """Generate new native-size candidates with PixelLab Pro; never resize output.

    A concept reference supplies design identity, not a pre-rendered sprite to
    downscale. Keep requests, jobs and exact original results for art review.
    """
    out_dir = Path(args.out_dir)
    request_path = out_dir / "request.json"
    job_path = out_dir / "job.json"
    if out_dir.exists() and any(out_dir.iterdir()) and not args.resume:
        raise SystemExit(f"Output exists; use --resume for the same job: {out_dir}")
    prompt = Path(args.prompt_file).read_text(encoding="utf-8-sig").strip()
    if not 1 <= len(prompt) <= 2000:
        raise SystemExit("Native generation prompt must contain 1-2000 characters.")
    if not (16 <= args.width <= 792 and 16 <= args.height <= 688):
        raise SystemExit("Native generation dimensions exceed the API limits.")
    payload = {
        "description": prompt,
        "image_size": {"width": args.width, "height": args.height},
        "seed": args.seed,
        "no_background": True,
    }
    record = dict(payload)
    if args.reference:
        ref_path = Path(args.reference)
        with Image.open(ref_path) as reference:
            ref_size = {"width": reference.width, "height": reference.height}
        payload["reference_images"] = [{
            "image": {"type": "base64", "base64": _b64_of(ref_path)},
            "size": ref_size,
            "usage_description": "Design and palette reference for the new sprite; redraw at native resolution, do not inherit the enlarged pixel grid.",
        }]
        record["reference_file"] = ref_path.as_posix()
        record["reference_size"] = ref_size
        record["reference_sha256"] = hashlib.sha256(ref_path.read_bytes()).hexdigest()
    record["endpoint"] = "/generate-image-v2"
    out_dir.mkdir(parents=True, exist_ok=True)
    if args.resume:
        if not request_path.exists() or not job_path.exists():
            raise SystemExit("Cannot resume without the saved request and job.")
        if json.loads(request_path.read_text(encoding="utf-8")) != record:
            raise SystemExit("Resume arguments differ from the original request.")
        accepted = json.loads(job_path.read_text(encoding="utf-8"))
    else:
        request_path.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        accepted = _post_json(f"{PIXELLAB_BASE}/generate-image-v2", payload,
                              _require_env("PIXELLAB_API_KEY"))
        job_path.write_text(json.dumps(accepted, indent=2) + "\n", encoding="utf-8")
    job_id = accepted.get("background_job_id")
    if not job_id:
        raise SystemExit("API did not return a generation job; inspect saved job.json.")
    print(f"accepted: {job_id}; usage={accepted.get('usage')}", flush=True)
    result = _wait_background_job(job_id, _require_env("PIXELLAB_API_KEY"))
    content = result.get("last_response", result)
    images = _find_b64_images(content)
    if not images:
        # Keep diagnostics local without echoing an image payload into the terminal.
        (out_dir / "response.json").write_text(json.dumps(result), encoding="utf-8")
        raise SystemExit("Completed job had no base64 image; inspect response.json.")
    outputs = []
    for i, b64 in enumerate(images):
        path = out_dir / f"candidate_{i:02d}.png"
        _save_b64_png(b64, path)
        with Image.open(path) as generated:
            outputs.append({"file": path.name, "width": generated.width,
                            "height": generated.height,
                            "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
    (out_dir / "outputs.json").write_text(json.dumps({
        "job_id": job_id, "usage": accepted.get("usage"),
        "requested_size": payload["image_size"], "outputs": outputs,
        "status": "candidate_pending_native_size_and_art_review",
        "postprocessing": "none; original generated bytes preserved",
    }, indent=2) + "\n", encoding="utf-8")
    print(f"Saved {len(outputs)} unmodified native candidates.")


def cmd_animate(args) -> None:
    """Keep paid jobs resumable and never mistake echoed inputs for frames."""
    out_dir = Path(args.out_dir)
    request_path, job_path = out_dir / "request.json", out_dir / "job.json"
    resume = getattr(args, "resume", False)
    if out_dir.exists() and any(out_dir.iterdir()) and not resume:
        raise SystemExit(f"Output exists; use --resume for the same job: {out_dir}")
    if args.frames not in range(4, 17, 2):
        raise SystemExit("Animation frame count must be even and between 4 and 16.")
    if not 1 <= len(args.action) <= 1000:
        raise SystemExit("Animation action must contain 1-1000 characters.")
    payload = {
        "action": args.action,
        "frame_count": args.frames,
        "no_background": True,
    }
    record = dict(payload)
    first_size = None
    for role in ("first", "last"):
        value = getattr(args, role, None)
        if not value:
            continue
        path = Path(value)
        with Image.open(path) as source:
            size = source.size
        if max(size) > 256 or size[0] * size[1] * args.frames > 524288:
            raise SystemExit("Animation input exceeds the API pixel limits.")
        if first_size is None:
            first_size = size
        elif size != first_size:
            raise SystemExit("Animation first and last frames must use the same canvas.")
        payload[f"{role}_frame"] = {"type": "base64", "base64": _b64_of(path)}
        record[f"{role}_frame"] = {"file": path.as_posix(), "size": list(size),
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
    if args.seed is not None:
        payload["seed"] = args.seed
        record["seed"] = args.seed
    if getattr(args, "drift_threshold", None) is not None:
        if args.drift_threshold < 0:
            raise SystemExit("Drift threshold must be non-negative.")
        payload["drift_threshold"] = args.drift_threshold
        record["drift_threshold"] = args.drift_threshold
    record["endpoint"] = "/animate-with-text-v3"
    out_dir.mkdir(parents=True, exist_ok=True)
    if resume:
        if not request_path.exists() or not job_path.exists():
            raise SystemExit("Cannot resume without the saved request and job.")
        if json.loads(request_path.read_text(encoding="utf-8")) != record:
            raise SystemExit("Resume arguments differ from the original request.")
        accepted = json.loads(job_path.read_text(encoding="utf-8"))
    else:
        request_path.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        accepted = _post_json(f"{PIXELLAB_BASE}/animate-with-text-v3", payload,
                              _require_env("PIXELLAB_API_KEY"))
        job_path.write_text(json.dumps(accepted, indent=2) + "\n", encoding="utf-8")
    job_id = accepted.get("background_job_id")
    if not job_id:
        raise SystemExit("API did not return an animation job; inspect saved job.json.")
    print(f"accepted: {job_id}; usage={accepted.get('usage')}", flush=True)
    result = _wait_background_job(job_id, _require_env("PIXELLAB_API_KEY"))
    content = result.get("last_response") or {}
    # Official v3 output contract is last_response.images. Other fields can
    # include source images, thumbnails or echoes; they are not animation frames.
    images = _find_b64_images(content.get("images", []))
    if not images:
        raise SystemExit("Completed job has no last_response.images; use --resume to inspect this job without paying again.")
    outputs = []
    for i, b64 in enumerate(images):
        path = out_dir / f"frame_{i:02d}.png"
        _save_b64_png(b64, path)
        with Image.open(path) as frame:
            outputs.append({"file": path.name, "width": frame.width, "height": frame.height,
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
    (out_dir / "outputs.json").write_text(json.dumps({
        "job_id": job_id, "accepted_usage": accepted.get("usage"),
        "completed_usage": result.get("usage"), "requested_frames": args.frames,
        "returned_frames": len(outputs), "frame_count_matches": len(outputs) == args.frames,
        "outputs": outputs, "status": "candidate_pending_frame_continuity_and_art_review",
        "postprocessing": "none; original generated bytes preserved",
    }, indent=2) + "\n", encoding="utf-8")
    print(f"Saved {len(outputs)} output frames; requested {args.frames}. Review continuity before use.")


def cmd_pixelart(args) -> None:
    """고해상 원본(gpt-image 등)을 PixelLab image-to-pixelart로 네이티브 픽셀 해상도로 변환."""
    token = _require_env("PIXELLAB_API_KEY")
    src = Image.open(args.src).convert("RGBA")
    if max(src.size) > 256:  # 입력 상한 대비 축소
        scale = 256 / max(src.size)
        src = src.resize((max(1, round(src.width * scale)), max(1, round(src.height * scale))), Image.LANCZOS)
    buffer = Path(args.src).with_suffix(".tmp256.png")
    src.save(buffer)
    width, height = (int(v) for v in args.target.lower().split("x"))
    payload = {
        "image": {"type": "base64", "base64": _b64_of(buffer)},
        "image_size": {"width": src.width, "height": src.height},
        "output_size": {"width": width, "height": height},
    }
    if args.seed is not None:
        payload["seed"] = args.seed
    result = _post_json(f"{PIXELLAB_BASE}/image-to-pixelart", payload, token)
    buffer.unlink(missing_ok=True)
    if result.get("background_job_id"):
        result = _wait_background_job(result["background_job_id"], token)
    images = _find_b64_images(result)
    if not images:
        raise SystemExit(f"이미지가 응답에 없다:\n{json.dumps(result)[:2000]}")
    _save_b64_png(images[0], Path(args.out))


def _quantize(img: Image.Image, colors: int) -> Image.Image:
    """알파를 이진화(하드 픽셀 경계)하고 불투명 픽셀만 지정 색수로 양자화한다."""
    img = img.convert("RGBA")
    alpha = img.getchannel("A").point(lambda a: 255 if a >= 128 else 0)
    rgb = img.convert("RGB").quantize(colors=colors, method=Image.MEDIANCUT, dither=Image.Dither.NONE)
    result = rgb.convert("RGB").convert("RGBA")
    result.putalpha(alpha)
    return result


def _cutout_background(img: "Image.Image", tolerance: int) -> "Image.Image":
    """가장자리에서 번져 들어가며 배경색을 지운다 (플러드 필).

    PixelLab pixen은 no_background=True를 줘도 불투명 배경을 실어 보낼 때가 있다
    (2026-08-03 확인). 색 하나만 골라 지우면 그 색이 캐릭터 안에도 있을 때 구멍이
    뚫리므로, **가장자리에 닿아 있는 영역만** 지운다 — 안쪽에 갇힌 같은 색은 남는다.
    """
    img = img.convert("RGBA")
    w, h = img.size
    px = img.load()
    seed = px[0, 0][:3]

    def near(c):
        return (abs(c[0] - seed[0]) <= tolerance
                and abs(c[1] - seed[1]) <= tolerance
                and abs(c[2] - seed[2]) <= tolerance)

    stack = [(x, 0) for x in range(w)] + [(x, h - 1) for x in range(w)]
    stack += [(0, y) for y in range(h)] + [(w - 1, y) for y in range(h)]
    seen = set()
    while stack:
        x, y = stack.pop()
        if (x, y) in seen or not (0 <= x < w and 0 <= y < h):
            continue
        seen.add((x, y))
        c = px[x, y]
        if c[3] == 0 or not near(c):
            continue
        px[x, y] = (c[0], c[1], c[2], 0)
        stack += [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)]
    return img


def cmd_post(args) -> None:
    src = Image.open(args.src).convert("RGBA")
    if getattr(args, "cutout", 0):
        src = _cutout_background(src, args.cutout)

    bbox = src.getchannel("A").getbbox()
    if bbox and not args.no_trim:
        src = src.crop(bbox)

    width, height = (int(v) for v in args.target.lower().split("x"))
    if args.stretch:
        # 종횡비를 버리고 **정확히** 목표 크기로 늘린다.
        #
        # 배경 레이어(원경·전경)는 화면을 빈틈없이 덮어야 하고 가로로 타일링된다.
        # 비율을 지키면 좌우에 투명 여백이 남아 (a) 타일마다 세로 이음매가 보이고
        # (b) 화면을 못 채운다 — 사람이 "뒤를 꽉채우는게 아니라 가운데만 조그맣게
        # 들어가면 안됨"이라고 보고한 것이 이것이다. cover로 채우면 이번엔 위아래
        # 띠(전경의 핵심)가 잘려 나간다. 세로로 약간 눌리는 쪽이 낫다.
        canvas = src.resize((width, height), Image.NEAREST)
    else:
        fit = max if args.cover else min      # cover: 꽉 채우고 중앙 크롭
        scale = fit(width / src.width, height / src.height)
        new_size = (max(1, round(src.width * scale)), max(1, round(src.height * scale)))
        resized = src.resize(new_size, Image.NEAREST)

        canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
        canvas.paste(resized, ((width - new_size[0]) // 2, (height - new_size[1]) // 2))

    final = _quantize(canvas, args.colors)
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    final.save(out)
    used = len({p for p in final.getdata() if p[3] > 0})
    print(f"saved: {out} ({width}x{height}, opaque colors={used})")


def cmd_tile(args) -> None:
    """가로 심리스화: 원본과 반폭 롤 버전을 삼각 가중치로 크로스페이드.
    x=0/x=w-1에서 롤 버전이 선택되므로 래핑 경계가 연속이 된다 (패럴랙스 타일용)."""
    import numpy as np

    img = np.asarray(Image.open(args.src).convert("RGBA"), dtype=np.float32)
    width = img.shape[1]
    rolled = np.roll(img, width // 2, axis=1)
    x = np.arange(width, dtype=np.float32)
    weight = 1.0 - np.abs(x - width / 2) / (width / 2)      # 가장자리 0, 중앙 1
    weight = weight[None, :, None]
    blended = img * weight + rolled * (1.0 - weight)
    result = Image.fromarray(blended.clip(0, 255).astype("uint8"), "RGBA")
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    result.save(out)
    print(f"saved: {out} (seamless-x)")


def cmd_sheet(args) -> None:
    frames = sorted(Path(args.src_dir).glob("*.png"))
    if not frames:
        raise SystemExit(f"{args.src_dir} 에 png가 없다.")
    images = [Image.open(f).convert("RGBA") for f in frames]
    cell_w = max(i.width for i in images)
    cell_h = max(i.height for i in images)
    sheet = Image.new("RGBA", (cell_w * len(images), cell_h), (0, 0, 0, 0))
    for i, img in enumerate(images):
        sheet.paste(img, (i * cell_w + (cell_w - img.width) // 2, (cell_h - img.height) // 2))
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    if args.colors:
        sheet = _quantize(sheet, args.colors)
    sheet.save(out)
    print(f"saved: {out} ({len(images)} frames, cell {cell_w}x{cell_h})")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("openai", help="gpt-image-2로 대형/키프레임 생성 (투명 배경)")
    p.add_argument("--prompt", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--model", default="gpt-image-2")
    p.add_argument("--size", default="1024x1024")
    p.add_argument("--quality", default="high")
    p.add_argument("--n", type=int, default=1)
    # 스프라이트는 투명 배경이 맞지만 **원경 배경은 화면을 꽉 채워야 한다**.
    # 투명을 강제하면 모델이 하늘/바닥을 비워 버려서, 가로 타일링 크로스페이드가
    # 그 빈 곳을 좌우 대칭 유령으로 만든다 (abyss/brood 1차 시도에서 실제로 그랬다).
    p.add_argument("--opaque", action="store_true",
                   help="투명 배경을 끈다 — 패럴랙스 원경처럼 꽉 찬 장면용")
    p.set_defaults(func=cmd_openai)

    p = sub.add_parser("xai", help="xAI(Grok) 이미지 — 크기·투명 지정 없음")
    p.add_argument("--prompt", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--model", default="grok-2-image-1212")
    p.add_argument("--n", type=int, default=1)
    p.set_defaults(func=cmd_xai)

    p = sub.add_parser("gemini", help="Gemini 이미지(Nano Banana) — --ref로 편집")
    p.add_argument("--prompt", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--model", default="gemini-2.5-flash-image")
    p.add_argument("--ref", action="append",
                   help="참조/편집 대상 png (여러 번 줄 수 있다)")
    p.set_defaults(func=cmd_gemini)

    p = sub.add_parser("pixen", help="PixelLab pixen으로 네이티브 저해상도 스프라이트 생성")
    p.add_argument("--prompt", required=True)
    p.add_argument("--width", type=int, required=True)
    p.add_argument("--height", type=int, required=True)
    p.add_argument("--seed", type=int, default=None)
    p.add_argument("--out", required=True)
    p.add_argument("--outline", choices=("single color black outline", "single color outline", "selective outline", "lineless"))
    p.add_argument("--detail", choices=("low detail", "medium detail", "highly detailed"))
    p.add_argument("--view", choices=("side", "low top-down", "high top-down"))
    p.add_argument("--direction", choices=("north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west"))
    p.set_defaults(func=cmd_pixen)

    p = sub.add_parser("native", help="PixelLab Pro native-size generation with saved provenance")
    p.add_argument("--prompt-file", required=True)
    p.add_argument("--width", type=int, required=True)
    p.add_argument("--height", type=int, required=True)
    p.add_argument("--seed", type=int, required=True)
    p.add_argument("--reference", help="Design reference PNG; not a downscale source")
    p.add_argument("--out-dir", required=True)
    p.add_argument("--resume", action="store_true", help="Poll the saved job without purchasing another generation")
    p.set_defaults(func=cmd_native)

    p = sub.add_parser("animate", help="PixelLab animate-with-text-v3로 프레임 생성")
    p.add_argument("--first", required=True, help="첫 프레임 PNG (max 256x256)")
    p.add_argument("--action", required=True)
    p.add_argument("--frames", type=int, default=8, help="4-16 짝수")
    p.add_argument("--seed", type=int, default=None)
    p.add_argument("--out-dir", required=True)
    p.add_argument("--last", help="Optional last frame to keep a loop seam stable")
    p.add_argument("--drift-threshold", type=float, default=None, help="API color de-flicker threshold")
    p.add_argument("--resume", action="store_true", help="Poll the saved job without purchasing another animation")
    p.set_defaults(func=cmd_animate)

    p = sub.add_parser("pixelart", help="고해상 원본을 PixelLab image-to-pixelart로 네이티브 픽셀화")
    p.add_argument("--src", required=True)
    p.add_argument("--target", required=True, help="예: 48x30")
    p.add_argument("--seed", type=int, default=None)
    p.add_argument("--out", required=True)
    p.set_defaults(func=cmd_pixelart)

    p = sub.add_parser("post", help="트림→최근접 다운스케일→알파 경화→팔레트 양자화")
    p.add_argument("--src", required=True)
    p.add_argument("--target", required=True, help="예: 48x30")
    p.add_argument("--colors", type=int, default=48)
    p.add_argument("--no-trim", action="store_true")
    p.add_argument("--cutout", type=int, default=0,
                   help="가장자리 플러드 필로 배경 제거 (색 허용오차, 0이면 비활성)")
    p.add_argument("--cover", action="store_true", help="배경용: 꽉 채우고 중앙 크롭")
    p.add_argument("--stretch", action="store_true",
                   help="배경 레이어용: 종횡비 무시하고 정확히 target 크기로 늘린다")
    p.add_argument("--out", required=True)
    p.set_defaults(func=cmd_post)

    p = sub.add_parser("tile", help="가로 심리스화 (패럴랙스 타일용 크로스페이드)")
    p.add_argument("--src", required=True)
    p.add_argument("--out", required=True)
    p.set_defaults(func=cmd_tile)

    p = sub.add_parser("sheet", help="프레임 폴더 → 가로 스트립 시트")
    p.add_argument("--src-dir", required=True)
    p.add_argument("--colors", type=int, default=None)
    p.add_argument("--out", required=True)
    p.set_defaults(func=cmd_sheet)

    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
