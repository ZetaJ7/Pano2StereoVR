"""Analyse the Pano2Stereo subjective study and write a report.

Usage:
    python analyze_study.py --data Data --config study_config.json --out report
    python analyze_study.py --data Data --config study_config.json --analysis analysis_config.json --out report

Inputs: one folder per session under --data, written by the Unity study mode (data contract v6:
session.json, checkin.json, stereotest.csv, eyerange.csv, eyerange_trace.csv, gazetraining.csv, part1.csv, part1_revisions.csv, part1_replays.csv,
part2.csv, ssq.csv, headpose.csv, playback.csv), plus pre_session.csv transcribed from the paper background form
(in --data or given with --pre-session). Folders written before contract v3 are read where the fields
exist (e.g. checkin.json pre_session_confirmed is accepted for paper_forms_confirmed).

Rows flagged skipped = 1 (QA skip control, contract v4; staff skip F8 with skip_reason = staff, user 11-20) are dropped
when the folders are read.

The unit of analysis is the session folder. A participant with several folders (for example an aborted
attempt and a complete one) is listed in the report. Complete sessions enter the results; with include_aborted_partial
(user decision 2026-11-23: an aborted session is not redone) the completed trials of an aborted session enter as well
when the participant has no complete session (Part 1: the clips rated under both conditions of a pair; Part 2: H - F
when at least one real and one identical pair were answered; SSQ: the measurements filled). sensitivity_complete_only
adds the results of complete sessions only.

Outputs under --out: report.md, results.json, tables/*.csv, tables/*.tex, figs/*.png.

Every decision rule (observer screening method used for the headline numbers, head-deviation
threshold, dropped-frame threshold, one- or two-sided tests, primary family) is read from the
analysis config. When a rule is left unset the report shows the numbers under every variant
and does not pick one.

Methods and sources:
  * MOS and 95% CI: ITU-R BT.500-15 Annex 1 to Part 1, eqs. (1)-(4): delta = 1.96 * S / sqrt(N).
  * Kurtosis-based observer screening: BT.500-15 A1-2.3.1 (beta2 in [2, 4] -> 2*S, else sqrt(20)*S;
    reject if (P+Q)/L > 0.05 and |P-Q|/(P+Q) < 0.3). BT.500 notes this is meant for fewer than
    about 20 observers.
  * Correlation-based observer screening: BT.500-15 A1-2.3.3; r = min(Pearson, Spearman);
    threshold = MCT if mean(r) - sd(r) > MCT else mean(r) - sd(r); MCT = 0.7 for SS methods.
  * SSQ: ITU-T P.919 section 7.2.1 item sets and weights.
  * Part 2 discrimination (P4a, user decision 2026-11-23): per participant H - F with raw proportions (H = share of
    "different" answers on the real pairs, F = the same on the identical pairs); group statistic = mean of H - F;
    two-sided within-participant permutation test: each participant's total number of "different" answers is kept
    and its placement over real and identical pairs is re-drawn (hypergeometric), giving the null distribution of
    the group mean (part2_permutations draws, part2_permutation_seed). z(H) - z(F) with the log-linear correction
    H = (hits + 0.5) / (n_signal + 1) is reported as a description only (yes-no formula, not the d' of a same-different
    task) and is not tested.
  * Part 1 (user decision 2026-11-23: the four methods are compared in confirmatory tests): final ratings (after any
    re-rating in the group summary). Per scale: Friedman test over the four conditions on per-participant means
    (participants with every condition); every pair of part1_pairs by a Wilcoxon signed-rank test over participants on
    the per-participant mean of the clip-paired differences (test - reference, equal clip weights); Holm over the six
    pairs of the same scale (scales are corrected separately, not pooled). Secondary for every pair (uncorrected): a
    sign test over the clip-level mean differences, and an order-only statistic per participant ((clips with
    test > reference - clips with test < reference) / clips), Wilcoxon signed-rank over participants. The revision
    rate is reported.
  * Part 2: P4a is tested alone at alpha (not pooled with Part 1). P4b is tested at alpha only when P4a is significant
    (p4b_requires); otherwise its numbers are reported without a test. SSQ changes are reported as numbers only.
  * Gaze training, viewing times and head deviation are reported as numbers only; no threshold is
    applied unless it is set in the analysis config.
  * Natural viewing range (contract v6, continuous sweeps, user 11-20): a and the PAw correction radius as
    logged in session.json, recomputed from the frames in eyerange_trace.csv with the session's threshold and
    the provisional rule (median head onset eccentricity over the sweeps, censored = maximum eccentricity) as a
    check; the censored count, the per-direction medians and Part 2 grouped by radius are reported as numbers
    only (no test, no exclusion).
"""
from __future__ import annotations

import argparse
import csv
import json
import math
import sys
from collections import defaultdict
from pathlib import Path

import numpy as np
from scipy import stats

SCALES = ["depth_quality", "visual_comfort", "artifact_annoyance"]
SCALE_LABEL = {
    "depth_quality": "立体感 Sense of depth",
    "visual_comfort": "观看舒适度 Viewing comfort",
    "artifact_annoyance": "画面瑕疵 Visual artifacts",
}
SSQ_ITEMS = ["GD", "FA", "HE", "ES", "DF", "IS", "SW", "NA", "CO", "FH", "BV", "DO", "DC", "VE", "SA", "BU"]
SSQ_N = ["GD", "IS", "SW", "NA", "CO", "SA", "BU"]
SSQ_O = ["GD", "FA", "HE", "ES", "DF", "CO", "BV"]
SSQ_D = ["DF", "NA", "FH", "BV", "DO", "DC", "VE"]

DEFAULT_ANALYSIS = {
    "screening_primary": None,          # None | "none" | "kurtosis" | "correlation"
    "correlation_mct": 0.7,             # BT.500 A1-2.3.3.3: 0.7 for SS and DSIS methods
    "alternative": "two-sided",         # passed to scipy.stats.wilcoxon
    "head_deviation_threshold_deg": None,
    "dropped_frame_fraction_threshold": None,
    "preference_no_difference_as_zero": True,
    "ssq_primary_contrast": ["S_pre", "S_post"],
    "stereo_pass_rule": None,           # e.g. {"max_disparity_px": 2, "min_correct_per_level": 2}
    "session_modes": None,              # None = every mode (QA sessions are listed); e.g. ["formal"]
    "include_aborted_partial": True,    # user 2026-11-23: no redo; the completed trials of an aborted session are analysed
    "sensitivity_complete_only": True,  # also report the results of complete sessions only (sensitivity)
    "exclude_self_reported_vision": None,  # None = report only; True = drop participants reporting abnormal vision
    "alpha": 0.05,
    "part1_pairs": [["mono", "ours"], ["deepsynthesis", "ours"], ["motionparallax", "ours"],
                    ["mono", "deepsynthesis"], ["mono", "motionparallax"], ["deepsynthesis", "motionparallax"]],
                                        # (reference, test): difference = test - reference; user 2026-11-23: all 6 pairs confirmatory
    "part1_correction": "holm_within_scale",   # Holm over the pairs of the same scale; scales are not pooled
    "part1_omnibus": "friedman",        # reported per scale; the pairwise tests do not depend on it
    "p4b_requires": "P4a",              # P4b tested at alpha only if P4a (tested alone) is significant; None = no gate
    "part2_permutations": 100000,       # within-participant permutation test of mean(H - F) (P4a)
    "part2_permutation_seed": 20261123,
    "conditions_part1": ["mono", "ours", "deepsynthesis", "motionparallax"],
    "ssq_descriptive_contrasts": [["S_pre", "S_mid1"], ["S_mid2", "S_post"], ["S_pre", "S_mid2"]],  # numbers only
    "conditions_part2": ["pa", "paw"],
    "bootstrap_iterations": 10000,
    "bootstrap_seed": 20261115,
}


# ----------------------------------------------------------------------------- loading
def read_csv(path: Path) -> list[dict]:
    if not path.is_file():
        return []
    with path.open(newline="", encoding="utf-8-sig") as f:
        return list(csv.DictReader(f))


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8")) if path.is_file() else {}


def to_float(v) -> float:
    try:
        return float(v)
    except (TypeError, ValueError):
        return math.nan


def not_skipped(rows: list[dict]) -> list[dict]:
    return [r for r in rows if str(r.get("skipped", "0")).strip() not in ("1", "true", "True")]


def count_skipped(rows: list[dict]) -> int:
    return len(rows) - len(not_skipped(rows))


def load_participants(root: Path) -> list[dict]:
    people = []
    for d in sorted(p for p in root.iterdir() if p.is_dir() and not p.name.startswith((".", "_"))):
        session = read_json(d / "session.json")
        if not session:
            continue
        people.append({
            "pid": session.get("participant_id", d.name),
            "uid": d.name,
            "dir": d,
            "session": session,
            "checkin": read_json(d / "checkin.json"),
            "stereotest": not_skipped(read_csv(d / "stereotest.csv")),
            "eyerange": read_csv(d / "eyerange.csv"),
            "eyerange_trace": read_csv(d / "eyerange_trace.csv"),
            "gazetraining": read_csv(d / "gazetraining.csv"),
            "part1": not_skipped(read_csv(d / "part1.csv")),
            "part1_revisions": read_csv(d / "part1_revisions.csv"),
            "part1_replays": read_csv(d / "part1_replays.csv"),
            "part2": not_skipped(read_csv(d / "part2.csv")),
            "skipped": {f: count_skipped(read_csv(d / f)) for f in ("stereotest.csv", "eyerange.csv", "part1.csv", "part2.csv")},
            "skip_reasons": {f: sorted(r.get("skip_reason", "") or "qa" for r in read_csv(d / f) if r not in not_skipped([r]))
                             for f in ("part1.csv", "part2.csv")},
            "ssq": read_csv(d / "ssq.csv"),
            "headpose": read_csv(d / "headpose.csv"),
            "playback": read_csv(d / "playback.csv"),
        })
    return people


def paper_forms_confirmed(checkin: dict):
    """checkin.json paper_forms_confirmed (contract v3), or pre_session_confirmed in older folders."""
    if "paper_forms_confirmed" in checkin:
        return checkin["paper_forms_confirmed"]
    return checkin.get("pre_session_confirmed")


def summary_stats(values) -> dict:
    x = np.asarray([v for v in values if not math.isnan(v)], dtype=float)
    if x.size == 0:
        return {"n": 0, "median": math.nan, "p95": math.nan, "max": math.nan, "mean": math.nan}
    return {"n": int(x.size), "median": float(np.median(x)), "p95": float(np.percentile(x, 95)),
            "max": float(x.max()), "mean": float(x.mean())}


# ----------------------------------------------------------------------------- statistics
def bt500_ci(values) -> tuple[float, float, float, int]:
    """Mean, S (N-1), delta = 1.96 S / sqrt(N), N  (BT.500-15 eqs. (1)-(4))."""
    x = np.asarray([v for v in values if not math.isnan(v)], dtype=float)
    n = x.size
    if n == 0:
        return math.nan, math.nan, math.nan, 0
    mean = float(x.mean())
    s = float(x.std(ddof=1)) if n > 1 else math.nan
    return mean, s, 1.96 * s / math.sqrt(n) if n > 1 else math.nan, n


def holm(pvals: dict[str, float]) -> dict[str, float]:
    keys = [k for k in pvals if not math.isnan(pvals[k])]
    order = sorted(keys, key=lambda k: pvals[k])
    m = len(order)
    adj, running = {}, 0.0
    for i, k in enumerate(order):
        running = max(running, min(1.0, (m - i) * pvals[k]))
        adj[k] = running
    for k in pvals:
        adj.setdefault(k, math.nan)
    return adj


def wilcoxon_summary(diffs, alternative: str, boot_iter: int, seed: int) -> dict:
    """Wilcoxon signed-rank on paired differences, with rank-biserial r and a bootstrap CI of the median."""
    d = np.asarray([v for v in diffs if not math.isnan(v)], dtype=float)
    out = {"n": int(d.size), "median_diff": math.nan, "mean_diff": math.nan, "W": math.nan,
           "p": math.nan, "rank_biserial": math.nan, "median_ci95": [math.nan, math.nan]}
    if d.size == 0:
        return out
    out["median_diff"] = float(np.median(d))
    out["mean_diff"] = float(d.mean())
    nz = d[d != 0]
    if nz.size >= 1:
        ranks = stats.rankdata(np.abs(nz))
        w_pos, w_neg = float(ranks[nz > 0].sum()), float(ranks[nz < 0].sum())
        out["rank_biserial"] = (w_pos - w_neg) / (w_pos + w_neg)
    if nz.size >= 2:
        res = stats.wilcoxon(d, alternative=alternative, zero_method="wilcox")
        out["W"], out["p"] = float(res.statistic), float(res.pvalue)
    rng = np.random.default_rng(seed)
    idx = rng.integers(0, d.size, size=(boot_iter, d.size))
    meds = np.median(d[idx], axis=1)
    out["median_ci95"] = [float(np.percentile(meds, 2.5)), float(np.percentile(meds, 97.5))]
    return out


def dprime(hits: int, n_signal: int, fas: int, n_noise: int) -> tuple[float, float, float]:
    """z(H) - z(F) with the log-linear correction; description only (yes-no formula)."""
    h = (hits + 0.5) / (n_signal + 1.0)
    f = (fas + 0.5) / (n_noise + 1.0)
    return float(stats.norm.ppf(h) - stats.norm.ppf(f)), h, f


def mean_ci_boot(values, boot_iter: int, seed: int) -> dict:
    """Mean with a percentile bootstrap 95% CI over participants."""
    x = np.asarray([v for v in values if not math.isnan(v)], dtype=float)
    if x.size == 0:
        return {"n": 0, "mean": math.nan, "ci95": [math.nan, math.nan]}
    rng = np.random.default_rng(seed)
    means = x[rng.integers(0, x.size, size=(boot_iter, x.size))].mean(axis=1)
    return {"n": int(x.size), "mean": float(x.mean()),
            "ci95": [float(np.percentile(means, 2.5)), float(np.percentile(means, 97.5))]}


def perm_test_hf(per_p: dict, n_perm: int, seed: int) -> dict:
    """Two-sided within-participant permutation test of mean(H - F) (P4a, user 2026-11-23).

    Under no discrimination a participant's answers are exchangeable between real and identical pairs, so with the
    participant's total k of "different" answers fixed, the number of hits is hypergeometric(n_signal, n_noise, k).
    p = (1 + #{|null| >= |observed|}) / (1 + n_perm)."""
    rows = [v for v in per_p.values() if v["n_signal"] and v["n_noise"]]
    out = {"n": len(rows), "mean_hf": math.nan, "p": math.nan, "n_perm": int(n_perm), "seed": int(seed)}
    if not rows:
        return out
    obs = float(np.mean([v["hits"] / v["n_signal"] - v["fas"] / v["n_noise"] for v in rows]))
    rng = np.random.default_rng(seed)
    null = np.zeros(n_perm)
    for v in rows:
        ns, nn, k = int(v["n_signal"]), int(v["n_noise"]), int(v["hits"] + v["fas"])
        if 0 < k < ns + nn:
            h0 = rng.hypergeometric(ns, nn, k, size=n_perm)
            null += h0 / ns - (k - h0) / nn
        # k = 0 or k = ns + nn: H - F = 0 in every permutation
    null /= len(rows)
    out["mean_hf"] = obs
    out["p"] = float((1 + np.sum(np.abs(null) >= abs(obs) - 1e-12)) / (1 + n_perm))
    out["null_q025_q975"] = [float(np.percentile(null, 2.5)), float(np.percentile(null, 97.5))]
    return out


def sign_test(values) -> dict:
    """Two-sided exact sign test against 0 (zeros dropped)."""
    x = [v for v in values if not math.isnan(v)]
    pos, neg = sum(v > 0 for v in x), sum(v < 0 for v in x)
    p = float(stats.binomtest(pos, pos + neg, 0.5).pvalue) if pos + neg else math.nan
    return {"n": len(x), "pos": int(pos), "neg": int(neg), "zero": int(len(x) - pos - neg), "p": p}


# ----------------------------------------------------------------------------- screening
def part1_matrix(people: list[dict]) -> tuple[list[str], list[tuple], np.ndarray]:
    """Scores u[i, l] for observer i and presentation l = (clip, condition, repetition, scale)."""
    pres = sorted({(r["clip_id"], r["condition"], r.get("repetition", "0"), s)
                   for p in people for r in p["part1"] if r["block"] == "main" for s in SCALES})
    col = {k: j for j, k in enumerate(pres)}
    pids = [p["uid"] for p in people]
    u = np.full((len(pids), len(pres)), np.nan)
    for i, p in enumerate(people):
        for r in p["part1"]:
            if r["block"] != "main":
                continue
            for s in SCALES:
                u[i, col[(r["clip_id"], r["condition"], r.get("repetition", "0"), s)]] = to_float(r[s])
    return pids, pres, u


def screen_kurtosis(pids, u) -> dict:
    P = np.zeros(len(pids))
    Q = np.zeros(len(pids))
    L = np.sum(~np.isnan(u), axis=1)
    for l in range(u.shape[1]):
        col = u[:, l]
        ok = ~np.isnan(col)
        x = col[ok]
        if x.size < 3:
            continue
        mean, s = x.mean(), x.std(ddof=1)
        m2 = np.mean((x - mean) ** 2)
        m4 = np.mean((x - mean) ** 4)
        beta2 = m4 / m2 ** 2 if m2 > 0 else math.nan
        k = 2.0 if (not math.isnan(beta2) and 2.0 <= beta2 <= 4.0) else math.sqrt(20.0)
        P[ok] += col[ok] >= mean + k * s
        Q[ok] += col[ok] <= mean - k * s
    out = {}
    for i, pid in enumerate(pids):
        r1 = (P[i] + Q[i]) / L[i] if L[i] else math.nan
        r2 = abs(P[i] - Q[i]) / (P[i] + Q[i]) if (P[i] + Q[i]) else math.nan
        reject = bool(r1 > 0.05 and (not math.isnan(r2)) and r2 < 0.3)
        out[pid] = {"P": int(P[i]), "Q": int(Q[i]), "ratio1": r1, "ratio2": r2, "reject": reject}
    return out


def screen_correlation(pids, u, mct: float) -> dict:
    mean_scores = np.nanmean(u, axis=0)
    rs = {}
    for i, pid in enumerate(pids):
        ok = ~np.isnan(u[i]) & ~np.isnan(mean_scores)
        x, y = mean_scores[ok], u[i, ok]
        if x.size < 3 or np.std(y) == 0 or np.std(x) == 0:
            rs[pid] = math.nan
            continue
        rs[pid] = min(stats.pearsonr(x, y)[0], stats.spearmanr(x, y)[0])
    vals = np.array([v for v in rs.values() if not math.isnan(v)])
    m, sd = (float(vals.mean()), float(vals.std(ddof=1))) if vals.size > 1 else (math.nan, math.nan)
    thr = mct if (m - sd) > mct else (m - sd)
    return {"threshold": thr, "mean_r": m, "sd_r": sd, "mct": mct,
            "observers": {pid: {"r": r, "reject": bool(math.isnan(r) or r <= thr)} for pid, r in rs.items()}}


# ----------------------------------------------------------------------------- analyses
def angle_from_reference(yaw: float, pitch: float) -> float:
    """Great-circle angle between head forward (yaw, pitch relative to the clip reference) and the reference."""
    y, p = math.radians(yaw), math.radians(pitch)
    c = math.cos(p) * math.cos(y)
    return math.degrees(math.acos(max(-1.0, min(1.0, c))))


def is_stopped(segment) -> bool:
    """Contract v5: a Part 2 viewing stopped by a head turn and then replayed from the start."""
    return str(segment or "").endswith("_stopped")


def head_deviation(people: list[dict]) -> dict:
    """Per (pid, part 2 trial): max and p95 deviation over both completed segments (stopped viewings left out)."""
    out = {}
    for p in people:
        per = defaultdict(list)
        for r in p["headpose"]:
            if str(r.get("part")) != "2" or is_stopped(r.get("segment")):
                continue
            per[r["trial_index"]].append(angle_from_reference(to_float(r["yaw_deg"]), to_float(r["pitch_deg"])))
        for t, angles in per.items():
            a = np.asarray(angles)
            out[(p["uid"], str(t))] = {"max": float(a.max()), "p95": float(np.percentile(a, 95)), "n": int(a.size)}
    return out


def playback_flags(people: list[dict], thr) -> dict:
    out = {}
    for p in people:
        for r in p["playback"]:
            if is_stopped(r.get("segment")):
                continue
            exp = to_float(r.get("frames_expected"))
            drop = to_float(r.get("frames_dropped"))
            frac = drop / exp if exp and not math.isnan(exp) else math.nan
            key = (p["uid"], str(r["part"]), str(r["trial_index"]))
            prev = out.get(key, 0.0)
            out[key] = max(prev, frac) if not math.isnan(frac) else prev
    flagged = {k for k, v in out.items() if thr is not None and v > thr}
    return {"max_fraction": out, "flagged": flagged}


def analyse_part1(people, keep: set[str], cfg: dict, acfg: dict, excluded_trials: set) -> dict:
    """Final ratings (user decision 2026-11-23: the four methods are compared in confirmatory tests).

    Per scale: MOS per condition from per-participant means; Friedman test over the conditions on the per-participant
    means (participants with a value under every condition); every pair of part1_pairs by a Wilcoxon signed-rank test
    on per-participant means of clip-paired differences (test - reference, only clips rated under both conditions by
    that participant, equal clip weights), Holm over the pairs of the same scale (part1_correction). Secondary per
    pair: sign test over the clip-level mean differences and the order-only statistic. Per cell: descriptive."""
    conditions = list(acfg["conditions_part1"])
    pairs = [tuple(x) for x in acfg["part1_pairs"]]
    res = {"per_condition": {}, "per_clip": {}, "friedman": {}, "pairwise": {}, "holm": {}, "pair_counts": {},
           "secondary": {}, "per_scene": {}, "conditions": conditions, "pairs": pairs}
    rows = [(p["uid"], r) for p in people if p["uid"] in keep for r in p["part1"]
            if r["block"] == "main" and (p["uid"], "1", r["trial_index"]) not in excluded_trials]
    scene_of = {c["clip_id"]: c.get("cell") or c.get("scene", "") for c in cfg["part1"]["clips"]}
    a, it, sd_ = acfg["alternative"], acfg["bootstrap_iterations"], acfg["bootstrap_seed"]
    for s in SCALES:
        pm = defaultdict(lambda: defaultdict(list))
        clip_scores = defaultdict(list)
        by_clip = defaultdict(dict)  # (uid, clip, repetition) -> condition -> final rating
        for uid, r in rows:
            v = to_float(r[s])
            if math.isnan(v):
                continue
            pm[uid][r["condition"]].append(v)
            clip_scores[(r["clip_id"], r["condition"])].append(v)
            by_clip[(uid, r["clip_id"], r.get("repetition", "0"))][r["condition"]] = v
        for cond in conditions:
            vals = [float(np.mean(pm[uid][cond])) for uid in pm if pm[uid][cond]]
            mean, sd, delta, n = bt500_ci(vals)
            res["per_condition"][(s, cond)] = {"mos": mean, "sd": sd, "ci95": delta, "n": n}
        for (clip, cond), vals in clip_scores.items():
            mean, sd, delta, n = bt500_ci(vals)
            res["per_clip"][(s, clip, cond)] = {"mos": mean, "sd": sd, "ci95": delta, "n": n}
        # omnibus: Friedman over the conditions (participants with every condition)
        full = [uid for uid in pm if all(pm[uid][c] for c in conditions)]
        fr = {"n": len(full), "k": len(conditions), "chi2": math.nan, "p": math.nan, "kendall_w": math.nan}
        if len(full) >= 2 and len(conditions) >= 3:
            cols = [[float(np.mean(pm[uid][c])) for uid in full] for c in conditions]
            st = stats.friedmanchisquare(*cols)
            fr.update(chi2=float(st.statistic), p=float(st.pvalue),
                      kendall_w=float(st.statistic) / (len(full) * (len(conditions) - 1)))
        res["friedman"][s] = fr
        # pairwise Wilcoxon, Holm within the scale; secondary analyses and per-cell description per pair
        pvals = {}
        for ref, test in pairs:
            pdiff = defaultdict(list)
            sdiff = defaultdict(lambda: defaultdict(list))
            clip_d = defaultdict(list)
            uid_clip = defaultdict(lambda: defaultdict(list))
            for (uid, clip, _rep), cv in by_clip.items():
                if ref in cv and test in cv:
                    d = cv[test] - cv[ref]
                    pdiff[uid].append(d)
                    sdiff[scene_of.get(clip, "")][uid].append(d)
                    clip_d[clip].append(d)
                    uid_clip[uid][clip].append(d)
            t = wilcoxon_summary([float(np.mean(v)) for v in pdiff.values()], a, it, sd_)
            res["pairwise"][(s, ref, test)] = t
            pvals[(ref, test)] = t["p"]
            res["pair_counts"][(s, ref, test)] = {"participants": len(pdiff),
                                                  "clip_pairs": int(sum(len(v) for v in pdiff.values()))}
            for scene, dd in sdiff.items():
                res["per_scene"][(s, ref, test, scene)] = wilcoxon_summary([float(np.mean(v)) for v in dd.values()], a, it, sd_)
            clip_means = {clip: float(np.mean(v)) for clip, v in sorted(clip_d.items())}
            order_stat = []
            for uid, cd in uid_clip.items():
                m = [float(np.mean(v)) for v in cd.values()]
                order_stat.append((sum(x > 0 for x in m) - sum(x < 0 for x in m)) / len(m))
            res["secondary"][(s, ref, test)] = {"by_clip": {**sign_test(list(clip_means.values())), "clip_means": clip_means},
                                                "order_stat": wilcoxon_summary(order_stat, a, it, sd_)}
        if acfg.get("part1_correction") == "holm_within_scale":
            adj = holm(pvals)
        else:
            adj = {k: math.nan for k in pvals}
        for (ref, test), v in adj.items():
            res["holm"][(s, ref, test)] = v
    return res


def part1_revision_summary(people, keep: set[str]) -> dict:
    """Re-rating in the Part 1 group summaries: share of main trials with a revision, share whose final
    rating differs from the first answer, and the mean absolute change per scale."""
    per, changes = {}, {s: [] for s in SCALES}
    initial_col = {"depth_quality": "initial_depth", "visual_comfort": "initial_comfort", "artifact_annoyance": "initial_artifact"}
    for p in people:
        if p["uid"] not in keep:
            continue
        rows = [r for r in p["part1"] if r.get("block") == "main" and "revisions" in r]
        if not rows:
            continue
        revised = sum(to_float(r["revisions"]) > 0 for r in rows)
        changed = 0
        for r in rows:
            diff = False
            for s in SCALES:
                d = to_float(r[s]) - to_float(r.get(initial_col[s]))
                if not math.isnan(d):
                    changes[s].append(abs(d))
                    diff = diff or d != 0
            changed += diff
        per[p["uid"]] = {"n": len(rows), "revised": int(revised), "changed": int(changed),
                         "events": int(sum(1 for r in p["part1_revisions"] if r.get("event") == "revision")),
                         "replays": len(p["part1_replays"])}
    n = sum(v["n"] for v in per.values())
    return {"per_participant": per, "n_trials": n, "replays": int(sum(v["replays"] for v in per.values())),
            "revised_rate": (sum(v["revised"] for v in per.values()) / n) if n else math.nan,
            "changed_rate": (sum(v["changed"] for v in per.values()) / n) if n else math.nan,
            "mean_abs_change": {s: (float(np.mean(v)) if v else math.nan) for s, v in changes.items()}}


def gaze_training_summary(people) -> dict:
    """Per session: attempts, maximum head deviation of each attempt, threshold and outcome as logged."""
    out = {}
    for p in people:
        rows = p["gazetraining"]
        if not rows:
            continue
        att = defaultdict(list)
        thr, passed = math.nan, {}
        for r in rows:
            a = int(to_float(r["attempt"]))
            att[a].append(to_float(r["max_dev_deg"]))
            thr = to_float(r.get("threshold_deg"))
            passed[a] = str(r.get("attempt_passed")).strip() in ("1", "true", "True")
        last = max(att)
        out[p["uid"]] = {"attempts": len(att), "max_dev_per_attempt": [float(np.max(att[k])) for k in sorted(att)],
                         "threshold_deg": thr, "last_attempt_passed": passed.get(last)}
    return out


def sweep_onsets_from_trace(trace: list[dict], threshold: float) -> dict:
    """Per sweep (as in eyerange.csv): the dot eccentricity of the first frame whose head_rot_deg exceeds the
    threshold, or NaN when it never does (censored). Same rule as EyeRangePlan.OnsetEccentricity."""
    frames = defaultdict(list)
    for r in trace:
        frames[str(r["sweep"])].append(r)
    out = {}
    for sw, rows in frames.items():
        rows.sort(key=lambda r: to_float(r["t_s"]))
        out[sw] = next((to_float(r["dot_ecc_deg"]) for r in rows if to_float(r["head_rot_deg"]) > threshold), math.nan)
    return out


def nearest_variant(variants: list[float], a: float) -> float:
    """The variant nearest to a; ties go to the smaller radius (as StudyPlan.cs)."""
    return min(sorted(variants), key=lambda v: abs(v - a)) if variants and not math.isnan(a) else math.nan


def eye_range_summary(people, keep: set[str]) -> dict:
    """Per session: a, chosen PAw radius, its source and the censored count as logged; a recomputed from
    eyerange_trace.csv; per-direction medians of the head onset eccentricity (censored = maximum eccentricity).
    Distribution of the per-direction medians and of a over the kept sessions."""
    per = {}
    for p in people:
        er = p["session"].get("eye_range") or {}
        rows = not_skipped(p["eyerange"])
        if not er and not p["eyerange"]:
            continue
        thr = to_float(er.get("head_threshold_deg"))
        emax = to_float(er.get("max_eccentricity_deg"))
        variants = [float(v) for v in er.get("paw_theta_variants_deg", [])]
        logged = [(to_float(r["direction_deg"]), emax if yes(r.get("censored")) else to_float(r.get("head_onset_ecc_deg"))) for r in rows]
        onsets = sweep_onsets_from_trace(p["eyerange_trace"], thr) if not math.isnan(thr) else {}
        recomputed = [emax if math.isnan(onsets.get(str(r["sweep"]), math.nan)) else onsets[str(r["sweep"])] for r in rows]
        a_re = float(np.median(recomputed)) if recomputed and not math.isnan(emax) else math.nan
        dirs = sorted({d_ for d_, _ in logged})
        per_dir = {d_: float(np.median([v for x, v in logged if x == d_])) for d_ in dirs}
        a_log = er.get("a_deg")
        per[p["uid"]] = {"a_deg": to_float(a_log) if a_log is not None else math.nan, "a_recomputed": a_re,
                         "theta": to_float(er.get("chosen_paw_theta_deg")), "theta_recomputed": nearest_variant(variants, a_re),
                         "source": er.get("paw_theta_source", ""), "mode": er.get("mode", ""), "threshold": thr,
                         "speed": to_float(er.get("speed_deg_s")), "max_ecc": emax,
                         "censored": sum(1 for r in rows if yes(r.get("censored"))),
                         "stopped_early": sum(1 for r in rows if yes(r.get("stopped_early"))), "n_rows": len(p["eyerange"]),
                         "n_skipped": count_skipped(p["eyerange"]), "n_frames": len(p["eyerange_trace"]), "per_direction": per_dir,
                         "time_on_page_s": to_float(er.get("result_time_on_page_s"))}
    dirs_all = sorted({d_ for v in per.values() for d_ in v["per_direction"]})

    def dist(values) -> dict:
        x = np.asarray([v for v in values if not math.isnan(v)])
        if not x.size:
            return {"n": 0, "min": math.nan, "p25": math.nan, "median": math.nan, "p75": math.nan, "max": math.nan}
        return {"n": int(x.size), "min": float(x.min()), "p25": float(np.percentile(x, 25)), "median": float(np.median(x)),
                "p75": float(np.percentile(x, 75)), "max": float(x.max())}
    kept = {uid: v for uid, v in per.items() if uid in keep}
    return {"per_session": per, "directions": dirs_all,
            "distribution": {d_: dist([v["per_direction"].get(d_, math.nan) for v in kept.values()]) for d_ in dirs_all},
            "a_distribution": dist([v["a_deg"] for v in kept.values() if v["source"] == "measured"])}


def part2_theta_of(p: dict) -> float:
    """PAw correction radius of a session: part2.csv paw_theta_deg (contract v6), else session.json eye_range."""
    for r in p["part2"]:
        v = to_float(r.get("paw_theta_deg"))
        if not math.isnan(v):
            return v
    return to_float((p["session"].get("eye_range") or {}).get("chosen_paw_theta_deg"))


def part2_by_theta(people, part2: dict) -> dict:
    """Descriptive Part 2 numbers per PAw radius group (participants of the analysed variant only; no test)."""
    theta = {p["uid"]: part2_theta_of(p) for p in people}
    groups = defaultdict(list)
    for uid, v in part2["per_participant"].items():
        groups[theta.get(uid, math.nan)].append(v)
    out = {}
    for t, vs in groups.items():
        hits, ns, fas, nn = (sum(v[k] for v in vs) for k in ("hits", "n_signal", "fas", "n_noise"))
        out[t] = {"n": len(vs), "hit_rate": hits / ns if ns else math.nan, "fa_rate": fas / nn if nn else math.nan,
                  "H_mean": float(np.mean([v["H"] for v in vs])), "F_mean": float(np.mean([v["F"] for v in vs])),
                  "HF_mean": float(np.mean([v["HF"] for v in vs])),
                  "dprime_mean": float(np.mean([v["dprime_loglinear"] for v in vs])),
                  "pref_mean": float(np.nanmean([v["pref_mean"] for v in vs])) if any(not math.isnan(v["pref_mean"]) for v in vs) else math.nan,
                  "pref_mean_diff_only": (float(np.nanmean([v["pref_mean_diff_only"] for v in vs]))
                                          if any(not math.isnan(v["pref_mean_diff_only"]) for v in vs) else math.nan)}
    return out


def viewing_summary(people, keep: set[str]) -> dict:
    """Viewing time (s), loop counts and logged per-segment maximum head deviation (deg)."""
    v2, loops2, dev2, v1, align, start_dev, restarts, reminder_off = [], [], [], [], [], [], [], []
    for p in people:
        if p["uid"] not in keep:
            continue
        for r in p["part2"]:
            if r.get("block") != "main":
                continue
            for seg in ("a", "b"):
                v2.append(to_float(r.get(f"view_{seg}_s")))
                loops2.append(to_float(r.get(f"loops_{seg}")))
                dev2.append(to_float(r.get(f"max_dev_{seg}_deg")))
            align.append(to_float(r.get("align_s")))
            start_dev.append(to_float(r.get("start_dev_deg")))
            if r.get("restarts_a") not in (None, "") or r.get("restarts_b") not in (None, ""):
                restarts.append(to_float(r.get("restarts_a") or 0) + to_float(r.get("restarts_b") or 0))
            for seg in ("a", "b"):
                if r.get(f"reminder_off_{seg}") not in (None, ""):
                    reminder_off.append(to_float(r.get(f"reminder_off_{seg}")))
        for r in p["part1"]:
            if r.get("block") == "main":
                v1.append(to_float(r.get("view_s")))
    return {"part2_view_s": summary_stats(v2), "part2_loops": summary_stats(loops2), "part2_max_dev_deg": summary_stats(dev2),
            "part1_view_s": summary_stats(v1), "part2_align_s": summary_stats(align),
            "part2_start_dev_deg": summary_stats(start_dev),
            "part2_restarts_per_trial": summary_stats(restarts),
            "part2_reminder_off_segments": {"n": len(reminder_off), "count": int(sum(v for v in reminder_off if not math.isnan(v)))},
            "break_s": summary_stats([to_float(p["session"].get("break_s")) for p in people if p["uid"] in keep])}


def analyse_part2(people, keep: set[str], acfg: dict, excluded_trials: set) -> dict:
    c_pa, c_paw = acfg["conditions_part2"]
    per_p = {}
    per_clip = defaultdict(lambda: {"hits": 0, "n_signal": 0, "fas": 0, "n_noise": 0, "pref": []})
    for p in people:
        if p["uid"] not in keep:
            continue
        hits = n_sig = fas = n_noise = 0
        prefs, prefs_diff_only = [], []
        for r in p["part2"]:
            if r["block"] != "main" or (p["uid"], "2", r["trial_index"]) in excluded_trials:
                continue
            diff = str(r["different"]).strip() in ("1", "true", "True", "yes")
            pc = per_clip[r["clip_id"]]
            if r["pair_type"] == "real":
                n_sig += 1
                hits += diff
                pc["n_signal"] += 1
                pc["hits"] += diff
                if diff:
                    v = to_float(r["preference"])
                    # preference: -3 = A much better ... +3 = B much better; sign toward PAw
                    signed = v if r["cond_b"] == c_paw else -v
                    prefs.append(signed)
                    prefs_diff_only.append(signed)
                    pc["pref"].append(signed)
                elif acfg["preference_no_difference_as_zero"]:
                    prefs.append(0.0)
                    pc["pref"].append(0.0)
            else:
                n_noise += 1
                fas += diff
                pc["n_noise"] += 1
                pc["fas"] += diff
        if n_sig == 0 or n_noise == 0:
            continue
        d, h_ll, f_ll = dprime(hits, n_sig, fas, n_noise)
        h, f = hits / n_sig, fas / n_noise
        per_p[p["uid"]] = {"H": h, "F": f, "HF": h - f, "dprime_loglinear": d, "H_loglinear": h_ll, "F_loglinear": f_ll,
                           "hits": hits, "n_signal": n_sig, "fas": fas, "n_noise": n_noise,
                           "pref_mean": float(np.mean(prefs)) if prefs else math.nan,
                           "pref_mean_diff_only": float(np.mean(prefs_diff_only)) if prefs_diff_only else math.nan}
    prefs = [v["pref_mean"] for v in per_p.values()]
    prefs_do = [v["pref_mean_diff_only"] for v in per_p.values()]
    clip_tab = {}
    for clip, c in per_clip.items():
        ok = c["n_signal"] and c["n_noise"]
        d = dprime(c["hits"], c["n_signal"], c["fas"], c["n_noise"])[0] if ok else math.nan
        h = c["hits"] / c["n_signal"] if c["n_signal"] else math.nan
        f = c["fas"] / c["n_noise"] if c["n_noise"] else math.nan
        mean, sd, delta, n = bt500_ci(c["pref"])
        clip_tab[clip] = {"pooled_dprime_loglinear": d, "H": h, "F": f, "HF": h - f, "pref_mean": mean, "pref_ci95": delta,
                          "n_pref": n}
    a, it, sd_ = acfg["alternative"], acfg["bootstrap_iterations"], acfg["bootstrap_seed"]
    vals = list(per_p.values())
    return {"per_participant": per_p, "per_clip": clip_tab,
            "hf_test": perm_test_hf(per_p, acfg["part2_permutations"], acfg["part2_permutation_seed"]),
            "H_mean_ci": mean_ci_boot([v["H"] for v in vals], it, sd_),
            "F_mean_ci": mean_ci_boot([v["F"] for v in vals], it, sd_),
            "HF_mean_ci": mean_ci_boot([v["HF"] for v in vals], it, sd_),
            "dprime_loglinear_mean_ci": bt500_ci([v["dprime_loglinear"] for v in vals]),
            "pref_test": wilcoxon_summary(prefs, a, it, sd_),
            "pref_test_diff_only": wilcoxon_summary(prefs_do, a, it, sd_)}


def ssq_scores(row: dict) -> dict:
    v = {k: to_float(row.get(k)) for k in SSQ_ITEMS}
    n_raw = sum(v[k] for k in SSQ_N)
    o_raw = sum(v[k] for k in SSQ_O)
    d_raw = sum(v[k] for k in SSQ_D)
    return {"N": 9.54 * n_raw, "O": 7.58 * o_raw, "D": 13.92 * d_raw, "TS": 3.74 * (n_raw + o_raw + d_raw)}


def analyse_ssq(people, keep: set[str], acfg: dict) -> dict:
    scores = defaultdict(dict)  # pid -> measurement -> scores
    for p in people:
        if p["uid"] not in keep:
            continue
        for r in p["ssq"]:
            scores[p["uid"]][r["measurement"]] = ssq_scores(r)
    canonical = ["S_pre", "S_mid1", "S_mid2", "S_mid", "S_post"]
    seen = {m for d in scores.values() for m in d}
    measurements = [m for m in canonical if m in seen] + sorted(seen - set(canonical))
    summary = {}
    for m in measurements:
        for k in ("N", "O", "D", "TS"):
            vals = [scores[pid][m][k] for pid in scores if m in scores[pid]]
            summary[(m, k)] = {"mean": float(np.mean(vals)), "median": float(np.median(vals)),
                               "sd": float(np.std(vals, ddof=1)) if len(vals) > 1 else math.nan, "n": len(vals)}
    contrasts = {}
    a, it, sd_ = acfg["alternative"], acfg["bootstrap_iterations"], acfg["bootstrap_seed"]
    pairs = [tuple(x) for x in acfg.get("ssq_descriptive_contrasts") or []]
    if "S_mid" in seen:
        pairs += [("S_pre", "S_mid"), ("S_mid", "S_post")]
    pairs.append(tuple(acfg["ssq_primary_contrast"]))
    for pre, post in dict.fromkeys(pairs):
        for k in ("N", "O", "D", "TS"):
            diffs = [scores[pid][post][k] - scores[pid][pre][k] for pid in scores
                     if pre in scores[pid] and post in scores[pid]]
            contrasts[(pre, post, k)] = wilcoxon_summary(diffs, a, it, sd_)
    return {"summary": summary, "contrasts": contrasts, "measurements": measurements}


def analyse_stereotest(people, acfg: dict) -> dict:
    """Per session: correct / total at each disparity level (main trials only). Levels are display pixels
    (contract v3) or arcseconds in older folders; "cant_tell" answers count as not correct."""
    per_p, levels, scale, cant = {}, set(), {}, {}
    unit = "px"
    for p in people:
        acc = defaultdict(lambda: [0, 0])
        for r in p["stereotest"]:
            if r.get("block") != "main":
                continue
            if "disparity_px" in r:
                d = to_float(r["disparity_px"])
            else:
                d, unit = to_float(r["disparity_arcsec"]), "arcsec"
            levels.add(d)
            acc[d][0] += str(r["correct"]).strip() in ("1", "true", "True")
            acc[d][1] += 1
        if acc:
            per_p[p["uid"]] = {d: tuple(v) for d, v in acc.items()}
            arc = [to_float(r.get("arcsec_per_px")) for r in p["stereotest"] if r.get("arcsec_per_px") not in (None, "")]
            scale[p["uid"]] = (float(np.median(arc)) if arc else to_float(p["session"].get("stereotest_arcsec_per_px")),
                               p["session"].get("stereotest_px_source", ""))
            cant[p["uid"]] = sum(1 for r in p["stereotest"] if r.get("block") == "main" and r.get("response") == "cant_tell")
    rule = acfg.get("stereo_pass_rule")
    passed = {}
    if rule:
        # max_disparity_px: the largest disparity a participant's threshold may have. The participant must reach
        # min_correct_per_level at this level and at every larger level; smaller levels are reported only.
        # 2026-11-23: the comparison was d <= limit (every smaller level required), which inverted the rule of the
        # analysis plan; corrected to d >= limit before the freeze (no study data were analysed with the old rule).
        limit = rule.get("max_disparity_px", rule.get("max_disparity_arcsec"))
        for uid, acc in per_p.items():
            need = [d for d in acc if d >= limit]
            passed[uid] = bool(need) and all(acc[d][0] >= rule["min_correct_per_level"] for d in need)
    return {"per_participant": per_p, "levels": sorted(levels, reverse=True), "passed": passed, "unit": unit,
            "arcsec_per_px": scale, "cant_tell": cant}


def load_pre_session(path) -> dict:
    rows = read_csv(Path(path)) if path else []
    return {r["participant_id"]: r for r in rows if r.get("participant_id")}


def yes(v) -> bool:
    return str(v).strip().lower() in ("1", "yes", "y", "true", "是")


# ----------------------------------------------------------------------------- reporting
def fmt(x, nd=2) -> str:
    if x is None or (isinstance(x, float) and math.isnan(x)):
        return "—"
    return f"{x:.{nd}f}"


def fmt_p(p) -> str:
    if p is None or (isinstance(p, float) and math.isnan(p)):
        return "—"
    return "<0.001" if p < 0.001 else f"{p:.3f}"


def md_table(header: list[str], rows: list[list[str]]) -> str:
    lines = ["| " + " | ".join(header) + " |", "| " + " | ".join("---" for _ in header) + " |"]
    lines += ["| " + " | ".join(r) + " |" for r in rows]
    return "\n".join(lines)


def write_csv(path: Path, header: list[str], rows: list[list]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f)
        w.writerow(header)
        w.writerows(rows)


def write_tex(path: Path, header: list[str], rows: list[list[str]], caption: str) -> None:
    body = " \\\\\n".join(" & ".join(r) for r in rows)
    tex = ("% generated by analyze_study.py\n\\begin{tabular}{" + "l" * len(header) + "}\n\\toprule\n"
           + " & ".join(header) + " \\\\\n\\midrule\n" + body + " \\\\\n\\bottomrule\n\\end{tabular}\n"
           + f"% caption: {caption}\n")
    path.write_text(tex, encoding="utf-8")


def make_figures(out: Path, p1: dict, p2: dict, ssq: dict, acfg: dict) -> list[str]:
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        return []
    figs = []
    (out / "figs").mkdir(parents=True, exist_ok=True)
    conds = p1["conditions"]
    fig, ax = plt.subplots(figsize=(6.4, 3.2))
    width = 0.8 / max(1, len(conds))
    x = np.arange(len(SCALES))
    for k, cond in enumerate(conds):
        m = [p1["per_condition"][(s, cond)]["mos"] for s in SCALES]
        e = [p1["per_condition"][(s, cond)]["ci95"] for s in SCALES]
        ax.bar(x + (k - (len(conds) - 1) / 2) * width, m, width, yerr=e, capsize=3, label=cond)
    ax.set_xticks(x, ["Sense of depth", "Viewing comfort", "Visual artifacts"])
    ax.set_ylim(1, 5)
    ax.set_ylabel("MOS (95% CI)")
    ax.legend(frameon=False)
    fig.tight_layout()
    fig.savefig(out / "figs" / "part1_mos.png", dpi=200)
    plt.close(fig)
    figs.append("figs/part1_mos.png")

    hfs = [v["HF"] for v in p2["per_participant"].values()]
    prefs = [v["pref_mean"] for v in p2["per_participant"].values()]
    if hfs:
        fig, axs = plt.subplots(1, 2, figsize=(6.4, 2.8))
        axs[0].hist(hfs, bins=12)
        axs[0].axvline(0, color="k", lw=0.8)
        axs[0].set_xlabel("H - F per participant")
        axs[1].hist(prefs, bins=12)
        axs[1].axvline(0, color="k", lw=0.8)
        axs[1].set_xlabel("Mean preference (+ = pose-aware)")
        fig.tight_layout()
        fig.savefig(out / "figs" / "part2_hf_pref.png", dpi=200)
        plt.close(fig)
        figs.append("figs/part2_hf_pref.png")
    return figs


def run(args) -> int:
    cfg = json.loads(Path(args.config).read_text(encoding="utf-8"))
    acfg = dict(DEFAULT_ANALYSIS)
    if args.analysis:
        acfg.update(json.loads(Path(args.analysis).read_text(encoding="utf-8")))
    people = load_participants(Path(args.data))
    if not people:
        print("no participant folders with session.json found", file=sys.stderr)
        return 2
    out = Path(args.out)
    (out / "tables").mkdir(parents=True, exist_ok=True)

    pre_path = Path(args.pre_session) if args.pre_session else (Path(args.data) / "pre_session.csv")
    pre_sess = load_pre_session(pre_path if pre_path.is_file() else None)
    modes = defaultdict(list)
    for p in people:
        modes[p["session"].get("mode", "formal")].append(p["uid"])
    if acfg.get("session_modes"):
        people = [p for p in people if p["session"].get("mode", "formal") in acfg["session_modes"]]
    st = analyse_stereotest(people, acfg)
    simulated = [p["uid"] for p in people if p["session"].get("simulated")]
    folders_of = defaultdict(list)
    for p in people:
        folders_of[p["pid"]].append((p["uid"], p["session"].get("status", "")))
    duplicates = {pid: v for pid, v in folders_of.items() if len(v) > 1}
    complete = {p["uid"] for p in people if p["session"].get("status") == "complete"}
    no_paper = sorted(p["uid"] for p in people if paper_forms_confirmed(p["checkin"]) is not True)
    vision_flag = {pid for pid, r in pre_sess.items()
                   if not (yes(r.get("corrected_vision_normal")) and yes(r.get("color_vision_normal")))}
    excluded_by_rule = set()
    if acfg.get("exclude_self_reported_vision"):
        excluded_by_rule |= {p["uid"] for p in people if p["pid"] in vision_flag}
    if st["passed"]:
        excluded_by_rule |= {uid for uid, ok in st["passed"].items() if not ok}
    complete -= excluded_by_rule

    def n_main(q) -> int:
        return (sum(1 for r in q["part1"] if r.get("block") == "main")
                + sum(1 for r in q["part2"] if r.get("block") == "main"))

    by_pid = defaultdict(list)
    for p in people:
        by_pid[p["pid"]].append(p)
    partial = set()
    if acfg.get("include_aborted_partial"):
        for pid, qs in by_pid.items():
            if any(q["session"].get("status") == "complete" for q in qs):
                continue  # a complete session of this participant exists; aborted attempts stay out
            cand = [q for q in qs if q["uid"] not in excluded_by_rule and n_main(q) > 0]
            if cand:
                partial.add(max(cand, key=n_main)["uid"])
    analysed = complete | partial
    incomplete = sorted({p["uid"] for p in people} - analysed)
    progress = {}
    for p in people:
        if p["session"].get("status") != "complete":
            progress[p["uid"]] = {
                "status": p["session"].get("status", ""), "reason": p["session"].get("abort_reason", ""),
                "part1_main": sum(1 for r in p["part1"] if r.get("block") == "main"),
                "part2_main": sum(1 for r in p["part2"] if r.get("block") == "main"),
                "ssq": [r.get("measurement", "") for r in p["ssq"]],
                "analysed": p["uid"] in partial}

    pids, pres, u = part1_matrix([p for p in people if p["uid"] in analysed])
    kurt = screen_kurtosis(pids, u)
    corr = screen_correlation(pids, u, acfg["correlation_mct"])
    variants = {
        "none": set(analysed),
        "kurtosis": {uid for uid in analysed if not kurt.get(uid, {}).get("reject", False)},
        "correlation": {uid for uid in analysed if not corr["observers"].get(uid, {}).get("reject", False)},
    }
    if acfg.get("sensitivity_complete_only"):
        variants["complete_only"] = set(complete)

    hd = head_deviation(people)
    thr = acfg["head_deviation_threshold_deg"]
    head_flagged = {(uid, "2", t) for (uid, t), v in hd.items() if thr is not None and v["max"] > thr}
    pb = playback_flags(people, acfg["dropped_frame_fraction_threshold"])
    excluded_trials = head_flagged | pb["flagged"]

    results = {}
    for name, keep in variants.items():
        p1 = analyse_part1(people, keep, cfg, acfg, excluded_trials)
        p2 = analyse_part2(people, keep, acfg, excluded_trials)
        sq = analyse_ssq(people, keep, acfg)
        alpha, gate = acfg["alpha"], acfg.get("p4b_requires")
        p4a = {"p": p2["hf_test"]["p"], "alpha": alpha}
        p4a["reject"] = None if math.isnan(p4a["p"]) else bool(p4a["p"] < alpha)
        p4b = {"requires": gate, "alpha": alpha, "p": p2["pref_test"]["p"], "n": p2["pref_test"]["n"],
               "tested": gate is None, "gate_met": None}
        if gate == "P4a":
            p4b["gate_met"] = p4a["reject"]
            p4b["tested"] = bool(p4a["reject"])
        results[name] = {"part1": p1, "part2": p2, "ssq": sq, "n": len(keep), "p4a": p4a, "p4b": p4b,
                         "part1_revisions": part1_revision_summary(people, keep),
                         "part2_by_theta": part2_by_theta(people, p2)}
    gaze = gaze_training_summary(people)
    eye = eye_range_summary(people, analysed)
    viewing = viewing_summary(people, analysed)

    # ------------------------------------------------------------------ report
    L = []
    L.append("# Pano2Stereo 主观实验分析报告\n")
    if simulated:
        L.append(f"> **模拟数据**：{len(simulated)} 个会话的 session.json 标记为 simulated。本报告只用于检查程序，数字不是实验结果。\n")
    L.append(f"- 会话文件夹：{len(people)}；完整完成并进入分析：{len(complete)}；中止但已完成部分进入分析：{len(partial)}"
             f"（{', '.join(sorted(partial)) or '无'}）；未进入分析：{', '.join(incomplete) or '无'}")
    if progress:
        L.append("- 未完成的会话（status ≠ complete）完成到哪里：" + "；".join(
            f"{uid}（{v['status'] or '—'}{'，' + v['reason'] if v['reason'] else ''}）第一部分正式 {v['part1_main']} 段、"
            f"第二部分正式 {v['part2_main']} 个、SSQ {'/'.join(v['ssq']) or '无'}，{'进入分析' if v['analysed'] else '未进入分析'}"
            for uid, v in sorted(progress.items())))
    L.append("- 会话模式：" + "；".join(f"{m} {len(v)}" for m, v in sorted(modes.items()))
             + (f"（QA 会话：{', '.join(modes['qa'])}）" if modes.get("qa") else "")
             + ("；按配置只分析 " + ", ".join(acfg["session_modes"]) if acfg.get("session_modes") else "；未按模式筛选"))
    if duplicates:
        L.append("- 同一编号有多个会话文件夹（只有 complete 会话进入分析；同一编号有多个 complete 会话时需用户裁决）："
                 + "；".join(f"{pid}: " + ", ".join(f"{u}（{st_}）" for u, st_ in v) for pid, v in sorted(duplicates.items())))
    L.append(f"- checkin.json 未确认纸质知情同意与基本信息表：{', '.join(no_paper) or '无'}")
    skipped_rows = {p["uid"]: sum(p["skipped"].values()) for p in people if sum(p["skipped"].values())}
    L.append("- 跳过的行（skipped = 1，已排除；QA 跳过或主试 F8 跳过）：" + ("；".join(f"{u} {n} 行" for u, n in sorted(skipped_rows.items())) or "无"))
    staff_rows = {p["uid"]: {f: v.count("staff") for f, v in p["skip_reasons"].items() if v.count("staff")} for p in people}
    L.append("- 其中主试跳过（skip_reason = staff）：" + ("；".join(f"{u} " + "，".join(f"{f} {n} 行" for f, n in v.items())
                                                        for u, v in sorted(staff_rows.items()) if v) or "无"))
    gate = acfg.get("p4b_requires")
    L.append(f"- 检验方向：{acfg['alternative']}；α = {acfg['alpha']}；第一部分：每项评分做 Friedman 总体检验，"
             f"{len(acfg['part1_pairs'])} 对两两 Wilcoxon 在该项评分内 Holm 校正（三项评分分别校正）；第二部分：P4a 单独检验，P4b "
             + (f"只在 {gate} 显著时检验，否则只报数" if gate else "不设前提") + "；SSQ 只报数")
    L.append(f"- 第二部分 P4a：每人 H − F（原始比例）的均值，被试内置换检验 {acfg['part2_permutations']} 次，种子 {acfg['part2_permutation_seed']}")
    L.append(f"- 头部偏离阈值：{thr if thr is not None else '未设定（只报告分布）'}；丢帧比例阈值："
             f"{acfg['dropped_frame_fraction_threshold'] if acfg['dropped_frame_fraction_threshold'] is not None else '未设定（只报告分布）'}")
    sp = acfg["screening_primary"]
    L.append(f"- 观察者筛除的主口径：{sp if sp else '未设定，下文按三种口径分别报告'}\n")

    L.append("## 0. 被试信息与立体视测试\n")
    if pre_sess:
        ages = [to_float(r.get("age")) for r in pre_sess.values() if not math.isnan(to_float(r.get("age")))]
        genders, vr = defaultdict(int), defaultdict(int)
        for r in pre_sess.values():
            genders[r.get("gender", "")] += 1
            vr[r.get("vr_experience", "")] += 1
        age_sd = float(np.std(ages, ddof=1)) if len(ages) > 1 else math.nan
        L.append(f"- 纸质基本信息表（录入 pre_session.csv）：{len(pre_sess)} 人；年龄 {fmt(float(np.mean(ages)), 1)} ± {fmt(age_sd, 1)}；"
                 f"性别 {dict(genders)}；VR 经验 {dict(vr)}")
        L.append(f"- 自报视力或色觉异常：{', '.join(sorted(vision_flag)) or '无'}"
                 f"（{'已按配置剔除' if acfg.get('exclude_self_reported_vision') else '只报告，未剔除'}）")
        missing = sorted({p['pid'] for p in people} - set(pre_sess))
        L.append(f"- 有实验数据但无基本信息表记录：{', '.join(missing) or '无'}")
    else:
        L.append("- 未找到 pre_session.csv。")
    if st["per_participant"]:
        rows = []
        unit = "px" if st["unit"] == "px" else "″"
        for uid, acc in sorted(st["per_participant"].items()):
            verdict = ("通过" if st["passed"][uid] else "未通过") if st["passed"] else "—"
            arc, src = st["arcsec_per_px"].get(uid, (math.nan, ""))
            rows.append([uid] + [f"{acc.get(d, (0, 0))[0]}/{acc.get(d, (0, 0))[1]}" for d in st["levels"]]
                        + [str(st["cant_tell"].get(uid, 0)), f"{fmt(arc, 1)}（{src or '—'}）", verdict])
        L.append("\n立体视测试（头显内随机点立体图，正式试次正确数/试次数；“看不清”计为不正确）：\n")
        L.append(md_table(["被试"] + [f"{d:g} {unit}" for d in st["levels"]] + ["看不清次数", "″/px（来源）", "按规则"], rows))
        L.append(f"\n通过规则：{acfg.get('stereo_pass_rule') or '未设定（只报告）'}。视差以渲染像素计，″/px 为视野中心一个渲染像素的角宽；"
                 "本测试为自制粗筛，未与 Randot 做等效验证。\n")
    if excluded_by_rule:
        L.append(f"按配置规则剔除：{', '.join(sorted(excluded_by_rule))}\n")
    L.append("## 1. 观察者筛除（BT.500-15 Annex 1 to Part 1 §A1-2.3）\n")
    L.append("BT.500-15 注明峰度法适用于观察者较少（如少于 20 人）且均为非专家的情形；相关法的 MCT 对单刺激法取 0.7。\n")
    rows = []
    for pid in pids:
        k = kurt[pid]
        c = corr["observers"].get(pid, {"r": math.nan, "reject": False})
        rows.append([pid, str(k["P"]), str(k["Q"]), fmt(k["ratio1"], 3), fmt(k["ratio2"], 3),
                     "剔除" if k["reject"] else "保留", fmt(c["r"], 3), "剔除" if c["reject"] else "保留"])
    L.append(md_table(["被试", "P", "Q", "(P+Q)/L", "|P−Q|/(P+Q)", "峰度法", "r=min(Pearson,Spearman)", "相关法"], rows))
    L.append(f"\n相关法阈值 {fmt(corr['threshold'], 3)}（mean(r)={fmt(corr['mean_r'], 3)}，sd(r)={fmt(corr['sd_r'], 3)}）。\n")
    write_csv(out / "tables" / "screening.csv",
              ["pid", "kurt_P", "kurt_Q", "kurt_ratio1", "kurt_ratio2", "kurt_reject", "corr_r", "corr_reject"],
              [[pid, kurt[pid]["P"], kurt[pid]["Q"], kurt[pid]["ratio1"], kurt[pid]["ratio2"], kurt[pid]["reject"],
                corr["observers"].get(pid, {}).get("r"), corr["observers"].get(pid, {}).get("reject")] for pid in pids])

    c_pa, c_paw = acfg["conditions_part2"]
    alpha = acfg["alpha"]
    variant_label = {"none": "none（主分析：完整会话 + 中止会话已完成的部分）", "kurtosis": "kurtosis（敏感性）",
                     "correlation": "correlation（敏感性）", "complete_only": "complete_only（只用完整会话，敏感性）"}
    for name in [n for n in ("none", "kurtosis", "correlation", "complete_only") if n in results]:
        R = results[name]
        L.append(f"## 2. 结果（筛除口径：{variant_label[name]}，N = {R['n']}）\n")
        L.append("### 2.1 第一部分（四种方法，正式检验）\n")
        conds = R["part1"]["conditions"]
        L.append(md_table(["量表"] + [f"{c} MOS ± 95% CI" for c in conds],
                          [[SCALE_LABEL[s]] + [f"{fmt(R['part1']['per_condition'][(s, c)]['mos'])} ± {fmt(R['part1']['per_condition'][(s, c)]['ci95'])}"
                                               for c in conds] for s in SCALES]))
        L.append("")
        csv_rows = []
        for s in SCALES:
            fr = R["part1"]["friedman"][s]
            L.append(f"\n**{SCALE_LABEL[s]}**：Friedman χ² = {fmt(fr['chi2'])}，k = {fr['k']}，N = {fr['n']}（{len(conds)} 种方法都有值的被试），"
                     f"p {fmt_p(fr['p'])}，Kendall W = {fmt(fr['kendall_w'], 3)}。两两比较（Wilcoxon 符号秩，双尾，本项评分 "
                     f"{len(R['part1']['pairs'])} 对内 Holm 校正）：\n")
            rows = []
            for ref, test in R["part1"]["pairs"]:
                t = R["part1"]["pairwise"][(s, ref, test)]
                h = R["part1"]["holm"][(s, ref, test)]
                pc = R["part1"]["pair_counts"][(s, ref, test)]
                verdict = "—" if math.isnan(h) else ("拒绝" if h < alpha else "不拒绝")
                rows.append([f"{test} − {ref}", f"{pc['participants']} / {pc['clip_pairs']}", fmt(t["median_diff"]),
                             f"[{fmt(t['median_ci95'][0])}, {fmt(t['median_ci95'][1])}]", fmt(t["mean_diff"]), fmt(t["W"], 1),
                             fmt_p(t["p"]), fmt_p(h), fmt(t["rank_biserial"]), verdict])
                csv_rows.append([s, ref, test, pc["participants"], pc["clip_pairs"], t["median_diff"], *t["median_ci95"],
                                 t["mean_diff"], t["W"], t["p"], h, t["rank_biserial"], t["n"]])
            L.append(md_table(["配对（后者 − 前者）", "被试 / 片段对", "差值中位数", "中位数 95% CI（bootstrap）", "均值", "W", "p",
                               "Holm 校正 p", "秩二列相关 r", f"α = {alpha}"], rows))
        L.append("\n评分为组末确认后的最终评分。差值按被试内同一片段配对，每人先对片段等权平均。")
        RV = R["part1_revisions"]
        L.append(f"重评：正式试次 {RV['n_trials']} 个，有重评记录 {fmt(RV['revised_rate'] * 100 if RV['n_trials'] else math.nan, 1)}%，"
                 f"最终评分与首次评分不同 {fmt(RV['changed_rate'] * 100 if RV['n_trials'] else math.nan, 1)}%；"
                 "平均绝对改动 " + "，".join(f"{SCALE_LABEL[s].split()[0]} {fmt(RV['mean_abs_change'][s])}" for s in SCALES)
                 + f"；组末回放 {RV['replays']} 次。")
        write_csv(out / "tables" / f"part1_{name}.csv",
                  ["scale", "reference", "test", "participants", "clip_pairs", "median_diff", "median_ci_lo", "median_ci_hi",
                   "mean_diff", "W", "p", "p_holm_within_scale", "rank_biserial", "n"], csv_rows)
        write_tex(out / "tables" / f"part1_{name}.tex", ["Scale"] + conds + ["Friedman $p$"],
                  [[s.replace("_", " ")] + [f"{fmt(R['part1']['per_condition'][(s, c)]['mos'])}$\\pm${fmt(R['part1']['per_condition'][(s, c)]['ci95'])}"
                                            for c in conds] + [fmt_p(R["part1"]["friedman"][s]["p"])] for s in SCALES],
                  "Part 1 MOS with 95% CI")

        L.append("\n次要分析（事先写明，不校正）：按片段的差值（每个片段先对被试取平均，再在片段间做双尾符号检验），"
                 "以及只用序关系的统计量（每人后者高于前者的片段数减低于的片段数，除以片段数；被试间 Wilcoxon）：\n")
        rows = []
        for s in SCALES:
            for ref, test in R["part1"]["pairs"]:
                b = R["part1"]["secondary"][(s, ref, test)]["by_clip"]
                o = R["part1"]["secondary"][(s, ref, test)]["order_stat"]
                rows.append([SCALE_LABEL[s].split()[0], f"{test} − {ref}", f"{b['pos']} / {b['neg']} / {b['zero']}", fmt_p(b["p"]),
                             str(o["n"]), fmt(o["mean_diff"]), fmt(o["median_diff"]), fmt_p(o["p"])])
        L.append(md_table(["量表", "配对", "片段数 高于 / 低于 / 相等", "符号检验 p", "序关系统计量 N", "均值", "中位数", "Wilcoxon p"], rows))

        L.append("\n按片段类别（cell）分层（描述，p 未校正）：\n")
        rows = []
        for (s, ref, test, scene), t in sorted(R["part1"]["per_scene"].items()):
            rows.append([SCALE_LABEL[s].split()[0], f"{test} − {ref}", scene or "—", str(t["n"]), fmt(t["median_diff"]), fmt_p(t["p"])])
        L.append(md_table(["量表", "配对", "类别", "N", "差值中位数", "p（未校正）"], rows))

        L.append("\n逐片段 MOS：\n")
        clips = sorted({k[1] for k in R["part1"]["per_clip"]})
        rows = []
        for clip in clips:
            row = [clip]
            for s in SCALES:
                for cond in conds:
                    v = R["part1"]["per_clip"].get((s, clip, cond), {"mos": math.nan, "ci95": math.nan})
                    row.append(f"{fmt(v['mos'])} ± {fmt(v['ci95'])}")
            rows.append(row)
        short = {"depth_quality": "立体感", "visual_comfort": "舒适度", "artifact_annoyance": "瑕疵"}
        hdr = ["片段"] + [f"{short[s]}/{c}" for s in SCALES for c in conds]
        L.append(md_table(hdr, rows))

        P2 = R["part2"]
        L.append(f"\n### 2.2 第二部分：{c_pa} 对 {c_paw}\n")
        ht, hci, fci, hfci = P2["hf_test"], P2["H_mean_ci"], P2["F_mean_ci"], P2["HF_mean_ci"]
        ci = lambda c: f"{fmt(c['mean'], 3)} [{fmt(c['ci95'][0], 3)}, {fmt(c['ci95'][1], 3)}]"
        L.append(md_table(["指标（每人一个值）", "N", "均值 [bootstrap 95% CI]", "检验", "p"], [
            ["H：真实配对答“有区别”的比例", str(hci["n"]), ci(hci), "—", "—"],
            ["F：相同配对答“有区别”的比例", str(fci["n"]), ci(fci), "—", "—"],
            ["H − F（P4a，能否分辨）", str(ht["n"]), ci(hfci), f"被试内置换检验（{ht['n_perm']} 次）", fmt_p(ht["p"])],
        ]))
        dl = P2["dprime_loglinear_mean_ci"]
        L.append(f"\n描述（不作检验）：z(H) − z(F)，对数线性校正 H = (命中 + 0.5)/(真实配对数 + 1)、F 同理；"
                 f"均值 {fmt(dl[0])} ± {fmt(dl[2])}（N {dl[3]}）。这是“是–否”任务的公式，不等于同–异任务中两种渲染之间的 d′。\n")
        t2, t3, g4b = P2["pref_test"], P2["pref_test_diff_only"], R["p4b"]
        if g4b["requires"] is None:
            status = f"P4b 不设前提，按 α = {g4b['alpha']} 检验"
        elif g4b["gate_met"] is None:
            status = f"{g4b['requires']} 无法检验，P4b 未检验，只报数"
        elif g4b["gate_met"]:
            status = f"{g4b['requires']} 显著（α = {g4b['alpha']}），P4b 按 α = {g4b['alpha']} 检验"
        else:
            status = f"{g4b['requires']} 不显著，P4b 不检验，只报数（下表 p 仅供参考）"
        L.append(f"偏好（P4b）：{status}。实际进入 Wilcoxon 检验的人数为差值不为 0 的被试数（从未在真实配对上答“有区别”的被试偏好为 0，被丢弃）。\n")
        n_nz = lambda t: sum(1 for v in P2["per_participant"].values() if not math.isnan(v[t]) and v[t] != 0)
        L.append(md_table(["指标", "N", "非零 N", "均值 / 中位数", "W", "p", "r"], [
            ["偏好（答“没有区别”计 0，正值偏向 pose-aware）", str(t2["n"]), str(n_nz("pref_mean")), f"{fmt(t2['mean_diff'])} / {fmt(t2['median_diff'])}",
             fmt(t2["W"], 1), fmt_p(t2["p"]), fmt(t2["rank_biserial"])],
            ["偏好（只用答“有区别”的试次，次要）", str(t3["n"]), str(n_nz("pref_mean_diff_only")), f"{fmt(t3['mean_diff'])} / {fmt(t3['median_diff'])}",
             fmt(t3["W"], 1), fmt_p(t3["p"]), fmt(t3["rank_biserial"])],
        ]))
        L.append("\n逐片段（全体被试合并计数；z(H) − z(F) 只作描述）：\n")
        rows = [[clip, fmt(v["H"]), fmt(v["F"]), fmt(v["HF"]), fmt(v["pooled_dprime_loglinear"]), f"{fmt(v['pref_mean'])} ± {fmt(v['pref_ci95'])}"]
                for clip, v in sorted(P2["per_clip"].items())]
        L.append(md_table(["片段", "命中率 H", "虚报率 F", "H − F", "合并 z(H) − z(F)", "偏好均值 ± 95% CI"], rows))
        BT = R["part2_by_theta"]
        if BT:
            L.append("\n按 PAw 修正半径 θ 分组（描述性，不做检验；θ 由自然注视范围测量按暂定规则选定）：\n")
            rows = [[("—" if math.isnan(t) else f"{t:g}°"), str(v["n"]), fmt(v["H_mean"]), fmt(v["F_mean"]), fmt(v["HF_mean"]),
                     fmt(v["dprime_mean"]), fmt(v["pref_mean"]), fmt(v["pref_mean_diff_only"])]
                    for t, v in sorted(BT.items(), key=lambda kv: (math.isnan(kv[0]), kv[0]))]
            L.append(md_table(["θ", "人数", "H 均值", "F 均值", "H − F 均值", "z(H) − z(F) 均值（描述）", "偏好均值（无区别计 0）",
                               "偏好均值（只用有区别）"], rows))

        SQ = R["ssq"]
        L.append("\n### 2.3 SSQ（P.919 §7.2.1）\n")
        rows = []
        for m in SQ["measurements"]:
            rows.append([m] + [f"{fmt(SQ['summary'][(m, k)]['mean'], 1)} / {fmt(SQ['summary'][(m, k)]['median'], 1)}" for k in ("N", "O", "D", "TS")])
        L.append(md_table(["测量", "N 均值/中位数", "O", "D", "TS"], rows))
        primary = tuple(acfg["ssq_primary_contrast"])
        rows = []
        for (pre, post, k), t in SQ["contrasts"].items():
            if k != "TS" or (pre, post) == primary:
                continue
            rows.append([f"{post} − {pre}", str(t["n"]), fmt(t["median_diff"], 1), fmt(t["mean_diff"], 1)])
        L.append("\nSSQ 前后差值（TS，只报数）：\n")
        L.append(md_table(["差值（TS）", "N", "中位数", "均值"], rows))
        t = SQ["contrasts"].get((primary[0], primary[1], "TS"))
        if t:
            L.append(f"\n{primary[1]} − {primary[0]}（TS，只报数）：N {t['n']}，中位数 {fmt(t['median_diff'], 1)}，均值 {fmt(t['mean_diff'], 1)}。")

        L.append("\n### 2.4 正式检验汇总\n")
        short = {"depth_quality": "立体感", "visual_comfort": "舒适度", "artifact_annoyance": "瑕疵"}
        rows = []
        for s in SCALES:
            fr = R["part1"]["friedman"][s]
            rows.append([f"第一部分 {short[s]}：Friedman", fmt_p(fr["p"]), "—", "—"])
            for ref, test in R["part1"]["pairs"]:
                h = R["part1"]["holm"][(s, ref, test)]
                rows.append([f"第一部分 {short[s]}：{test} − {ref}", fmt_p(R["part1"]["pairwise"][(s, ref, test)]["p"]), fmt_p(h),
                             "—" if math.isnan(h) else ("拒绝" if h < alpha else "不拒绝")])
        a4 = R["p4a"]
        rows.append(["第二部分 P4a：H − F（置换检验，单独检验）", fmt_p(a4["p"]), "—",
                     "—" if a4["reject"] is None else ("拒绝" if a4["reject"] else "不拒绝")])
        g4b = R["p4b"]
        if g4b["tested"]:
            rows.append([f"第二部分 P4b：偏好（以 {g4b['requires'] or '无'} 为前提）", fmt_p(g4b["p"]), "—",
                         "拒绝" if g4b["p"] < g4b["alpha"] else "不拒绝"])
        else:
            rows.append([f"第二部分 P4b：偏好（以 {g4b['requires']} 为前提）", "—", "—", "未检验，只报数"])
        L.append(md_table(["检验", "p", "Holm 校正 p（本项评分内）", f"α = {alpha}"], rows))
        L.append("")

    L.append("## 3. 依从性、观看时长与播放\n")
    if eye["per_session"]:
        L.append("自然注视范围测量（注视练习之前，连续向外扫动；a 的规则与阈值为暂定，只报数，不剔除）。每次扫动的头部起动偏心度 = "
                 "头部相对扫动开始时朝向的转动首次超过阈值时圆点的偏心度；a = 全部扫动的中位数，删失（到最大偏心度仍未超过）按最大偏心度计。"
                 "下表各方向为该方向各次扫动的中位数（°）：\n")
        dirs = eye["directions"]
        rows = []
        for uid, v in sorted(eye["per_session"].items()):
            rows.append([uid, fmt(v["a_deg"], 1), fmt(v["a_recomputed"], 1), fmt(v["theta"], 0), v["source"] or "—",
                         f"{fmt(v['speed'], 0)}°/s，{fmt(v['max_ecc'], 0)}°，阈值 {fmt(v['threshold'], 1)}°",
                         f"{v['n_rows'] - v['n_skipped']}/{v['n_rows']}", str(v["censored"]), str(v["stopped_early"])]
                        + [fmt(v["per_direction"].get(d_, math.nan), 1) for d_ in dirs])
        L.append(md_table(["会话", "a（°，记录）", "a（°，逐帧复算）", "θ（°）", "θ 来源", "速度、最大偏心度、阈值", "完成/扫动", "删失", "提前结束"]
                          + [f"{d_:g}°" for d_ in dirs], rows))
        mismatch = sorted(uid for uid, v in eye["per_session"].items() if v["source"] == "measured"
                          and not (abs(v["a_deg"] - v["a_recomputed"]) < 1e-3 and v["theta"] == v["theta_recomputed"]))
        L.append(f"\n记录值与按 eyerange_trace.csv 逐帧复算不一致的会话：{', '.join(mismatch) or '无'}。"
                 "结果页时间：" + "；".join(f"{uid} {fmt(v['time_on_page_s'], 1)} s" for uid, v in sorted(eye["per_session"].items())
                                         if not math.isnan(v["time_on_page_s"])) + "。\n")
        ad = eye["a_distribution"]
        L.append(f"a 在完整会话间的分布（按测量结果选定 θ 的会话）：N {ad['n']}，最小 {fmt(ad['min'], 1)}°，P25 {fmt(ad['p25'], 1)}°，"
                 f"中位数 {fmt(ad['median'], 1)}°，P75 {fmt(ad['p75'], 1)}°，最大 {fmt(ad['max'], 1)}°。\n")
        L.append("各方向头部起动偏心度中位数在被试间的分布（完整会话，°，只报数）：\n")
        L.append(md_table(["方向", "N", "最小", "P25", "中位数", "P75", "最大"],
                          [[f"{d_:g}°", str(x["n"]), fmt(x["min"], 1), fmt(x["p25"], 1), fmt(x["median"], 1), fmt(x["p75"], 1), fmt(x["max"], 1)]
                           for d_, x in eye["distribution"].items()]))
        L.append("")
        write_csv(out / "tables" / "eye_range.csv", ["uid", "a_deg", "a_recomputed", "paw_theta_deg", "source", "censored",
                                                    "speed_deg_s", "max_eccentricity_deg", "threshold_deg"]
                  + [f"median_onset_ecc_dir_{d_:g}" for d_ in dirs],
                  [[uid, v["a_deg"], v["a_recomputed"], v["theta"], v["source"], v["censored"], v["speed"], v["max_ecc"], v["threshold"]]
                   + [v["per_direction"].get(d_, math.nan) for d_ in dirs] for uid, v in sorted(eye["per_session"].items())])
    if gaze:
        L.append("注视练习（第二部分前；阈值与通过与否为程序按会话配置记录，分析不另设规则）：\n")
        rows = [[uid, str(v["attempts"]), " / ".join(fmt(x, 1) for x in v["max_dev_per_attempt"]), fmt(v["threshold_deg"], 1),
                 {True: "是", False: "否", None: "—"}[v["last_attempt_passed"]]] for uid, v in sorted(gaze.items())]
        L.append(md_table(["会话", "次数", "每次最大头部偏离（°）", "程序阈值（°）", "最后一次在阈值内"], rows))
        L.append("")
    vs = viewing
    L.append(md_table(["观看指标（完整会话，正式试次）", "N", "中位数", "p95", "最大"], [
        ["第二部分每段观看时长（s）", str(vs["part2_view_s"]["n"]), fmt(vs["part2_view_s"]["median"], 1), fmt(vs["part2_view_s"]["p95"], 1), fmt(vs["part2_view_s"]["max"], 1)],
        ["第二部分每段循环次数", str(vs["part2_loops"]["n"]), fmt(vs["part2_loops"]["median"], 0), fmt(vs["part2_loops"]["p95"], 0), fmt(vs["part2_loops"]["max"], 0)],
        ["第二部分每段最大头部偏离（°，part2.csv）", str(vs["part2_max_dev_deg"]["n"]), fmt(vs["part2_max_dev_deg"]["median"], 1), fmt(vs["part2_max_dev_deg"]["p95"], 1), fmt(vs["part2_max_dev_deg"]["max"], 1)],
        ["第一部分每段观看时长（s）", str(vs["part1_view_s"]["n"]), fmt(vs["part1_view_s"]["median"], 1), fmt(vs["part1_view_s"]["p95"], 1), fmt(vs["part1_view_s"]["max"], 1)],
        ["第二部分朝向对准耗时（s）", str(vs["part2_align_s"]["n"]), fmt(vs["part2_align_s"]["median"], 1), fmt(vs["part2_align_s"]["p95"], 1), fmt(vs["part2_align_s"]["max"], 1)],
        ["第二部分开始时头部偏离（°）", str(vs["part2_start_dev_deg"]["n"]), fmt(vs["part2_start_dev_deg"]["median"], 1), fmt(vs["part2_start_dev_deg"]["p95"], 1), fmt(vs["part2_start_dev_deg"]["max"], 1)],
        ["第二部分每试次因转头重播次数（contract v5）", str(vs["part2_restarts_per_trial"]["n"]), fmt(vs["part2_restarts_per_trial"]["median"], 0), fmt(vs["part2_restarts_per_trial"]["p95"], 0), fmt(vs["part2_restarts_per_trial"]["max"], 0)],
        ["第二部分关闭转头提醒后播放的段数（计数，不排除）", str(vs["part2_reminder_off_segments"]["n"]), str(vs["part2_reminder_off_segments"]["count"]), "—", "—"],
        ["中场实际休息时长（s）", str(vs["break_s"]["n"]), fmt(vs["break_s"]["median"], 0), fmt(vs["break_s"]["p95"], 0), fmt(vs["break_s"]["max"], 0)],
    ]))
    L.append("")
    if hd:
        mx = np.array([v["max"] for v in hd.values()])
        L.append(f"第二部分每个试次的头部最大偏离角：中位数 {fmt(float(np.median(mx)), 1)}°，p95 {fmt(float(np.percentile(mx, 95)), 1)}°，"
                 f"最大 {fmt(float(mx.max()), 1)}°（{len(mx)} 个试次）。超过阈值的试次：{len(head_flagged) if thr is not None else '未设定阈值'}。")
    fr = np.array(list(pb["max_fraction"].values())) if pb["max_fraction"] else np.array([])
    if fr.size:
        L.append(f"\n丢帧比例（每个试次取最大段）：中位数 {fmt(float(np.median(fr)), 4)}，最大 {fmt(float(fr.max()), 4)}。"
                 f"超过阈值的试次：{len(pb['flagged']) if acfg['dropped_frame_fraction_threshold'] is not None else '未设定阈值'}。")
    figs = make_figures(out, results["none"]["part1"], results["none"]["part2"], results["none"]["ssq"], acfg)
    if figs:
        L.append("\n## 4. 图（筛除口径：none）\n")
        L += [f"![]({f})" for f in figs]
    (out / "report.md").write_text("\n".join(L) + "\n", encoding="utf-8")

    def jsonable(o):
        if isinstance(o, dict):
            return {(" | ".join(map(str, k)) if isinstance(k, tuple) else str(k)): jsonable(v) for k, v in o.items()}
        if isinstance(o, (list, tuple)):
            return [jsonable(v) for v in o]
        if isinstance(o, set):
            return sorted(jsonable(v) for v in o)
        if isinstance(o, float) and math.isnan(o):
            return None
        if isinstance(o, (np.floating, np.integer)):
            return o.item()
        return o
    (out / "results.json").write_text(json.dumps(jsonable({"analysis_config": acfg, "simulated": simulated,
                                                           "incomplete": incomplete, "duplicates": duplicates,
                                                           "modes": dict(modes), "gaze_training": gaze, "eye_range": eye,
                                                           "viewing": viewing, "stereotest": st, "results": results}),
                                                          ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"report written to {out / 'report.md'} ({len(complete)} complete participant(s))")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--data", required=True)
    ap.add_argument("--config", required=True, help="study_config.json used to build the plans")
    ap.add_argument("--analysis", default=None, help="optional analysis_config.json overriding the defaults")
    ap.add_argument("--out", required=True)
    ap.add_argument("--pre-session", default=None, help="pre_session.csv transcribed from the paper background form (default: <data>/pre_session.csv)")
    return run(ap.parse_args())


if __name__ == "__main__":
    sys.exit(main())
