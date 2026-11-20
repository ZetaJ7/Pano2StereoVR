"""Generate reproducible per-participant trial plans for the Pano2Stereo subjective study.

Usage:
    python make_trial_plans.py --config study_config.json --participants P01-P40 --out plans
    python make_trial_plans.py --config study_config.json --participants T01-T05 --out plans_pilot
    python make_trial_plans.py --config study_config.json --participants P01,P02 --out plans --check-files

Stimulus names in the plans are relative to the stimuli folder and include a subfolder (forward slashes,
user 11-20) from the config "stimulus_folders": Part 1 training -> part1/practice, dummy -> part1/dummy,
main -> part1/<clip cell>; Part 2 training -> part2/practice, main -> part2/near (field {folder} of the
file patterns). The subfolders do not change any random order.

Each plan is a JSON file read by the Unity study mode. The random order depends only on
(study_id, config_version, participant_id), so a plan can be regenerated exactly.

Plan format 5 (2026-11-20, user decisions 11-20): flow token "eyerange" (natural viewing range
measurement, before the gaze training) with an "eye_range" block: continuous outward sweeps (user 11-20,
option A), one per direction x repeat in a random order per participant, the sweep speed and range, the
provisional rule for the participant's natural eye-only range a, and the pose-aware (PAw) correction
radius variants. Part 2 PAw file names carry the
placeholder "{paw_theta}" (config part2.file_patterns), resolved at run time to the variant nearest to a;
the plan lists every variant in eye_range.paw_theta_variants_deg. The eye-range order uses its own
random stream (derived from the participant seed), so the orders of the other parts are unchanged.

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
  * Eye range (format 5): every direction `repeats` times (one sweep each), shuffled with a separate random
    stream seeded from the participant seed, so the orders of all other blocks stay as in format 4.
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

PLAN_FORMAT_VERSION = 5
DEFAULT_FLOW = ["checkin", "ssq:S_pre", "hmd_fit", "stereotest", "part:1", "ssq:S_mid1", "break", "ssq:S_mid2", "hmd_fit",
                "eyerange", "gazetraining", "part:2", "ssq:S_post", "post_questionnaire", "end"]
DEFAULT_ALIGNMENT = {"tolerance_deg": 5.0, "dwell_s": 1.0}
# Placeholder in Part 2 file names for the pose-aware correction radius (degrees), resolved at run time.
PAW_THETA_PLACEHOLDER = "{paw_theta}"
DEFAULT_EYE_RANGE = {
    "mode": "continuous",            # user 11-20, option A: continuous outward sweeps (the discrete mode is removed)
    "directions_deg": [0, 45, 90, 135, 180, 225, 270, 315],   # 0 = up, clockwise for the viewer
    "repeats": 2,
    "speed_deg_s": 8.0,              # pending, to be adjusted after the pilot
    "max_eccentricity_deg": 40.0,
    "head_threshold_deg": 3.0,       # provisional (user 11-20), to be fixed after the pilot
    "stop_at_onset": True,           # user 11-20: a sweep ends as soon as the head-onset criterion is met
    "rule": "median_onset_ecc",
    "measure": "head_rot_deg",       # great-circle head rotation since the sweep onset
    "paw_theta_variants_deg": [10, 15, 20, 25, 30],
    "default_paw_theta_deg": 15.0,   # used when the step is disabled, absent from the flow or skipped
    "dot_size_deg": 1.5,
    "align_dwell_s": 0.5,
    "gap_s": 0.3,
    "reference_yaw_deg": 0.0,
    "reference_pitch_deg": 0.0,
}
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


def sub_seed(seed: int, name: str) -> int:
    """Seed of a separate random stream (e.g. the eye-range order), so that adding it leaves the main stream alone."""
    return int(hashlib.sha256(f"{seed}|{name}".encode("utf-8")).hexdigest()[:12], 16)


def participant_number(pid: str, seed: int) -> int:
    digits = re.findall(r"\d+", pid)
    return int(digits[-1]) if digits else seed


def stim_folder(cfg: dict, part: int, block: str, cell: str = "") -> str:
    """Subfolder of a stimulus under the stimuli folder from config "stimulus_folders" (user 11-20), e.g.
    part1/practice, part1/dummy, part1/<cell>, part2/practice, part2/near; "" when the config has none."""
    folders = cfg.get("stimulus_folders", {}).get(f"part{part}", {})
    template = folders.get(block)
    if template is None:
        if folders:
            raise SystemExit(f"stimulus_folders.part{part} has no entry for block '{block}'")
        return ""
    if "{cell}" in template and not cell:
        raise SystemExit(f"stimulus_folders.part{part}.{block} needs the clip cell, but the clip has none")
    return template.format(cell=cell).strip("/")


def stim_file(cfg: dict, clip_id: str, condition: str, patterns: dict | None = None, folder: str = "") -> str:
    """Stimulus name relative to the stimuli folder (forward slashes); a per-condition pattern
    (part2.file_patterns) overrides file_pattern. A {paw_theta} field in the pattern is kept as the
    placeholder and resolved by the study program; {folder} is the subfolder from stim_folder."""
    pattern = (patterns or {}).get(condition, cfg["file_pattern"])
    name = pattern.format(clip_id=clip_id, condition=condition, paw_theta=PAW_THETA_PLACEHOLDER, folder=folder)
    return name.lstrip("/")


def format_theta(theta: float) -> str:
    """Theta in a file name: 20 -> "20", 12.5 -> "12.5" (same as StudyPlan.FormatTheta)."""
    return f"{float(theta):g}"


def expand_paw_theta(name: str, variants: list[float]) -> list[str]:
    if PAW_THETA_PLACEHOLDER not in name:
        return [name]
    return [name.replace(PAW_THETA_PLACEHOLDER, format_theta(v)) for v in variants]


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
        t["file"] = stim_file(cfg, t["clip_id"], t["condition"], folder=stim_folder(cfg, 1, t["block"], t.get("cell", "")))
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
    patterns = {k: v for k, v in p2.get("file_patterns", {}).items() if k != "note" and isinstance(v, str)}
    for i, t in enumerate(trials):
        t["index"] = i
        t["pair_type"] = "same" if t["cond_a"] == t["cond_b"] else "real"
        folder = stim_folder(cfg, 2, t["block"], t.get("cell", ""))
        t["file_a"] = stim_file(cfg, t["clip_id"], t["cond_a"], patterns, folder)
        t["file_b"] = stim_file(cfg, t["clip_id"], t["cond_b"], patterns, folder)
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


def build_eye_range(cfg: dict, seed: int) -> dict | None:
    """Natural viewing range measurement, continuous sweeps (user 11-20, option A): every direction `repeats` times,
    in a random order from a separate stream. The rule for a and the variant choice are applied by the study
    program (provisional, user 11-20)."""
    e = cfg.get("eye_range")
    if e is None:
        return None
    v = dict(DEFAULT_EYE_RANGE)
    v.update({k: e[k] for k in DEFAULT_EYE_RANGE if k in e})
    dirs = [float(x) for x in v["directions_deg"]]
    repeats = int(v["repeats"])
    variants = [float(x) for x in v["paw_theta_variants_deg"]]
    default = float(v["default_paw_theta_deg"])
    if v["mode"] != "continuous":
        raise SystemExit("eye_range.mode must be continuous (the discrete mode was removed on 11-20)")
    if v["rule"] != "median_onset_ecc" or v["measure"] != "head_rot_deg":
        raise SystemExit("eye_range: rule must be median_onset_ecc and measure head_rot_deg")
    if not dirs or len(set(dirs)) != len(dirs) or repeats < 1:
        raise SystemExit("eye_range: directions_deg must be non-empty without repeats, and repeats at least 1")
    if float(v["speed_deg_s"]) <= 0 or float(v["max_eccentricity_deg"]) <= 0:
        raise SystemExit("eye_range: speed_deg_s and max_eccentricity_deg must be positive")
    if not variants or default not in variants:
        raise SystemExit("eye_range: default_paw_theta_deg must be one of paw_theta_variants_deg")
    sweeps = [(d, r) for r in range(repeats) for d in dirs]
    random.Random(sub_seed(seed, "eye_range")).shuffle(sweeps)
    scalars = ("speed_deg_s", "max_eccentricity_deg", "head_threshold_deg", "dot_size_deg", "align_dwell_s", "gap_s",
               "reference_yaw_deg", "reference_pitch_deg")
    return {"enabled": bool(e.get("enabled", True)), "mode": "continuous", "rule": str(v["rule"]), "measure": str(v["measure"]),
            "directions_deg": dirs, "repeats": repeats, **{k: float(v[k]) for k in scalars}, "stop_at_onset": bool(v["stop_at_onset"]),
            "paw_theta_variants_deg": variants, "default_paw_theta_deg": default,
            "sweeps": [{"index": i, "direction_deg": d, "repeat": r} for i, (d, r) in enumerate(sweeps)]}


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
    p2_trials = build_part2(cfg, rng, pnum)
    built = {1: {"options": viewing_options(cfg["part1"]), "n_groups": p1_groups, "trials": p1_trials},
             2: {"options": viewing_options(cfg["part2"]), "trials": p2_trials}}
    eye_range = build_eye_range(cfg, seed)  # separate random stream: the orders above do not change
    if "eyerange" in flow and eye_range is None:
        raise SystemExit("flow has eyerange but the config has no eye_range block")
    if "eyerange" in flow and "part:2" in flow and flow.index("eyerange") > flow.index("part:2"):
        raise SystemExit("eyerange must come before part:2 (it chooses the PAw correction radius)")
    if eye_range is None and any(PAW_THETA_PLACEHOLDER in t[k] for t in p2_trials for k in ("file_a", "file_b")):
        raise SystemExit("part2 file names use " + PAW_THETA_PLACEHOLDER + " but the config has no eye_range block")
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
    if eye_range is not None:
        plan["eye_range"] = eye_range
    if gaze is not None:
        plan["gaze_training"] = gaze
    plan["parts"] = [{"part": n, **built[n]} for n in part_order]
    return plan


def check_files(cfg: dict, plans: list[dict], stimuli_root: Path) -> list[str]:
    """Missing stimulus files; PAw names with the theta placeholder are checked for every variant."""
    needed = set()
    for plan in plans:
        variants = plan.get("eye_range", {}).get("paw_theta_variants_deg", [])
        for part in plan["parts"]:
            for t in part["trials"]:
                for key in ("file", "file_a", "file_b"):
                    if key in t:
                        needed.update(expand_paw_theta(t[key], variants))
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
    er = plans[0].get("eye_range")
    eye = (f" | eyerange {len(er['sweeps'])} sweeps x {er['max_eccentricity_deg'] / er['speed_deg_s']:g} s, PAw theta variants "
           f"{er['paw_theta_variants_deg']}" if er else "")
    print(f"wrote {len(plans)} plan(s) to {args.out} | stereotest {len(plans[0]['stereotest']['trials'])} trials{eye} | {parts}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
