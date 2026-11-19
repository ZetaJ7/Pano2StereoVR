"""Remove the camera yaw of a Replica walk sequence from ERP frames (user 11-19: the camera turning along its path
makes the world rotate around the viewer and causes dizziness).

A rotation of the camera about the vertical axis moves an equirectangular image by a horizontal circular shift, and
the same holds for both eyes of an omnidirectional stereo (ODS) pair. Each frame k is shifted by
    s_k = sign * round((yaw_k - yaw_0) / 360 deg * W)   pixels (integer, so no resampling),
with yaw_k from the trajectory ground truth (meta.json "poses"[k]["yaw_rad"]). The same shift is applied to every
condition of a clip (mono, ours, DeepSynthesis, MotionParallax), so the conditions stay identical apart from their
own stereo synthesis. For clips rendered by a pose-aware method with a fixed forward direction (Part 2, PAw), shift
the INPUT frames (rgb and depth) before rendering instead of the outputs.

Usage (on lab-gpu2, pano2stereo env python):
  python yaw_stabilize.py shifts --meta META.json --width 5768 [--frames 300] [--sign -1]
  python yaw_stabilize.py apply  --meta META.json --in DIR --out DIR [--pattern "*.png"] [--frames 300] [--sign -1]
  python yaw_stabilize.py check  --in DIR [--pattern "*.png"] [--frames 300]   # rough residual yaw from the images
"""
import argparse
import glob
import json
import math
import os
import sys

import numpy as np


def yaw_track(meta_path, frames):
    poses = json.load(open(meta_path, encoding="utf-8"))["poses"][:frames]
    yaw = np.unwrap(np.array([float(p["yaw_rad"]) for p in poses]))
    return np.degrees(yaw - yaw[0])


def shifts(meta_path, width, frames, sign):
    d = yaw_track(meta_path, frames)
    return [int(round(sign * v / 360.0 * width)) % width for v in d], d


def list_frames(in_dir, pattern, frames):
    files = sorted(glob.glob(os.path.join(in_dir, pattern)))
    if not files:
        sys.exit("no frames matching %s in %s" % (pattern, in_dir))
    return files[:frames]


def cmd_shifts(a):
    s, d = shifts(a.meta, a.width, a.frames, a.sign)
    print(json.dumps({"width": a.width, "sign": a.sign, "yaw_deg": [round(float(v), 3) for v in d], "shift_px": s}))


def cmd_apply(a):
    import cv2
    files = list_frames(a.inp, a.pattern, a.frames)
    first = cv2.imread(files[0], cv2.IMREAD_UNCHANGED)
    width = first.shape[1]
    s, d = shifts(a.meta, width, len(files), a.sign)
    if len(s) < len(files):
        sys.exit("trajectory has %d poses, fewer than %d frames" % (len(s), len(files)))
    os.makedirs(a.out, exist_ok=True)
    for f, k in zip(files, s):
        im = cv2.imread(f, cv2.IMREAD_UNCHANGED)  # keeps 8-bit RGB and 16-bit depth as they are
        if im.shape[1] != width:
            sys.exit("width changes within the sequence: %s" % f)
        cv2.imwrite(os.path.join(a.out, os.path.basename(f)), np.roll(im, k, axis=1))
    info = {"tool": "yaw_stabilize.py", "meta": os.path.abspath(a.meta), "input": os.path.abspath(a.inp),
            "pattern": a.pattern, "frames": len(files), "width": width, "sign": a.sign,
            "yaw_deg": [round(float(v), 3) for v in d[:len(files)]], "shift_px": s[:len(files)]}
    json.dump(info, open(os.path.join(a.out, "yaw_stabilize.json"), "w"), indent=1)
    print("wrote %d frames to %s (yaw range %.1f deg)" % (len(files), a.out, float(np.ptp(d[:len(files)]))))


def residual_yaw(files, step=5, w=720):
    """Rough yaw between frames k and k+step from a circular correlation of the horizon band (deg)."""
    import cv2

    def band(path):
        im = cv2.imread(path, cv2.IMREAD_GRAYSCALE)
        h0, w0 = im.shape
        im = cv2.resize(im, (w, int(h0 * w / w0)), interpolation=cv2.INTER_AREA).astype(np.float32)
        h = im.shape[0]
        b = im[int(h * 0.40):int(h * 0.60)]
        return cv2.Sobel(b - b.mean(axis=1, keepdims=True), cv2.CV_32F, 1, 0)

    bands = [band(f) for f in files[::step]]
    out = []
    for i in range(len(bands) - 1):
        F = np.fft.fft(bands[i + 1], axis=1) * np.conj(np.fft.fft(bands[i], axis=1))
        c = np.real(np.fft.ifft(F / (np.abs(F) + 1e-6), axis=1)).sum(axis=0)
        k = int(np.argmax(c))
        out.append((k if k <= w // 2 else k - w) * 360.0 / w)
    return np.array(out)


def cmd_check(a):
    files = list_frames(a.inp, a.pattern, a.frames)
    s = residual_yaw(files)
    print("frames %d | total |yaw| %.0f deg, net %.0f deg, max step %.1f deg" % (
        len(files), float(np.abs(s).sum()), float(s.sum()), float(np.abs(s).max())))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sp = ap.add_subparsers(dest="cmd", required=True)
    p = sp.add_parser("shifts")
    p.add_argument("--meta", required=True)
    p.add_argument("--width", type=int, required=True)
    p.add_argument("--frames", type=int, default=300)
    p.add_argument("--sign", type=int, choices=[-1, 1], default=-1)
    p.set_defaults(fn=cmd_shifts)
    p = sp.add_parser("apply")
    p.add_argument("--meta", required=True)
    p.add_argument("--in", dest="inp", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--pattern", default="*.png")
    p.add_argument("--frames", type=int, default=300)
    p.add_argument("--sign", type=int, choices=[-1, 1], default=-1)
    p.set_defaults(fn=cmd_apply)
    p = sp.add_parser("check")
    p.add_argument("--in", dest="inp", required=True)
    p.add_argument("--pattern", default="*.png")
    p.add_argument("--frames", type=int, default=300)
    p.set_defaults(fn=cmd_check)
    a = ap.parse_args()
    a.fn(a)


if __name__ == "__main__":
    main()
