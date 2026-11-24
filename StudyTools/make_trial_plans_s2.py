"""Trial plans for Study 2 (SSQ, Mono vs Ours PA), built with the Study 1 plan functions.

Usage:
    python make_trial_plans_s2.py --config study2_config_mono.json --participants S01-S20 --out plans_s2_mono
    python make_trial_plans_s2.py --config study2_config_ours.json --participants S01-S20 --out plans_s2_ours
    (add --check-files --stimuli-root <Stimuli_s2> to verify every stimulus file)

make_trial_plans.py (Study 1, frozen with bb1755e) always builds a Part 2; Study 2 has no Part 2, eye-range
measurement or gaze training. This script imports that file unchanged and assembles the plan from its stereo-test and
Part 1 builders in the same order as make_plan (stereo test first, then Part 1, one random stream per participant
seeded from study_id | config_version | participant_id), so the plan format (5) and every rule of Part 1 (practice
outside groups, groups shuffled without adjacent repeats of a clip) are those of Study 1. With one condition per
config each group holds one clip. The two configs have different config_version values, so a participant gets
different clip orders on the two days.
"""
from __future__ import annotations

import argparse
import json
import random
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import make_trial_plans as M  # noqa: E402


def make_plan_s2(cfg: dict, cfg_sha: str, pid: str) -> dict:
    flow = cfg["flow"]
    if any(s in flow for s in ("part:2", "eyerange", "gazetraining")):
        raise SystemExit("Study 2 flow must not contain part:2, eyerange or gazetraining")
    if len(cfg["part1"]["conditions"]) != 1:
        raise SystemExit("a Study 2 config has exactly one Part 1 condition")
    seed = M.participant_seed(cfg["study_id"], cfg["config_version"], pid)
    rng = random.Random(seed)
    stereotest = M.build_stereotest(cfg, rng)
    pnum = M.participant_number(pid, seed)
    p1_trials, p1_groups = M.build_part1(cfg, rng, pnum)
    plan = {
        "plan_format_version": M.PLAN_FORMAT_VERSION,
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
    alignment = dict(M.DEFAULT_ALIGNMENT)
    alignment.update({k: float(v) for k, v in cfg.get("alignment", {}).items() if k in M.DEFAULT_ALIGNMENT})
    plan["alignment"] = alignment
    plan["parts"] = [{"part": 1, "options": M.viewing_options(cfg["part1"]), "n_groups": p1_groups, "trials": p1_trials}]
    return plan


def check_plan(plan: dict) -> None:
    """Study 2 rules: 1 practice + 36 main trials, each clip 3 times, never the same clip twice in a row."""
    trials = plan["parts"][0]["trials"]
    main = [t for t in trials if t["block"] == "main"]
    train = [t for t in trials if t["block"] == "training"]
    clips = [t["clip_id"] for t in main]
    if len(train) != 1 or len(main) != 36 or any(clips.count(c) != 3 for c in set(clips)) or len(set(clips)) != 12:
        raise SystemExit(f"{plan['participant_id']}: unexpected trial counts")
    if any(a == b for a, b in zip(clips, clips[1:])):
        raise SystemExit(f"{plan['participant_id']}: a clip appears twice in a row")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", type=Path, required=True)
    ap.add_argument("--participants", required=True, help="e.g. S01-S20")
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--check-files", action="store_true")
    ap.add_argument("--stimuli-root", type=Path, default=None)
    a = ap.parse_args()
    cfg, cfg_sha = M.load_config(a.config)
    plans = [make_plan_s2(cfg, cfg_sha, pid) for pid in M.parse_participants(a.participants)]
    for p in plans:
        check_plan(p)
    if a.check_files:
        root = a.stimuli_root or (a.config.parent / cfg.get("stimuli_dir", "Stimuli_s2"))
        missing = M.check_files(cfg, plans, root)
        if missing:
            print(f"missing {len(missing)} stimulus file(s) under {root}:", file=sys.stderr)
            for f in missing:
                print("  " + f, file=sys.stderr)
            return 2
    a.out.mkdir(parents=True, exist_ok=True)
    for p in plans:
        (a.out / f"{p['participant_id']}.json").write_text(json.dumps(p, ensure_ascii=False, indent=2), encoding="utf-8")
    t = plans[0]["parts"][0]["trials"]
    print(f"wrote {len(plans)} plan(s) to {a.out} | stereotest {len(plans[0]['stereotest']['trials'])} trials | "
          f"part1 {len(t)} trials ({sum(x['block'] == 'main' for x in t)} main, {plans[0]['parts'][0]['n_groups']} groups)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
