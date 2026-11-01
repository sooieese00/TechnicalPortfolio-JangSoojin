# -*- coding: utf-8 -*-

import torch
import torch.nn as nn

from lk_dataset import NBR_TYPES, LK_NODE_DIM, LK_CAND_DIM, LK_GLOB_DIM


def _mlp(i, h, o, dropout=0.2):
    return nn.Sequential(nn.Linear(i, h), nn.LeakyReLU(),
                         nn.Dropout(dropout), nn.Linear(h, o))


WIDE_TYPES = ("disj", "all")
AGG_CODE = {"mean": 0, "max": 1, "meanmax": 2}
AGG_NAME = {v: k for k, v in AGG_CODE.items()}


class LkGnn(nn.Module):
    def __init__(self, node_feat_dim=LK_NODE_DIM, cand_extra_dim=LK_CAND_DIM,
                 d=16, rounds=2, dropout=0.2, glob_dim=LK_GLOB_DIM, disj_agg="mean",
                 attn="none", pool="mean", scorer="point", ctx=False, wait_action=False,
                 cand_drop=()):
        super().__init__()
        self.d = d
        self.rounds = rounds
        self.cand_extra_dim = cand_extra_dim


        self.cand_drop = tuple(int(i) for i in cand_drop)
        self.register_buffer("drop_code",
                             torch.tensor(list(self.cand_drop), dtype=torch.long))
        self._drop_checked = False
        self.glob_dim = glob_dim

        assert disj_agg in AGG_CODE, disj_agg
        assert attn in ("none", "gatv2"), attn
        assert not (attn != "none" and disj_agg != "mean"), "어텐션과 max 집계는 같이 쓰지 않는다"
        self.disj_agg = disj_agg
        self.attn = attn
        self.register_buffer("agg_code", torch.tensor(AGG_CODE[disj_agg], dtype=torch.long))
        self.register_buffer("attn_code", torch.tensor(1 if attn == "gatv2" else 0, dtype=torch.long))

        self.ctx = bool(ctx)
        self.register_buffer("ctx_code", torch.tensor(1 if ctx else 0, dtype=torch.long))
        ctx_extra = 4 * cand_extra_dim if ctx else 0

        assert scorer in ("point", "pair"), scorer
        self.scorer = scorer
        self.register_buffer("scorer_code", torch.tensor(1 if scorer == "pair" else 0, dtype=torch.long))

        assert pool in ("mean", "none"), pool
        self.pool = pool
        self.register_buffer("pool_code", torch.tensor(1 if pool == "mean" else 0, dtype=torch.long))
        wide_extra = len(WIDE_TYPES) if disj_agg == "meanmax" else 0
        self.enc = _mlp(node_feat_dim, d, d, dropout)

        self.msg = nn.ModuleList([
            nn.ModuleDict({c: _mlp(d + node_feat_dim, d, d, dropout) for c in NBR_TYPES})
            for _ in range(rounds)])
        if attn == "gatv2":

            self.att_dst = nn.ModuleList([
                nn.ModuleDict({c: nn.Linear(d, d, bias=False) for c in NBR_TYPES})
                for _ in range(rounds)])
            self.att_vec = nn.ParameterList([
                nn.Parameter(torch.empty(len(NBR_TYPES), d)) for _ in range(rounds)])
            for p in self.att_vec:
                nn.init.xavier_uniform_(p)
        self.upd = nn.ModuleList([
            _mlp((len(NBR_TYPES) + wide_extra) * d + d + node_feat_dim, d, d, dropout)
            for _ in range(rounds)])


        self.dec = _mlp(2 * d + cand_extra_dim + ctx_extra + glob_dim, 2 * d, 1, dropout)


        self.wait_action = bool(wait_action)
        self.register_buffer("wait_code", torch.tensor(1 if wait_action else 0, dtype=torch.long))
        if wait_action:
            self.wait_head = _mlp(d + glob_dim, 2 * d, 1, dropout)
        if scorer == "pair":
            zdim = d + cand_extra_dim + ctx_extra

            self.pair = _mlp(3 * zdim + d + glob_dim, 2 * d, 1, dropout)

    def _check_drop(self, width):

        self._drop_checked = True
        import lk_dataset as _ld
        if list(self.cand_drop) != [int(i) for i in _ld.CAND_DROP]:
            raise ValueError(
                f"후보 피처 드롭 설정 불일치: 모델 {list(self.cand_drop)} vs "
                f"데이터셋 lk_dataset.CAND_DROP {list(_ld.CAND_DROP)}. "
                f"모델 로드 직전에 lk_dataset.CAND_DROP 을 맞춰라 "
                f"(set_cand_drop_from_name 또는 체크포인트의 drop_code)")
        want = self.cand_extra_dim + len(self.cand_drop)


        if width < want and not getattr(LkGnn, "_warned_slice", False):
            LkGnn._warned_slice = True
            print(f"[lk_gnn] 경고: 후보 피처 {width} -> {self.cand_extra_dim} 로 잘라 쓴다"
                  " (구버전 체크포인트 호환 경로)")

    def forward(self, batch):
        x = batch["x"]
        n = x.shape[0]
        h = self.enc(x)
        for l in range(self.rounds):
            ms = []
            for c in NBR_TYPES:
                ei = batch["nbr"][c]
                src, dst = ei[0], ei[1]
                agg = torch.zeros(n, self.d, device=x.device)
                cnt = torch.zeros(n, 1, device=x.device)
                m = None
                if src.numel():
                    m = self.msg[l][c](torch.cat([h[src], x[src]], dim=1))
                    agg.index_add_(0, dst, m)
                    cnt.index_add_(0, dst, torch.ones(src.shape[0], 1, device=x.device))
                if self.attn == "gatv2":
                    out = torch.zeros(n, self.d, device=x.device)
                    if m is not None:
                        ci = NBR_TYPES.index(c)
                        e = (torch.nn.functional.leaky_relu(m + self.att_dst[l][c](h[dst]), 0.2)
                             * self.att_vec[l][ci]).sum(dim=1)

                        mx = torch.full((n,), float("-inf"), device=x.device)
                        mx = mx.scatter_reduce(0, dst, e, reduce="amax", include_self=False)
                        ex = torch.exp(e - mx[dst])
                        den = torch.zeros(n, device=x.device).index_add(0, dst, ex)
                        alpha = ex / den[dst].clamp(min=1e-16)
                        out = out.index_add(0, dst, alpha.unsqueeze(1) * m)
                    ms.append(out)
                    continue
                mean = agg / cnt.clamp(min=1.0)
                if c in WIDE_TYPES and self.disj_agg != "mean":
                    mx = torch.zeros(n, self.d, device=x.device)
                    if m is not None:
                        idx = dst.unsqueeze(1).expand(-1, self.d)
                        mx = mx.scatter_reduce(0, idx, m, reduce="amax", include_self=False)

                        mx = torch.where(cnt > 0, mx, torch.zeros_like(mx))
                    if self.disj_agg == "max":
                        ms.append(mx)
                    else:
                        ms.append(mean)
                        ms.append(mx)
                else:
                    ms.append(mean)
            h = self.upd[l](torch.cat(ms + [h, x], dim=1))

        g = batch["node_graph"]
        ng = batch["num_graphs"]
        hm = torch.zeros(ng, self.d, device=x.device).index_add_(0, g, h)
        cntg = torch.zeros(ng, 1, device=x.device).index_add_(
            0, g, torch.ones(n, 1, device=x.device))
        hm = hm / cntg.clamp(min=1.0)
        if self.pool == "none":
            hm = torch.zeros_like(hm)


        cg = batch["cand_graph"]
        ce = batch["cand_extra"]
        if not self._drop_checked:
            self._check_drop(ce.shape[1])
        ce = ce[:, :self.cand_extra_dim]
        if self.ctx:
            ce = torch.cat([ce, self._ctx_feats(ce, cg, ng)], dim=1)
        ctx = hm[cg]
        if self.glob_dim > 0:
            ctx = torch.cat([ctx, batch["glob"][cg][:, :self.glob_dim]], dim=1)
        if self.scorer == "pair":
            return self._pair_scores(h[batch["cand_node"]], ce, ctx, cg, batch)
        q = self.dec(torch.cat([h[batch["cand_node"]], hm[cg], ce] +
                               ([batch["glob"][cg][:, :self.glob_dim]] if self.glob_dim > 0 else []), dim=1))
        q = q.squeeze(-1)
        w = batch.get("cand_is_wait")
        if self.wait_action and w is not None and bool(w.any()):
            gin = hm if self.glob_dim == 0 else torch.cat([hm, batch["glob"][:, :self.glob_dim]], dim=1)
            qw = self.wait_head(gin).squeeze(-1)
            q = torch.where(w, qw[cg], q)
        return q

    def _ctx_feats(self, ce, cg, ng):

        C, F = ce.shape
        one = torch.ones(C, 1, device=ce.device)
        cnt = torch.zeros(ng, 1, device=ce.device).index_add(0, cg, one)[cg]
        NEG, POS = torch.finfo(ce.dtype).min, torch.finfo(ce.dtype).max
        mx = torch.full((ng, F), NEG, device=ce.device).scatter_reduce(
            0, cg.unsqueeze(1).expand(-1, F), ce, reduce="amax", include_self=False)[cg]
        mn = torch.full((ng, F), POS, device=ce.device).scatter_reduce(
            0, cg.unsqueeze(1).expand(-1, F), ce, reduce="amin", include_self=False)[cg]
        rng = (mx - mn).clamp(min=1e-6)

        gt = torch.zeros_like(ce)
        ptr = 0

        bounds = torch.nonzero(torch.cat([torch.ones(1, dtype=torch.bool, device=ce.device),
                                          cg[1:] != cg[:-1]])).flatten().tolist() + [C]
        for a, b in zip(bounds[:-1], bounds[1:]):
            seg = ce[a:b]
            gt[a:b] = (seg.unsqueeze(1) < seg.unsqueeze(0)).sum(dim=1).to(ce.dtype)
        denom = (cnt - 1).clamp(min=1.0)
        rank = gt / denom
        return torch.cat([rank, 1.0 - rank, (ce - mn) / rng, (mx - ce) / rng], dim=1)

    def _pair_scores(self, hc, ce, ctx, cg, batch):

        z = torch.cat([hc, ce], dim=1)
        C = z.shape[0]
        ptr = batch["cand_ptr"]
        ii, jj = [], []
        for g in range(batch["num_graphs"]):
            a, b = ptr[g], ptr[g + 1]
            n = b - a
            if n < 2:
                continue
            idx = torch.arange(a, b, device=z.device)
            gi = idx.repeat_interleave(n)
            gj = idx.repeat(n)
            keep = gi != gj
            ii.append(gi[keep]); jj.append(gj[keep])
        if not ii:
            batch["pair_s"], batch["pair_i"], batch["pair_j"] = None, None, None
            return torch.zeros(C, device=z.device)
        i, j = torch.cat(ii), torch.cat(jj)
        x_ij = torch.cat([z[i], z[j], z[i] - z[j], ctx[i]], dim=1)
        x_ji = torch.cat([z[j], z[i], z[j] - z[i], ctx[i]], dim=1)
        s = (self.pair(x_ij) - self.pair(x_ji)).squeeze(-1)
        batch["pair_s"], batch["pair_i"], batch["pair_j"] = s, i, j
        borda = torch.zeros(C, device=z.device).index_add(0, i, s)
        cnt = torch.zeros(C, device=z.device).index_add(0, i, torch.ones_like(s))
        return borda / cnt.clamp(min=1.0)


def load_lk(state, cand_drop=None):

    """체크포인트 state_dict 에서 구조를 추론해 LkGnn 을 만든다.

    디코더 입력 잔여 차원이 전역 패널(21)을 담을 만큼 크면 신버전
    (cand 9 + glob 21), 아니면 구버전(cand 7, glob 0)으로 판정한다.
    """
    if "agg_code" not in state:          
        state = dict(state)
        state["agg_code"] = torch.tensor(0, dtype=torch.long)
    if "pool_code" not in state:
        state = dict(state)
        state["pool_code"] = torch.tensor(1, dtype=torch.long)
    if "ctx_code" not in state:
        state = dict(state)
        state["ctx_code"] = torch.tensor(0, dtype=torch.long)
    if "wait_code" not in state:
        state = dict(state)
        state["wait_code"] = torch.tensor(1 if any(k.startswith("wait_head.") for k in state) else 0,
                                          dtype=torch.long)
    if "scorer_code" not in state:
        state = dict(state)
        state["scorer_code"] = torch.tensor(1 if any(k.startswith("pair.") for k in state) else 0, dtype=torch.long)
    if "attn_code" not in state:
        state = dict(state)
        state["attn_code"] = torch.tensor(1 if any(k.startswith("att_") for k in state) else 0,
                                          dtype=torch.long)
    if "drop_code" not in state:         
        state = dict(state)
        state["drop_code"] = torch.tensor(list(cand_drop or ()), dtype=torch.long)
    elif cand_drop is not None and [int(x) for x in state["drop_code"].tolist()] != list(cand_drop):
        raise ValueError(f"drop_code {state['drop_code'].tolist()} 와 인자 {list(cand_drop)} 가 다르다")
    agg = AGG_NAME[int(state["agg_code"].item())]
    attn = "gatv2" if int(state["attn_code"].item()) else "none"
    pool = "mean" if int(state["pool_code"].item()) else "none"
    scorer = "pair" if int(state["scorer_code"].item()) else "point"
    ctx = bool(int(state["ctx_code"].item()))
    d = state["enc.3.weight"].shape[0]
    node_dim = state["enc.0.weight"].shape[1]
    extra = state["dec.0.weight"].shape[1] - 2 * d
    if bool(int(state["ctx_code"].item())):
        extra = extra // 5

    glob_dim = LK_GLOB_DIM if extra >= LK_GLOB_DIM + 7 else 0
    cand_extra = extra - glob_dim
    rounds = 1 + max(int(k.split(".")[1]) for k in state if k.startswith("upd."))
    model = LkGnn(node_feat_dim=node_dim, cand_extra_dim=cand_extra, d=d,
                  rounds=rounds, glob_dim=glob_dim, disj_agg=agg, attn=attn, pool=pool, scorer=scorer,
                  ctx=ctx, wait_action=bool(int(state["wait_code"].item())),
                  cand_drop=[int(x) for x in state["drop_code"].tolist()])
    model.load_state_dict(state)
    model.eval()
    return model
