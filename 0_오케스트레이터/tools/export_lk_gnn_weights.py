# -*- coding: utf-8 -*-

from __future__ import print_function

import argparse
import json
import os
import struct


def scalar(state, name, default=0):
    value = state.get(name)
    return int(value.item()) if value is not None else int(default)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("checkpoint")
    parser.add_argument("output_prefix")
    args = parser.parse_args()
    import torch

    state = torch.load(os.path.abspath(args.checkpoint), map_location="cpu", weights_only=True)
    drop = [int(x) for x in state.get("drop_code", torch.tensor([], dtype=torch.long)).tolist()]
    hidden = int(state["enc.3.weight"].shape[0])
    node_dim = int(state["enc.0.weight"].shape[1])
    rounds = 1 + max(int(k.split(".")[1]) for k in state if k.startswith("upd."))
    ctx = bool(scalar(state, "ctx_code"))
    scorer = "pair" if scalar(state, "scorer_code") else "point"
    extra = int(state["dec.0.weight"].shape[1]) - 2 * hidden
    if ctx:
        extra //= 5


    glob_dim = 21 if extra >= 28 else 0
    candidate_dim = extra - glob_dim

    prefix = os.path.abspath(args.output_prefix)
    os.makedirs(os.path.dirname(prefix), exist_ok=True)
    tensors, offset = [], 0
    with open(prefix + ".bin", "wb") as stream:
        for name, tensor in state.items():
            if not tensor.is_floating_point():
                continue
            tensor = tensor.detach().cpu().contiguous().float()
            values = tensor.reshape(-1).tolist()
            stream.write(struct.pack("<{}f".format(len(values)), *values))
            tensors.append({"name": str(name), "shape": [int(x) for x in tensor.shape],
                            "floatOffset": offset, "floatCount": len(values)})
            offset += len(values)
    manifest = {
        "format": "dt-keti-lk-gnn-weights/v2",
        "binaryFile": os.path.basename(prefix) + ".bin",
        "floatEncoding": "IEEE754-little-endian-float32",
        "model": {"type": "LkGnn", "hidden": hidden, "rounds": rounds,
                  "nodeFeatureDim": node_dim, "candidateExtraDim": candidate_dim,
                  "dropCode": drop, "aggregation": {0: "mean", 1: "max", 2: "meanmax"}[scalar(state, "agg_code")],
                  "attention": bool(scalar(state, "attn_code")), "context": ctx,
                  "scorer": scorer, "pool": "mean" if scalar(state, "pool_code", 1) else "none",
                  "waitAction": bool(scalar(state, "wait_code")), "globalDim": glob_dim,
                  "neighborTypes": ["pred", "succ", "disj", "all", "head", "tail"],
                  "parameterCount": offset},
        "tensors": tensors,
    }
    with open(prefix + ".manifest.json", "w", encoding="utf-8") as stream:
        json.dump(manifest, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    print("wrote", prefix + ".bin")
    print("wrote", prefix + ".manifest.json")
    print("parameters", offset)


if __name__ == "__main__":
    main()
