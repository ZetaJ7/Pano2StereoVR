"""Generate reproducible per-participant trial plans for the Pano2Stereo subjective study.

Usage:
    python make_trial_plans.py --config study_config.json --participants P01-P30 --out plans
    python make_trial_plans.py --config study_config.json --participants P01,P02 --out plans --check-files

Each plan is a JSON file read by the Unity study mode. The random order depends only on
(study_id, config_version, participant_id), so a plan can be regenerated exactly.

Plan format 4 (2026-11-19, second walkthrough): flow token "break" (rest of at least min_break_s,
then the participant chooses when to continue) and an "alignment" block (playback of a Part 2 trial
and the gaze training start once the head stays within tolerance_deg of the reference for dwell_s).
Default flow: Part 1 first, SSQ S_mid1, break, SSQ S_mid2, headset re-fit, gaze training, Part 2.

Plan format 3 (2026-11-19):
  * flow token "gazetraining" (gaze training before Part 2) with a "gaze_training" block;
  * stereo-test disparities in whole display pixels ("disparity_px"), dot size "dot_px" and a
    "display_fallback" (eye width in px, horizontal FOV) used when no XR eye texture is available;
  * per-part "options" (self-paced viewing, minimum and maximum viewing time, re-view on re-rate);
  * Part 1 grouped by clip: every trial has "group_index" (0 = practice outside groups),
    "group_size" and "position_in_group".
The session order comes from the config "flow" (see DEFAULT_FLOW). Parts are listed in the order
they appear in the flow.

Ordering rules:
  * Stereo test: one practice trial, then each disparity level `repeats_per_level` times in random
    order; the target shape is random and never repeats on consecutive trials.
  * Gaze training: one random dot order per allowed attempt.
  * Part 1: training trials (one at a time) -> dummy groups (shuffled) -> main groups (shuffled).
    A group is one clip (and repetition) under every condition, shown consecutively. Within-group
    condition orders are the rows of a cyclic Latin square of the configured condition list, taken
    in turn starting at row (participant number mod number of conditions) and assigned to the
    groups at random, so each condition takes each position equally often (+-1) for every
    participant, and the condition that leads one extra group rotates over participants.
    Adjacent groups never share a clip.
  * Part 2: training -> main. Per clip: (pa, paw), (paw, pa) and `same_pairs_per_clip`
    identical pairs. The identical pair alternates between the two conditions across clips
    and participants, so each condition appears equally often over the panel.
  * In the Part 2 main block the same clip never appears in two consecutive trials.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import random
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

PLAN_FORMAT_VERSION = 4
DEFAULT_FLOW = ["checkin", "ssq:S_pre", "hmd_fit", "stereotest", "part:1", "ssq:S_mid1", "break", "ssq:S_mid2", "hmd_fit",
                "gazetraining", "part:2", "ssq:S_post", "post_questionnaire", "end"]
DEFAULT_ALIGNMENT = {"tolerance_deg": 5.0, "dwell_s": 1.0}
MAX_SHUFFLE_ATTEMPTS = 20000
DEFAULT_VIEWING = {"self_paced": False, "min_view_s": None, "max_view_s": 0, "allow_review_on_rerate": False}


def load_config(path: Path) -> tuple[dict, str]:
    raw = path.read_bytes()
    return json.loads(raw.decode("utf-8")), hashlib.sha256(raw).hexdigest()


def parse_participants(spec: str) -> list[str]:
    """Accept 'P01-P30', 'P01,P02,P05' or a mix such as 'P01-P03,P10'."""
    out: list[str] = []
    for part in spec.split(","):
        part = part.strip()
        if not part:
            continue
        m = re.fullmatch(r"([A-Za-z_]*)(\d+)-([A-Za-z_]*)(\d+)", part)
        if m and m.group(1) == m.group(3):
            prefix, a, b = m.group(1), m.group(2), m.group(4)
            width = max(len(a), len(b))
            for n in range(int(a), int(b) + 1):
                out.append(f"{prefix}{n:0{width}d}")
        else:
            out.append(part)
    if len(set(out)) != len(out):
        raise SystemExit("duplicate participant IDs in --participants")
    return out


def participant_seed(study_id: str, config_version: str, pid: str) -> int:
    digest = hashlib.sha256(f"{study_id}|{config_version}|{pid}".encode("utf-8")).hexdigest()
    return int(digest[:12], 16)


def participant_number(pid: str, seed: int) -> int:
    digits = re.findall(r"\d+", pid)
    return int(digits[-1]) if digits else seed


def stim_file(cfg: dict, clip_id: str, condition: str) -> str:
    return cfg["file_pattern"].format(clip_id=clip_id, condition=condition)


def shuffle_no_adjacent_clip(items: list[dict], rng: random.Random) -> list[dict]:
    """Shuffle so that no two consecutive items share clip_id (rejection sampling)."""
    items = list(items)
    for _ in range(MAX_SHUFFLE_ATTEMPTS):
        rng.shuffle(items)
        if all(items[i]["clip_id"] != items[i + 1]["clip_id"] for i in range(len(items) - 1)):
            return items
    raise RuntimeError("could not find an order without adjacent repeats of a clip")


def viewing_options(section: dict) -> dict:
    """Viewing options of a part; values not given in the config keep the defaults (several are pending)."""
    opts = dict(DEFAULT_VIEWING)
    opts.update({k: v for k, v in section.get("viewing", {}).items() if k in DEFAULT_VIEWING})
    return opts


def latin_orders(conditions: list[str], n_groups: int, offset: int, rng: random.Random) -> list[list[str]]:
    """Condition order for each of n_groups: rows of the cyclic Latin square of `conditions`, taken
    in turn from row `offset`, then shuffled over the groups."""
    base = list(conditions)
    k = len(base)
    rows = [base[i:] + base[:i] for i in range(k)]
    orders = [rows[(offset + g) % k] for g in range(n_groups)]
    rng.shuffle(orders)
    return orders


def make_groups(units: list[dict], conditions_of, block: str, offset: int, rng: random.Random) -> list[list[dict]]:
    """One group per unit (clip + repetition); within-group orders balanced over the groups."""
    if not units:
        return []
    conds_per_unit = [conditions_of(u) for u in units]
    groups: list[list[dict]] = []
    same_set = all(c == conds_per_unit[0] for c in conds_per_unit)
    orders = latin_orders(conds_per_unit[0], len(units), offset, rng) if same_set else None
    for gi, u in enumerate(units):
        order = orders[gi] if orders else rng.sample(conds_per_unit[gi], len(conds_per_unit[gi]))
        groups.append([{"block": block, "clip_id": u["clip_id"], "condition": c, "cell": u.get("cell", ""),
                        "scene": u.get("scene", ""), "repetition": u.get("repetition", 0)} for c in order])
    return groups


def shuffle_groups_no_adjacent_clip(groups: list[list[dict]], rng: random.Random) -> list[list[dict]]:
    groups = list(groups)
    for _ in range(MAX_SHUFFLE_ATTEMPTS):
        rng.shuffle(groups)
        if all(groups[i][0]["clip_id"] != groups[i + 1][0]["clip_id"] for i in range(len(groups) - 1)):
            return groups
    raise RuntimeError("could not find a group order without adjacent repeats of a clip")


def build_part1(cfg: dict, rng: random.Random, pnum: int) -> tuple[list[dict], int]:
    p1 = cfg["part1"]
    trials: list[dict] = []
    for t in p1.get("training", []):
        trials.append({"block": "training", "clip_id": t["clip_id"], "condition": t["condition"],
                       "group_index": 0, "group_size": 0, "position_in_group": 0})

    # dummy groups: dummy entries grouped by clip, in the order of first appearance
    dummy_units, dummy_conds = [], {}
    for t in p1.get("dummy", []):
        if t["clip_id"] not in dummy_conds:
            dummy_conds[t["clip_id"]] = []
            dummy_units.append({"clip_id": t["clip_id"]})
        dummy_conds[t["clip_id"]].append(t["condition"])
    dummy = make_groups(dummy_units, lambda u: dummy_conds[u["clip_id"]], "dummy", pnum, rng)
    dummy = shuffle_groups_no_adjacent_clip(dummy, rng)

    main_units = [{"clip_id": c["clip_id"], "cell": c.get("cell", ""), "scene": c.get("scene", ""), "repetition": rep}
                  for rep in range(int(p1.get("repetitions", 1))) for c in p1["clips"]]
    main = make_groups(main_units, lambda u: list(p1["conditions"]), "main", pnum, rng)
    main = shuffle_groups_no_adjacent_clip(main, rng)
    if dummy and main and dummy[-1][0]["clip_id"] == main[0][0]["clip_id"]:
        raise RuntimeError("a dummy clip is also a main clip")

    groups = dummy + main
    for gi, g in enumerate(groups, start=1):
        for pos, t in enumerate(g):
            t["group_index"] = gi
            t["group_size"] = len(g)
            t["position_in_group"] = pos
            trials.append(t)
    for i, t in enumerate(trials):
        t["index"] = i
        t["file"] = stim_file(cfg, t["clip_id"], t["condition"])
    return trials, len(groups)


def build_part2(cfg: dict, rng: random.Random, pnum: int) -> list[dict]:
    p2 = cfg["part2"]
    c0, c1 = p2["conditions"]
    trials: list[dict] = []
    for t in p2.get("training", []):
        trials.append({
            "block": "training", "clip_id": t["clip_id"], "cond_a": t["cond_a"], "cond_b": t["cond_b"],
            "reference_yaw_deg": float(t.get("reference_yaw_deg", 0.0)),
            "reference_pitch_deg": float(t.get("reference_pitch_deg", 0.0)),
        })
    main = []
    n_same = int(p2.get("same_pairs_per_clip", 1))
    for ci, clip in enumerate(p2["clips"]):
        ref = {"reference_yaw_deg": float(clip.get("reference_yaw_deg", 0.0)),
               "reference_pitch_deg": float(clip.get("reference_pitch_deg", 0.0)),
               "cell": clip.get("cell", ""), "scene": clip.get("scene", "")}
        main.append({"block": "main", "clip_id": clip["clip_id"], "cond_a": c0, "cond_b": c1, **ref})
        main.append({"block": "main", "clip_id": clip["clip_id"], "cond_a": c1, "cond_b": c0, **ref})
        for s in range(n_same):
            same = (c0, c1)[(ci + pnum + s) % 2]
            main.append({"block": "main", "clip_id": clip["clip_id"], "cond_a": same, "cond_b": same, **ref})
    trials.extend(shuffle_no_adjacent_clip(main, rng))
    for i, t in enumerate(trials):
        t["index"] = i
        t["pair_type"] = "same" if t["cond_a"] == t["cond_b"] else "real"
        t["file_a"] = stim_file(cfg, t["clip_id"], t["cond_a"])
        t["file_b"] = stim_file(cfg, t["clip_id"], t["cond_b"])
    return trials


def build_stereotest(cfg: dict, rng: random.Random) -> dict:
    st = cfg.get("stereotest", {"enabled": False})
    if not st.get("enabled", False):
        return {"enabled": False, "trials": []}
    shapes = list(st["shapes"])
    levels = [("practice", int(st["practice_disparity_px"]))]
    main = [("main", int(d)) for d in st["disparities_px"] for _ in range(int(st.get("repeats_per_level", 1)))]
    rng.shuffle(main)
    trials, prev = [], None
    for i, (block, disp) in enumerate(levels + main):
        shape = rng.choice([s for s in shapes if s != prev])
        prev = shape
        trials.append({"index": i, "block": block, "shape": shape, "disparity_px": disp, "sign": "crossed"})
    geom = {k: st[k] for k in ("quad_distance_m", "quad_size_deg", "dot_px", "display_fallback") if k in st}
    return {"enabled": True, "shapes": shapes, **geom, "trials": trials}


def build_gaze_training(cfg: dict, rng: random.Random) -> dict | None:
    g = cfg.get("gaze_training")
    if g is None:
        return None
    n = int(g.get("n_dots", 8))
    attempts = int(g.get("max_attempts", 3))
    orders = []
    for _ in range(attempts):
        order = list(range(n))
        rng.shuffle(order)
        orders.append(order)
    keys = ("radius_deg", "dwell_s", "dot_size_deg", "max_head_deviation_deg", "reference_yaw_deg", "reference_pitch_deg")
    return {"enabled": bool(g.get("enabled", True)), "n_dots": n, "max_attempts": attempts,
            **{k: float(g[k]) for k in keys if k in g}, "orders": orders}


def make_plan(cfg: dict, cfg_sha: str, pid: str) -> dict:
    seed = participant_seed(cfg["study_id"], cfg["config_version"], pid)
    rng = random.Random(seed)
    flow = cfg.get("flow", DEFAULT_FLOW)
    stereotest = build_stereotest(cfg, rng)
    gaze = build_gaze_training(cfg, rng)
    if "gazetraining" in flow and gaze is None:
        raise SystemExit("flow has gazetraining but the config has no gaze_training block")
    pnum = participant_number(pid, seed)
    p1_trials, p1_groups = build_part1(cfg, rng, pnum)
    built = {1: {"options": viewing_options(cfg["part1"]), "n_groups": p1_groups, "trials": p1_trials},
             2: {"options": viewing_options(cfg["part2"]), "trials": build_part2(cfg, rng, pnum)}}
    part_order = [int(step.split(":")[1]) for step in flow if step.startswith("part:")]
    plan = {
        "plan_format_version": PLAN_FORMAT_VERSION,
        "study_id": cfg["study_id"],
        "config_version": cfg["config_version"],
        "config_sha256": cfg_sha,
        "participant_id": pid,
        "seed": seed,
        "generated_utc": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "timing": cfg["timing"],
        "flow": flow,
        "min_break_s": float(cfg.get("min_break_s", 120)),
        "stereotest": stereotest,
    }
    alignment = dict(DEFAULT_ALIGNMENT)
    alignment.update({k: float(v) for k, v in cfg.get("alignment", {}).items() if k in DEFAULT_ALIGNMENT})
    plan["alignment"] = alignment
    if gaze is not None:
        plan["gaze_training"] = gaze
    plan["parts"] = [{"part": n, **built[n]} for n in part_order]
    return plan


def check_files(cfg: dict, plans: list[dict], stimuli_root: Path) -> list[str]:
    needed = set()
    for plan in plans:
        for part in plan["parts"]:
            for t in part["trials"]:
                for key in ("file", "file_a", "file_b"):
                    if key in t:
                        needed.add(t[key])
    return sorted(f for f in needed if not (stimuli_root / f).is_file())


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", type=Path, required=True)
    ap.add_argument("--participants", required=True, help="e.g. P01-P30 or P01,P02")
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--check-files", action="store_true", help="verify that every stimulus file exists")
    ap.add_argument("--stimuli-root", type=Path, default=None,
                    help="directory holding the stimulus files (default: <config dir>/<stimuli_dir>)")
    args = ap.parse_args()

    cfg, cfg_sha = load_config(args.config)
    pids = parse_participants(args.participants)
    plans = [make_plan(cfg, cfg_sha, pid) for pid in pids]

    if args.check_files:
        root = args.stimuli_root or (args.config.parent / cfg.get("stimuli_dir", "Stimuli"))
        missing = check_files(cfg, plans, root)
        if missing:
            print(f"missing {len(missing)} stimulus file(s) under {root}:", file=sys.stderr)
            for f in missing:
                print("  " + f, file=sys.stderr)
            return 2

    args.out.mkdir(parents=True, exist_ok=True)
    for plan in plans:
        path = args.out / f"{plan['participant_id']}.json"
        path.write_text(json.dumps(plan, ensure_ascii=False, indent=2), encoding="utf-8")
    parts = " | ".join(
        f"part{p['part']} {len(p['trials'])} trials ({sum(t['block'] == 'main' for t in p['trials'])} main"
        + (f", {p['n_groups']} groups" if "n_groups" in p else "") + ")"
        for p in plans[0]["parts"])
    print(f"wrote {len(plans)} plan(s) to {args.out} | stereotest {len(plans[0]['stereotest']['trials'])} trials | {parts}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
