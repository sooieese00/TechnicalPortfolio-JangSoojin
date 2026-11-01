# -*- coding: utf-8 -*-

import argparse
import collections
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from rollout_builder import Builder, Instance   # noqa: E402
from dispatch import Dispatcher                 # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--inst", required=True, help="exportinst 가 만든 *_INSTANCE.json")
    ap.add_argument("--ckpt", default=os.path.join(HERE, "model.pt"))
    ap.add_argument("--limit", type=int, default=0, help="앞쪽 N 결정만 (0=끝까지)")
    a = ap.parse_args()

    inst = Instance.from_json(a.inst)
    print(f"P4R 정적 구조: 잡 {inst.m} · 스테이션 {inst.n} · AMR {inst.r} · 활성 단계 {len(inst.active)}")

    d = Dispatcher(a.ckpt)
    print(f"모델 {os.path.basename(a.ckpt)} · 채점기 {d.model.scorer}")

    b = Builder(inst)
    step, kinds = 0, collections.Counter()
    while not b.done():
        rec = b.emit_state(step)
        if not rec["candidates"]:
            print(f"step {step}: 후보 없음 - 중단")
            break
        pick = d.decide(rec)
        b.commit(pick["kind"], pick["job"], pick["res"])   
        kinds[pick["kind"]] += 1
        step += 1
        if a.limit and step >= a.limit:
            break

    last = b.emit_state(step)
    print(f"{step} 결정 · 결합 아크 {len(last['edges'])} · 이접 아크 {len(last['undecided'])}"
          f" · 완주 {b.done()}")
    print("결정 종류:", dict(kinds))


if __name__ == "__main__":
    main()
