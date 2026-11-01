# -*- coding: utf-8 -*-

import json
import os
import re


NODE_KINDS = ["ret", "sto", "pplus", "dplus", "pminus", "dminus", "proc"]
KIND_INDEX = {k: i for i, k in enumerate(NODE_KINDS)}


def quality_weights(files, alpha=1.0):

    tard = {}
    for f in files:
        head = json.loads(open(f, encoding="utf-8").readline())
        tard[f] = max(float(head.get("tardiness", 1.0)), 1e-9)
    groups = {}
    for f in tard:
        base = re.sub(r"_s\d+", "", os.path.basename(f))   
        key = base.rsplit("_", 1)[0]                        
        groups.setdefault(key, []).append(f)
    w = {}
    for key, fs in groups.items():
        best = min(tard[f] for f in fs)
        for f in fs:
            w[f] = max(0.2, (best / tard[f]) ** alpha)
    return w
