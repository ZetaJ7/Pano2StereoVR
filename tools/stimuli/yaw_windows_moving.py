"""Least-turning 12 s window among windows where the camera is moving (user 11-19: prepare both the plain
least-turning window and this one for sequences whose least-turning window is a stop).

Moving: the median 1 s image change inside the window (mean absolute grey-level difference of the middle band
between frames k and k+fps, 512 px wide) is at least --min-motion-frac of the sequence median of the same measure.
Turning: as yaw_windows.py (phase-correlation yaw, 1 s smoothing, total absolute turning).

Usage: python yaw_windows_moving.py --out result.json --fps 10 --window-s 12 --min-motion-frac 0.7 name=DIR[:STEP] ...
"""
import argparse
import glob
import json
import os
from concurrent.futures import ProcessPoolExecutor

import numpy as np

from yaw_windows import band, step_yaw


def small(path):
    import cv2
    im = cv2.imread(path, cv2.IMREAD_GRAYSCALE)
    s = cv2.resize(im, (512, int(im.shape[0] * 512 / im.shape[1])), interpolation=cv2.INTER_AREA).astype(np.float32)
    h = s.shape[0]
    return s[int(h * 0.3):int(h * 0.7)]


def both(path):
    return band(path), small(path)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", required=True)
    ap.add_argument("--fps", type=float, default=10.0)
    ap.add_argument("--window-s", type=float, default=12.0)
    ap.add_argument("--min-motion-frac", type=float, default=0.7)
    ap.add_argument("--workers", type=int, default=8)
    ap.add_argument("seqs", nargs="+")
    a = ap.parse_args()
    res = {"tool": "yaw_windows_moving.py", "fps": a.fps, "window_s": a.window_s, "min_motion_frac": a.min_motion_frac,
           "sequences": {}}
    for spec in a.seqs:
        name, rest = spec.split("=", 1)
        d, step = (rest.rsplit(":", 1) if ":" in rest.split("/")[-1] else (rest, "1"))
        step = int(step)
        files = sorted(glob.glob(os.path.join(d, "*.png")) + glob.glob(os.path.join(d, "*.jpg")))
        idx = list(range(0, len(files), step))
        with ProcessPoolExecutor(a.workers) as ex:
            out = list(ex.map(both, [files[i] for i in idx], chunksize=4))
        bands = [o[0] for o in out]
        smalls = [o[1] for o in out]
        dt = step / a.fps
        k1 = max(1, int(round(1.0 / dt)))                      # samples per second
        dy = np.array([step_yaw(bands[i], bands[i + 1]) for i in range(len(bands) - 1)])
        yaw = np.concatenate([[0.0], np.cumsum(dy)])
        sm = np.convolve(np.pad(yaw, (k1 // 2, k1 - 1 - k1 // 2), mode="edge"), np.ones(k1) / k1, mode="valid")
        motion = np.array([float(np.abs(smalls[i + k1] - smalls[i]).mean()) for i in range(len(smalls) - k1)])
        seq_med = float(np.median(motion))
        n_win = int(round(a.window_s / dt))
        cands = []
        for s0 in range(0, len(sm) - n_win, k1):
            seg = sm[s0:s0 + n_win + 1]
            mot = float(np.median(motion[s0:min(len(motion), s0 + n_win - k1 + 1)]))
            cands.append({"start_frame": idx[s0], "frames": int(round(a.window_s * a.fps)),
                          "total_turn_deg": round(float(np.abs(np.diff(seg)).sum()), 1),
                          "net_turn_deg": round(float(seg[-1] - seg[0]), 1),
                          "max_rate_deg_s": round(float(np.abs(np.diff(seg)).max() / dt), 1),
                          "motion": round(mot, 2), "motion_frac": round(mot / seq_med, 2)})
        moving = [c for c in cands if c["motion_frac"] >= a.min_motion_frac]
        best_any = min(cands, key=lambda c: c["total_turn_deg"])
        best_mov = min(moving, key=lambda c: c["total_turn_deg"]) if moving else None
        res["sequences"][name] = {"dir": d, "n_files": len(files), "step": step, "seq_motion_median": round(seq_med, 2),
                                  "best_any": best_any, "best_moving": best_mov,
                                  "top3_moving": sorted(moving, key=lambda c: c["total_turn_deg"])[:3]}
        bm = best_mov or {}
        print("%-20s any: start %5d turn %5.1f motion %.2f | moving: start %5s turn %5s motion %s" % (
            name, best_any["start_frame"], best_any["total_turn_deg"], best_any["motion_frac"],
            bm.get("start_frame"), bm.get("total_turn_deg"), bm.get("motion_frac")), flush=True)
    json.dump(res, open(a.out, "w"), indent=1)


if __name__ == "__main__":
    main()
