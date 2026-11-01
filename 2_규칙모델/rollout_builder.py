# -*- coding: utf-8 -*-

import json
import sys
from collections import defaultdict

CHAIN_KINDS = ["ret", "pplus", "dplus", "proc", "pminus", "dminus", "sto"]


class Instance:


    def __init__(self):
        self.m = 0
        self.n = 0
        self.r = 0
        self.active = {}
        self.res_options = {}
        self.pinned_crane = []
        self.due = {}
        self.dur = {}
        self.node_id = {}
        self.b_in = []
        self.b_out = []
        self.out_cap = -1
        self.in_cap = -1
        self.makespan = 1.0
        self.dur_by_res = {}

    @staticmethod
    def from_json(path):

        d = json.load(open(path, encoding="utf-8"))
        inst = Instance()
        inst.m, inst.n, inst.r = d["m"], d["n"], d["r"]
        inst.b_in = list(d.get("b_in") or [])
        inst.b_out = list(d.get("b_out") or [])
        inst.out_cap = d.get("out_cap", -1)
        inst.in_cap = d.get("in_cap", -1)
        inst.makespan = 1.0

        for a in d["active"]:
            key = (a["kind"], a["job"])
            inst.active[key] = True
            inst.node_id[key] = a["node"]
            inst.dur[key] = a.get("dur", 0.0)
        for job, due in (d.get("due") or {}).items():
            inst.due[int(job)] = due
        inst.res_options = {(o["kind"], o["job"]): sorted(o["res"]) for o in d["res_options"]}
        inst.pinned_crane = [(p["kind"], p["job"]) for p in (d.get("pinned_crane") or [])]

        inst.dur_by_res = {(x["kind"], x["job"]): dict(x["dur"]) for x in (d.get("dur_by_res") or [])}
        return inst

    @staticmethod
    def harvest(trace_path):

        inst = Instance()
        rows = [json.loads(l) for l in open(trace_path, encoding="utf-8")]
        head = rows[0]
        inst.m, inst.n, inst.r = head["m"], head["n"], head["r"]
        inst.b_in = head.get("b_in", [])
        inst.b_out = head.get("b_out", [])
        inst.out_cap = head.get("asrsOutCap", -1)
        inst.in_cap = head.get("asrsInCap", -1)
        inst.makespan = max(float(head.get("makespan", 1.0)), 1.0)

        options = defaultdict(set)
        for rec in rows[1:]:
            if rec.get("t") != "decision":
                continue
            for nd in rec["nodes"]:
                key = (nd["kind"], nd["job"])
                inst.active[key] = True
                inst.due[nd["job"]] = nd["due"]
                if nd.get("dur") is not None:
                    inst.dur[key] = nd["dur"]
                inst.node_id[key] = nd["id"]
            for c in rec["candidates"]:
                options[(c["kind"], c["job"])].add(c["res"])

            if rec.get("pinned"):
                lab = rec["label"]
                inst.pinned_crane.append((lab["kind"], _job_of(lab, rec)))

        inst.res_options = {k: sorted(v) for k, v in options.items()}
        return inst


def _job_of(label, rec):

    for c in rec["candidates"]:
        if c["op"] == label["op"] and c["kind"] == label["kind"] and c["res"] == label["res"]:
            return c["job"]
    raise ValueError(f"label not in candidates at step {rec['step']}")


class Builder:


    def __init__(self, inst):
        self.inst = inst
        self.committed = set()
        self.seq = defaultdict(list)
        self.res_of = {}
        self.exact_done = {}
        self.pin_index = 0


        self.ws_choice = {}


    def ready(self, kind, job):
        idx = CHAIN_KINDS.index(kind)
        for prev in range(idx - 1, -1, -1):
            pk = CHAIN_KINDS[prev]
            if not self.inst.active.get((pk, job)):
                continue
            return (pk, job) in self.committed
        return True

    def candidates(self):
        out = []
        pin_left = self.pin_index < len(self.inst.pinned_crane)
        for (kind, job), _ in self.inst.active.items():
            if (kind, job) in self.committed or not self.ready(kind, job):
                continue
            if kind in ("ret", "sto"):
                if pin_left and self.inst.pinned_crane[self.pin_index] != (kind, job):
                    continue
                out.append((kind, job, "crane"))
            else:
                for res in self.inst.res_options.get((kind, job), []):
                    out.append((kind, job, res))
        return out


    def commit(self, kind, job, res):
        assert (kind, job) not in self.committed, f"double commit {(kind, job)}"
        self.committed.add((kind, job))
        self.seq[res].append((kind, job))
        self.res_of[(kind, job)] = res
        if kind in ("ret", "sto") and self.pin_index < len(self.inst.pinned_crane) \
                and self.inst.pinned_crane[self.pin_index] == (kind, job):
            self.pin_index += 1

    def done(self):
        return len(self.committed) == len(self.inst.active)


    def _edges(self):

        inst = self.inst
        nid = inst.node_id
        edges = []

        def E(uk, vk, w, t):
            edges.append({"u": nid[uk], "v": nid[vk], "w": round(w, 3), "type": t})


        crane = self.seq.get("crane", [])
        rets, stos = [], []
        for i, cur in enumerate(crane):
            if i > 0:
                E(crane[i - 1], cur, inst.dur.get(crane[i - 1], 0.0), "crane_seq")
            kind, job = cur
            if kind == "ret":
                if inst.out_cap > 0 and len(rets) >= inst.out_cap:
                    gate = rets[len(rets) - inst.out_cap]
                    if ("pplus", gate[1]) in self.committed:
                        E(("pplus", gate[1]), cur, 0.0, "buf_asrs_out")
                rets.append(cur)
            else:
                if inst.in_cap > 0 and len(stos) >= inst.in_cap:
                    gate = stos[len(stos) - inst.in_cap]
                    if ("dminus", job) in self.committed:
                        E(gate, ("dminus", job), 0.0, "buf_asrs_in")
                stos.append(cur)


        for h in range(inst.r):
            route = self.seq.get(f"amr{h}", [])
            for i in range(1, len(route)):
                E(route[i - 1], route[i], inst.dur.get(route[i - 1], 0.0), "amr_route")


        for k in range(inst.n):
            seq = self.seq.get(f"ws{k}", [])
            b_in = inst.b_in[k] if k < len(inst.b_in) else 1
            b_out = inst.b_out[k] if k < len(inst.b_out) else 1
            for q, cur in enumerate(seq):
                job = cur[1]
                if q > 0:
                    E(seq[q - 1], cur, inst.dur.get(seq[q - 1], 0.0), "machine_seq")
                if q >= b_in and inst.active.get(("dplus", job)) and ("dplus", job) in self.committed:
                    E(seq[q - b_in], ("dplus", job), 0.0, "buf_ws_in")
                if (q >= b_out and inst.active.get(("pminus", job)) and ("pminus", job) in self.committed
                        and inst.active.get(("pminus", seq[q - b_out][1]))
                        and ("pminus", seq[q - b_out][1]) in self.committed):
                    E(("pminus", seq[q - b_out][1]), ("pminus", job), 0.0, "buf_ws_out")
                if (q >= b_out + 1 and inst.active.get(("pminus", seq[q - 1 - b_out][1]))
                        and ("pminus", seq[q - 1 - b_out][1]) in self.committed):
                    E(("pminus", seq[q - 1 - b_out][1]), cur, inst.dur.get(cur, 0.0), "buf_ws_block")


        for job in range(inst.m):
            chain = [(k, job) for k in CHAIN_KINDS if inst.active.get((k, job))]
            for c in range(len(chain) - 1):
                both = chain[c] in self.committed and chain[c + 1] in self.committed
                E(chain[c], chain[c + 1], 0.0, "conj" if both else "conj_open")
        return edges

    def _times(self):

        inst = self.inst


        LENGTH_RULE = {
            "crane_seq": "w", "amr_route": "w", "machine_seq": "w", "buf_ws_block": "w",
            "conj": "dur", "buf_asrs_in": "dur", "buf_asrs_out": "dur",
            "buf_ws_in": "zero", "buf_ws_out": "zero",
        }
        id2key = {v: k for k, v in inst.node_id.items()}
        adj = defaultdict(list)
        indeg = defaultdict(int)
        nodes = set()
        for e in self._edges():
            uk, vk = id2key[e["u"]], id2key[e["v"]]
            if uk not in self.committed or vk not in self.committed:
                continue
            rule = LENGTH_RULE.get(e["type"], "zero")
            length = e["w"] if rule == "w" else inst.dur.get(uk, 0.0) if rule == "dur" else 0.0
            adj[uk].append((vk, length))
            indeg[vk] += 1
            nodes.update([uk, vk])
        for key in self.committed:
            nodes.add(key)

        H = {key: 0.0 for key in nodes}
        queue = [key for key in nodes if indeg[key] == 0]
        seen = 0
        while queue:
            u = queue.pop()
            seen += 1
            for v, length in adj[u]:
                H[v] = max(H[v], H[u] + length)
                indeg[v] -= 1
                if indeg[v] == 0:
                    queue.append(v)
        return H

    def emit_state(self, step=0):

        inst = self.inst
        H = self._times()

        rem = defaultdict(float)
        flex = {}
        for key in inst.active:
            if key not in self.committed:
                rem[key[1]] += inst.dur.get(key, 0.0)
        for key in inst.active:
            if key[0] == "proc":
                flex[key[1]] = 0 if key in self.committed \
                    else len(inst.res_options.get(key, []))

        all_res = set()
        for opts in inst.res_options.values():
            all_res.update(opts)
        res_state = {}
        for res in all_res:
            s = self.seq.get(res, [])
            if s and s[-1] in self.exact_done:
                free = self.exact_done[s[-1]]
            else:
                free = H[s[-1]] + inst.dur.get(s[-1], 0.0) if s else 0.0
            res_state[res] = {"free": round(free, 3), "load": len(s)}

        nodes = []
        for key in inst.active:
            kind, job = key
            committed = key in self.committed
            if committed and key in self.exact_done:

                done = round(self.exact_done[key], 3)
                start = round(done - inst.dur.get(key, 0.0), 3)
            else:
                start = round(H[key], 3) if committed else None
                done = round(H[key] + inst.dur.get(key, 0.0), 3) if committed else None
            nodes.append({"id": inst.node_id[key], "kind": kind, "job": job,
                          "due": inst.due[job], "committed": committed,
                          "start": start, "done": done, "dur": inst.dur.get(key, 0.0),
                          "rem": round(rem[job], 3), "flex": flex.get(job, 0),

                          "res": self.res_of.get(key),
                          "opts": None if committed else inst.res_options.get(key, [])})

        cands = [{"op": _op_index(kind, job, inst.m), "node": inst.node_id[(kind, job)],
                  "kind": kind, "job": job, "res": res}
                 for kind, job, res in self.candidates()]

        undecided = []
        groups = defaultdict(set)
        for c in cands:
            groups[c["res"]].add(c["node"])
        for res, members in groups.items():
            mem = sorted(members)
            for a in range(len(mem)):
                for b in range(a + 1, len(mem)):
                    undecided.append({"u": mem[a], "v": mem[b], "res": res})

        return {"t": "decision", "step": step, "pinned": False,
                "nodes": nodes, "edges": self._edges(),
                "undecided": undecided, "candidates": cands,
                "res_state": res_state, "ws_choice": dict(self.ws_choice),
                "caps": {"b_in": list(inst.b_in), "b_out": list(inst.b_out),
                         "out_cap": inst.out_cap, "in_cap": inst.in_cap}}


def _op_index(kind, job, m):

    return {"ret": job, "sto": m + job,
            "pplus": job, "dplus": m + job, "pminus": 2 * m + job, "dminus": 3 * m + job,
            "proc": job}[kind]


def validate_against(trace_path, verbose=False):

    inst = Instance.harvest(trace_path)
    b = Builder(inst)
    rows = [json.loads(l) for l in open(trace_path, encoding="utf-8")]
    mismatches = 0
    steps = 0
    for rec in rows[1:]:
        if rec.get("t") != "decision":
            continue
        steps += 1
        recorded = {(c["kind"], c["job"], c["res"]) for c in rec["candidates"]}
        mine = set(b.candidates())
        if mine != recorded:
            mismatches += 1
            if verbose and mismatches <= 3:
                print(f"  step {rec['step']}: 빌더에만 {sorted(mine - recorded)[:4]}"
                      f" / 기록에만 {sorted(recorded - mine)[:4]}")
        lab = rec["label"]
        b.commit(lab["kind"], _job_of(lab, rec), lab["res"])
    return steps, mismatches, b.done()


if __name__ == "__main__":
    import glob
    import os
    rulelog = sys.argv[1] if len(sys.argv) > 1 else "data"
    files = sorted(glob.glob(os.path.join(rulelog, "replay_*.jsonl")))
    files += sorted(glob.glob(os.path.join(rulelog, "verify_instances", "replay_*.jsonl")))
    total_bad = 0
    for f in files:
        steps, bad, complete = validate_against(f, verbose=True)
        total_bad += bad
        tag = "OK " if bad == 0 and complete else "FAIL"
        print(f"{tag} {os.path.basename(f):48s} 결정 {steps:3d}, 후보 불일치 {bad}, 완주 {complete}")
    print()
    print("전체 후보 불일치:", total_bad)
