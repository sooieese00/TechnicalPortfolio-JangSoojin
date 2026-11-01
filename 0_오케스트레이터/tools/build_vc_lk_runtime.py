
import argparse
import re
from pathlib import Path


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--gnn-dir", required=True)
    p.add_argument("--kernel", required=True)
    p.add_argument("--output", required=True)
    a = p.parse_args()
    gnn = Path(a.gnn_dir)

    builder = (gnn / "rollout_builder.py").read_text(encoding="utf-8")
    builder = builder[builder.index("CHAIN_KINDS ="):builder.index("def validate_against")]
    builder = builder.replace('raise ValueError(f"label not in candidates at step {rec[\'step\']}")',
                              'raise ValueError("label not in candidates at step {}".format(rec["step"]))')
    builder = builder.replace('assert (kind, job) not in self.committed, f"double commit {(kind, job)}"',
                              'assert (kind, job) not in self.committed, "double commit {}".format((kind, job))')
    builder = builder.replace('f"amr{h}"', '"amr{}".format(h)').replace('f"ws{k}"', '"ws{}".format(k)')

    feature = (gnn / "lk_dataset.py").read_text(encoding="utf-8")
    feature = feature[feature.index("CHAIN_ORDER ="):feature.index("def append_wait_slot")]


    start, end = feature.index("def set_cand_drop_from_name"), feature.index("AMR_KINDS =")
    feature = feature[:start] + '''def apply_cand_drop(rows):
    drop = list(VC_LK_DROP_CODE)
    if not drop:
        return rows
    width = len(rows[0]) if rows else 0
    keep = [i for i in range(width) if i not in set(drop)]
    order = keep + [i for i in drop if i < width]
    return [[row[i] for i in order] for row in rows]

def layout_travel():
    return None

''' + feature[end:]
    class_start, function_start = feature.index("class LkDecision"), feature.index("def lk_from_record")
    feature = feature[:class_start] + feature[function_start:]
    feature = feature.replace('f"in{k}"', '"in{}".format(k)').replace('f"out{k}"', '"out{}".format(k)')
    feature = feature.replace('f"label not in candidates: {source} step {rec.get(\'step\')}"',
                              '"label not in candidates: {} step {}".format(source, rec.get("step"))')
    feature = feature.replace('return min(occ / cap, 1.5)',
                              'return min(float(occ) / cap, 1.5)')
    feature = feature.replace('min((max(r_est[c["node"]], _free0.get(c["res"], 0.0)) for c in cands),\n                default=0.0)',
                              'min([max(r_est[c["node"]], _free0.get(c["res"], 0.0)) for c in cands] or [0.0])')
    feature = feature.replace('max((p[nd["id"]] for nd in retained), default=1.0)',
                              'max([p[nd["id"]] for nd in retained] or [1.0])')
    feature = feature.replace('nbr_t[c] = (torch.tensor(pairs, dtype=torch.long).t().contiguous()\n                    if pairs else torch.zeros(2, 0, dtype=torch.long))',
                              'nbr_t[c] = [[pair[0] for pair in pairs], [pair[1] for pair in pairs]]')
    return_start = feature.index("    lab_c = cands[label] if label >= 0 else None")
    feature = feature[:return_start] + '''    return {"x": feats, "nbr": nbr_t, "glob": glob,
            "candidateNode": cand_node, "candidateExtra": apply_cand_drop(cand_extra)}

'''

    kernel = Path(a.kernel).read_text(encoding="utf-8")
    kernel = kernel[kernel.index("class VcLkWeights"):]
    wrapper = r'''
def vc_lk_instance_from_json(raw_json):
    raw = json.loads(raw_json)
    inst = Instance()
    inst.m, inst.n, inst.r = int(raw["m"]), int(raw["n"]), int(raw["r"])
    inst.b_in, inst.b_out = list(raw.get("b_in") or []), list(raw.get("b_out") or [])
    inst.out_cap, inst.in_cap = int(raw.get("out_cap", -1)), int(raw.get("in_cap", -1))
    inst.makespan = 1.0
    for item in raw["active"]:
        key = (item["kind"], int(item["job"]))
        inst.active[key] = True
        inst.node_id[key] = int(item["node"])
        inst.dur[key] = float(item.get("dur", 0.0))
    for job, due in raw.get("due", {}).items():
        inst.due[int(job)] = float(due)
    for item in raw["res_options"]:
        inst.res_options[(item["kind"], int(item["job"]))] = sorted(item["res"])
    inst.pinned_crane = [(item["kind"], int(item["job"])) for item in raw.get("pinned_crane", [])]
    inst.dur_by_res = {}
    for item in raw.get("dur_by_res", []):
        inst.dur_by_res[(item["kind"], int(item["job"]))] = dict(item["dur"])
    return inst

class VcLkEmbeddedSession(object):
    def __init__(self, manifest_json, weights_b64, instance_json):
        started = time.time()
        self.weights = VcLkWeights(manifest_json, weights_b64)
        global VC_LK_DROP_CODE
        VC_LK_DROP_CODE = list(self.weights.model.get("dropCode", []))
        self.model = VcLkGnn(self.weights)
        self.builder = Builder(vc_lk_instance_from_json(instance_json))
        self.state_revision = 0
        self.last_choice_key = None
        self.last_choice_value = None
        self.load_seconds = time.time() - started

    @staticmethod
    def key(item):
        return (str(item.get("kind", "")), int(item.get("job", -1)), str(item.get("res", "")))

    def choose(self, candidates, now=None, res_state=None):
        if not candidates:
            raise ValueError("No GNN candidates")
        cache_key = (self.state_revision, tuple([self.key(item) for item in candidates]))
        if cache_key == self.last_choice_key:
            choice, scores = self.last_choice_value
            return choice, list(scores), 0.0
        record = self.builder.emit_state(self.state_revision)
        feasible = dict((self.key(item), item) for item in record["candidates"])
        mapped = []
        for item in candidates:
            key = self.key(item)
            if key not in feasible:
                raise ValueError("VC candidate is not feasible in Lk graph: {}".format(key))
            mapped.append(feasible[key])
        record["candidates"] = mapped
        if now is not None:
            record["now"] = float(now)
        if res_state is not None:
            record["res_state"] = res_state
        started = time.time()
        scores = self.model.forward(lk_from_record(record, allow_no_label=True))
        choice = max(range(len(scores)), key=lambda i: scores[i])
        self.last_choice_key = cache_key
        self.last_choice_value = (choice, tuple(scores))
        return choice, scores, time.time() - started

    def commit(self, kind, job, resource, done=None):
        key = (str(kind), int(job), str(resource))
        operation = (key[0], key[1])
        if operation in self.builder.committed:
            if done is not None:
                self.builder.exact_done[operation] = float(done)
                self.state_revision += 1
                self.last_choice_key = self.last_choice_value = None
                return True
            return False
        if key not in set(self.builder.candidates()):
            return False
        self.builder.commit(key[0], key[1], key[2])
        if done is not None:
            self.builder.exact_done[(key[0], key[1])] = float(done)
        self.state_revision += 1
        self.last_choice_key = self.last_choice_value = None
        return True

def vc_embedded_gnn_create(instance_json):
    return VcLkEmbeddedSession(EMBEDDED_GNN_MANIFEST_JSON,
                               EMBEDDED_GNN_WEIGHTS_B64, instance_json)
'''


    header = ("# -*- coding: utf-8 -*-\n"
              "import base64, json, math, operator, struct, time\n"
              "from collections import defaultdict\n"
              "try:\n    from itertools import imap as lk_imap\n"
              "except ImportError:\n    lk_imap=map\n"
              "NODE_KINDS=['ret','sto','pplus','dplus','pminus','dminus','proc']\n"
              "KIND_INDEX=dict((kind,index) for index,kind in enumerate(NODE_KINDS))\n"
              "GNN_RUNTIME_VERSION='lk-gnn-v2'\n"
              "VC_LK_DROP_CODE=[]\n")
    output = header + "\n" + builder + "\n" + feature + "\n" + kernel + "\n" + wrapper
    compile(output, str(a.output), "exec")
    Path(a.output).write_text(output, encoding="utf-8")
    print("created", Path(a.output).resolve(), "chars", len(output))


if __name__ == "__main__":
    main()
