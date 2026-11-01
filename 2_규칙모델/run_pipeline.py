# -*- coding: utf-8 -*-

import argparse
import glob
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
PKG = os.path.dirname(HERE)


def find_exe():
    hits = glob.glob(os.path.join(PKG, "1_메타휴리스틱", "bin", "**", "PDP_ASRS_WIP.exe"),
                     recursive=True)
    if not hits:
        raise SystemExit("실행 파일이 없다. ../1_메타휴리스틱 에서 `dotnet build` 를 먼저 하라.")
    return os.path.abspath(hits[0])


def find_scenario(given):
    if given:
        return os.path.abspath(given)
    hits = sorted(glob.glob(os.path.join(PKG, "3_시나리오", "*P4R*.json")))
    hits = [h for h in hits if "_OUTPUT_" not in h and "VCRAW" not in h]
    if not hits:
        raise SystemExit("../3_시나리오 에 P4R 입력이 없다.")
    return os.path.abspath(hits[0])


def run_meta(exe, scn, seed, timeout):
    t0 = time.time()
    r = subprocess.run([exe, scn, "hyb", str(seed), "0"], capture_output=True, text=True,
                       encoding="utf-8", errors="replace",
                       env=dict(os.environ, PDP_DROP_STOCK_SHORT="1"),
                       cwd=os.path.dirname(exe), timeout=timeout)
    return r.returncode, time.time() - t0


def best_teacher(scn):

    d = os.path.join(os.path.dirname(scn), "rulelogs", "auto")
    best, best_t = None, float("inf")
    for f in sorted(glob.glob(os.path.join(d, "replay_*_hyb.jsonl"))):
        try:
            t = float(json.loads(open(f, encoding="utf-8").readline()).get("tardiness", 1e18))
        except Exception:
            continue
        if t < best_t:
            best, best_t = f, t
    return best, best_t


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scenario", default="")
    ap.add_argument("--seeds", type=int, nargs="+", default=[1, 2, 3])
    ap.add_argument("--folds", type=int, default=5)
    ap.add_argument("--epochs", type=int, default=200)
    ap.add_argument("--out", default=os.path.join(HERE, "model.pt"))
    ap.add_argument("--workdir", default=os.path.join(HERE, "work"))
    ap.add_argument("--timeout", type=int, default=3600)
    ap.add_argument("--skip-meta", action="store_true")
    ap.add_argument("--cpu", action="store_true")
    a = ap.parse_args()

    scn = find_scenario(a.scenario)
    print(f"시나리오 {os.path.basename(scn)}")


    if not a.skip_meta:
        exe = find_exe()
        for s in a.seeds:
            rc, sec = run_meta(exe, scn, s, a.timeout)
            print(f"  HYB seed{s}  rc={rc}  {sec:.0f}s", flush=True)


    teacher, tard = best_teacher(scn)
    if teacher is None:
        raise SystemExit("hyb 리플레이가 없다. 먼저 메타휴리스틱을 돌려라(--skip-meta 를 빼고).")
    print(f"교사 = {os.path.basename(teacher)}  총지연 {tard:,.1f}")


    os.makedirs(a.workdir, exist_ok=True)
    data_dir = os.path.join(a.workdir, "teacher")
    os.makedirs(data_dir, exist_ok=True)
    for old in glob.glob(os.path.join(data_dir, "*.jsonl")):
        os.remove(old)
    import shutil
    shutil.copy(teacher, os.path.join(data_dir, os.path.basename(teacher)))


    print(f"\n=== {a.folds}겹 교차검증 (한 인스턴스의 궤적을 블록 단위로 나눔) ===", flush=True)
    results = []
    for k in range(a.folds):
        ck = os.path.join(a.workdir, f"fold{k}.pt")
        cmd = [sys.executable, os.path.join(HERE, "train.py"),
               "--data", data_dir, "--out", ck,
               "--cv-folds", str(a.folds), "--cv-fold", str(k),
               "--epochs", str(a.epochs), "--select", "20"]
        if a.cpu:
            cmd.append("--cpu")
        r = subprocess.run(cmd, capture_output=True, text=True,
                           encoding="utf-8", errors="replace",
                           env=dict(os.environ, PYTHONIOENCODING="utf-8"))
        paa = None
        for line in r.stdout.splitlines():
            if "채택 =" in line:
                try:
                    paa = float(line.split("쌍승률")[1].strip().rstrip(")"))
                except Exception:
                    pass
        if paa is None:
            print(f"  fold{k} 실패:\n{(r.stdout + r.stderr)[-500:]}")
            continue
        results.append((paa, k, ck))
        print(f"  fold{k}  검증 쌍승률 {paa:.4f}", flush=True)

    if not results:
        raise SystemExit("학습이 모두 실패했다.")


    results.sort(reverse=True)
    best_paa, best_k, best_ck = results[0]
    shutil.copy(best_ck, a.out)
    mean = sum(r[0] for r in results) / len(results)
    print(f"\n최고 = fold{best_k} (검증 쌍승률 {best_paa:.4f}) · 겹 평균 {mean:.4f}")
    print(f"모델 저장 → {a.out}")
    print(f"겹별 체크포인트 → {a.workdir}  (다른 기준으로 다시 고를 수 있다)")
    json.dump({"scenario": os.path.basename(scn), "teacher": os.path.basename(teacher),
               "teacher_tardiness": tard, "best_fold": best_k, "best_paa": best_paa,
               "fold_paa": {f"fold{k}": p for p, k, _ in sorted(results, key=lambda x: x[1])},
               "note": "쌍승률은 대리지표다. 총지연으로 다시 고르려면 work/fold*.pt 를 각각 굴려 비교하라."},
              open(os.path.join(HERE, "pipeline_result.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)
    print(f"요약 → {os.path.join(HERE, 'pipeline_result.json')}")
    print(f"\n다음: python dispatch.py --ckpt {os.path.basename(a.out)} --jsonl {teacher} --limit 3")


if __name__ == "__main__":
    main()
