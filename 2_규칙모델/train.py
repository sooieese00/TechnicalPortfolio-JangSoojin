# -*- coding: utf-8 -*-

import argparse
import json
import os
import random
import time

import torch

import lk_dataset as ld
from lk_dataset import collate_lk, load_dataset_lk
from lk_gnn import LkGnn


CAND_DROP = [7, 8]
CAND_DIM = 15
DIM = 16
ROUNDS = 3
DROPOUT = 0.2
SCORER = "pair"         
BLOCK_SIZE = 16
VAL_FRAC = 0.3
SPLIT_SEED = 0
LR = 1e-3
WEIGHT_DECAY = 1e-4
BATCH = 32


def _seg_sum(v, g, G):
    return torch.zeros(G, device=v.device, dtype=v.dtype).index_add(0, g, v)


def bt_loss(scores, batch):

    g, G = batch["cand_graph"], batch["num_graphs"]
    sy = scores[batch["labels"]][g]
    val = torch.nn.functional.softplus(-(sy - scores))
    keep = torch.ones_like(scores)
    keep[batch["labels"]] = 0.0
    cnt = _seg_sum(keep, g, G).clamp(min=1.0)
    per = _seg_sum(val * keep, g, G) / cnt
    return (batch["weights"] * per).sum() / G


@torch.no_grad()
def evaluate(model, decisions, device, batch_size=64):

    model.eval()
    paa = top1 = mrr = 0.0
    n = 0
    for i in range(0, len(decisions), batch_size):
        b = collate_lk(decisions[i:i + batch_size], device)
        scores = model(b)
        g, G = b["cand_graph"], b["num_graphs"]
        sy = scores[b["labels"]]
        gt = (scores > sy[g]).to(scores.dtype)
        rank = 1.0 + _seg_sum(gt, g, G)
        cnt = _seg_sum(torch.ones_like(scores), g, G)
        paa += float(((cnt - rank) / (cnt - 1).clamp(min=1)).sum())
        top1 += float((rank == 1).sum())
        mrr += float((1.0 / rank).sum())
        n += G
    return paa / n, top1 / n, mrr / n, n


def cv_split(per_file, block_size, folds, fold):

    train, val = [], []
    for _, ds in sorted(per_file.items()):
        blocks = [ds[i:i + block_size] for i in range(0, len(ds), block_size)]
        for bi, blk in enumerate(blocks):
            (val if bi % folds == fold else train).extend(blk)
    return train, val


def block_split(per_file, block_size, val_frac, seed):

    srnd = random.Random(seed)
    train, val = [], []
    for _, ds in sorted(per_file.items()):
        blocks = [ds[i:i + block_size] for i in range(0, len(ds), block_size)]
        k = round(len(blocks) * val_frac) if len(blocks) > 1 else 0
        val_idx = set(srnd.sample(range(len(blocks)), k)) if k else set()
        for bi, blk in enumerate(blocks):
            (val if bi in val_idx else train).extend(blk)
    return train, val


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", required=True, help="학습 리플레이 디렉터리 (replay_*.jsonl)")
    ap.add_argument("--test-data", default="", help="검증 리플레이 디렉터리. 없으면 --data 를 블록 분할")
    ap.add_argument("--out", default="model.pt")
    ap.add_argument("--epochs", type=int, default=200)
    ap.add_argument("--select", type=int, default=5, help="몇 epoch 마다 검증해 최고 모델을 남길지")
    ap.add_argument("--cv-folds", type=int, default=0,
                    help="한 인스턴스 안에서 N겹 교차검증. --cv-fold 와 함께 쓴다")
    ap.add_argument("--cv-fold", type=int, default=0, help="검증에 쓸 겹 번호 (0..N-1)")
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--cpu", action="store_true")
    ap.add_argument("--metrics", default="", help="학습 이력 JSON 출력 경로")
    a = ap.parse_args()

    device = torch.device("cpu" if a.cpu or not torch.cuda.is_available() else "cuda")
    torch.manual_seed(a.seed)
    rnd = random.Random(a.seed)


    ld.CAND_DROP = list(CAND_DROP)

    per_file = load_dataset_lk(a.data)
    if a.cv_folds > 1:
        train, val = cv_split(per_file, BLOCK_SIZE, a.cv_folds, a.cv_fold)
        print(f"분할 = 인스턴스 내 {a.cv_folds}겹 중 {a.cv_fold}번째가 검증 "
              f"(블록 {BLOCK_SIZE}결정) · 학습 {len(train)} · 검증 {len(val)} 결정")
    elif a.test_data:
        train = [d for ds in per_file.values() for d in ds]
        val = [d for ds in load_dataset_lk(a.test_data).values() for d in ds]
        print(f"분할 = 디렉터리 지정 · 학습 {len(train)} · 검증 {len(val)} 결정")
    else:
        train, val = block_split(per_file, BLOCK_SIZE, VAL_FRAC, SPLIT_SEED)
        print(f"분할 = 블록 {BLOCK_SIZE}결정 · 검증 {VAL_FRAC:.0%} · 학습 {len(train)} · 검증 {len(val)} 결정")
    if not train:
        raise SystemExit("학습 결정이 없다. --data 경로에 replay_*.jsonl 이 있는지 확인하라.")

    model = LkGnn(cand_extra_dim=CAND_DIM - len(CAND_DROP), d=DIM, rounds=ROUNDS,
                  dropout=DROPOUT, glob_dim=0, scorer=SCORER, ctx=True,
                  cand_drop=CAND_DROP).to(device)
    n_par = sum(p.numel() for p in model.parameters())
    print(f"모델 = 쌍별 채점기 · BT 손실 · 후보피처 {CAND_DIM - len(CAND_DROP)} · d={DIM} L={ROUNDS}"
          f" · 파라미터 {n_par:,} · {device}")

    opt = torch.optim.AdamW(model.parameters(), lr=LR, weight_decay=WEIGHT_DECAY)
    sched = torch.optim.lr_scheduler.CosineAnnealingLR(opt, T_max=a.epochs)

    best = {"paa": -1.0, "epoch": 0, "state": None}
    t0 = time.time()
    history = []
    for epoch in range(1, a.epochs + 1):
        model.train()
        rnd.shuffle(train)
        tot = nb = 0
        for i in range(0, len(train), BATCH):
            b = collate_lk(train[i:i + BATCH], device)
            loss = bt_loss(model(b), b)
            opt.zero_grad()
            loss.backward()
            opt.step()
            tot += loss.item()
            nb += 1
        sched.step()

        if val and (epoch % a.select == 0 or epoch == a.epochs):
            paa, t1, mrr, _ = evaluate(model, val, device)
            history.append(dict(epoch=epoch, loss=tot / max(nb, 1), paa=paa, top1=t1, mrr=mrr))
            mark = ""
            if paa > best["paa"]:
                best.update(paa=paa, epoch=epoch,
                            state={k: v.detach().cpu().clone() for k, v in model.state_dict().items()})
                mark = "  <-- 최고"
            print(f"epoch {epoch:4d}  loss {tot / max(nb, 1):.4f}  "
                  f"검증 쌍승률 {paa:.4f}  top-1 {t1:.3f}  MRR {mrr:.3f}{mark}")
        elif epoch % 10 == 0:
            print(f"epoch {epoch:4d}  loss {tot / max(nb, 1):.4f}")

    state = best["state"] or model.state_dict()
    if best["state"] is not None:
        print(f"채택 = epoch {best['epoch']} (검증 쌍승률 {best['paa']:.4f})")
    torch.save(state, a.out)
    if a.metrics:
        with open(a.metrics, "w", encoding="utf-8") as f:
            json.dump(dict(seed=a.seed, epochs=a.epochs, train_count=len(train),
                           validation_count=len(val), best_epoch=best["epoch"],
                           best_paa=best["paa"], torch_version=torch.__version__,
                           history=history), f, indent=2)
    print(f"학습 {time.time() - t0:.1f}s · 저장 {a.out}")


if __name__ == "__main__":
    main()
