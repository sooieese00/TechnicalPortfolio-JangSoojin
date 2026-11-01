# -*- coding: utf-8 -*-

import argparse
import json

import torch

import lk_dataset as ld
from lk_dataset import collate_lk, lk_from_record
from lk_gnn import load_lk


class Dispatcher:


    def __init__(self, ckpt_path, device=None):
        self.device = torch.device(device or ("cuda" if torch.cuda.is_available() else "cpu"))
        state = torch.load(ckpt_path, map_location="cpu")

        if "drop_code" in state:
            ld.CAND_DROP = [int(x) for x in state["drop_code"].tolist()]
        else:
            ld.set_cand_drop_from_name(ckpt_path)
        self.model = load_lk(state, cand_drop=list(ld.CAND_DROP)).to(self.device).eval()
        self.cand_drop = list(ld.CAND_DROP)

    @torch.no_grad()
    def scores(self, record):

        d = lk_from_record(record, allow_no_label=True)

        return self.model(collate_lk([d], self.device)).reshape(-1)

    def decide(self, record):
        cands = record.get("candidates") or record.get("cands") or []
        if not cands:
            raise ValueError("후보가 없다: candidates 키를 확인하라")
        s = self.scores(record)
        idx = int(torch.argmax(s).item())
        c = cands[idx]
        return {
            "index": idx,
            "job": c.get("job"),
            "kind": c.get("kind"),
            "res": c.get("res"),
            "op": c.get("op"),
            "scores": [round(float(v), 4) for v in s.tolist()],
        }


KIND_KO = {"ret": "출고", "sto": "입고", "pplus": "P+ 픽업", "dplus": "D+ 반입",
           "pminus": "P- 픽업", "dminus": "D- 반출", "proc": "가공"}


def show(rec, pick, n_show=6):
    cands = rec.get("candidates") or rec.get("cands") or []
    order = sorted(range(len(cands)), key=lambda i: -pick["scores"][i])
    print(f"  후보 {len(cands)}개 · 기준시각 {rec.get('now')}")
    for r, i in enumerate(order[:n_show], 1):
        c = cands[i]
        star = "  <-- 선택" if i == pick["index"] else ""
        print(f"    {r}. {pick['scores'][i]:+8.4f}  작업 {str(c.get('job')):>3s}  "
              f"{KIND_KO.get(c.get('kind'), c.get('kind')):<8s} {str(c.get('res')):<10s}{star}")
    if len(cands) > n_show:
        print(f"    … 나머지 {len(cands) - n_show}개 생략")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ckpt", required=True)
    ap.add_argument("--state", default="", help="결정 레코드 하나가 담긴 JSON")
    ap.add_argument("--jsonl", default="", help="리플레이 JSONL (decision 줄만 처리)")
    ap.add_argument("--limit", type=int, default=3, help="--jsonl 에서 처리할 결정 수")
    ap.add_argument("--json-out", action="store_true", help="사람이 읽는 표 대신 JSON 만 출력")
    a = ap.parse_args()
    if not a.state and not a.jsonl:
        raise SystemExit("--state 또는 --jsonl 중 하나가 필요하다")

    d = Dispatcher(a.ckpt)
    if not a.json_out:
        print(f"모델 {a.ckpt} · 후보피처 드롭 {d.cand_drop} · 채점기 {d.model.scorer} · {d.device}")

    if a.state:
        rec = json.load(open(a.state, encoding="utf-8"))
        pick = d.decide(rec)
        if a.json_out:
            print(json.dumps(pick, ensure_ascii=False))
        else:
            print("\n[결정 1건]")
            show(rec, pick)
            print(f"  => 작업 {pick['job']} · {KIND_KO.get(pick['kind'], pick['kind'])} · 자원 {pick['res']}")
        return

    n = 0
    for line in open(a.jsonl, encoding="utf-8"):
        try:
            rec = json.loads(line)
        except Exception:
            continue
        if rec.get("t") != "decision":
            continue
        pick = d.decide(rec)
        n += 1
        if a.json_out:
            print(json.dumps({"step": rec.get("step"), **pick}, ensure_ascii=False))
        else:
            print(f"\n[결정 {n}] step {rec.get('step')}")
            show(rec, pick)
            print(f"  => 작업 {pick['job']} · {KIND_KO.get(pick['kind'], pick['kind'])} · 자원 {pick['res']}")
            if rec.get("label"):
                lab = rec["label"]
                hit = (lab.get("kind") == pick["kind"] and lab.get("res") == pick["res"]
                       and lab.get("op") == pick["op"])
                print(f"     교사: {KIND_KO.get(lab.get('kind'), lab.get('kind'))} "
                      f"{lab.get('res')}  {'일치' if hit else '불일치'}")
        if n >= a.limit:
            break
    if n == 0:
        print("처리한 결정이 없다. JSONL 에 t==\"decision\" 줄이 있는지 확인하라.")


if __name__ == "__main__":
    main()
