# -*- coding: utf-8 -*-

import argparse
import json
import os
import sys


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--gnn-dir", required=True)
    p.add_argument("--checkpoint", required=True)
    p.add_argument("--instance", required=True)
    p.add_argument("--output", required=True)
    a = p.parse_args()
    sys.path.insert(0, os.path.abspath(a.gnn_dir))
    import torch
    from dispatch import Dispatcher
    from lk_dataset import collate_lk, lk_from_record
    from rollout_builder import Builder, Instance
    dispatcher = Dispatcher(a.checkpoint, device="cpu")
    builder = Builder(Instance.from_json(a.instance))
    fixtures, step = [], 0
    while not builder.done():
        record = builder.emit_state(step)
        decision = lk_from_record(record, allow_no_label=True)
        batch = collate_lk([decision], "cpu")
        with torch.no_grad():
            scores = dispatcher.model(batch).reshape(-1).tolist()
        if step in (0, 40, 100) or len(record["candidates"]) == 1:
            fixtures.append({"step": step, "candidates": record["candidates"],
                "input": {"x": batch["x"].tolist(),
                          "nbr": {k: v.tolist() for k, v in batch["nbr"].items()},
                          "candidateNode": batch["cand_node"].tolist(),
                          "candidateExtra": batch["cand_extra"].tolist()},
                "expectedScores": scores,
                "expectedIndex": int(torch.argmax(torch.tensor(scores)).item())})
        pick = dispatcher.decide(record)
        builder.commit(pick["kind"], pick["job"], pick["res"])
        step += 1
    output = os.path.abspath(a.output)
    os.makedirs(os.path.dirname(output), exist_ok=True)
    with open(output, "w", encoding="utf-8") as stream:
        json.dump({"format": "dt-keti-lk-gnn-fixtures/v2", "fixtures": fixtures},
                  stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    print("wrote", output, "fixtures", len(fixtures), "rollout", step)


if __name__ == "__main__":
    main()
