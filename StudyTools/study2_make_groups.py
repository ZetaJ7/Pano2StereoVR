"""Order groups of Study 2 (user 11-23: block randomization, both orders equally often in every block of 4).

Writes study2_groups.csv next to this file: participant_id, block, order (MO = Mono on day 1 and Ours on day 2,
OM = the reverse), day1_condition, day2_condition. Deterministic (seed below); generate and commit before the first
participant, and assign S01, S02, ... in the order of arrival.
"""
from __future__ import annotations

import csv
import random
from pathlib import Path

SEED = 20261124
N = 20
BLOCK = 4

rng = random.Random(SEED)
rows = []
for b in range(N // BLOCK):
    orders = ["MO", "MO", "OM", "OM"]
    rng.shuffle(orders)
    for k, o in enumerate(orders):
        i = b * BLOCK + k + 1
        d1, d2 = ("mono", "ours") if o == "MO" else ("ours", "mono")
        rows.append({"participant_id": f"S{i:02d}", "block": b + 1, "order": o, "day1_condition": d1, "day2_condition": d2})
out = Path(__file__).resolve().parent / "study2_groups.csv"
with out.open("w", encoding="utf-8", newline="") as fh:
    w = csv.DictWriter(fh, fieldnames=list(rows[0]))
    w.writeheader()
    w.writerows(rows)
print("wrote", out.name, "MO", sum(r["order"] == "MO" for r in rows), "OM", sum(r["order"] == "OM" for r in rows))
