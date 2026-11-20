"""Write small synthetic top-bottom stereo ERP clips for checking the Unity study flow on the desktop.

Every stimulus file named in the plans (every PAw correction-radius variant) gets a 3 s, 25 fps, 1024x1024 H.264 clip (left eye on top):
a latitude/longitude grid, "FRONT"/"LEFT"/"RIGHT"/"BACK" labels at 0/-90/+90/180 deg longitude, the clip id
and condition, the eye letter, and a frame counter. A box in front (0 deg) carries a small crossed disparity,
so it should appear nearer than the grid in the headset. These clips are test material only.

Usage:
    python tools/make_test_clips.py --plans StudyTools/plans --out Stimuli --ffmpeg D:/WorkSpace/ffmpeg/bin/ffmpeg.exe
"""
from __future__ import annotations

import argparse
import json
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

W, H_EYE, FPS, SECONDS = 1024, 512, 25, 3


def font(size: int):
    for name in ("arial.ttf", "C:/Windows/Fonts/arial.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def eye_image(label: str, eye: str, disparity_px: int) -> Image.Image:
    img = Image.new("RGB", (W, H_EYE), (70, 75, 85) if eye == "L" else (80, 72, 70))
    d = ImageDraw.Draw(img)
    for lon in range(-180, 181, 30):
        x = int((lon + 180) / 360 * (W - 1))
        d.line([(x, 0), (x, H_EYE)], fill=(130, 130, 130) if lon % 90 else (220, 220, 220), width=1 if lon % 90 else 2)
    for lat in range(-60, 61, 30):
        y = int((90 - lat) / 180 * (H_EYE - 1))
        d.line([(0, y), (W, y)], fill=(130, 130, 130) if lat else (220, 220, 220), width=1 if lat else 2)
    f_big, f_mid = font(28), font(18)
    for lon, name in ((0, "FRONT"), (-90, "LEFT"), (90, "RIGHT"), (180, "BACK"), (-180, "BACK")):
        x = int((lon + 180) / 360 * (W - 1))
        d.text((x + 6, H_EYE // 2 + 8), name, fill=(255, 255, 255), font=f_mid)
    d.text((W // 2 - 160, 70), label, fill=(255, 230, 120), font=f_big)
    d.text((W // 2 - 160, 105), f"eye {eye}", fill=(255, 255, 255), font=f_big)
    # crossed disparity: the box is shifted right in the left eye and left in the right eye
    shift = disparity_px // 2 if eye == "L" else -(disparity_px - disparity_px // 2)
    cx, cy = W // 2 + shift, H_EYE // 2 - 40
    d.rectangle([cx - 30, cy - 30, cx + 30, cy + 30], outline=(120, 255, 120), width=4)
    return img


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--plans", type=Path, required=True)
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--ffmpeg", default="ffmpeg")
    ap.add_argument("--disparity-px", type=int, default=8)
    args = ap.parse_args()

    files = set()
    for p in sorted(args.plans.glob("*.json")):
        plan = json.loads(p.read_text(encoding="utf-8"))
        # plan format 5: PAw names carry "{paw_theta}"; one clip per correction-radius variant
        variants = plan.get("eye_range", {}).get("paw_theta_variants_deg", [])
        for part in plan["parts"]:
            for t in part["trials"]:
                for k in ("file", "file_a", "file_b"):
                    name = t.get(k)
                    if not name:
                        continue
                    if "{paw_theta}" in name:
                        files.update(name.replace("{paw_theta}", f"{float(v):g}") for v in variants)
                    else:
                        files.add(name)
    args.out.mkdir(parents=True, exist_ok=True)
    f_small = font(20)
    for name in sorted(files):
        out = args.out / name
        if out.exists():
            continue
        label = Path(name).stem.replace("__", "  /  ")
        left = eye_image(label, "L", args.disparity_px)
        right = eye_image(label, "R", args.disparity_px)
        cmd = [args.ffmpeg, "-loglevel", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24",
               "-s", f"{W}x{2 * H_EYE}", "-r", str(FPS), "-i", "-", "-c:v", "libx264", "-pix_fmt", "yuv420p",
               "-crf", "23", "-movflags", "+faststart", str(out)]
        proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
        for i in range(FPS * SECONDS):
            frame = Image.new("RGB", (W, 2 * H_EYE))
            frame.paste(left, (0, 0))
            frame.paste(right, (0, H_EYE))
            d = ImageDraw.Draw(frame)
            for y0 in (0, H_EYE):
                d.text((W // 2 - 160, y0 + 140), f"frame {i:03d}", fill=(255, 255, 255), font=f_small)
            proc.stdin.write(np.asarray(frame, dtype=np.uint8).tobytes())
        proc.stdin.close()
        if proc.wait() != 0:
            print("ffmpeg failed for", name)
            return 1
    print(f"{len(files)} clip(s) in {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
