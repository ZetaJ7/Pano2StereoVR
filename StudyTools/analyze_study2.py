"""Analysis of Study 2 (SSQ, Mono vs Ours PA crossover), design docs/Study2_SSQ_Design_20261123.md section 5.

Usage:
    python analyze_study2.py --mono StudyData_s2_mono --ours StudyData_s2_ours --groups study2_groups.csv --out report_s2
    python analyze_study2.py --simulate 2000 --out report_s2_sim      (no data: type-I error and power check)

Per session: dTS = TS(S_post) - TS(S_pre) (ITU-T P.919 7.2.1 scoring, analyze_study.ssq_scores), the same for the
O (oculomotor) subscale, and the session mean of the Part 1 comfort ratings (main trials, not skipped).
Primary (user 11-23): Ours - Mono in dTS, two-sided, alpha 0.05, corrected for the period effect: per participant
c = dTS(day 1) - dTS(day 2); estimate (Ours - Mono) = (mean c[OM] - mean c[MO]) / 2, period effect
(day 1 - day 2) = (mean c[MO] + mean c[OM]) / 2; test = permutation of the order-group labels (statistic
mean c[OM] - mean c[MO], zeros kept, 100000 permutations, seed 20261124; exact when the number of label sets is
small enough). Secondary: paired D = dTS(Ours) - dTS(Mono), two-sided Wilcoxon signed-rank. Secondary measures
named in advance: the O subscale and the session comfort mean, the same two analyses. Descriptive: N and D
subscales, pre-test values per condition, zero counts, Ours above / below / equal Mono, the carry-over difference
of c between groups (described only). A participant enters when both sessions have S_pre and S_post; a session
stopped early uses the S_post written at the stop (the report lists such sessions).
"""
from __future__ import annotations

import argparse
import csv
import itertools
import json
import math
import random
import statistics
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from analyze_study import ssq_scores  # noqa: E402

PERMUTATIONS = 100000
SEED = 20261124
ALPHA = 0.05


def read_csv(p: Path) -> list[dict]:
    if not p.is_file():
        return []
    with p.open(encoding="utf-8", newline="") as fh:
        return list(csv.DictReader(fh))


def session_dirs(root: Path) -> dict[str, Path]:
    """Formal sessions by participant (top-level folders with session.json, mode formal; the latest if several)."""
    out = {}
    for d in sorted(root.iterdir()) if root.is_dir() else []:
        s = d / "session.json"
        if not s.is_file():
            continue
        meta = json.loads(s.read_text(encoding="utf-8"))
        if meta.get("mode", "formal") != "formal":
            continue
        out[meta.get("participant_id", d.name)] = d
    return out


def session_values(d: Path) -> dict | None:
    ssq = {r["measurement"]: ssq_scores(r) for r in read_csv(d / "ssq.csv")}
    if "S_pre" not in ssq or "S_post" not in ssq:
        return None
    meta = json.loads((d / "session.json").read_text(encoding="utf-8"))
    p1 = [r for r in read_csv(d / "part1.csv") if r.get("block") == "main" and r.get("skipped") != "1"]
    comfort = [float(r["visual_comfort"]) for r in p1 if r.get("visual_comfort") not in (None, "")]
    return {"dTS": ssq["S_post"]["TS"] - ssq["S_pre"]["TS"], "dO": ssq["S_post"]["O"] - ssq["S_pre"]["O"],
            "dN": ssq["S_post"]["N"] - ssq["S_pre"]["N"], "dD": ssq["S_post"]["D"] - ssq["S_pre"]["D"],
            "pre_TS": ssq["S_pre"]["TS"], "comfort": statistics.mean(comfort) if comfort else None,
            "n_rated": len(p1), "status": meta.get("status"), "start_utc": meta.get("start_utc")}


def crossover(c_mo: list[float], c_om: list[float], perms: int | None = None, seed: int = SEED) -> dict:
    """Estimate and two-sided permutation p of Ours - Mono from the period differences c of the two order groups."""
    import numpy as np
    perms = perms or PERMUTATIONS
    allc = np.asarray(c_mo + c_om, dtype=float)
    n, k = len(allc), len(c_mo)
    obs = float(np.mean(c_om) - np.mean(c_mo))
    total, tot_sum = math.comb(n, k), float(allc.sum())
    if total <= perms:
        idx = np.array(list(itertools.combinations(range(n), k)), dtype=int)
        exact = True
    else:
        rng = np.random.default_rng(seed)
        idx = np.argsort(rng.random((perms, n)), axis=1)[:, :k]
        exact = False
    s_mo = allc[idx].sum(axis=1)
    stats = (tot_sum - s_mo) / (n - k) - s_mo / k
    ge = int(np.sum(np.abs(stats) >= abs(obs) - 1e-12))
    cnt = len(stats)
    p = ge / cnt if exact else (ge + 1) / (cnt + 1)
    return {"estimate_ours_minus_mono": obs / 2, "period_effect_day1_minus_day2": float((np.mean(c_mo) + np.mean(c_om)) / 2),
            "p_perm": p, "exact": exact, "n_mo": k, "n_om": n - k, "permutations": cnt,
            "carryover_c_mean_mo": float(np.mean(c_mo)), "carryover_c_mean_om": float(np.mean(c_om))}


def wilcoxon(d: list[float]) -> dict:
    try:
        from scipy.stats import wilcoxon as W
    except ImportError:  # pragma: no cover
        return {"p": None, "note": "scipy not available"}
    nz = [x for x in d if abs(x) > 1e-12]
    if len(nz) < 1:
        return {"p": 1.0, "n_nonzero": 0}
    r = W(nz, alternative="two-sided")
    return {"statistic": float(r.statistic), "p": float(r.pvalue), "n_nonzero": len(nz)}


def boot_ci(v: list[float], it: int = 10000, seed: int = SEED) -> tuple[float, float]:
    rng = random.Random(seed)
    m = [statistics.mean(rng.choices(v, k=len(v))) for _ in range(it)]
    m.sort()
    return m[int(0.025 * it)], m[int(0.975 * it) - 1]


def analyse(rows: list[dict]) -> dict:
    """rows: participant_id, order, and per condition the session values (keys mono, ours)."""
    out = {"n": len(rows), "measures": {}}
    for key, label in (("dTS", "SSQ total dTS (primary)"), ("dO", "SSQ oculomotor dO"), ("comfort", "Part 1 comfort mean")):
        ok = [r for r in rows if r["mono"].get(key) is not None and r["ours"].get(key) is not None]
        day = {r["participant_id"]: ((r["mono"], r["ours"]) if r["order"] == "MO" else (r["ours"], r["mono"])) for r in ok}
        c = {pid: v[0][key] - v[1][key] for pid, v in day.items()}
        c_mo = [c[r["participant_id"]] for r in ok if r["order"] == "MO"]
        c_om = [c[r["participant_id"]] for r in ok if r["order"] == "OM"]
        d = [r["ours"][key] - r["mono"][key] for r in ok]
        res = {"label": label, "n": len(ok)}
        if c_mo and c_om:
            res["crossover"] = crossover(c_mo, c_om)
        if d:
            res["paired_D"] = {"mean": statistics.mean(d), "median": statistics.median(d),
                               "ci95_boot_mean": boot_ci(d) if len(d) > 1 else None, "wilcoxon": wilcoxon(d),
                               "ours_above": sum(x > 1e-12 for x in d), "ours_below": sum(x < -1e-12 for x in d),
                               "equal": sum(abs(x) <= 1e-12 for x in d)}
        res["zero_sessions"] = {"mono": sum(abs(r["mono"][key]) <= 1e-12 for r in ok),
                                "ours": sum(abs(r["ours"][key]) <= 1e-12 for r in ok)}
        out["measures"][key] = res
    out["pre_TS"] = {cond: statistics.mean([r[cond]["pre_TS"] for r in rows]) if rows else None for cond in ("mono", "ours")}
    out["dN_mean"] = {cond: statistics.mean([r[cond]["dN"] for r in rows]) if rows else None for cond in ("mono", "ours")}
    out["dD_mean"] = {cond: statistics.mean([r[cond]["dD"] for r in rows]) if rows else None for cond in ("mono", "ours")}
    return out


def simulate(n_sims: int, out: Path) -> int:
    """Rejection rate of the primary test under no effect and under effects of d_z 0.5 and 0.8, with zero inflation."""
    groups = list(csv.DictReader((Path(__file__).resolve().parent / "study2_groups.csv").open(encoding="utf-8")))
    rng = random.Random(SEED)
    res = {}
    for dz in (0.0, 0.5, 0.8):
        for zero_share in (0.0, 0.5):
            rej_x = rej_w = 0
            for s in range(n_sims):
                rows = []
                for g in groups:
                    base = rng.gauss(10, 8)
                    eff = dz * 8.0  # D = Ours - Mono has sd 8 (two sessions with sd 8 / sqrt 2 each), so d_z = eff / 8
                    vals = {}
                    for cond in ("mono", "ours"):
                        v = base + (eff if cond == "ours" else 0) + rng.gauss(0, 8 / math.sqrt(2))
                        if rng.random() < zero_share:
                            v = 0.0
                        vals[cond] = {"dTS": v, "dO": v, "dN": 0.0, "dD": 0.0, "pre_TS": 0.0, "comfort": None}
                    rows.append({"participant_id": g["participant_id"], "order": g["order"], **vals})
                # primary analysis only (the full analyse() adds bootstraps that are not needed here)
                c = {r["participant_id"]: (r["mono"]["dTS"] - r["ours"]["dTS"]) if r["order"] == "MO"
                     else (r["ours"]["dTS"] - r["mono"]["dTS"]) for r in rows}
                c_mo = [c[r["participant_id"]] for r in rows if r["order"] == "MO"]
                c_om = [c[r["participant_id"]] for r in rows if r["order"] == "OM"]
                rej_x += crossover(c_mo, c_om)["p_perm"] < ALPHA
                rej_w += (wilcoxon([r["ours"]["dTS"] - r["mono"]["dTS"] for r in rows]).get("p") or 1) < ALPHA
            res[f"dz{dz}_zero{zero_share}"] = {"crossover_rejection": rej_x / n_sims, "wilcoxon_rejection": rej_w / n_sims}
            print(dz, zero_share, res[f"dz{dz}_zero{zero_share}"], flush=True)
    out.mkdir(parents=True, exist_ok=True)
    (out / "simulation.json").write_text(json.dumps({"n_sims": n_sims, "results": res}, indent=1), encoding="utf-8")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--mono", type=Path)
    ap.add_argument("--ours", type=Path)
    ap.add_argument("--groups", type=Path, default=Path(__file__).resolve().parent / "study2_groups.csv")
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--simulate", type=int, default=0)
    a = ap.parse_args()
    if a.simulate:
        global PERMUTATIONS
        PERMUTATIONS = 4000  # simulation only: Monte Carlo p per data set
        return simulate(a.simulate, a.out)
    groups = {r["participant_id"]: r for r in read_csv(a.groups)}
    mono, ours = session_dirs(a.mono), session_dirs(a.ours)
    rows, missing = [], []
    for pid, g in groups.items():
        vm = session_values(mono[pid]) if pid in mono else None
        vo = session_values(ours[pid]) if pid in ours else None
        if vm is None or vo is None:
            missing.append(pid)
            continue
        rows.append({"participant_id": pid, "order": g["order"], "mono": vm, "ours": vo})
    res = analyse(rows)
    res["participants_without_two_sessions"] = missing
    res["stopped_sessions"] = [(r["participant_id"], c) for r in rows for c in ("mono", "ours") if r[c]["status"] != "complete"]
    a.out.mkdir(parents=True, exist_ok=True)
    (a.out / "study2_results.json").write_text(json.dumps(res, indent=1, default=str), encoding="utf-8")
    print(json.dumps(res, indent=1, default=str)[:3000])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
