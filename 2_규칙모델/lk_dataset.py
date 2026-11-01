# -*- coding: utf-8 -*-

import glob
import json
import math
import os
import re

import torch

from common import quality_weights, KIND_INDEX, NODE_KINDS

CHAIN_ORDER = {"ret": 0, "pplus": 1, "dplus": 2, "proc": 3,
               "pminus": 4, "dminus": 5, "sto": 6}

NBR_TYPES = ["pred", "succ", "disj", "all", "head", "tail"]


LK_NODE_DIM = len(NODE_KINDS) + 5 + 1 + 8 + 2 + 1


LK_CAND_DIM = 3 + 4 + 2


LK_CAND_DIM_T25 = LK_CAND_DIM + 6


LK_CAND_DIM_T27 = LK_CAND_DIM_T25 + 12


CAND_DROP = []


def set_cand_drop_from_name(name):

    global CAND_DROP


    m = re.search(r"_d(\d+)(?=_ctx_|_d\d+L)", os.path.basename(name))
    CAND_DROP = sorted(int(c) for c in m.group(1)) if m else []
    return CAND_DROP


def apply_cand_drop(t):
    if not CAND_DROP:
        return t
    n = t.shape[1]
    drop = sorted(i for i in CAND_DROP if i < n)
    if not drop:
        return t
    keep = [i for i in range(n) if i not in set(drop)]
    return t[:, keep + drop]


LAYOUT_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                           os.environ.get("PDP_SCENARIO_STEM", "scenario_03_P4R_260806") + "_LAYOUT.json")
_LAYOUT = None


def layout_travel():

    global _LAYOUT
    if _LAYOUT is None:
        try:
            _LAYOUT = json.load(open(LAYOUT_PATH, encoding="utf-8"))["travel"]
        except (OSError, KeyError, ValueError):
            _LAYOUT = {}
    return _LAYOUT or None


AMR_KINDS = ("pplus", "dplus", "pminus", "dminus")


def _ws_index(nd, chains, ws_choice):

    for v in chains[nd["job"]]:
        if v["kind"] == "proc":
            res = v.get("res") if v["committed"] else None
            res = res or ws_choice.get(nd["job"]) or ws_choice.get(str(nd["job"]))
            if res is None:
                opts = v.get("opts") or []
                res = opts[0] if opts else None
            return int(res[2:]) if res else 0
    return 0


def _gate_state(nodes, chains, ws_choice, t, caps, d_est):

    by = {}
    for nd in nodes:
        by[(nd["kind"], nd["job"])] = nd

    def done_by(kind, job):
        nd = by.get((kind, job))
        return nd is not None and nd["committed"] and d_est[nd["id"]] <= t + 1e-9

    def start_of(kind, job):

        nd = by.get((kind, job))
        if nd is None:
            return float("inf")          
        if not nd["committed"]:
            return float("inf")
        return d_est[nd["id"]] - (nd.get("dur") or 0.0)

    n_ws = max(len(caps.get("b_in") or []), 1)
    in_occ, out_occ = [0] * n_ws, [0] * n_ws
    in_free, out_free = [float("inf")] * n_ws, [float("inf")] * n_ws
    aout = ain = 0
    aout_free = ain_free = float("inf")
    for job, chain in chains.items():
        k = _ws_index(chain[0], chains, ws_choice)
        if k >= n_ws:
            k = n_ws - 1
        if done_by("dplus", job) and start_of("proc", job) > t + 1e-9:
            in_occ[k] += 1
            in_free[k] = min(in_free[k], start_of("proc", job))
        if done_by("proc", job) and ("pminus", job) in by and start_of("pminus", job) > t + 1e-9:
            out_occ[k] += 1
            out_free[k] = min(out_free[k], start_of("pminus", job))
        if done_by("ret", job) and ("pplus", job) in by and start_of("pplus", job) > t + 1e-9:
            aout += 1
            aout_free = min(aout_free, start_of("pplus", job))
        if done_by("dminus", job) and ("sto", job) in by and start_of("sto", job) > t + 1e-9:
            ain += 1
            ain_free = min(ain_free, start_of("sto", job))
    return {"in_occ": in_occ, "out_occ": out_occ, "in_free": in_free, "out_free": out_free,
            "aout": aout, "ain": ain, "aout_free": aout_free, "ain_free": ain_free}


def _frac(occ, cap):
    if cap is None or cap <= 0:
        return 0.0
    return min(occ / cap, 1.5)


def _endpoints(nd, chains, ws_choice):

    k = _ws_index(nd, chains, ws_choice)
    return {"pplus": ("asrs_out", f"in{k}"), "dplus": (f"in{k}", f"in{k}"),
            "pminus": (f"out{k}", "asrs_in"), "dminus": ("asrs_in", "asrs_in")}[nd["kind"]]

RES_ORDER = ["crane", "amr0", "amr1", "ws0", "ws1", "ws2", "ws3"]
LK_GLOB_DIM = len(RES_ORDER) * 3


def _slog(x):

    return math.copysign(math.log1p(abs(x)), x)


class LkDecision:
    __slots__ = ("x", "nbr", "cand_node", "cand_extra", "cand_due", "cand_op",
                 "label", "weight", "source", "step", "n_nodes", "glob",
                 "lab_kind", "lab_job", "lab_res",
                 "pos_mask", "regret",   
                 "cand_is_wait")         

    def __init__(self, **kw):
        self.pos_mask = None
        self.regret = None
        self.cand_is_wait = None
        for k, v in kw.items():
            setattr(self, k, v)


def lk_from_record(rec, quality_weight=1.0, source="", allow_no_label=False):

    nodes = rec["nodes"]
    cands = rec["candidates"]
    res_state = rec.get("res_state", {})

    by_id = {nd["id"]: nd for nd in nodes}


    chains = {}
    for nd in nodes:
        chains.setdefault(nd["job"], []).append(nd)
    r_est, d_est = {}, {}
    for job, chain in chains.items():
        chain.sort(key=lambda d: CHAIN_ORDER[d["kind"]])
        prev_done = 0.0
        for nd in chain:
            dur = nd.get("dur") or 0.0
            if nd["committed"]:
                r_est[nd["id"]] = nd["start"] or 0.0
                d_est[nd["id"]] = nd["done"] if nd["done"] is not None else prev_done + dur
            else:
                r_est[nd["id"]] = prev_done
                d_est[nd["id"]] = prev_done + dur
            prev_done = d_est[nd["id"]]


    if rec.get("now") is not None:
        t = float(rec["now"])
    else:
        _free0 = {res: float(rs.get("free", 0.0)) for res, rs in res_state.items()}
        t = min((max(r_est[c["node"]], _free0.get(c["res"], 0.0)) for c in cands),
                default=0.0)


    p = {}
    for nd in nodes:
        if nd["committed"]:
            p[nd["id"]] = max(0.0, d_est[nd["id"]] - t)
        else:
            p[nd["id"]] = nd.get("dur") or 0.0


    retained = [nd for nd in nodes
                if not (nd["committed"] and d_est[nd["id"]] < t - 1e-9)]
    local = {nd["id"]: i for i, nd in enumerate(retained)}
    retained_ids = set(local)

    p_max = max((p[nd["id"]] for nd in retained), default=1.0)
    p_max = max(p_max, 1.0)


    tail_sum, tail_cnt, head_ids, tail_ids = {}, {}, {}, {}
    job_done_est = {}
    for job, chain in chains.items():
        alive = [nd for nd in chain if nd["id"] in retained_ids]
        job_done_est[job] = d_est[chain[-1]["id"]] if chain else t
        for i, nd in enumerate(alive):
            tail = alive[i + 1:]
            head = alive[:i]
            tail_sum[nd["id"]] = sum(p[v["id"]] for v in tail)
            tail_cnt[nd["id"]] = len(tail)
            head_ids[nd["id"]] = [v["id"] for v in head]
            tail_ids[nd["id"]] = [v["id"] for v in tail]


    load = {res: 0.0 for res in res_state}
    for nd in retained:
        if nd["committed"]:
            res = nd.get("res")
            if res is not None:
                load[res] = load.get(res, 0.0) + p[nd["id"]]
        else:
            opts = nd.get("opts") or []
            if opts:
                share = p[nd["id"]] / len(opts)
                for res in opts:
                    load[res] = load.get(res, 0.0) + share
    total_load = max(sum(load.values()), 1e-9)
    free = {res: float(rs.get("free", 0.0)) for res, rs in res_state.items()}

    cand_node_ids = {c["node"] for c in cands}


    res_comp = {res: [0, -1, float("inf"), float("inf")] for res in RES_ORDER}
    for nd in retained:
        if nd["committed"]:
            continue
        for res in nd.get("opts") or []:
            rc = res_comp.get(res)
            if rc is None:
                continue
            rc[0] += 1
            if nd["due"] < rc[2]:
                rc[3] = rc[2]
                rc[1], rc[2] = nd["id"], nd["due"]
            elif nd["due"] < rc[3]:
                rc[3] = nd["due"]


    glob = []
    for res in RES_ORDER:
        cnt = res_comp[res][0]
        glob += [_slog((free.get(res, 0.0) - t) / p_max),
                 load.get(res, 0.0) / total_load,
                 math.log1p(cnt) / 2.0]


    feats = []
    for nd in retained:
        nid = nd["id"]
        onehot = [0.0] * len(NODE_KINDS)
        onehot[KIND_INDEX[nd["kind"]]] = 1.0
        r = r_est[nid]


        status = [0.0] * 5
        if nd["committed"]:
            status[0 if r <= t + 1e-9 else 1] = 1.0
        else:
            chain = chains[nd["job"]]
            pos = next(i for i, v in enumerate(chain) if v["id"] == nid)


            ready = pos == 0 or chain[pos - 1]["committed"] or nid in cand_node_ids
            if not ready:
                status[4] = 1.0
            elif r <= t + 1e-9:
                status[2] = 1.0
            else:
                status[3] = 1.0
        is_cand = 1.0 if nid in cand_node_ids else 0.0

        opts = nd.get("opts") or []
        if nd["committed"] or not opts:
            g = gp = 0.0
            res = nd.get("res")
            l_share = load.get(res, 0.0) / total_load if res else 0.0
        else:
            free_star = min(free.get(o, 0.0) for o in opts)
            g = max(0.0, free_star - r)
            gp = max(0.0, r - free_star)
            l_share = max(load.get(o, 0.0) for o in opts) / total_load
        pt = tail_sum.get(nid, 0.0)
        po = p[nid]
        cont = [po / p_max,
                max(0.0, r - t) / p_max,
                pt / p_max,
                tail_cnt.get(nid, 0) / 6.0,
                po / max(po + pt, 1e-9),
                math.log1p(g / p_max),
                math.log1p(gp / p_max),
                l_share]
        due = nd["due"]
        dues = [_slog((due - t) / p_max),
                _slog((due - job_done_est[nd["job"]]) / p_max)]
        flexf = [min(len(opts), 4) / 4.0]
        feats.append(onehot + status + [is_cand] + cont + dues + flexf)


    nbr = {c: (set() if c in ("disj", "all") else []) for c in NBR_TYPES}
    for job, chain in chains.items():
        alive_pos = [i for i, nd in enumerate(chain) if nd["id"] in retained_ids]
        for i in alive_pos:
            nid = chain[i]["id"]
            o = local[nid]

            if i > 0 and chain[i - 1]["id"] in retained_ids:
                nbr["pred"].append((local[chain[i - 1]["id"]], o))
            if i + 1 < len(chain) and chain[i + 1]["id"] in retained_ids:
                nbr["succ"].append((local[chain[i + 1]["id"]], o))
            for h in head_ids.get(nid, []):
                nbr["head"].append((local[h], o))
            for tl in tail_ids.get(nid, []):
                nbr["tail"].append((local[tl], o))
    by_res = {}
    for nd in retained:
        if not nd["committed"]:
            for res in nd.get("opts") or []:
                by_res.setdefault(res, []).append(local[nd["id"]])
    for members in by_res.values():
        for a in range(len(members)):
            for b in range(a + 1, len(members)):
                nbr["disj"].add((members[a], members[b]))
                nbr["disj"].add((members[b], members[a]))
    nbr["all"] = nbr["disj"] | set(nbr["pred"]) | set(nbr["succ"])
    nbr_t = {}
    for c in NBR_TYPES:
        pairs = list(nbr[c])
        nbr_t[c] = (torch.tensor(pairs, dtype=torch.long).t().contiguous()
                    if pairs else torch.zeros(2, 0, dtype=torch.long))


    travel = layout_travel()
    ws_choice = rec.get("ws_choice") or {}
    amr_pos = {}
    if travel:
        last_done = {}
        for nd in nodes:
            if nd["committed"] and nd["kind"] in AMR_KINDS and nd.get("res"):
                dn = d_est[nd["id"]]
                if dn >= last_done.get(nd["res"], -1.0):
                    last_done[nd["res"]] = dn
                    amr_pos[nd["res"]] = _endpoints(nd, chains, ws_choice)[1]


    caps = rec.get("caps") or {}
    gate = _gate_state(nodes, chains, ws_choice, t, caps, d_est)
    b_in, b_out = caps.get("b_in") or [], caps.get("b_out") or []
    out_cap, in_cap = caps.get("out_cap", -1), caps.get("in_cap", -1)
    queue = {}
    for nd in retained:
        if nd["committed"]:
            continue
        for rr in nd.get("opts") or []:
            queue.setdefault(rr, []).append(nd)
    for rr in queue:
        queue[rr].sort(key=lambda v: v["due"])


    cand_node, cand_extra, cand_due, cand_op = [], [], [], []
    label = -1
    lab = rec.get("label")
    for ci, c in enumerate(cands):
        nid = c["node"]
        cand_node.append(local[nid])
        res = c["res"]
        rc = [0.0, 0.0, 0.0]
        rc[0 if res == "crane" else 1 if res.startswith("amr") else 2] = 1.0
        fr = free.get(res, 0.0)
        r = r_est[nid]

        cc = res_comp.get(res, [0, -1, float("inf"), float("inf")])
        n_rival = max(0, cc[0] - 1)
        rival_due = (cc[3] if cc[1] == nid else cc[2]) if n_rival > 0 else None

        setup = 0.0
        if travel and res.startswith("amr") and c["kind"] in AMR_KINDS:
            h = int(res[3:])
            pos = amr_pos.get(res, "asrs_out")
            sp = _endpoints(by_id[nid], chains, ws_choice)[0]
            try:
                setup = float(travel[pos][sp][h])
            except (KeyError, IndexError, TypeError):
                setup = 0.0
        st = max(r, fr + setup)
        fin = st + (by_id[nid].get("dur") or 0.0)
        t25 = [math.log1p(setup / p_max),
               math.log1p(max(0.0, st - t) / p_max),
               math.log1p(max(0.0, fin - t) / p_max),
               _slog((by_id[nid]["due"] - (fin + tail_sum.get(nid, 0.0))) / p_max),
               math.log1p(max(0.0, r - fr - setup) / p_max),
               math.log1p(max(0.0, fr + setup - r) / p_max)]

        kk = int(res[2:]) if res.startswith("ws") else _ws_index(by_id[nid], chains, ws_choice)
        kk = min(kk, max(len(b_in) - 1, 0))
        cap_in = b_in[kk] if kk < len(b_in) else 0
        cap_out = b_out[kk] if kk < len(b_out) else 0
        kind = c["kind"]
        if kind == "dplus":
            full = cap_in > 0 and gate["in_occ"][kk] >= cap_in
            g_free = gate["in_free"][kk]
        elif kind == "ret":
            full = out_cap > 0 and gate["aout"] >= out_cap
            g_free = gate["aout_free"]
        elif kind == "dminus":
            full = in_cap > 0 and gate["ain"] >= in_cap
            g_free = gate["ain_free"]
        elif kind == "proc":
            full = cap_out > 0 and gate["out_occ"][kk] >= cap_out    
            g_free = gate["out_free"][kk]
        else:
            full, g_free = False, float("inf")
        g_rel = 1.0 if (not full or g_free == float("inf")) else min(max(0.0, g_free - t) / p_max, 1.0)
        gate_f = [_frac(gate["in_occ"][kk], cap_in), _frac(gate["out_occ"][kk], cap_out),
                  _frac(gate["aout"], out_cap), _frac(gate["ain"], in_cap),
                  1.0 if full else 0.0, g_rel if full else 0.0]

        qf = []
        for v in [v for v in queue.get(res, []) if v["id"] != nid][:3]:
            qf += [_slog((v["due"] - t) / p_max), p[v["id"]] / p_max]
        qf += [0.0] * (6 - len(qf))
        cand_extra.append(rc + [math.log1p(max(0.0, fr - r) / p_max),
                                math.log1p(max(0.0, r - fr) / p_max),
                                load.get(res, 0.0) / total_load,
                                _slog((fr - t) / p_max),
                                math.log1p(n_rival) / 2.0,
                                _slog((rival_due - t) / p_max) if rival_due is not None else 0.0]
                          + t25 + gate_f + qf)
        cand_due.append(by_id[nid]["due"])
        cand_op.append(c["op"] * 8 + KIND_INDEX[c["kind"]])
        if lab is not None and c["op"] == lab["op"] and c["kind"] == lab["kind"] \
                and c["res"] == lab["res"]:
            label = ci
    if not allow_no_label:
        assert label >= 0, f"label not in candidates: {source} step {rec.get('step')}"

    lab_c = cands[label] if label >= 0 else None
    return LkDecision(
        lab_kind=lab_c["kind"] if lab_c else None,
        lab_job=lab_c["job"] if lab_c else None,
        lab_res=lab_c["res"] if lab_c else None,
        x=torch.tensor(feats, dtype=torch.float32),
        nbr=nbr_t,
        glob=torch.tensor(glob, dtype=torch.float32),
        cand_node=torch.tensor(cand_node, dtype=torch.long),
        cand_extra=apply_cand_drop(torch.tensor(cand_extra, dtype=torch.float32)),
        cand_due=torch.tensor(cand_due, dtype=torch.float32),
        cand_op=cand_op,
        label=label,
        weight=quality_weight,
        source=source,
        step=rec.get("step", -1),
        n_nodes=len(retained),
    )


def append_wait_slot(d):

    c = d.cand_node.shape[0]
    d.cand_node = torch.cat([d.cand_node, torch.zeros(1, dtype=torch.long)])
    d.cand_extra = torch.cat([d.cand_extra, torch.zeros(1, d.cand_extra.shape[1])])

    d.cand_due = torch.cat([d.cand_due, torch.full((1,), float("inf"))])
    d.cand_op = list(d.cand_op) + [-1]
    mask = torch.zeros(c + 1, dtype=torch.bool)
    mask[c] = True
    d.cand_is_wait = mask
    return d


def load_wait_dataset(path, quality_weight=1.0):

    if os.path.isfile(path):
        files = [path]
    else:
        files = sorted(glob.glob(os.path.join(path, "waitset_*.jsonl"))
                       + glob.glob(os.path.join(path, "seqset_*.jsonl")))
    per_file = {}
    for f in files:
        rows = [json.loads(l) for l in open(f, encoding="utf-8")]


        has_wait = any(r.get("wait") for r in rows[1:] if r.get("t") == "decision")
        out = []
        for rec in rows[1:]:
            if rec.get("t") != "decision":
                continue
            d = lk_from_record(rec, quality_weight=quality_weight,
                               source=os.path.basename(f), allow_no_label=True)
            if has_wait:
                append_wait_slot(d)
            d.label = int(rec["label_idx"])
            lab_c = rec["candidates"][d.label] if d.label < len(rec["candidates"]) else None
            d.lab_kind = lab_c["kind"] if lab_c else "wait"
            d.lab_job = lab_c["job"] if lab_c else -1
            d.lab_res = lab_c["res"] if lab_c else "-"
            out.append(d)
        per_file[os.path.basename(f)] = out
    return per_file


def merge_exact(mine, rec, builder):

    for nd in rec.get("nodes", []):
        if nd.get("e") is not None:
            builder.exact_done[(nd["kind"], nd["job"])] = float(nd["e"])
    if rec.get("now") is not None:
        mine["now"] = float(rec["now"])
    for res, v in (rec.get("res_state") or {}).items():
        slot = mine["res_state"].setdefault(res, {"free": 0.0, "load": 0})
        slot["free"] = float(v.get("free", slot["free"]))
        slot["load"] = v.get("load", slot["load"])


def load_dataset_lk(rulelog_dir, walpha=1.0):

    from rollout_builder import Instance, Builder, _job_of
    files = sorted(glob.glob(os.path.join(rulelog_dir, "replay_*.jsonl")))
    w = quality_weights(files, alpha=walpha)
    per_file = {}
    for f in files:
        inst = Instance.harvest(f)
        b = Builder(inst)
        rows = [json.loads(l) for l in open(f, encoding="utf-8")]
        for rec in rows[1:]:
            if rec.get("t") == "decision" and rec["label"]["kind"] == "proc":
                b.ws_choice[_job_of(rec["label"], rec)] = rec["label"]["res"]
        out = []
        for rec in rows[1:]:
            if rec.get("t") != "decision":
                continue
            lab = rec["label"]
            job = _job_of(lab, rec)
            for nd in rec.get("nodes", []):            
                if nd.get("e") is not None:            
                    b.exact_done[(nd["kind"], nd["job"])] = float(nd["e"])   
            if not rec.get("pinned") and len(rec["candidates"]) >= 2:


                mine = b.emit_state(rec["step"])
                mine["label"] = lab
                merge_exact(mine, rec, b)
                if not rec.get("skip"):
                    out.append(lk_from_record(mine, w[f], source=os.path.basename(f)))
            b.commit(lab["kind"], job, lab["res"])
        per_file[os.path.basename(f)] = out
    return per_file


def collate_lk(decisions, device):

    xs, cn, cextra, cgraph, node_graph, globs, cpos = [], [], [], [], [], [], []
    cwait = []
    nbrs = {c: [] for c in NBR_TYPES}
    labels, weights, cand_ptr = [], [], [0]
    off = 0
    for gi, d in enumerate(decisions):
        xs.append(d.x)
        globs.append(d.glob)
        for c in NBR_TYPES:
            nbrs[c].append(d.nbr[c] + off)
        cn.append(d.cand_node + off)
        cextra.append(d.cand_extra)
        cgraph.append(torch.full((d.cand_node.shape[0],), gi, dtype=torch.long))
        node_graph.append(torch.full((d.n_nodes,), gi, dtype=torch.long))
        if getattr(d, "pos_mask", None) is not None:      
            cpos.append(d.pos_mask)
        else:
            lab_op = d.cand_op[d.label]
            cpos.append(torch.tensor([o == lab_op for o in d.cand_op], dtype=torch.bool))
        nc = d.cand_node.shape[0]
        cwait.append(d.cand_is_wait if d.cand_is_wait is not None
                     else torch.zeros(nc, dtype=torch.bool))
        labels.append(cand_ptr[-1] + d.label)
        weights.append(d.weight)
        cand_ptr.append(cand_ptr[-1] + d.cand_node.shape[0])
        off += d.n_nodes
    return dict(
        x=torch.cat(xs).to(device),
        glob=torch.stack(globs).to(device),
        nbr={c: torch.cat(nbrs[c], dim=1).to(device) for c in NBR_TYPES},
        node_graph=torch.cat(node_graph).to(device),
        cand_node=torch.cat(cn).to(device),
        cand_extra=torch.cat(cextra).to(device),
        cand_graph=torch.cat(cgraph).to(device),
        cand_pos=torch.cat(cpos).to(device),
        cand_is_wait=torch.cat(cwait).to(device),
        labels=torch.tensor(labels, dtype=torch.long, device=device),
        weights=torch.tensor(weights, dtype=torch.float32, device=device),
        cand_ptr=cand_ptr,
        num_graphs=len(decisions),
    )
