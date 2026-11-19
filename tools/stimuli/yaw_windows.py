"""Pick, per real sequence (no pose ground truth), the 12 s window with the least camera turning (user 11-19).

Yaw between consecutive sampled frames is estimated from a circular phase correlation of the horizon band of the
ERP image (same estimator as yaw_stabilize.py check; noisy by roughly +-10 deg/s because translation also moves the
band). The yaw track is smoothed over 1 s, and every window of `--window-s` seconds (start on a 1 s grid) is scored by
its total absolute turning. Frames are read with a process pool; nothing is written except the JSON result.

Usage (lab-gpu2, pano2stereo env python):
  python yaw_windows.py --out result.json --fps 10 --window-s 12 --workers 8 \
      name=DIR[:STEP] [name=DIR[:STEP] ...]      # STEP: read every STEP-th frame (default 1)
"""
import argparse
import glob
import json
import os
from concurrent.futures import ProcessPoolExecutor

import numpy as np

BAND_W = 1024


def band(path):
    import cv2
    im = cv2.imread(path, cv2.IMREAD_GRAYSCALE)
    h0, w0 = im.shape
    im = cv2.resize(im, (BAND_W, int(h0 * BAND_W / w0)), interpolation=cv2.INTER_AREA).astype(np.float32)
    h = im.shape[0]
    b = im[int(h * 0.42):int(h * 0.58)]
    return cv2.Sobel(b - b.mean(axis=1, keepdims=True), cv2.CV_32F, 1, 0)


def step_yaw(a, b):
    F = np.fft.fft(b, axis=1) * np.conj(np.fft.fft(a, axis=1))
    c = np.real(np.fft.ifft(F / (np.abs(F) + 1e-6), axis=1)).sum(axis=0)
    k = int(np.argmax(c))
    # parabolic sub-pixel refinement
    l, r = c[(k - 1) % c.size], c[(k + 1) % c.size]
    den = l - 2 * c[k] + r
    off = 0.5 * (l - r) / den if den != 0 else 0.0
    k = k + off
    return (k if k <= c.size / 2 else k - c.size) * 360.0 / c.size


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", required=True)
    ap.add_argument("--fps", type=float, default=10.0)
    ap.add_argument("--window-s", type=float, default=12.0)
    ap.add_argument("--workers", type=int, default=8)
    ap.add_argument("seqs", nargs="+")
    a = ap.parse_args()
    res = {"tool": "yaw_windows.py", "fps": a.fps, "window_s": a.window_s, "sequences": {}}
    for spec in a.seqs:
        name, rest = spec.split("=", 1)
        d, step = (rest.rsplit(":", 1) + ["1"])[:2] if ":" in rest.split("/")[-1] else (rest, "1")
        step = int(step)
        files = sorted(glob.glob(os.path.join(d, "*.png")) + glob.glob(os.path.join(d, "*.jpg")))
        idx = list(range(0, len(files), step))
        with ProcessPoolExecutor(a.workers) as ex:
            bands = list(ex.map(band, [files[i] for i in idx], chunksize=4))
        dy = np.array([step_yaw(bands[i], bands[i + 1]) for i in range(len(bands) - 1)])
        yaw = np.concatenate([[0.0], np.cumsum(dy)])          # deg at sampled frames
        dt = step / a.fps
        k = max(1, int(round(1.0 / dt)))                       # 1 s smoothing
        sm = np.convolve(np.pad(yaw, (k // 2, k - 1 - k // 2), mode="edge"), np.ones(k) / k, mode="valid")
        n_win = int(round(a.window_s / dt))                    # samples per window
        n_frames = int(round(a.window_s * a.fps))              # source frames per window
        cands = []
        for s0 in range(0, len(sm) - n_win, max(1, int(round(1.0 / dt)))):
            seg = sm[s0:s0 + n_win + 1]
            turn = float(np.abs(np.diff(seg)).sum())
            rate = float(np.abs(np.diff(seg)).max() / dt)
            cands.append({"start_frame": idx[s0], "frames": n_frames, "total_turn_deg": round(turn, 1),
                          "net_turn_deg": round(float(seg[-1] - seg[0]), 1), "max_rate_deg_s": round(rate, 1)})
        if not cands:
            cands = [{"start_frame": 0, "frames": len(files), "total_turn_deg": float(np.abs(np.diff(sm)).sum()),
                      "net_turn_deg": float(sm[-1] - sm[0]), "max_rate_deg_s": float(np.abs(np.diff(sm)).max() / dt),
                      "note": "sequence shorter than the window"}]
        best = sorted(cands, key=lambda c: c["total_turn_deg"])
        res["sequences"][name] = {"dir": d, "n_files": len(files), "step": step, "best": best[0], "top3": best[:3],
                                  "start0": cands[0], "seq_total_turn_deg": round(float(np.abs(np.diff(sm)).sum()), 1)}
        b0 = best[0]
        print("%-22s files %5d | best start %5d: turn %6.1f deg (net %6.1f, max %5.1f deg/s) | start 0: turn %6.1f" % (
            name, len(files), b0["start_frame"], b0["total_turn_deg"], b0["net_turn_deg"], b0["max_rate_deg_s"],
            cands[0]["total_turn_deg"]), flush=True)
    json.dump(res, open(a.out, "w"), indent=1)


if __name__ == "__main__":
    main()
