# -*- coding: utf-8 -*-

import argparse
import base64
import json
import os
import sys


def main():
    p = argparse.ArgumentParser()
    p.add_argument("manifest")
    p.add_argument("fixtures")
    p.add_argument("--runtime-dir", required=True)
    p.add_argument("--tolerance", type=float, default=2e-5)
    a = p.parse_args()
    sys.path.insert(0, os.path.abspath(a.runtime_dir))
    from vc_lk_gnn_runtime import VcLkGnn, VcLkWeights
    manifest = open(a.manifest, encoding="utf-8").read()
    metadata = json.loads(manifest)
    binary = os.path.join(os.path.dirname(os.path.abspath(a.manifest)), metadata["binaryFile"])
    weights = base64.b64encode(open(binary, "rb").read()).decode("ascii")
    model = VcLkGnn(VcLkWeights(manifest, weights))
    suite = json.load(open(a.fixtures, encoding="utf-8"))
    maximum = 0.0
    for fixture in suite["fixtures"]:
        actual = model.forward(fixture["input"])
        expected = fixture["expectedScores"]
        error = max([abs(x - y) for x, y in zip(actual, expected)] or [0.0])
        maximum = max(maximum, error)
        actual_index = max(range(len(actual)), key=lambda i: actual[i])
        if actual_index != fixture["expectedIndex"]:
            raise AssertionError("choice mismatch at step {}: {} != {}".format(
                fixture["step"], actual_index, fixture["expectedIndex"]))
    print("fixtures={}".format(len(suite["fixtures"])))
    print("maxAbsError={:.9g}".format(maximum))
    if maximum > a.tolerance:
        raise AssertionError("score error exceeds tolerance")
    print("PASS scores and choices")


if __name__ == "__main__":
    main()
