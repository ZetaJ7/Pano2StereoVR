"""Write SIMULATED participant folders in the Unity study-mode format (data contract v6), for testing analyze_study.py.

The output is for checking the data pipeline only. Every session.json is marked "simulated": true
and analyze_study.py prints a banner when it sees such sessions. Never mix this output with real data.
The generator also writes one aborted duplicate session folder and one checkin.json with the
pre-v3 field name, so that the analysis paths for both cases are exercised. One session carries rows
flagged skipped = 1 (QA skip control), which the analysis must drop (another session has one staff skip, F8, in each part,
skip_reason = staff); in that session the natural viewing range
measurement is skipped as well, so its PAw radius is the default (15 deg). The other sessions get plausible
continuous sweeps (eyerange.csv, eyerange_trace.csv at 30 frames per second) and the radius chosen by the same
provisional rule as the study program.

Usage:
    python simulate_study_data.py --plans plans --out _simulated_data
"""
from __future__ import annotations

import argparse
import csv
import json
import math
import random
import statistics
from pathlib import Path

SSQ_ITEMS = ["GD", "FA", "HE", "ES", "DF", "IS", "SW", "NA", "CO", "FH", "BV", "DO", "DC", "VE", "SA", "BU"]
SCALES = ["depth_quality", "visual_comfort", "artifact_annoyance"]
# Arbitrary generator settings, used only to exercise the code paths.
GEN = {"shift": {"ours": {"depth_quality": 1.0, "visual_comfort": -0.2, "artifact_annoyance": -0.4},
                 "deepsynthesis": {"depth_quality": 0.6, "visual_comfort": -0.3, "artifact_annoyance": -0.8},
                 "motionparallax": {"depth_quality": 0.4, "visual_comfort": -0.1, "artifact_annoyance": -0.6}},
       "p_diff_real": 0.45, "p_diff_same": 0.25, "pref_bias": 0.3, "p_revise": 0.15,
       "p_ok_px": {8: 0.98, 4: 0.95, 2: 0.85, 1: 0.6}, "arcsec_per_px": 248.92}

HEADERS = {
    "stereotest.csv": ["participant_id", "trial_index", "block", "shape", "disparity_px", "disparity_arcsec", "arcsec_per_px",
                       "px_source", "response", "correct", "rt_s", "skipped"],
    "part1.csv": ["participant_id", "block", "trial_index", "group_index", "position_in_group", "clip_id", "condition", "repetition",
                  "depth_quality", "visual_comfort", "artifact_annoyance", "rt_depth_s", "rt_comfort_s", "rt_artifact_s",
                  "initial_depth", "initial_comfort", "initial_artifact", "revisions", "view_s", "loops", "replays", "skipped", "skip_reason"],
    "part1_replays.csv": ["participant_id", "group_index", "position_in_group", "trial_index", "clip_id", "condition", "replay",
                          "t_start_utc", "view_s", "loops", "completed"],
    "part1_revisions.csv": ["participant_id", "group_index", "trial_index", "clip_id", "condition", "event", "revision", "reviewed",
                            "depth_quality", "visual_comfort", "artifact_annoyance", "t_utc"],
    "part2.csv": ["participant_id", "block", "trial_index", "clip_id", "cond_a", "cond_b", "pair_type", "different", "preference",
                  "rt_q1_s", "rt_q2_s", "view_a_s", "view_b_s", "loops_a", "loops_b", "max_dev_a_deg", "max_dev_b_deg",
                  "align_s", "start_dev_deg", "restarts_a", "restarts_b", "reminder_off_a", "reminder_off_b", "paw_theta_deg", "skipped",
                  "skip_reason"],
    "eyerange.csv": ["participant_id", "sweep", "direction_deg", "repeat", "speed_deg_s", "onset_dev_deg", "head_onset_ecc_deg", "censored",
                     "stopped_early", "max_head_rot_deg", "skipped"],
    "eyerange_trace.csv": ["participant_id", "sweep", "direction_deg", "repeat", "t_s", "dot_ecc_deg", "head_yaw_deg", "head_pitch_deg",
                           "head_rot_deg", "head_rot_along_deg"],
    "gazetraining.csv": ["participant_id", "attempt", "position", "dot_id", "dot_angle_deg", "dwell_s", "max_dev_deg",
                         "attempt_max_dev_deg", "threshold_deg", "attempt_passed"],
    "headpose.csv": ["participant_id", "part", "trial_index", "segment", "t_s", "video_frame", "yaw_deg", "pitch_deg", "roll_deg"],
    "playback.csv": ["participant_id", "part", "trial_index", "segment", "file", "frames_expected", "frames_shown",
                     "frames_dropped", "dt_mean_ms", "dt_p99_ms"],
}


def clamp(v, lo, hi):
    return max(lo, min(hi, v))


def angle_from_reference(yaw: float, pitch: float) -> float:
    y, p = math.radians(yaw), math.radians(pitch)
    return math.degrees(math.acos(max(-1.0, min(1.0, math.cos(p) * math.cos(y)))))


def unit(yaw: float, pitch: float) -> tuple[float, float, float]:
    y, p = math.radians(yaw), math.radians(pitch)
    return math.cos(p) * math.sin(y), math.sin(p), math.cos(p) * math.cos(y)


def great_circle(yaw0: float, pitch0: float, yaw1: float, pitch1: float) -> float:
    (x0, y0, z0), (x1, y1, z1) = unit(yaw0, pitch0), unit(yaw1, pitch1)
    cross = math.sqrt((y0 * z1 - z0 * y1) ** 2 + (z0 * x1 - x0 * z1) ** 2 + (x0 * y1 - y0 * x1) ** 2)
    return math.degrees(math.atan2(cross, x0 * x1 + y0 * y1 + z0 * z1))


def rotation_along(yaw0: float, pitch0: float, yaw1: float, pitch1: float, direction: float) -> float:
    """As StudySchema.RotationAlongDirection: rotation from direction 0 to 1 projected on the target direction."""
    rot = great_circle(yaw0, pitch0, yaw1, pitch1)
    if rot < 1e-9:
        return 0.0
    ox, oy, oz = unit(yaw0, pitch0)
    tx, ty, tz = unit(yaw1, pitch1)
    rn = math.hypot(oz, ox) or 1.0
    rx, rz = oz / rn, -ox / rn
    ux, uy, uz = oy * rz, oz * rx - ox * rz, -oy * rx
    az = math.atan2(tx * rx + tz * rz, tx * ux + ty * uy + tz * uz)
    return rot * math.cos(az - math.radians(direction))


def simulate_eye_range(plan: dict, pid: str, rng: random.Random, skip: bool) -> tuple[list[list], list[list], dict]:
    """eyerange.csv rows, eyerange_trace.csv rows and the session.json block for continuous sweeps (user 11-20).
    Arbitrary generator: the head is left slightly off the reference after the alignment and starts to follow the
    dot once it passes a per-participant eccentricity; the summary and the rule repeat the study program on the
    rounded trace values (head_rot_deg above the threshold, median over the sweeps, censored = max eccentricity)."""
    er = plan.get("eye_range")
    if not er:
        return [], [], {}
    speed, emax, thr = float(er["speed_deg_s"]), float(er["max_eccentricity_deg"]), float(er["head_threshold_deg"])
    limit = rng.choice([12.0, 18.0, 22.0, 28.0, 34.0, 60.0])
    gain = rng.uniform(0.3, 0.7)
    rows, trace, results = [], [], []
    n_frames = int(round(emax / speed * 30.0))
    for k, s in enumerate(er["sweeps"]):
        d = float(s["direction_deg"])
        if skip and k >= len(er["sweeps"]) // 3:
            rows.append([pid, s["index"], d, s["repeat"], speed, "", "", "", "", "", 1])
            continue
        y0, p0 = rng.gauss(0.0, 1.5), rng.gauss(0.0, 1.2)
        lim = limit + rng.gauss(0.0, 2.0)
        onset, max_rot = None, 0.0
        for f in range(n_frames + 1):
            t = f / 30.0
            ecc = min(speed * t, emax)
            m = max(0.0, ecc - lim) * gain
            yaw = y0 + m * math.sin(math.radians(d)) + rng.gauss(0.0, 0.1)
            pitch = p0 + m * math.cos(math.radians(d)) + rng.gauss(0.0, 0.1)
            rot = round(great_circle(y0, p0, yaw, pitch), 3)
            trace.append([pid, s["index"], d, s["repeat"], round(t, 4), round(ecc, 3), round(yaw, 3), round(pitch, 3), rot,
                          round(rotation_along(y0, p0, yaw, pitch, d), 3)])
            max_rot = max(max_rot, rot)
            if onset is None and rot > thr:
                onset = round(ecc, 3)
                if er.get("stop_at_onset", True):
                    break  # user 11-20: the sweep ends at the head onset
        censored = onset is None
        stopped = int(not censored and bool(er.get("stop_at_onset", True)))
        rows.append([pid, s["index"], d, s["repeat"], speed, round(angle_from_reference(y0, p0), 2), "" if censored else round(onset, 2),
                     int(censored), stopped, round(max_rot, 2), 0])
        results.append((d, onset, censored))
    values = [emax if c else o for _, o, c in results]
    a = None
    theta = float(er["default_paw_theta_deg"])
    source = "default_skipped" if skip else "measured"
    if not skip and values:
        a = statistics.median(values)
        theta = min(sorted(er["paw_theta_variants_deg"]), key=lambda v: abs(v - a))
    per_dir = []
    for dd in sorted(float(x) for x in er["directions_deg"]):
        vs = [emax if c else o for x, o, c in results if x == dd]
        med = statistics.median(vs) if vs else None
        per_dir.append({"direction_deg": dd, "median_head_onset_ecc_deg": None if med is None else round(med, 3),
                        "shown": shown_degrees(med, emax), "n": len(vs), "censored": sum(1 for x, _, c in results if x == dd and c)})
    block = {"mode": "continuous", "a_deg": None if a is None else round(a, 3), "a_shown": shown_degrees(a, emax), "rule": er["rule"],
             "measure": er["measure"], "speed_deg_s": speed, "max_eccentricity_deg": emax, "stop_at_onset": er.get("stop_at_onset", True),
             "head_threshold_deg": thr, "rule_provisional": True, "per_direction": per_dir, "censored": sum(1 for _, _, c in results if c),
             "stopped_early": sum(1 for _, _, c in results if not c) if er.get("stop_at_onset", True) else 0,
             "chosen_paw_theta_deg": float(theta), "paw_theta_source": source, "skipped": skip, "enabled": True,
             "default_paw_theta_deg": er["default_paw_theta_deg"], "paw_theta_variants_deg": er["paw_theta_variants_deg"],
             "n_sweeps": len(er["sweeps"]), "n_answered": len(results)}
    if not skip:
        block.update({"result_shown_utc": "2026-11-20T00:00:00.000Z", "result_confirm_utc": "2026-11-20T00:00:08.000Z",
                      "result_time_on_page_s": round(rng.uniform(4.0, 15.0), 2)})
    return rows, trace, block


def shown_degrees(value, emax: float) -> str:
    """As EyeRangeShapes.ShownDegrees: whole degrees, ">=max" for censored, a dash without data."""
    if value is None:
        return "—"
    if value >= emax - 1e-3:
        return f"≥{emax:g}°"
    return f"{int(math.floor(value + 0.5))}°"


def resolve_theta(name: str, theta: float) -> str:
    return name.replace("{paw_theta}", f"{float(theta):g}")


def write_csv(path: Path, header: list[str], rows: list[list]) -> None:
    with path.open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(header)
        w.writerows(rows)


def simulate(plan: dict, d: Path, rng: random.Random, *, random_rater: bool, incomplete: bool, old_checkin: bool,
             qa_skips: bool = False, staff_skips: bool = False) -> None:
    pid = plan["participant_id"]
    d.mkdir(parents=True, exist_ok=True)
    gaze = plan.get("gaze_training", {})
    thr = float(gaze.get("max_head_deviation_deg", 5.0))
    attempts = []
    for a in range(1, int(gaze.get("max_attempts", 3)) + 1):
        dots = [round(abs(rng.gauss(2.0 if a > 1 else 3.5, 2.0)), 2) for _ in range(int(gaze.get("n_dots", 8)))]
        attempts.append(dots)
        if max(dots) <= thr:
            break
    er_rows, er_trace, er_block = simulate_eye_range(plan, pid, rng, skip=qa_skips)
    theta = er_block.get("chosen_paw_theta_deg", 15.0)
    session = {"participant_id": pid, "simulated": True, "mode": "formal", "language": rng.choice(["zh", "en"]),
               "plan_seed": plan["seed"], "config_sha256": plan["config_sha256"], "plan_format_version": plan["plan_format_version"],
               "data_contract_version": 6, "status": "aborted" if incomplete else "complete",
               "abort_reason": "simulated abort" if incomplete else "", "app_version": "simulator",
               "stereotest_arcsec_per_px": GEN["arcsec_per_px"], "stereotest_px_source": "config_fallback",
               "break_s": round(rng.uniform(120.0, 260.0), 1),
               "gaze_training": {"attempts": len(attempts), "passed": max(attempts[-1]) <= thr,
                                 "max_dev_deg_per_attempt": [max(x) for x in attempts]}}
    if er_block:
        session["eye_range"] = er_block
    (d / "session.json").write_text(json.dumps(session, indent=2), encoding="utf-8")
    checkin = {"participant_id": pid, "verbal_reminder_given": True}
    checkin["pre_session_confirmed" if old_checkin else "paper_forms_confirmed"] = True
    (d / "checkin.json").write_text(json.dumps(checkin), encoding="utf-8")

    gz_rows = []
    for a, dots in enumerate(attempts, start=1):
        order = gaze.get("orders", [list(range(len(dots)))])[(a - 1) % max(1, len(gaze.get("orders", [0])))]
        for pos, k in enumerate(order):
            gz_rows.append([pid, a, pos, k, round(360.0 * k / len(dots), 1), gaze.get("dwell_s", 2.0), dots[pos],
                            max(dots), thr, int(max(dots) <= thr)])
    write_csv(d / "gazetraining.csv", HEADERS["gazetraining.csv"], gz_rows)
    write_csv(d / "eyerange.csv", HEADERS["eyerange.csv"], er_rows)
    write_csv(d / "eyerange_trace.csv", HEADERS["eyerange_trace.csv"], er_trace)

    parts = {pp["part"]: pp["trials"] for pp in plan["parts"]}
    st_rows = []
    for t in plan.get("stereotest", {}).get("trials", []):
        px = int(t["disparity_px"])
        p_ok = 0.25 if random_rater else GEN["p_ok_px"].get(px, 0.8)
        ok = rng.random() < p_ok
        others = [x for x in plan["stereotest"]["shapes"] if x != t["shape"]] + ["cant_tell", "cant_tell"]
        resp = t["shape"] if ok else rng.choice(others)
        st_rows.append([pid, t["index"], t["block"], t["shape"], px, round(px * GEN["arcsec_per_px"], 1), GEN["arcsec_per_px"],
                        "config_fallback", resp, int(ok), round(rng.uniform(1, 4), 2), 0])
    write_csv(d / "stereotest.csv", HEADERS["stereotest.csv"], st_rows)

    p1_rows, rev_rows, rep_rows, p2_rows, hp_rows, pb_rows = [], [], [], [], [], []
    last_group = max((t["group_index"] for t in parts[1]), default=0)
    base = {"depth_quality": 3.0, "visual_comfort": 3.8, "artifact_annoyance": 4.2}
    for t in parts[1]:
        scores = []
        for s in SCALES:
            if random_rater:
                scores.append(rng.randint(1, 5))
            else:
                mu = base[s] + GEN["shift"].get(t["condition"], {}).get(s, 0.0)
                scores.append(int(round(clamp(rng.gauss(mu, 0.8), 1, 5))))
        initial = list(scores)
        rev_rows.append([pid, t["group_index"], t["index"], t["clip_id"], t["condition"], "initial", 0, 0, *scores,
                         "2026-11-19T00:00:00.000Z"])
        revisions = 0
        if t["group_index"] > 0 and rng.random() < GEN["p_revise"]:
            revisions = 1
            k = rng.randrange(3)
            scores[k] = clamp(scores[k] + rng.choice([-1, 1]), 1, 5)
            rev_rows.append([pid, t["group_index"], t["index"], t["clip_id"], t["condition"], "revision", 1, 0, *scores,
                             "2026-11-19T00:01:00.000Z"])
        view = round(rng.uniform(10.0, 10.2), 2)
        replays = 0
        if t["group_index"] > 0 and rng.random() < 0.1:
            replays = 1
            rep_rows.append([pid, t["group_index"], t["position_in_group"], t["index"], t["clip_id"], t["condition"], 1,
                             "2026-11-19T00:00:30.000Z", round(rng.uniform(2.0, 10.0), 2), 0, 0])
        skipped = int(qa_skips and t["group_index"] == last_group)
        staff = staff_skips and t["block"] == "main" and t["group_index"] == 3 and t["position_in_group"] == 1
        if staff:
            # staff skip (F8, user 11-20): this clip only, no rating
            p1_rows.append([pid, t["block"], t["index"], t["group_index"], t["position_in_group"], t["clip_id"], t["condition"],
                            t.get("repetition", 0), *[""] * 9, 0, round(rng.uniform(1.0, 5.0), 2), 0, 0, 1, "staff"])
            pb_rows.append([pid, 1, t["index"], "clip", t["file"], 300, 120, 0, 33.3, 34.0])
            continue
        p1_rows.append([pid, t["block"], t["index"], t["group_index"], t["position_in_group"], t["clip_id"], t["condition"],
                        t.get("repetition", 0), *scores, *[round(rng.uniform(1.0, 4.0), 2) for _ in range(3)],
                        *initial, revisions, view, 1, replays, skipped, "qa" if skipped else ""])
        pb_rows.append([pid, 1, t["index"], "clip", t["file"], 300, 300, rng.choice([0, 0, 0, 1]), 33.3, 34.0])
    trials2 = parts[2]
    if incomplete:
        trials2 = trials2[: len(trials2) // 2]
    for ti, t in enumerate(trials2):
        p_diff = GEN["p_diff_real"] if t["pair_type"] == "real" else GEN["p_diff_same"]
        diff = rng.random() < p_diff
        pref = ""
        if diff:
            toward_paw = clamp(round(rng.gauss(GEN["pref_bias"], 1.4)), -3, 3)
            pref = toward_paw if t["cond_b"] == "paw" else -toward_paw
        view_a, view_b = round(rng.uniform(10.0, 24.0), 2), round(rng.uniform(10.0, 24.0), 2)
        seg_max = {}
        for seg in ("A", "B"):
            m = 0.0
            for k in range(0, 300, 3):
                yaw, pitch = round(rng.gauss(0, 2.5), 2), round(rng.gauss(0, 2.0), 2)
                m = max(m, (yaw ** 2 + pitch ** 2) ** 0.5)
                hp_rows.append([pid, 2, t["index"], seg, round(k / 30.0, 3), k, yaw, pitch, round(rng.gauss(0, 1.0), 2)])
            seg_max[seg] = round(m, 2)
            pb_rows.append([pid, 2, t["index"], seg, resolve_theta(t["file_a"] if seg == "A" else t["file_b"], theta), 300, 300,
                            rng.choice([0, 0, 0, 2]), 33.3, 34.5])
        p2_rows.append([pid, t["block"], t["index"], t["clip_id"], t["cond_a"], t["cond_b"], t["pair_type"],
                        int(diff), pref, round(rng.uniform(0.8, 3.0), 2), round(rng.uniform(1.0, 4.0), 2) if diff else "",
                        view_a, view_b, int(view_a // 10), int(view_b // 10), seg_max["A"], seg_max["B"],
                        round(rng.uniform(1.0, 4.0), 2), round(rng.uniform(0.5, 4.9), 2), 0, 0, 0, 0, theta, int(qa_skips and ti == 2),
                        "qa" if (qa_skips and ti == 2) else ""])
        if staff_skips and ti == 5:
            # staff skip (F8, user 11-20): the whole trial; answers and viewing columns empty
            p2_rows[-1] = [pid, t["block"], t["index"], t["clip_id"], t["cond_a"], t["cond_b"], t["pair_type"], "", "", "", "",
                           "", "", "", "", "", "", round(rng.uniform(1.0, 4.0), 2), round(rng.uniform(0.5, 4.9), 2), 0, 0, "", "",
                           theta, 1, "staff"]
    write_csv(d / "part1.csv", HEADERS["part1.csv"], p1_rows)
    write_csv(d / "part1_revisions.csv", HEADERS["part1_revisions.csv"], rev_rows)
    write_csv(d / "part1_replays.csv", HEADERS["part1_replays.csv"], rep_rows)
    write_csv(d / "part2.csv", HEADERS["part2.csv"], p2_rows)
    write_csv(d / "headpose.csv", HEADERS["headpose.csv"], hp_rows)
    write_csv(d / "playback.csv", HEADERS["playback.csv"], pb_rows)
    ssq_rows = []
    for m in (["S_pre", "S_mid1"] if incomplete else ["S_pre", "S_mid1", "S_mid2", "S_post"]):
        level = {"S_pre": 0.1, "S_mid1": 0.35, "S_mid2": 0.2, "S_post": 0.5}[m]
        ssq_rows.append([pid, m] + [min(3, int(rng.random() < level) + int(rng.random() < level / 3)) for _ in SSQ_ITEMS])
    write_csv(d / "ssq.csv", ["participant_id", "measurement"] + SSQ_ITEMS, ssq_rows)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--plans", type=Path, required=True)
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--seed", type=int, default=1)
    args = ap.parse_args()
    if not args.out.name.startswith("_simulated"):
        raise SystemExit("--out folder name must start with '_simulated' to keep it apart from real data")
    rng = random.Random(args.seed)
    plans = sorted(args.plans.glob("*.json"))
    args.out.mkdir(parents=True, exist_ok=True)
    pre_rows = []
    for i, path in enumerate(plans):
        pid = json.loads(path.read_text(encoding="utf-8"))["participant_id"]
        pre_rows.append([pid, "yes", rng.randint(19, 35), rng.choice(["F", "M"]), rng.choice(["none", "some", "frequent"]),
                         "no" if i == 3 else "yes", "yes", "no"])
    write_csv(args.out / "pre_session.csv", ["participant_id", "consent", "age", "gender", "vr_experience",
                                             "corrected_vision_normal", "color_vision_normal", "epilepsy_history"], pre_rows)
    for i, path in enumerate(plans):
        plan = json.loads(path.read_text(encoding="utf-8"))
        simulate(plan, args.out / plan["participant_id"], rng, random_rater=(i == 1),
                 incomplete=(i == len(plans) - 1), old_checkin=(i == 2), qa_skips=(i == 4), staff_skips=(i == 6))
    if len(plans) > 2:
        # an aborted earlier attempt of the second participant in a time-stamped folder
        plan = json.loads(plans[1].read_text(encoding="utf-8"))
        simulate(plan, args.out / (plan["participant_id"] + "__20261119_010000"), rng, random_rater=False,
                 incomplete=True, old_checkin=False)
    print(f"simulated {len(plans)} participant folder(s) in {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
